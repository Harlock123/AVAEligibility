using AVAEligibility.Core.X12;

namespace AVAEligibility.Core.Eligibility;

/// <summary>Where a person stands: ready for the visit, something to check, something to do first, or no answer.</summary>
public enum Verdict
{
    /// <summary>Covered, nothing in the way.</summary>
    Ready,

    /// <summary>Covered, with something to look at: an authorization for some services, a limit nearly used, another payer.</summary>
    Check,

    /// <summary>Something to do before the visit: coverage isn't active, another payer is primary, a referral, an authorization.</summary>
    Act,

    /// <summary>The payer couldn't answer about this person: fix the inquiry and ask again.</summary>
    Rejected,

    /// <summary>No answer: the payer sent nothing about this person, or couldn't answer at all.</summary>
    NoAnswer,

    /// <summary>A 270's question, not yet answered.</summary>
    Asked,
}

/// <summary>One thing the answer says that matters, what to do about it, and the segments it rests on.</summary>
public sealed record Note(Verdict Level, string Text, string Fix, IReadOnlyList<Segment> Segments);

/// <summary>What the patient pays for a kind of care: a copay or coinsurance, in network and out.</summary>
public sealed record CostRow(string Service, string Kind, string InNetwork, string OutOfNetwork, string Either, IReadOnlyList<string> Messages, IReadOnlyList<Segment> Segments)
{
    public override string ToString() =>
        $"{Service}: {Kind} " + string.Join(", ", new[] { InNetwork.Length > 0 ? $"{InNetwork} in network" : "", OutOfNetwork.Length > 0 ? $"{OutOfNetwork} out of network" : "", Either.Length > 0 ? Either : "" }.Where(x => x.Length > 0));
}

/// <summary>A deductible or out-of-pocket maximum: the total for its period, and what's left.</summary>
public sealed record Accumulator(string Kind, string Level, string Network, decimal? Total, decimal? Remaining, decimal? Met, string Period, IReadOnlyList<Segment> Segments)
{
    /// <summary>"Deductible, individual, in network: $1,500.00 per calendar year, $1,120.00 left ($380.00 met)".</summary>
    public override string ToString()
    {
        var head = string.Join(", ", new[] { Kind, Level, Network }.Where(x => x.Length > 0));
        var parts = new List<string>();
        if (Total is { } t) parts.Add(Words.WithPeriod(Words.Money(t), Period));
        if (Remaining is { } r) parts.Add($"{Words.Money(r)} left");
        if ((Met ?? (Total - Remaining)) is { } m && m >= 0) parts.Add($"{Words.Money(m)} met");
        return $"{head}: {string.Join(", ", parts)}";
    }
}

/// <summary>A limit on visits, days or dollars, and what's left of it.</summary>
public sealed record Limit(string Service, string Unit, decimal? Total, decimal? Remaining, string Period, IReadOnlyList<Segment> Segments)
{
    public override string ToString()
    {
        static string N(decimal d) => d.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        var unit = Unit.Length > 0 ? " " + Unit : "";
        var period = Period.Length > 0 ? Words.WithPeriod("", Period).Trim() : "";
        return (Total, Remaining) switch
        {
            ({ } t, { } r) => $"{Service}: {N(r)} of {N(t)}{unit} left" + (period.Length > 0 ? $" ({period})" : ""),
            ({ } t, null) => $"{Service}: {N(t)}{unit}" + (period.Length > 0 ? $" {period}" : ""),
            (null, { } r) => $"{Service}: {N(r)}{unit} left",
            _ => $"{Service}: limited",
        };
    }
}

/// <summary>Another payer the answer names (EB01 R), and whether it comes first.</summary>
public sealed record OtherPayer(string Name, string Order, string InsuranceType, string PolicyNumber, DateOnly? Since, IReadOnlyList<Segment> Segments)
{
    public override string ToString() =>
        $"{(Name.Length > 0 ? Name : "Another payer")}" + (Order.Length > 0 ? $" ({Order.ToLowerInvariant()})" : "")
        + (InsuranceType.Length > 0 ? $", {Codes.InsuranceType.Say(InsuranceType)}" : "")
        + (PolicyNumber.Length > 0 ? $", policy {PolicyNumber}" : "") + (Since is { } d ? $", since {d:yyyy-MM-dd}" : "");
}

