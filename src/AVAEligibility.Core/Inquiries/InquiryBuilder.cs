using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AVAEligibility.Core.Inquiries;

/// <summary>Who's asking, and the envelope a 270 goes in.</summary>
public sealed record InquirySettings
{
    /// <summary>The provider asking (2100B): the practice or the billing provider, by name and NPI.</summary>
    public string ProviderName { get; init; } = "";
    public string ProviderNpi { get; init; } = "";

    /// <summary>ISA06 and ISA08 (GS02, GS03): the sender's and receiver's IDs, as the clearinghouse assigned them.</summary>
    public string SenderId { get; init; } = "";
    public string ReceiverId { get; init; } = "";

    /// <summary>TRN03: the ID the provider's traces are made under, "9" and nine more characters.</summary>
    public string TraceOriginator { get; init; } = "";

    /// <summary>BHT03's prefix: each payer's transaction is this and its number (MSFM0001).</summary>
    public string ReferencePrefix { get; init; } = "INQ";

    /// <summary>ISA15: T for test, P for production.</summary>
    public string Usage { get; init; } = "T";

    /// <summary>ISA13 and GS06: the next interchange control number.</summary>
    public int ControlNumber { get; init; } = 1;
}

public enum CheckLevel
{
    /// <summary>Said for information: a self-pay visit has nothing to ask.</summary>
    Info,

    /// <summary>Sent, but likely to be rejected or answered wrong.</summary>
    Warning,

    /// <summary>Not sent: the payer can't answer it as it is.</summary>
    Error,
}

/// <summary>What's wrong with an appointment's question, before it's sent.</summary>
public sealed record InquiryCheck(Appointment Appointment, CheckLevel Level, string Text)
{
    public override string ToString() => $"Row {Appointment.Row} ({Appointment.SortName}): {Text}";
}

/// <summary>A built 270: its text, the trace each appointment was sent under, and the checks.</summary>
public sealed record BuiltInquiry(string Text, IReadOnlyDictionary<Appointment, string> Traces, IReadOnlyList<InquiryCheck> Checks, int Transactions)
{
    public IEnumerable<Appointment> Sent => Traces.Keys;
}

/// <summary>
/// Builds a 270 (005010X279A1) from an appointment list: one transaction per payer, the provider asking, and each
/// patient with their trace, member ID, name, birth date, gender, date of service and the service types to ask about
/// (<see cref="ServiceTypes"/>). A dependent goes under their subscriber's loop when the subscriber is on the list too,
/// and under a loop of the subscriber's name alone when not. Checks run first; rows with errors aren't sent.
/// </summary>
public static class InquiryBuilder
{
    /// <summary>Characters that would break the file: the separators.</summary>
    private static readonly char[] Separators = ['*', '~', ':', '^', '|'];

    private static readonly Regex LetterAmongDigits = new(@"(?<=\d)[OoIl](?=\d)|(?<=\d)[OoIl]$|^[OoIl](?=\d)", RegexOptions.CultureInvariant);

