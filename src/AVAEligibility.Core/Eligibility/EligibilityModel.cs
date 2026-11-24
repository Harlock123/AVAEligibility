using AVAEligibility.Core.X12;

namespace AVAEligibility.Core.Eligibility;

/// <summary>An eligibility file: a 270 (the questions) or a 271 (the answers), read into its transactions.</summary>
public sealed class EligibilityFile
{
    public required string Name { get; init; }
    public string? Path { get; init; }
    public required X12File X12 { get; init; }
    public required IReadOnlyList<EligibilityTransaction> Transactions { get; init; }

    /// <summary>Problems reading it: a transaction that isn't a 270 or 271, a loop out of place. Never people's details.</summary>
    public required IReadOnlyList<string> Problems { get; init; }

    /// <summary>"270", "271", or "270 and 271" when a file holds both.</summary>
    public string Kind => string.Join(" and ", Transactions.Select(t => t.Code).Distinct().Order());

    public IEnumerable<Person> People => Transactions.SelectMany(t => t.People);
}

/// <summary>One 270 or 271 transaction: one payer, the provider asking, and the people asked about.</summary>
public sealed class EligibilityTransaction
{
    public required Transaction Source { get; init; }

    /// <summary>270 or 271.</summary>
    public string Code => Source.Code;

    public bool IsResponse => Code == "271";

    /// <summary>BHT03, the transaction's reference.</summary>
    public string Reference { get; init; } = "";

    /// <summary>BHT04 and BHT05: when it was made.</summary>
    public DateTime? Created { get; init; }

    /// <summary>The payer (2100A), or null when the loop is missing.</summary>
    public Party? Payer { get; set; }

    /// <summary>The provider asking (2100B).</summary>
    public Party? Provider { get; set; }

    public List<Person> People { get; } = [];
}

/// <summary>An NM1 with what goes with it: the payer, the provider, or an entity a benefit names (2120).</summary>
public sealed class Party
{
    /// <summary>NM101: PR, 1P, P3, PRP…</summary>
    public required string Entity { get; init; }

    public required string Name { get; init; }

    /// <summary>NM108: PI, XX (NPI), FI, MI…</summary>
    public string IdQualifier { get; init; } = "";

    public string Id { get; init; } = "";

    public List<Contact> Contacts { get; } = [];
    public List<Rejection> Rejections { get; } = [];
    public List<Segment> Segments { get; } = [];

    /// <summary>Who this is, in words: "Primary Care Provider".</summary>
    public string Role => Codes.Entity.Meaning(Entity) ?? Entity;
}

/// <summary>A PER: how to reach someone.</summary>
public sealed record Contact(string Name, IReadOnlyList<(string Kind, string Number)> Numbers);

/// <summary>A subscriber (2000C) or a dependent (2000D): who was asked about, and the answer about them.</summary>
public sealed class Person
{
    public required EligibilityTransaction Transaction { get; init; }

    public bool IsDependent { get; init; }

    /// <summary>The subscriber a dependent is under.</summary>
    public Person? Subscriber { get; init; }

    /// <summary>The HL's number, as the file numbers it.</summary>
    public string Hl { get; init; } = "";

    public string Last { get; set; } = "";
    public string First { get; set; } = "";
    public string Middle { get; set; } = "";

    /// <summary>NM109 with NM108 MI: the member ID, as sent (a dependent usually has none).</summary>
    public string MemberId { get; set; } = "";

    public DateOnly? BirthDate { get; set; }

    /// <summary>DMG03: M, F or U.</summary>
    public string Gender { get; set; } = "";

    /// <summary>INS02: how the person is related to the subscriber (18 self, 19 child…).</summary>
    public string Relationship { get; set; } = "";

    public string Address { get; set; } = "";

    public List<Trace> Traces { get; } = [];
    public List<IdReference> References { get; } = [];
    public List<DateEntry> Dates { get; } = [];
    public List<Rejection> Rejections { get; } = [];

    /// <summary>A 270's questions (EQ).</summary>
    public List<Inquiry> Inquiries { get; } = [];

    /// <summary>A 271's answers (EB), in the file's order.</summary>
    public List<Benefit> Benefits { get; } = [];

    /// <summary>Every segment of the person's loops, their benefits' included, in file order.</summary>
    public List<Segment> Segments { get; } = [];