/// <summary>A service that needs authorization, and whom to call.</summary>
public sealed record Authorization(string Service, string Contact, IReadOnlyList<string> Messages, IReadOnlyList<Segment> Segments);

/// <summary>
/// A 271's answer about one person, made sense of: the verdict, the notes behind it (each with what to do), and the
/// benefits grouped the way a front desk reads them. Nothing here is guessed: a line the payer didn't send isn't
/// filled in, and the notes say which segments they rest on.
/// </summary>
public sealed class Interpretation
{
    public required Person Person { get; init; }
    public Verdict Verdict { get; internal set; }
    public List<Note> Notes { get; } = [];

    /// <summary>True active, false inactive, null when the answer doesn't say.</summary>
    public bool? Active { get; internal set; }

    public string Plan { get; internal set; } = "";
    public string InsuranceType { get; internal set; } = "";

    /// <summary>When coverage begins and ends, from the plan or eligibility dates.</summary>
    public DateOnly? Begins { get; internal set; }
    public DateOnly? Ends { get; internal set; }

    public List<CostRow> Costs { get; } = [];
    public List<Accumulator> Accumulators { get; } = [];
    public List<Limit> Limits { get; } = [];
    public List<Authorization> Authorizations { get; } = [];
    public List<OtherPayer> OtherPayers { get; } = [];
    public List<string> NotCovered { get; } = [];

    /// <summary>The primary care provider the payer has on file, with their ID.</summary>
    public Party? PrimaryCareProvider { get; internal set; }

    /// <summary>The payer's own words (MSG) that aren't attached to a line shown elsewhere.</summary>
    public List<string> Messages { get; } = [];

    /// <summary>The verdict in a word, as the list shows it.</summary>
    public string Word => Verdict switch
    {
        Verdict.Ready => "ready",
        Verdict.Check => "check",
        Verdict.Act => "act first",
        Verdict.Rejected => "rejected",
        Verdict.NoAnswer => "no answer",
        _ => "asked",
    };

    /// <summary>One line: the verdict and the note that decided it.</summary>
    public string Headline => Notes.Where(n => n.Level == Verdict).Select(n => n.Text).FirstOrDefault()
        ?? (Active == true ? $"Active{(Plan.Length > 0 ? $": {Plan}" : "")}" : Word);
}

/// <summary>Turns a person's answer into an <see cref="Interpretation"/>.</summary>
public static class Interpreter
{
    private static readonly HashSet<string> ActiveCodes = ["1", "2", "3", "4", "5"];
    private static readonly HashSet<string> InactiveCodes = ["6", "7", "8"];

    /// <summary>
    /// Reads a person's answer. <paramref name="on"/> is the date of service, when it's known: coverage that ends before
    /// it, or begins after, is a problem; without it, the answer's own dates are only shown.
    /// </summary>
    public static Interpretation Read(Person p, DateOnly? on = null)
    {
        var r = new Interpretation { Person = p };
        if (!p.Transaction.IsResponse)
        {
            r.Verdict = Verdict.Asked;
            return r;
        }

        foreach (var x in p.Transaction.Payer?.Rejections ?? []) r.Notes.Add(Reject(x));
        foreach (var x in p.Transaction.Provider?.Rejections ?? []) r.Notes.Add(Reject(x));
        foreach (var x in p.Rejections) r.Notes.Add(Reject(x));
        if (p.Benefits.Count == 0 && r.Notes.Count == 0)
            r.Notes.Add(new Note(Verdict.NoAnswer, "The payer sent no benefits for this person.", "Ask again, or call the payer.", p.Segments));

        ReadCoverage(p, r, on);
        ReadOtherPayers(p, r);
        ReadMoney(p, r);
        ReadLimits(p, r);
        ReadAuthorizations(p, r);
        foreach (var b in p.Benefits)
        {
            switch (b.Information)
            {
                case "L":
                    r.PrimaryCareProvider = b.Entities.FirstOrDefault(e => e.Entity == "P3") ?? b.Entities.FirstOrDefault();
                    break;
                case "I":
                    r.NotCovered.Add(b.ServiceTypes.Count > 0 ? b.Services : b.Procedure.Length > 0 ? $"procedure {b.Procedure}" : "(not said)");
                    break;
                case "U":
                    var who = b.Entities.FirstOrDefault();
                    r.Notes.Add(new Note(Verdict.Check, $"The payer says to ask {(who != null ? Words.Describe(who) : "another entity")} about {(b.ServiceTypes.Count > 0 ? b.Services : "these benefits")}.",
                        "Call them before the visit.", b.Segments));
                    break;
                case "V":
                    r.Notes.Add(new Note(Verdict.Check, $"The payer couldn't process the question about {(b.ServiceTypes.Count > 0 ? b.Services : "these benefits")}.", "Ask about that service again, or call the payer.", b.Segments));
                    break;
            }
            if (b.Information is "1" or "2" or "3" or "4" or "5" or "6" or "7" or "8" or "L" or "R" or "D" && b.Messages.Count > 0 && !r.Authorizations.Any(a => a.Segments.Contains(b.Segment)))
                r.Messages.AddRange(b.Messages);
        }

        r.Verdict = r.Notes.Count == 0 ? Verdict.Ready : r.Notes.Max(n => n.Level);
        return r;
    }

