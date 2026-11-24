using System.Globalization;

namespace AVAEligibility.Core.Inquiries;

/// <summary>
/// The service types to ask about for a visit: always 30 (health benefit plan coverage, which every payer must answer),
/// and the kind of care its procedure code falls in, so the answer carries that care's copay, limits and authorization.
/// The ranges are the CPT and HCPCS sections the codes belong to (office visits, preventive visits, emergency, mental
/// health, MRI and CT, other imaging, lab, physical medicine, chiropractic, vision exams, immunizations, surgery, dental);
/// a code outside them asks about plan coverage alone. Payers' companion guides can say otherwise.
/// </summary>
public static class ServiceTypes
{
    private static readonly (int From, int To)[] MriAndCt =
    [
        (70336, 70336), (70450, 70498), (70540, 70559), (71250, 71275), (71550, 71555), (72125, 72133), (72141, 72159),
        (72191, 72197), (73200, 73206), (73218, 73225), (73700, 73706), (73718, 73725), (74150, 74185), (74261, 74263),
        (75557, 75574), (77046, 77049),
    ];

    /// <summary>The service types to ask about, 30 first.</summary>
    public static IReadOnlyList<string> For(string procedure)
    {
        var care = Care(procedure);
        return care == null ? ["30"] : ["30", care];
    }

    /// <summary>The kind of care a procedure is, as a service type, or null when it's none of the sections known.</summary>
    public static string? Care(string procedure)
    {
        var code = procedure.Trim().ToUpperInvariant();
        if (code.Length == 0) return null;
        if (code.StartsWith('D') && code.Length == 5 && int.TryParse(code[1..], out _)) return "35";
        if (code is "S9083" or "S9088") return "UC";
        if (code.Length != 5 || !int.TryParse(code, NumberStyles.None, CultureInfo.InvariantCulture, out var n)) return null;
        return n switch
        {
            >= 99202 and <= 99215 => "98",
            >= 99381 and <= 99397 => "BZ",
            >= 99281 and <= 99285 => "86",
            >= 90785 and <= 90899 => "MH",
            >= 70010 and <= 79999 => MriAndCt.Any(r => n >= r.From && n <= r.To) ? "62" : "4",
            >= 80047 and <= 89398 => "5",
            >= 97010 and <= 97799 => "PT",
            >= 98940 and <= 98943 => "33",
            >= 92002 and <= 92014 => "AL",
            >= 90460 and <= 90749 => "80",
            >= 10004 and <= 69990 => "2",
            _ => null,
        };
    }
}
