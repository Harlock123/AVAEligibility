using System.Globalization;

namespace AVAEligibility.Core.Eligibility;

/// <summary>
/// The 270's and 271's lines in plain words, the same in the window, the reports and what agents read: a benefit as
/// "Co-Payment: $25.00 per visit, Professional (Physician) Visit - Office, in network".
/// </summary>
public static class Words
{
    private static readonly CultureInfo Us = CultureInfo.GetCultureInfo("en-US");

    public static string Money(decimal amount) => amount.ToString("C2", Us);

    /// <summary>EB08 is a fraction: .2 is 20%.</summary>
    public static string Percent(decimal fraction) => (fraction * 100).ToString("0.##", CultureInfo.InvariantCulture) + "%";

    /// <summary>An amount with its period: "$25.00 per visit", "$1,120.00 remaining".</summary>
    public static string WithPeriod(string amount, string period) => period switch
    {
        "" => amount,
        "6" => $"{amount} per hour",
        "7" => $"{amount} per day",
        "13" => $"{amount} per 24 hours",
        "21" => $"{amount} per year",
        "22" => $"{amount} per service year",
        "23" => $"{amount} per calendar year",
        "24" => $"{amount} year to date",
        "25" => $"{amount} for the contract",
        "26" => $"{amount} per episode",
        "27" => $"{amount} per visit",
        "28" => $"{amount} outlier",
        "29" => $"{amount} remaining",
        "30" => $"{amount} exceeded",
        "31" => $"{amount} not exceeded",
        "32" => $"{amount} lifetime",
        "33" => $"{amount} lifetime remaining",
        "34" => $"{amount} per month",
        "35" => $"{amount} per week",
        "36" => $"{amount} per admission",
        _ => $"{amount} ({Codes.TimePeriod.Say(period)})",
    };

    /// <summary>EB12 in words.</summary>
    public static string Network(string code) => code switch
    {
        "Y" => "in network",
        "N" => "out of network",
        "W" => "in or out of network",
        "U" => "network unknown",
        _ => "",
    };

    /// <summary>One benefit line, all it says, in words.</summary>
    public static string Describe(Benefit b)
    {
        var parts = new List<string>();
        var value = new List<string>();
        if (b.Amount is { } a) value.Add(WithPeriod(Money(a), b.Period));
        if (b.Percent is { } p) value.Add(WithPeriod(Percent(p), b.Amount == null ? b.Period : ""));
        if (b.Quantity is { } q) value.Add(WithPeriod($"{q.ToString("0.##", CultureInfo.InvariantCulture)} {QuantityWord(b.QuantityQualifier, q)}", b.Amount == null && b.Percent == null ? b.Period : ""));
        if (value.Count == 0 && b.Period.Length > 0) value.Add(Codes.TimePeriod.Say(b.Period).ToLowerInvariant());
        parts.Add(value.Count > 0 ? $"{b.What}: {string.Join(", ", value)}" : b.What);
        if (b.ServiceTypes.Count > 0) parts.Add(b.Services);
        if (b.CoverageLevel is { Length: > 0 } level and not "IND") parts.Add(Codes.CoverageLevel.Say(level).ToLowerInvariant());
        if (Network(b.InNetwork) is { Length: > 0 } n) parts.Add(n);
        if (b.Authorization == "Y") parts.Add("authorization required");
        else if (b.Authorization == "N") parts.Add("no authorization needed");
        if (b.Procedure.Length > 0) parts.Add($"procedure {b.Procedure}");
        if (b.InsuranceType.Length > 0 && b.Information is "1" or "2" or "3" or "4" or "5" or "6" or "7" or "8" or "R") parts.Add(Codes.InsuranceType.Say(b.InsuranceType));
        if (b.PlanDescription.Length > 0) parts.Add($"\"{b.PlanDescription}\"");
        return string.Join(", ", parts);
    }

    private static string QuantityWord(string qualifier, decimal q) => qualifier switch
    {
        "VS" => q == 1 ? "visit" : "visits",
        "DY" => q == 1 ? "day" : "days",
        "HS" => q == 1 ? "hour" : "hours",
        "MN" => q == 1 ? "month" : "months",
        "YY" => q == 1 ? "year" : "years",
        "FL" => q == 1 ? "unit" : "units",
        "" => "",
        _ => Codes.Quantity.Say(qualifier).ToLowerInvariant(),
    };

    /// <summary>A rejection: "Invalid/Missing Subscriber/Insured ID (72). Please Correct and Resubmit."</summary>
    public static string Describe(Rejection r) =>
        $"{r.Meaning} ({r.Reason})" + (r.WhatToDo.Length > 0 ? $". {r.WhatToDo}." : ".");

    /// <summary>A 270's question: "Health Benefit Plan Coverage; Professional (Physician) Visit - Office".</summary>
    public static string Describe(Inquiry q) =>
        (q.ServiceTypes.Count > 0 ? string.Join("; ", q.ServiceTypes.Select(s => Codes.ServiceType.Say(s))) : "no service type")
        + (q.Procedure.Length > 0 ? $", procedure {q.Procedure.Replace(':', ' ')}" : "");

    /// <summary>A related entity: "Primary Payer: MEDICARE PART B (PI MCRB1), TE 8005550147".</summary>
    public static string Describe(Party p) =>
        $"{p.Role}: {p.Name}" + (p.Id.Length > 0 ? $" ({p.IdQualifier} {p.Id})" : "")
        + string.Concat(p.Contacts.SelectMany(c => c.Numbers).Select(n => $", {n.Kind} {n.Number}"));
}