    private static void ReadCoverage(Person p, Interpretation r, DateOnly? on)
    {
        var plan = p.Benefits.Where(b => b.ServiceTypes.Count == 0 || b.ServiceTypes.Contains("30")).ToList();
        var active = plan.FirstOrDefault(b => ActiveCodes.Contains(b.Information));
        var inactive = plan.FirstOrDefault(b => InactiveCodes.Contains(b.Information));
        var main = active ?? inactive;
        if (main != null)
        {
            r.Active = active != null;
            r.Plan = main.PlanDescription;
            r.InsuranceType = main.InsuranceType;
        }
        var dates = p.Dates.Concat(main?.Dates ?? []).ToList();
        r.Begins = dates.Where(d => d.Qualifier is "346" or "356" or "348" or "539").Select(d => (DateOnly?)d.From).Min()
            ?? dates.Where(d => d.Qualifier is "291" or "307" && d.To != null).Select(d => (DateOnly?)d.From).Min();
        r.Ends = dates.Where(d => d.Qualifier is "347" or "357" or "349" or "540").Select(d => (DateOnly?)d.From).Max()
            ?? dates.Where(d => d.Qualifier is "291" or "307" && d.To != null).Select(d => d.To).Max();

        if (active == null && inactive != null)
            r.Notes.Add(new Note(Verdict.Act,
                r.Ends is { } end ? $"Coverage ended {end:yyyy-MM-dd}." : "Coverage isn't active.",
                "Ask the patient for their current insurance before the visit; without it, the visit is self-pay.",
                [inactive.Segment, .. dates.Where(d => d.Qualifier is "347" or "357").Select(d => d.Segment)]));
        else if (active != null && on is { } day)
        {
            if (r.Ends is { } end && day > end)
                r.Notes.Add(new Note(Verdict.Act, $"Coverage ends {end:yyyy-MM-dd}, before the visit on {day:yyyy-MM-dd}.", "Ask the patient for the plan that covers the visit.", [.. dates.Select(d => d.Segment)]));
            if (r.Begins is { } begin && day < begin)
                r.Notes.Add(new Note(Verdict.Act, $"Coverage begins {begin:yyyy-MM-dd}, after the visit on {day:yyyy-MM-dd}.", "Ask the patient for the plan that covers the visit.", [.. dates.Select(d => d.Segment)]));
        }
        if (active != null && active.Information == "5")
            r.Notes.Add(new Note(Verdict.Check, "Coverage is active, pending the payer's investigation.", "Confirm with the payer before a costly service.", [active.Segment]));
    }

    private static void ReadOtherPayers(Person p, Interpretation r)
    {
        foreach (var b in p.Benefits.Where(b => b.Information == "R"))
        {
            var e = b.Entities.FirstOrDefault(x => x.Entity is "PRP" or "SEP" or "TTP") ?? b.Entities.FirstOrDefault();
            var order = e?.Entity switch { "PRP" => "Primary", "SEP" => "Secondary", "TTP" => "Tertiary", _ => "" };
            var other = new OtherPayer(e?.Name ?? "", order, b.InsuranceType, b.References.FirstOrDefault(x => x.Qualifier is "IG" or "18" or "1L" or "6P")?.Value ?? "",
                b.Dates.FirstOrDefault(d => d.Qualifier == "290")?.From, b.Segments);
            r.OtherPayers.Add(other);
            var name = other.Name.Length > 0 ? other.Name : "Another payer";
            r.Notes.Add(order switch
            {
                "Primary" => new Note(Verdict.Act, $"{name} is primary; this plan pays second.", $"Bill {name} first, then this plan with its remittance. Check the patient's {name} coverage too.", b.Segments),
                "Secondary" or "Tertiary" => new Note(Verdict.Check, $"{name} is {order.ToLowerInvariant()}; this plan pays first.", $"After this plan pays, bill {name}.", b.Segments),
                _ => new Note(Verdict.Check, $"{name} also covers this person, and the payer didn't say which pays first.", "Ask the patient which plan is primary (coordination of benefits) before billing.", b.Segments),
            });
        }
    }

