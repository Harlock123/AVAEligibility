using System.Globalization;
using AVAEligibility.Core.X12;

namespace AVAEligibility.Core.Eligibility;

/// <summary>
/// Reads 270s and 271s (005010X279A1) into payers, providers and people, following the HL levels: 20 the information
/// source (the payer, 2000A), 21 the receiver (the provider, 2000B), 22 a subscriber (2000C) and 23 a dependent (2000D).
/// A person's loop runs to its first EQ or EB; each EQ or EB starts a 2110 loop, and LS…LE inside one is a 2120 loop of
/// related entities. Whatever is out of place is read where it falls and named in <see cref="EligibilityFile.Problems"/>.
/// </summary>
public static class EligibilityReader
{
    public static EligibilityFile Load(string path) => Parse(File.ReadAllText(path), System.IO.Path.GetFileName(path), path);

    public static EligibilityFile Parse(string text, string name = "pasted", string? path = null)
    {
        var x12 = X12File.Parse(text);
        var problems = new List<string>();
        var transactions = new List<EligibilityTransaction>();
        foreach (var t in x12.Transactions)
        {
            if (t.Code is not ("270" or "271"))
            {
                problems.Add($"Transaction {t.Control} is a {t.Code}, not a 270 or 271: it's left out.");
                continue;
            }
            transactions.Add(Read(t, x12.Delimiters, problems));
        }
        if (x12.Transactions.Count == 0) problems.Add("There's no ST segment: this isn't an X12 file, or it's empty.");
        return new EligibilityFile { Name = name, Path = path, X12 = x12, Transactions = transactions, Problems = problems };
    }

    /// <summary>ISA11, the repetition separator, when the interchange has one (005010 does); EQ01 and EB03 repeat with it.</summary>
    private static char? Repetition(Transaction t) =>
        t.Interchange is { } isa && isa[11] is { Length: 1 } r && !char.IsLetterOrDigit(r[0]) && r[0] != ' ' ? r[0] : null;

    private static EligibilityTransaction Read(Transaction t, Delimiters d, List<string> problems)
    {
        var bht = t.Body.FirstOrDefault(s => s.Id == "BHT");
        var result = new EligibilityTransaction
        {
            Source = t,
            Reference = bht?[3] ?? "",
            Created = bht == null ? null : Date(bht[4]) is { } day ? day.ToDateTime(Time(bht[5])) : null,
        };
        var repeat = Repetition(t);
        var level = "";
        Party? party = null;          // the 2100A or 2100B party being read
        Person? person = null;
        Benefit? benefit = null;
        Inquiry? inquiry = null;
        Party? entity = null;         // inside LS…LE
        var inEntities = false;
        var people = new Dictionary<string, Person>();

        foreach (var s in t.Body)
        {
            switch (s.Id)
            {
                case "BHT":
                    continue;
                case "HL":
                    level = s[3].Trim();
                    party = null;
                    benefit = null;
                    inquiry = null;
                    inEntities = false;
                    entity = null;
                    person = null;
                    if (level is "22" or "23")
                    {
                        var subscriber = level == "23" && people.TryGetValue(s[2].Trim(), out var parent) ? parent : null;
                        if (level == "23" && subscriber == null) problems.Add($"Dependent HL {s[1]} has no subscriber HL {s[2]} above it.");
                        person = new Person { Transaction = result, IsDependent = level == "23", Subscriber = subscriber, Hl = s[1].Trim() };
                        people[person.Hl] = person;
                        result.People.Add(person);
                        person.Segments.Add(s);
                    }
                    else if (level is not ("20" or "21")) problems.Add($"HL {s[1]} has level {level}: a 270 or 271 uses 20, 21, 22 and 23.");
                    continue;
            }

            if (person == null)
            {
                // The payer's and the provider's levels.
                switch (s.Id)
                {
                    case "NM1":
                        party = new Party { Entity = s[1].Trim(), Name = OrgName(s), IdQualifier = s[8].Trim(), Id = s[9].Trim() };
                        if (level == "20") result.Payer ??= party;
                        else if (level == "21") result.Provider ??= party;
                        party.Segments.Add(s);
                        break;
                    case "PER" when party != null:
                        party.Contacts.Add(Contact(s));
                        party.Segments.Add(s);
                        break;
                    case "AAA":
                        var where = level == "20" ? "the payer" : "the provider";
                        var r = Reject(s, where);
                        if (party != null) { party.Rejections.Add(r); party.Segments.Add(s); }
                        else problems.Add($"An AAA at segment {s.Index + 1} comes before its level's NM1.");
                        break;
                    default:
                        party?.Segments.Add(s);
                        break;
                }
                continue;
            }

            person.Segments.Add(s);
            if (inEntities)
            {
                switch (s.Id)
                {
                    case "LE":
                        inEntities = false;
                        entity = null;
                        break;
                    case "NM1":
                        entity = new Party { Entity = s[1].Trim(), Name = s[2].Trim() == "1" ? PersonName(s) : OrgName(s), IdQualifier = s[8].Trim(), Id = s[9].Trim() };
                        entity.Segments.Add(s);
                        benefit!.Entities.Add(entity);
                        break;
                    case "PER" when entity != null:
                        entity.Contacts.Add(Contact(s));
                        entity.Segments.Add(s);
                        break;
                    default:
                        entity?.Segments.Add(s);
                        break;
                }
                benefit?.Segments.Add(s);
                continue;
            }

            switch (s.Id)
            {
                case "EB":
                    inquiry = null;
                    benefit = new Benefit
                    {
                        Information = s[1].Trim(),
                        CoverageLevel = s[2].Trim(),
                        ServiceTypes = Split(s[3], repeat),
                        InsuranceType = s[4].Trim(),
                        PlanDescription = s[5].Trim(),
                        Period = s[6].Trim(),
                        Amount = Number(s[7]),
                        Percent = Number(s[8]),
                        QuantityQualifier = s[9].Trim(),
                        Quantity = Number(s[10]),
                        Authorization = s[11].Trim(),
                        InNetwork = s[12].Trim(),
                        ProcedureQualifier = Part(s[13], d.Component, 0),
                        Procedure = Part(s[13], d.Component, 1),
                        Segment = s,
                    };
                    benefit.Segments.Add(s);
                    person.Benefits.Add(benefit);
                    if (!result.IsResponse) problems.Add($"An EB at segment {s.Index + 1} is in a 270: EB belongs in a 271.");
                    continue;
                case "EQ":
                    benefit = null;
                    inquiry = new Inquiry(Split(s[1], repeat), s[2].Trim(), s[3].Trim(), s);
                    person.Inquiries.Add(inquiry);
                    continue;
            }

            if (benefit != null)
            {
                benefit.Segments.Add(s);
                switch (s.Id)
                {
                    case "HSD":
                        benefit.Deliveries.Add(new Delivery(s[1].Trim(), Number(s[2]), s[3].Trim(), Number(s[4]), s[5].Trim(), Number(s[6]), s));
                        break;
                    case "REF":
                        benefit.References.Add(new IdReference(s[1].Trim(), s[2].Trim(), s[3].Trim()));
                        break;
                    case "DTP" when DateOf(s) is { } date:
                        benefit.Dates.Add(date);
                        break;
                    case "AAA":
                        benefit.Rejections.Add(Reject(s, "a benefit"));
                        break;
                    case "MSG":
                        benefit.Messages.Add(s[1].Trim());
                        break;
                    case "LS":
                        inEntities = true;
                        break;
                }
                continue;
            }
            if (inquiry != null) continue; // AMT, III, REF and DTP of a 270's question

            switch (s.Id)
            {
                case "TRN":
                    person.Traces.Add(new Trace(s[1].Trim(), s[2].Trim(), s[3].Trim()));
                    break;
                case "NM1":
                    person.Last = s[3].Trim();
                    person.First = s[4].Trim();
                    person.Middle = s[5].Trim();
                    if (s[8].Trim() == "MI") person.MemberId = s[9].Trim();
                    break;
                case "REF":
                    person.References.Add(new IdReference(s[1].Trim(), s[2].Trim(), s[3].Trim()));
                    break;
                case "N3":
                    person.Address = string.Join(" ", new[] { s[1].Trim(), s[2].Trim() }.Where(x => x.Length > 0));
                    break;
                case "N4":
                    person.Address = string.Join(", ", new[] { person.Address, s[1].Trim(), $"{s[2].Trim()} {s[3].Trim()}".Trim() }.Where(x => x.Length > 0));
                    break;
                case "DMG":
                    if (s[1].Trim() == "D8") person.BirthDate = Date(s[2]);
                    person.Gender = s[3].Trim();
                    break;
                case "INS":
                    person.Relationship = s[2].Trim();
                    break;
                case "DTP" when DateOf(s) is { } date:
                    person.Dates.Add(date);
                    break;
                case "AAA":
                    person.Rejections.Add(Reject(s, person.IsDependent ? "the dependent" : "the subscriber"));
                    break;
            }
        }
        if (result.Payer == null) problems.Add($"Transaction {t.Control} has no payer (2100A).");
        if (result.IsResponse ? result.Payer?.Rejections.Count == 0 && result.Provider == null : result.Provider == null)
            problems.Add($"Transaction {t.Control} has no provider (2100B).");
        return result;
    }