    /// <summary>The checks for every appointment, before building.</summary>
    public static IReadOnlyList<InquiryCheck> Check(IReadOnlyList<Appointment> appointments, DateOnly today)
    {
        var checks = new List<InquiryCheck>();
        var subscribers = appointments.Where(a => !a.IsDependent && !a.SelfPay).ToList();
        foreach (var a in appointments)
        {
            void Say(CheckLevel level, string text) => checks.Add(new InquiryCheck(a, level, text));
            foreach (var u in a.Unread) Say(CheckLevel.Error, $"{Capital(u)}.");
            if (a.SelfPay)
            {
                Say(CheckLevel.Info, "Self-pay: there's no insurance to ask about.");
                continue;
            }
            if (a.PayerId.Length == 0) Say(CheckLevel.Error, "There's no payer ID: the clearinghouse can't route the question.");
            if (a.Last.Length == 0) Say(CheckLevel.Error, "There's no last name.");
            if (a.First.Length == 0) Say(CheckLevel.Warning, "There's no first name: most payers need it to find the patient.");
            if (a.MemberId.Length == 0 && (a.BirthDate == null || a.First.Length == 0))
                Say(CheckLevel.Error, "There's no member ID, and without one the payer needs the name and birth date.");
            if (a.Date == null && !a.Unread.Any(u => u.StartsWith("the visit date", StringComparison.Ordinal))) Say(CheckLevel.Error, "There's no visit date.");
            if (a.BirthDate == null && !a.Unread.Any(u => u.StartsWith("the birth date", StringComparison.Ordinal))) Say(CheckLevel.Warning, "There's no birth date: payers match on it.");
            if (a.BirthDate is { } born && a.Date is { } day && born > day) Say(CheckLevel.Error, $"The birth date {born:yyyy-MM-dd} is after the visit.");
            if (a.Date is { } d && (d > today.AddMonths(12) || d < today.AddMonths(-12)))
                Say(CheckLevel.Warning, $"The visit date {d:yyyy-MM-dd} is more than a year from today: payers seldom answer for dates that far out.");
            if (LetterAmongDigits.IsMatch(a.MemberId))
                Say(CheckLevel.Warning, $"Member ID {a.MemberId} has a letter O, I or l among its digits: is it a zero or a one? Check the card.");
            if (a.MemberId.Any(c => c is ' ' or '-'))
                Say(CheckLevel.Warning, $"Member ID {a.MemberId} has spaces or dashes; it's sent without them.");
            if (a.Sex.Length > 0 && a.Sex is not ("M" or "F" or "U"))
                Say(CheckLevel.Warning, $"Sex \"{a.Sex}\" isn't M, F or U; it's left out.");
            if (new[] { a.Last, a.First, a.Middle, a.MemberId, a.PayerId }.Any(v => v.IndexOfAny(Separators) >= 0))
                Say(CheckLevel.Error, "A name or ID holds a separator character (* ~ : ^ |) that would break the file.");
            if (a.IsDependent)
            {
                var (sl, sf) = a.SubscriberName;
                if (!subscribers.Any(s => s.PayerId == a.PayerId && Clean(s.MemberId) == Clean(a.MemberId) && s.Last == sl && s.First == sf))
                    Say(CheckLevel.Warning, $"The subscriber {a.Subscriber} isn't on the list: the question goes with the subscriber's name and member ID only.");
            }
        }
        return checks;
    }