    private static string Value(Benefit b) =>
        b.Amount is { } a ? Words.WithPeriod(Words.Money(a), b.Period == "27" ? "" : b.Period) : b.Percent is { } pc ? Words.Percent(pc) : "";

    private static void ReadMoney(Person p, Interpretation r)
    {
        // Copays and coinsurance, a row per kind of care, in and out of network side by side.
        foreach (var g in p.Benefits.Where(b => b.Information is "A" or "B" && (b.Amount != null || b.Percent != null))
                     .GroupBy(b => (Service: b.ServiceTypes.Count > 0 ? b.Services : "All services", Kind: b.Information == "B" ? "copay" : "coinsurance", b.CoverageLevel)))
        {
            string Of(string network) => string.Join(" or ", g.Where(b => b.InNetwork == network).Select(Value).Distinct());
            var either = string.Join(" or ", g.Where(b => b.InNetwork is not ("Y" or "N")).Select(b => Value(b) + (Words.Network(b.InNetwork) is { Length: > 0 } n && b.InNetwork != "U" ? $" {n}" : "")).Distinct());
            r.Costs.Add(new CostRow(g.Key.Service + (g.Key.CoverageLevel is { Length: > 0 } l and not "IND" ? $" ({Codes.CoverageLevel.Say(l).ToLowerInvariant()})" : ""), g.Key.Kind,
                Of("Y"), Of("N"), either, [.. g.SelectMany(b => b.Messages)], [.. g.SelectMany(b => b.Segments)]));
        }

        // Deductibles and out-of-pocket maximums: totals paired with what's left, by level and network.
        foreach (var g in p.Benefits.Where(b => b.Information is "C" or "G" && b.Amount != null)
                     .GroupBy(b => (Kind: b.Information == "C" ? "Deductible" : "Out-of-pocket maximum", Level: b.CoverageLevel is "FAM" ? "family" : b.CoverageLevel is "IND" or "" ? "individual" : Codes.CoverageLevel.Say(b.CoverageLevel).ToLowerInvariant(), b.InNetwork)))
        {
            var total = g.FirstOrDefault(b => b.Period is not ("29" or "33" or "24"));
            var left = g.FirstOrDefault(b => b.Period is "29" or "33");
            var met = g.FirstOrDefault(b => b.Period == "24");
            r.Accumulators.Add(new Accumulator(g.Key.Kind, g.Key.Level, Words.Network(g.Key.InNetwork), total?.Amount, left?.Amount, met?.Amount, total?.Period ?? "",
                [.. g.SelectMany(b => b.Segments)]));
        }
    }

    private static void ReadLimits(Person p, Interpretation r)
    {
        foreach (var g in p.Benefits.Where(b => b.Information == "F" && (b.Quantity != null || b.Amount != null))
                     .GroupBy(b => (Service: b.ServiceTypes.Count > 0 ? b.Services : "All services", Unit: b.Quantity != null ? Unit(b.QuantityQualifier) : "dollars")))
        {
            decimal? Of(Benefit? b) => b == null ? null : b.Quantity ?? b.Amount;
            var total = g.FirstOrDefault(b => b.Period is not ("29" or "33"));
            var left = g.FirstOrDefault(b => b.Period is "29" or "33");
            var limit = new Limit(g.Key.Service, g.Key.Unit, Of(total), Of(left), total?.Period ?? "", [.. g.SelectMany(b => b.Segments)]);
            r.Limits.Add(limit);
            if (limit.Remaining is { } rem && rem <= 0)
                r.Notes.Add(new Note(Verdict.Act, $"{limit.Service}: none left ({limit}).", "The plan won't pay for more this period: tell the patient, or ask the payer about an exception.", limit.Segments));
            else if (limit.Remaining is { } few && few <= 2)
                r.Notes.Add(new Note(Verdict.Check, $"{limit}.", "Tell the patient how few are left.", limit.Segments));
        }
    }

