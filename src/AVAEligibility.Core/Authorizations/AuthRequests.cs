using System.Globalization;
using System.Text;
using AVAEligibility.Core.Eligibility;
using AVAEligibility.Core.Inquiries;

namespace AVAEligibility.Core.Authorizations;

/// <summary>
/// A visit whose care the payer's 271 says needs authorization: the visit, the benefit that says so, and who reviews it
/// (the 2120 loop's utilization review, when the 271 names one, or the payer).
/// </summary>
public sealed record AuthNeed(Appointment Appointment, Benefit Benefit, Authorization Authorization, string Care, string UmoName, string UmoPhone)
{
    public string Contact => Authorization.Contact;
}

/// <summary>The 278s written for the visits, with the trace each review went under, and what the checks left out.</summary>
public sealed record BuiltAuth(string Text, IReadOnlyDictionary<Appointment, string> Traces, IReadOnlyList<InquiryCheck> Checks, int Transactions);

/// <summary>
/// The 278 side of the worklist: which visits need authorization, the checks before asking, the request (005010X217:
/// one transaction per visit, as a 278 request carries one patient event), and the answer about a visit among the open
/// 278s. The request asks a health services review (UM01 HS), initial (UM02 I), for the visit's care (UM03) in an
/// office (UM04 11), with the visit's diagnosis, date, provider and procedure. Payers support the 278 unevenly: many
/// take it through a clearinghouse, some only on their portal or by phone, which the 271's contact says.
/// </summary>
public static class AuthRequests
{
    /// <summary>The visits on the worklist whose care the 271 says needs authorization (denied claims asked about again aren't visits).</summary>
    public static List<AuthNeed> Needed(IEnumerable<WorkItem> work) =>
        [.. work.Where(w => w.Reading != null).Select(w => For(w.Appointment, w.Reading!)).OfType<AuthNeed>()];

    /// <summary>The visit's need for authorization, from its answer; null when its care needs none (or it's a denied claim).</summary>
    public static AuthNeed? For(Appointment a, Interpretation r)
    {
        if (a.Denied || ServiceTypes.Care(a.Procedure) is not { } care) return null;
        var b = r.Person.Benefits.FirstOrDefault(x => x.Authorization == "Y" && x.ServiceTypes.Contains(care));
        if (b == null) return null;
        var auth = r.Authorizations.First(x => x.Segments.Contains(b.Segment));
        var umo = b.Entities.FirstOrDefault(e => e.Entity == "X3");
        var phone = (umo ?? b.Entities.FirstOrDefault())?.Contacts.SelectMany(c => c.Numbers).Where(n => n.Kind == "TE").Select(n => n.Number).FirstOrDefault() ?? "";
        return new AuthNeed(a, b, auth, care, umo?.Name ?? a.Payer, phone);
    }

    /// <summary>The checks before a 278: what it can't be sent without, and what a payer would likely reject.</summary>
    public static IReadOnlyList<InquiryCheck> Check(IReadOnlyList<AuthNeed> needs)
    {
        var checks = new List<InquiryCheck>();
        foreach (var n in needs)
        {
            var a = n.Appointment;
            void Say(CheckLevel level, string text) => checks.Add(new InquiryCheck(a, level, text));
            if (a.Diagnosis.Length == 0) Say(CheckLevel.Error, "There's no diagnosis: a 278 needs one (an ICD-10-CM code, in the list's Diagnosis column).");
            else if (!System.Text.RegularExpressions.Regex.IsMatch(a.Diagnosis.Replace(".", ""), "^[A-Z][0-9][0-9A-Z]{1,5}$"))
                Say(CheckLevel.Error, $"The diagnosis \"{a.Diagnosis}\" isn't shaped like an ICD-10-CM code.");
            if (a.Date == null) Say(CheckLevel.Error, "There's no visit date.");
            if (a.MemberId.Length == 0) Say(CheckLevel.Error, "There's no member ID.");
            if (a.ProviderNpi.Length == 0) Say(CheckLevel.Warning, "There's no provider NPI for the visit: the review won't name who does it.");
            else if (!Npi.IsValid(a.ProviderNpi)) Say(CheckLevel.Error, $"The visit's provider NPI {a.ProviderNpi} isn't a valid NPI.");
            if (a.Procedure.Length == 0) Say(CheckLevel.Error, "There's no procedure code.");
        }
        return checks;
    }