    /// <summary>Builds the 270, leaving out the appointments with errors and the self-pay ones.</summary>
    public static BuiltInquiry Build(IReadOnlyList<Appointment> appointments, InquirySettings settings, DateTime created)
    {
        var checks = Check(appointments, DateOnly.FromDateTime(created)).ToList();
        if (!Npi.IsValid(settings.ProviderNpi))
            throw new InvalidOperationException($"The provider's NPI \"{settings.ProviderNpi}\" isn't a valid NPI: set it in the inquiry settings.");
        if (settings.ProviderName.Trim().Length == 0 || settings.SenderId.Trim().Length == 0 || settings.ReceiverId.Trim().Length == 0)
            throw new InvalidOperationException("The inquiry settings need the provider's name and the sender and receiver IDs.");

        var bad = checks.Where(c => c.Level == CheckLevel.Error).Select(c => c.Appointment).ToHashSet();
        var send = appointments.Where(a => !a.SelfPay && !bad.Contains(a)).ToList();
        var traces = new Dictionary<Appointment, string>();
        var segments = new List<string>();
        var control = settings.ControlNumber;
        var stamp = created.ToString("HHmm", CultureInfo.InvariantCulture);
        var transactions = 0;

        foreach (var payer in send.GroupBy(a => a.PayerId))
        {
            transactions++;
            var body = new List<string>
            {
                Seg("BHT", "0022", "13", $"{settings.ReferencePrefix}{transactions:D4}", created.ToString("yyyyMMdd", CultureInfo.InvariantCulture), stamp),
                Seg("HL", "1", "", "20", "1"),
                Seg("NM1", "PR", "2", payer.First().Payer.Length > 0 ? payer.First().Payer.ToUpperInvariant() : payer.Key, "", "", "", "", "PI", payer.Key),
                Seg("HL", "2", "1", "21", "1"),
                Seg("NM1", "1P", "2", settings.ProviderName.ToUpperInvariant(), "", "", "", "", "XX", settings.ProviderNpi),
            };
            var hl = 2;
            var traceNumber = 0;
            var prefix = new string(payer.Key.Where(char.IsLetterOrDigit).Take(2).ToArray()).ToUpperInvariant();
            string Trace(Appointment a) => traces[a] = $"{prefix}{++traceNumber:D4}";
            var people = payer.ToList();
            var done = new HashSet<Appointment>();
            foreach (var a in people)
            {
                if (done.Contains(a)) continue;
                List<Appointment> dependents;
                if (a.IsDependent)
                {
                    // A dependent whose subscriber isn't on the list: a subscriber loop of the name and ID alone.
                    var (sl, sf) = a.SubscriberName;
                    hl++;
                    var parent = hl;
                    body.Add(Seg("HL", hl.ToString(CultureInfo.InvariantCulture), "2", "22", "1"));
                    body.Add(Seg("NM1", "IL", "1", sl, sf, "", "", "", "MI", Clean(a.MemberId)));
                    dependents = [a];
                    foreach (var d in dependents) AddDependent(d, parent);
                    continue;
                }
                dependents = people.Where(d => d.IsDependent && !done.Contains(d) && Clean(d.MemberId) == Clean(a.MemberId) && d.SubscriberName == (a.Last, a.First)).ToList();
                hl++;
                var subscriberHl = hl;
                body.Add(Seg("HL", hl.ToString(CultureInfo.InvariantCulture), "2", "22", dependents.Count > 0 ? "1" : "0"));
                body.Add(Seg("TRN", "1", Trace(a), settings.TraceOriginator));
                body.Add(Seg("NM1", "IL", "1", a.Last, a.First, a.Middle, "", "", "MI", Clean(a.MemberId)));
                AddDemographics(a);
                done.Add(a);
                foreach (var d in dependents) AddDependent(d, subscriberHl);
            }
            void AddDependent(Appointment d, int parent)
            {
                hl++;
                body.Add(Seg("HL", hl.ToString(CultureInfo.InvariantCulture), parent.ToString(CultureInfo.InvariantCulture), "23", "0"));
                body.Add(Seg("TRN", "1", Trace(d), settings.TraceOriginator));
                body.Add(Seg("NM1", "03", "1", d.Last, d.First, d.Middle));
                AddDemographics(d);
                done.Add(d);
            }
            void AddDemographics(Appointment a)
            {
                if (a.BirthDate is { } born) body.Add(Seg("DMG", "D8", born.ToString("yyyyMMdd", CultureInfo.InvariantCulture), a.Sex is "M" or "F" or "U" ? a.Sex : ""));
                if (a.Date is { } day) body.Add(Seg("DTP", "291", "D8", day.ToString("yyyyMMdd", CultureInfo.InvariantCulture)));
                body.Add(Seg("EQ", string.Join("^", ServiceTypes.For(a.Procedure))));
            }
            segments.Add(Seg("ST", "270", $"{transactions:D4}", "005010X279A1"));
            segments.AddRange(body);
            segments.Add(Seg("SE", (body.Count + 2).ToString(CultureInfo.InvariantCulture), $"{transactions:D4}"));
        }

        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"ISA*00*          *00*          *ZZ*{Pad(settings.SenderId)}*ZZ*{Pad(settings.ReceiverId)}*{created:yyMMdd}*{stamp}*^*00501*{control:D9}*0*{(settings.Usage == "P" ? "P" : "T")}*:~\n");
        text.Append(Seg("GS", "HS", settings.SenderId, settings.ReceiverId, created.ToString("yyyyMMdd", CultureInfo.InvariantCulture), stamp, control.ToString(CultureInfo.InvariantCulture), "X", "005010X279A1")).Append('\n');
        foreach (var s in segments) text.Append(s).Append('\n');
        text.Append(Seg("GE", transactions.ToString(CultureInfo.InvariantCulture), control.ToString(CultureInfo.InvariantCulture))).Append('\n');
        text.Append(Seg("IEA", "1", control.ToString("D9", CultureInfo.InvariantCulture))).Append('\n');
        return new BuiltInquiry(text.ToString(), traces, checks, transactions);
    }

    private static string Pad(string id) => id.Trim().ToUpperInvariant().PadRight(15)[..15];

    /// <summary>A member ID as it's sent: without spaces or dashes.</summary>
    public static string Clean(string id) => new([.. id.Where(c => c is not (' ' or '-'))]);

    private static string Capital(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    /// <summary>A segment, with trailing empty elements left off.</summary>
    private static string Seg(string id, params string[] elements)
    {
        var parts = elements.ToList();
        while (parts.Count > 0 && parts[^1].Length == 0) parts.RemoveAt(parts.Count - 1);
        return parts.Count == 0 ? id + "~" : $"{id}*{string.Join("*", parts)}~";
    }
}

/// <summary>NPI check digits, as CMS describes them: the Luhn formula over 80840 and the first nine digits.</summary>
public static class Npi
{
    public static bool IsValid(string npi)
    {
        if (npi.Length != 10 || !npi.All(char.IsAsciiDigit)) return false;
        var digits = ("80840" + npi[..9]).Select(c => c - '0').ToArray();
        var sum = 0;
        for (var i = 0; i < digits.Length; i++)
        {
            var d = digits[digits.Length - 1 - i];
            if (i % 2 == 0)
            {
                d *= 2;
                if (d > 9) d -= 9;
            }
            sum += d;
        }
        return (10 - sum % 10) % 10 == npi[9] - '0';
    }
}