    private static string Unit(string qualifier) => qualifier switch
    {
        "VS" => "visits", "DY" => "days", "HS" => "hours", "MN" => "months", "YY" => "years", "FL" => "units", "P6" => "services",
        _ => Codes.Quantity.Meaning(qualifier)?.ToLowerInvariant() ?? "",
    };

    private static void ReadAuthorizations(Person p, Interpretation r)
    {
        foreach (var b in p.Benefits.Where(b => b.Authorization == "Y"))
        {
            var who = b.Entities.FirstOrDefault();
            var contact = who == null ? "" : who.Name + string.Concat(who.Contacts.SelectMany(c => c.Numbers).Where(n => n.Kind == "TE").Select(n => $" at {Phone(n.Number)}"));
            var service = b.ServiceTypes.Count > 0 ? b.Services : b.Procedure.Length > 0 ? $"procedure {b.Procedure}" : "Some services";
            r.Authorizations.Add(new Authorization(service, contact, b.Messages, b.Segments));
            r.Notes.Add(new Note(Verdict.Check, $"{service} needs authorization before the service.",
                contact.Length > 0 ? $"Get it from {contact} before scheduling it." : "Get it from the payer before scheduling it.", b.Segments));
        }
    }

    /// <summary>A ten-digit phone number as 800-555-0147.</summary>
    public static string Phone(string digits) =>
        digits.Length == 10 && digits.All(char.IsDigit) ? $"{digits[..3]}-{digits[3..6]}-{digits[6..]}" : digits;

    private static Note Reject(Rejection x)
    {
        var text = $"The payer couldn't answer about {x.Where}: {x.Meaning} ({x.Reason}).";
        var fix = Fix(x.Reason) + (x.FollowUp switch
        {
            "N" or "S" or "Y" => $" The payer says: {x.WhatToDo}.",
            "W" or "X" => $" The payer says: {x.WhatToDo}.",
            _ => "",
        });
        return new Note(x.Reason == "42" || x.Reason == "80" ? Verdict.NoAnswer : Verdict.Rejected, text, fix, [x.Segment]);
    }

    /// <summary>What to do about a rejection reason, in plain words.</summary>
    public static string Fix(string reason) => reason switch
    {
        "15" => "Something the payer needs is missing: check the inquiry has the member ID, name, birth date and date of service.",
        "41" => "The payer won't answer this provider: check the provider is set up with the payer for eligibility inquiries.",
        "42" => "The payer's system couldn't answer just now. Ask again later.",
        "43" or "44" or "45" or "46" or "47" or "97" => "Check the provider's NPI, name and address as the payer has them on file.",
        "48" => "Check the referring provider's NPI.",
        "49" => "The payer says this provider isn't the patient's primary care provider: a referral may be needed.",
        "50" or "51" => "The payer has no record of this provider for inquiries: check the NPI, or enroll with the payer.",
        "52" => "The provider isn't enrolled with the plan for those dates.",
        "56" or "57" or "62" or "63" => "Check the date of service: payers answer only for dates in their window (usually not far ahead or back).",
        "58" or "60" or "71" => "Check the birth date with the patient and against the card, then ask again.",
        "61" => "The payer has a date of death before the service: call the payer.",
        "64" or "72" => "Check the member ID against the card: a letter O for a zero, a missing prefix or suffix, a dependent's suffix.",
        "65" or "73" => "Check the name exactly as the card prints it: no nicknames, hyphens and suffixes as on the card.",
        "66" or "74" => "Check the patient's gender as the payer has it.",
        "67" or "75" => "The payer can't find this person: check the payer, member ID, name and birth date, and ask for the patient's current card; they may have a new plan.",
        "68" or "76" => "The payer found more than one match: send the member ID with the name and birth date so it can tell them apart.",
        "77" => "The subscriber was found but not the patient: check the dependent's name and birth date, or send their own member ID if they have one.",
        "78" => "The subscriber isn't in the group or plan named: check the group number and plan.",
        "79" => "Check the patient's identifiers.",
        "80" => "No answer came back: ask again.",
        "T4" => "Add the payer's name and ID to the inquiry.",
        _ => "Correct what the payer's reason names, and ask again.",
    };
}