    /// <summary>The 278 requests for the visits, leaving out those with errors. Uses the inquiry settings (who asks, the envelope).</summary>
    public static BuiltAuth Build(IReadOnlyList<AuthNeed> needs, InquirySettings settings, DateTime created)
    {
        if (!Npi.IsValid(settings.ProviderNpi))
            throw new InvalidOperationException($"The provider's NPI \"{settings.ProviderNpi}\" isn't a valid NPI: set it in the inquiry settings.");
        if (settings.ProviderName.Trim().Length == 0 || settings.SenderId.Trim().Length == 0 || settings.ReceiverId.Trim().Length == 0)
            throw new InvalidOperationException("The inquiry settings need the provider's name and the sender and receiver IDs.");
        var checks = Check(needs);
        var bad = checks.Where(c => c.Level == CheckLevel.Error).Select(c => c.Appointment).ToHashSet();
        var control = settings.ControlNumber;
        var stamp = created.ToString("HHmm", CultureInfo.InvariantCulture);
        var day = created.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var traces = new Dictionary<Appointment, string>();
        var segments = new List<string>();
        var number = 0;
        foreach (var n in needs.Where(n => !bad.Contains(n.Appointment)))
        {
            var a = n.Appointment;
            number++;
            var prefix = new string(a.PayerId.Where(char.IsLetterOrDigit).Take(2).ToArray()).ToUpperInvariant();
            var trace = traces[a] = $"{prefix}A{number:D3}";
            var date = a.Date!.Value.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            var body = new List<string>
            {
                Seg("BHT", "0007", "13", $"{settings.ReferencePrefix}A{number:D3}", day, stamp),
                Seg("HL", "1", "", "20", "1"),
                Seg("NM1", "X3", "2", n.UmoName.ToUpperInvariant(), "", "", "", "", "PI", a.PayerId),
                Seg("HL", "2", "1", "21", "1"),
                Seg("NM1", "1P", "2", settings.ProviderName.ToUpperInvariant(), "", "", "", "", "XX", settings.ProviderNpi),
            };
            var member = InquiryBuilder.Clean(a.MemberId);
            var hl = 3;
            if (a.IsDependent)
            {
                var (sl, sf) = a.SubscriberName;
                body.Add(Seg("HL", "3", "2", "22", "1"));
                body.Add(Seg("NM1", "IL", "1", sl, sf, "", "", "", "MI", member));
                body.Add(Seg("HL", "4", "3", "23", "1"));
                body.Add(Seg("NM1", "QC", "1", a.Last, a.First, a.Middle));
                if (a.BirthDate is { } born) body.Add(Seg("DMG", "D8", born.ToString("yyyyMMdd", CultureInfo.InvariantCulture), a.Sex is "M" or "F" or "U" ? a.Sex : ""));
                hl = 5;
            }
            else
            {
                body.Add(Seg("HL", "3", "2", "22", "1"));
                body.Add(Seg("NM1", "IL", "1", a.Last, a.First, a.Middle, "", "", "MI", member));
                if (a.BirthDate is { } born) body.Add(Seg("DMG", "D8", born.ToString("yyyyMMdd", CultureInfo.InvariantCulture), a.Sex is "M" or "F" or "U" ? a.Sex : ""));
                hl = 4;
            }
            body.Add(Seg("HL", hl.ToString(CultureInfo.InvariantCulture), (hl - 1).ToString(CultureInfo.InvariantCulture), "EV", "1"));
            body.Add(Seg("TRN", "1", trace, settings.TraceOriginator));
            body.Add(Seg("UM", "HS", "I", n.Care, "11:B"));
            body.Add(Seg("DTP", "AAH", "D8", date));
            body.Add(Seg("HI", "ABK:" + a.Diagnosis.Replace(".", "")));
            if (a.ProviderNpi.Length > 0)
            {
                var (pl, pf) = a.Provider.Split(',', 2) is [var l, var f] ? (l.Trim(), f.Trim()) : (a.Provider.Trim(), "");
                body.Add(Seg("NM1", "SJ", "1", pl.ToUpperInvariant(), pf.ToUpperInvariant(), "", "", "", "XX", a.ProviderNpi));
            }
            body.Add(Seg("HL", (hl + 1).ToString(CultureInfo.InvariantCulture), hl.ToString(CultureInfo.InvariantCulture), "SS", "0"));
            body.Add(Seg("UM", "HS", "I"));
            body.Add(Seg("DTP", "472", "D8", date));
            body.Add(Seg("SV1", "HC:" + a.Procedure, a.Charge is { } c ? c.ToString("0.##", CultureInfo.InvariantCulture) : "0", "UN", "1"));
            segments.Add(Seg("ST", "278", $"{number:D4}", "005010X217"));
            segments.AddRange(body);
            segments.Add(Seg("SE", (body.Count + 2).ToString(CultureInfo.InvariantCulture), $"{number:D4}"));
        }
        if (number == 0) throw new InvalidOperationException("No visit can be asked about as it is: " + string.Join(" ", checks.Where(c => c.Level == CheckLevel.Error).Select(c => c.ToString())));
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"ISA*00*          *00*          *ZZ*{Pad(settings.SenderId)}*ZZ*{Pad(settings.ReceiverId)}*{created:yyMMdd}*{stamp}*^*00501*{control:D9}*0*{(settings.Usage == "P" ? "P" : "T")}*:~\n");
        text.Append(Seg("GS", "HI", settings.SenderId, settings.ReceiverId, day, stamp, control.ToString(CultureInfo.InvariantCulture), "X", "005010X217")).Append('\n');
        foreach (var s in segments) text.Append(s).Append('\n');
        text.Append(Seg("GE", number.ToString(CultureInfo.InvariantCulture), control.ToString(CultureInfo.InvariantCulture))).Append('\n');
        text.Append(Seg("IEA", "1", control.ToString("D9", CultureInfo.InvariantCulture))).Append('\n');
        return new BuiltAuth(text.ToString(), traces, checks, number);
    }

    /// <summary>
    /// The newest review about a visit among the open 278s, answers before requests: by the payer (the UMO's ID), the
    /// member ID, the patient's last name, the date, and the procedure when the review names services.
    /// </summary>
    public static AuthReview? For(Appointment a, IEnumerable<AuthFile> files)
    {
        var member = InquiryBuilder.Clean(a.MemberId);
        return files.SelectMany(f => f.Reviews)
            .Where(r => r.UmoId.Equals(a.PayerId, StringComparison.OrdinalIgnoreCase)
                        && InquiryBuilder.Clean(r.MemberId).Equals(member, StringComparison.OrdinalIgnoreCase)
                        && (r.WhoLast.Length == 0 || r.WhoLast.Equals(a.Last, StringComparison.OrdinalIgnoreCase))
                        && (r.EventDate == null || r.EventDate == a.Date || r.EventEnd is { } end && a.Date >= r.EventDate && a.Date <= end)
                        && (r.Services.Count == 0 || r.Services.Any(s => s.Procedure.Equals(a.Procedure, StringComparison.OrdinalIgnoreCase))))
            .OrderByDescending(r => r.IsResponse).ThenByDescending(r => r.Created ?? DateTime.MinValue)
            .FirstOrDefault();
    }

    /// <summary>
    /// What a review means for the visit, as a note: certified (the number to put on the claim, if the dates cover the
    /// visit), pended, not certified, contact the payer, not needed, or refused (AAA); a request with no answer yet says
    /// it's waiting. Rests on the review's segments.
    /// </summary>
    public static Note Read(AuthReview r, AuthNeed need)
    {
        var a = need.Appointment;
        var umo = need.UmoName + (need.UmoPhone.Length > 0 ? $" at {Interpreter.Phone(need.UmoPhone)}" : "");
        var seg = r.Segments;
        if (r.Rejections.Count > 0)
        {
            var x = r.Rejections[0];
            return new Note(Verdict.Act, $"The 278 was refused: {Words.Describe(x)}.", Interpreter.Fix(x.Reason) + $" Or ask {umo}.", [x.Segment]);
        }
        if (!r.IsResponse)
            return new Note(Verdict.Act, $"This visit's authorization was asked for ({r.Trace}); no answer is open yet.", $"Open the 278 answer when it comes, or call {umo} before the visit.", seg);
        var reason = r.Reason.Length > 0 ? $" (reason code {r.Reason})" : "";
        var number = r.Number.Length > 0 ? r.Number : "(no number given)";
        var covers = a.Date is not { } day || (r.Effective is not { } from || day >= from) && (r.Expires is not { } to || day <= to);
        return r.Action switch
        {
            "A1" or "A2" or "A6" when !covers => new Note(Verdict.Act, $"Authorization {number} runs {r.Runs}, which doesn't cover the visit on {a.Date:yyyy-MM-dd}.", $"Ask {umo} to extend it, or move the visit.", seg),
            "A1" => new Note(Verdict.Ready, $"Authorized: {number}" + (r.Runs.Length > 0 ? $", {r.Runs}." : "."), $"Put {number} on the claim (REF*G1).", seg),
            "A2" => new Note(Verdict.Check, $"Authorized in part: {number}{reason}" + (r.Runs.Length > 0 ? $", {r.Runs}." : "."), $"Check which services were approved before the visit ({string.Join(", ", r.Services.Select(s => $"{s.Code} {Say(s.Action)}"))}); put {number} on the claim.", seg),
            "A6" => new Note(Verdict.Check, $"Authorized with changes: {number}{reason}.", $"Check what the reviewer changed before the visit; put {number} on the claim.", seg),
            "A4" => new Note(Verdict.Check, $"The authorization is pending{(r.Number.Length > 0 ? $" ({r.Number})" : "")}{reason}.", $"Follow up with {umo} before the visit.", seg),
            "A3" => new Note(Verdict.Act, $"Authorization was refused (not certified){reason}.", $"Tell the patient before the visit: ask {umo} to reconsider (an appeal, with the records), reschedule, or the patient agrees in writing to pay.", seg),
            "CT" => new Note(Verdict.Act, $"The reviewer wants a call about the authorization{reason}.", $"Call {umo} before the visit.", seg),
            "NA" => new Note(Verdict.Ready, "The reviewer says no authorization is needed for this.", "Nothing to do; keep the 278's answer with the visit.", seg),
            "C" => new Note(Verdict.Act, $"The authorization was cancelled{reason}.", $"Ask {umo} again before the visit.", seg),
            _ => new Note(Verdict.Check, $"The 278 answer gives no decision{(r.Action.Length > 0 ? $" (action code {r.Action})" : "")}.", $"Call {umo}.", seg),
        };
    }

    private static string Say(string action) => action.Length == 0 ? "" : AuthCodes.Say(AuthCodes.Action, action).ToLowerInvariant();

    private static string Pad(string id) => id.Trim().ToUpperInvariant().PadRight(15)[..15];

    private static string Seg(string id, params string[] elements)
    {
        var parts = elements.ToList();
        while (parts.Count > 0 && parts[^1].Length == 0) parts.RemoveAt(parts.Count - 1);
        return id + (parts.Count > 0 ? "*" + string.Join("*", parts) : "") + "~";
    }
}