    private static Rejection Reject(Segment s, string where) => new(s[1].Trim() == "Y", s[3].Trim(), s[4].Trim(), where, s);

    private static Contact Contact(Segment s)
    {
        var numbers = new List<(string, string)>();
        for (var i = 3; i + 1 <= 8; i += 2)
            if (s[i].Trim().Length > 0) numbers.Add((s[i].Trim(), s[i + 1].Trim()));
        return new Contact(s[2].Trim(), numbers);
    }

    private static string OrgName(Segment s) => s[2].Trim() == "1" ? PersonName(s) : s[3].Trim();

    private static string PersonName(Segment s) => string.Join(" ", new[] { s[4].Trim(), s[5].Trim(), s[3].Trim() }.Where(x => x.Length > 0));

    private static IReadOnlyList<string> Split(string value, char? repeat) =>
        value.Trim().Length == 0 ? [] : repeat is { } r ? [.. value.Split(r).Select(v => v.Trim()).Where(v => v.Length > 0)] : [value.Trim()];

    private static string Part(string composite, char separator, int index)
    {
        var parts = composite.Split(separator);
        return index < parts.Length ? parts[index].Trim() : "";
    }

    internal static decimal? Number(string value) =>
        decimal.TryParse(value.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var n) ? n : null;

    internal static DateOnly? Date(string value) =>
        DateOnly.TryParseExact(value.Trim(), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    private static TimeOnly Time(string value) =>
        TimeOnly.TryParseExact(value.Trim().PadRight(4, '0')[..4], "HHmm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : TimeOnly.MinValue;

    /// <summary>A DTP's date (D8) or range (RD8), or null when it's in neither.</summary>
    private static DateEntry? DateOf(Segment s)
    {
        var q = s[1].Trim();
        switch (s[2].Trim())
        {
            case "D8" when Date(s[3]) is { } d:
                return new DateEntry(q, d, null, s);
            case "RD8":
                var parts = s[3].Split('-');
                if (parts.Length == 2 && Date(parts[0]) is { } from && Date(parts[1]) is { } to) return new DateEntry(q, from, to, s);
                return null;
            default:
                return null;
        }
    }
}
