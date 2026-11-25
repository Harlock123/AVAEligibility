using AVAEligibility.Core.Eligibility;
using AVAEligibility.Core.X12;

namespace AVAEligibility.Core.Authorizations;

/// <summary>
/// The 278's own code lists (005010X217), worded as the implementation guide words them: the review's action (HCR01),
/// the request category (UM01) and the certification type (UM02). The decision reason codes (HCR03) are an external list
/// X12 keeps; they're shown as codes. AAA rejection reasons use the same codes as the 271's and are worded from that list.
/// </summary>
public static class AuthCodes
{
    public static readonly IReadOnlyDictionary<string, string> Action = new Dictionary<string, string>
    {
        ["A1"] = "Certified in total",
        ["A2"] = "Certified - partial",
        ["A3"] = "Not Certified",
        ["A4"] = "Pended",
        ["A6"] = "Modified",
        ["C"] = "Cancelled",
        ["CT"] = "Contact Payer",
        ["NA"] = "No Action Required",
    };

    public static readonly IReadOnlyDictionary<string, string> Category = new Dictionary<string, string>
    {
        ["AR"] = "Admission Review",
        ["HS"] = "Health Services Review",
        ["SC"] = "Specialty Care Review",
    };

    public static readonly IReadOnlyDictionary<string, string> CertificationType = new Dictionary<string, string>
    {
        ["1"] = "Appeal - Immediate",
        ["2"] = "Appeal - Standard",
        ["3"] = "Cancel",
        ["4"] = "Extension",
        ["I"] = "Initial",
        ["R"] = "Renewal",
        ["S"] = "Revised",
    };

    public static string Say(IReadOnlyDictionary<string, string> list, string code) =>
        code.Length == 0 ? "" : list.TryGetValue(code, out var w) ? w : $"code {code} (not in the list)";
}

/// <summary>A 278 file: its requests or answers, one review (patient event) each.</summary>
public sealed class AuthFile
{
    public required string Name { get; init; }
    public string? Path { get; init; }
    public required X12File X12 { get; init; }
    public List<AuthReview> Reviews { get; } = [];
    public List<string> Problems { get; } = [];
    public bool IsResponse => Reviews.Count > 0 && Reviews.All(r => r.IsResponse);
}

/// <summary>A service asked about (SV1, SV2 or SV3 in a 2000F loop), with the decision on it when the answer gives one.</summary>
public sealed record AuthService(string Procedure, IReadOnlyList<string> Modifiers, decimal? Charge, decimal? Units, DateOnly? Date, string Action, string Number, IReadOnlyList<Segment> Segments)
{
    public string Code => string.Join('-', new[] { Procedure }.Concat(Modifiers));
}

/// <summary>
/// One review in a 278: who's asked (the UMO), who asks (the requester), the patient, the event (category, type,
/// service type, place, date, diagnoses, the provider who'll do it), its services, and in an answer the decision: the
/// action (HCR01), the review number to put on the claim (HCR02), the reason (HCR03), the dates the certification runs,
/// and any AAA rejection. Every part keeps the segments behind it.
/// </summary>
public sealed class AuthReview
{
    public required Transaction Source { get; init; }
    public bool IsResponse { get; init; }
    public DateTime? Created { get; init; }
    public string Reference { get; init; } = "";

    public string UmoName { get; set; } = "";
    public string UmoId { get; set; } = "";
    public string RequesterName { get; set; } = "";
    public string RequesterId { get; set; } = "";

    public string Last { get; set; } = "";
    public string First { get; set; } = "";
    public string MemberId { get; set; } = "";
    public DateOnly? BirthDate { get; set; }

    /// <summary>The patient, when not the subscriber (2010D).</summary>
    public string PatientLast { get; set; } = "";
    public string PatientFirst { get; set; } = "";
    public DateOnly? PatientBirthDate { get; set; }
    public bool IsDependent => PatientLast.Length > 0;
    public string WhoLast => IsDependent ? PatientLast : Last;
    public string WhoFirst => IsDependent ? PatientFirst : First;
    public string SortName => WhoFirst.Length > 0 ? $"{WhoLast}, {WhoFirst}" : WhoLast;

    public string Trace { get; set; } = "";
    public string TraceOriginator { get; set; } = "";
    public string Category { get; set; } = "";
    public string CertificationType { get; set; } = "";
    public string ServiceType { get; set; } = "";
    public string Facility { get; set; } = "";
    public DateOnly? EventDate { get; set; }
    public DateOnly? EventEnd { get; set; }
    public List<string> Diagnoses { get; } = [];
    public string ProviderName { get; set; } = "";
    public string ProviderNpi { get; set; } = "";
    public List<AuthService> Services { get; } = [];

    public string Action { get; set; } = "";
    public string Number { get; set; } = "";
    public string Reason { get; set; } = "";
    public DateOnly? Issued { get; set; }
    public DateOnly? Effective { get; set; }
    public DateOnly? Expires { get; set; }
    public List<Rejection> Rejections { get; } = [];
    public List<Segment> Segments { get; } = [];

    /// <summary>The decision in a word: certified, partly certified, not certified, pended, modified, cancelled, contact the payer, not needed, rejected, or asked (a request).</summary>
    public string Word => Rejections.Count > 0 ? "rejected" : !IsResponse ? "asked" : Action switch
    {
        "A1" => "certified",
        "A2" => "partly certified",
        "A3" => "not certified",
        "A4" => "pended",
        "A6" => "modified",
        "C" => "cancelled",
        "CT" => "contact the payer",
        "NA" => "not needed",
        "" => "no decision",
        _ => $"action {Action}",
    };

    /// <summary>"2026-12-02 to 2027-01-01", or what of it the answer gave.</summary>
    public string Runs => (Effective, Expires) switch
    {
        ({ } a, { } b) => $"{a:yyyy-MM-dd} to {b:yyyy-MM-dd}",
        ({ } a, null) => $"from {a:yyyy-MM-dd}",
        (null, { } b) => $"until {b:yyyy-MM-dd}",
        _ => "",
    };
}