    public string Name => string.Join(" ", new[] { First, Middle, Last }.Where(s => s.Length > 0));

    /// <summary>"LAST, FIRST", as the files and schedules have it.</summary>
    public string SortName => First.Length > 0 ? $"{Last}, {First}" : Last;

    /// <summary>The provider's trace number for this person (TRN01 1 in a 270, 2 in the 271 answering it).</summary>
    public string? Trace => Traces.FirstOrDefault(t => t.Type == (Transaction.IsResponse ? "2" : "1"))?.Number;
}

/// <summary>A TRN: TRN01 (1 the sender's, 2 the one sent back), the number, and who made it.</summary>
public sealed record Trace(string Type, string Number, string Originator);

/// <summary>A REF.</summary>
public sealed record IdReference(string Qualifier, string Value, string Description)
{
    public string Kind => Codes.Reference.Meaning(Qualifier) ?? Qualifier;
}

/// <summary>A DTP: a date or a range of them.</summary>
public sealed record DateEntry(string Qualifier, DateOnly From, DateOnly? To, Segment Segment)
{
    public string Kind => Codes.DateKind.Meaning(Qualifier) ?? Qualifier;

    public override string ToString() => To is { } t && t != From ? $"{From:yyyy-MM-dd} to {t:yyyy-MM-dd}" : $"{From:yyyy-MM-dd}";
}

/// <summary>An AAA: why the payer couldn't answer, at the level it was sent.</summary>
public sealed record Rejection(bool Valid, string Reason, string FollowUp, string Where, Segment Segment)
{
    public string Meaning => Codes.Reject.Say(Reason);
    public string WhatToDo => Codes.FollowUp.Meaning(FollowUp) ?? "";
}

/// <summary>An EQ: what a 270 asks about.</summary>
public sealed record Inquiry(IReadOnlyList<string> ServiceTypes, string Procedure, string CoverageLevel, Segment Segment);

/// <summary>An HSD: how often, or how many, a benefit allows.</summary>
public sealed record Delivery(string QuantityQualifier, decimal? Quantity, string Unit, decimal? SampleSize, string Period, decimal? Periods, Segment Segment);

/// <summary>An EB, with the segments of its loop (2110C or 2110D) and the entities it names (2120).</summary>
public sealed class Benefit
{
    /// <summary>EB01: active, inactive, copay, deductible…</summary>
    public required string Information { get; init; }

    /// <summary>EB02: IND, FAM…</summary>
    public string CoverageLevel { get; init; } = "";

    /// <summary>EB03, its repetitions split.</summary>
    public IReadOnlyList<string> ServiceTypes { get; init; } = [];

    /// <summary>EB04.</summary>
    public string InsuranceType { get; init; } = "";

    /// <summary>EB05: the plan's own words for it.</summary>
    public string PlanDescription { get; init; } = "";

    /// <summary>EB06.</summary>
    public string Period { get; init; } = "";

    /// <summary>EB07: dollars.</summary>
    public decimal? Amount { get; init; }

    /// <summary>EB08: a fraction, .2 for 20%.</summary>
    public decimal? Percent { get; init; }

    /// <summary>EB09 and EB10.</summary>
    public string QuantityQualifier { get; init; } = "";
    public decimal? Quantity { get; init; }

    /// <summary>EB11: Y when authorization or certification is required.</summary>
    public string Authorization { get; init; } = "";

    /// <summary>EB12: Y in network, N out, W either, U unknown.</summary>
    public string InNetwork { get; init; } = "";

    /// <summary>EB13: a procedure, as qualifier and code ("HC", "99213").</summary>
    public string ProcedureQualifier { get; init; } = "";
    public string Procedure { get; init; } = "";

    public required Segment Segment { get; init; }

    public List<Delivery> Deliveries { get; } = [];
    public List<IdReference> References { get; } = [];
    public List<DateEntry> Dates { get; } = [];
    public List<Rejection> Rejections { get; } = [];
    public List<string> Messages { get; } = [];
    public List<Party> Entities { get; } = [];
    public List<Segment> Segments { get; } = [];

    /// <summary>EB01 in words.</summary>
    public string What => Codes.Information.Say(Information);

    /// <summary>EB03 in words, joined.</summary>
    public string Services => string.Join(", ", ServiceTypes.Select(s => Codes.ServiceType.Say(s)));
}
