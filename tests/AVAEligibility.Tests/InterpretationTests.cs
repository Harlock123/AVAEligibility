using AVAEligibility.Core.Eligibility;

namespace AVAEligibility.Tests;

/// <summary>Each sample patient's answer, made sense of: the verdict, the notes and what to do, and the money.</summary>
public sealed class InterpretationTests
{
    private static Interpretation Read(string file, string first, DateOnly? on = null) => Interpreter.Read(Samples.Person(file, first), on);

    [Fact]
    public void Every_sample_patient_gets_the_verdict_planted_for_them()
    {
        var verdicts = new[] { "bluewater-271.x12", "prairie-271.x12", "summit-271.x12" }
            .SelectMany(f => Samples.Load(f).People).ToDictionary(p => p.First, p => Interpreter.Read(p).Verdict);
        Assert.Equal(new Dictionary<string, Verdict>
        {
            ["DANIEL"] = Verdict.Ready, ["EMMA"] = Verdict.Ready, ["GRACE"] = Verdict.Act, ["PETER"] = Verdict.Rejected,
            ["ROSE"] = Verdict.Rejected, ["MARCUS"] = Verdict.Act, ["KENJI"] = Verdict.Check, ["ANA"] = Verdict.Ready,
            ["MATEO"] = Verdict.Ready, ["LOUISE"] = Verdict.Ready, ["SIMONE"] = Verdict.Rejected,
        }, verdicts);
        Assert.Equal(Verdict.Asked, Interpreter.Read(Samples.Person("clinic-270.x12", "DANIEL")).Verdict);
    }

    [Fact]
    public void A_full_ppo_answer_reads_as_copays_deductibles_and_out_of_pocket()
    {
        var r = Read("bluewater-271.x12", "DANIEL");
        Assert.Equal("Active: GOLD PPO 1500", r.Headline);
        Assert.Equal((true, "PR", new DateOnly(2026, 1, 1)), (r.Active, r.InsuranceType, r.Begins));
        Assert.Empty(r.Notes);
        Assert.Equal(
        [
            "Professional (Physician) Visit - Office: copay $25.00 in network, $60.00 out of network",
            "Emergency Services: copay $250.00 in or out of network",
            "Urgent Care: copay $50.00 in network",
            "Health Benefit Plan Coverage: coinsurance 20% in network, 40% out of network",
            "Professional (Physician) Visit - Office: coinsurance 0% in network",
        ], r.Costs.Select(c => c.ToString()));
        Assert.Equal(["OFFICE VISIT COPAY ONLY, DEDUCTIBLE WAIVED"], r.Costs[4].Messages);
        Assert.Equal(
        [
            "Deductible, individual, in network: $1,500.00 per calendar year, $1,120.00 left, $380.00 met",
            "Deductible, family, in network: $3,000.00 per calendar year, $2,400.00 left, $600.00 met",
            "Deductible, individual, out of network: $3,000.00 per calendar year",
            "Out-of-pocket maximum, individual, in network: $6,000.00 per calendar year, $5,210.00 left, $790.00 met",
        ], r.Accumulators.Select(a => a.ToString()));
        Assert.Equal(["Vision (Optometry)"], r.NotCovered);
    }

    [Fact]
    public void Ended_coverage_another_payer_first_and_an_authorization_each_say_what_to_do()
    {
        var grace = Read("bluewater-271.x12", "GRACE");
        Assert.Equal((Verdict.Act, "Coverage ended 2026-10-31."), (grace.Verdict, grace.Headline));
        Assert.Equal("Ask the patient for their current insurance before the visit; without it, the visit is self-pay.", grace.Notes[0].Fix);
        Assert.Equal(["EB", "DTP"], grace.Notes[0].Segments.Select(s => s.Id));

        var marcus = Read("bluewater-271.x12", "MARCUS");
        Assert.Equal("MEDICARE PART B is primary; this plan pays second.", marcus.Headline);
        Assert.Equal("Bill MEDICARE PART B first, then this plan with its remittance. Check the patient's MEDICARE PART B coverage too.", marcus.Notes[0].Fix);
        Assert.Equal("MEDICARE PART B (primary), Medicare Part B, policy 4TQ8NJ2KR63, since 2022-07-01", Assert.Single(marcus.OtherPayers).ToString());

        var kenji = Read("bluewater-271.x12", "KENJI");
        Assert.Equal((Verdict.Check, "MRI/CAT Scan needs authorization before the service."), (kenji.Verdict, kenji.Headline));
        Assert.Equal("Get it from BLUEWATER UTILIZATION REVIEW at 800-555-0147 before scheduling it.", kenji.Notes[0].Fix);
        Assert.Equal("Deductible, individual, in network: $1,500.00 per calendar year, $310.00 left, $1,190.00 met", Assert.Single(kenji.Accumulators).ToString());
    }

    [Fact]
    public void Rejections_say_what_to_check_and_a_payer_that_couldnt_answer_is_no_answer()
    {
        var peter = Read("bluewater-271.x12", "PETER");
        Assert.Equal("The payer couldn't answer about the subscriber: Invalid/Missing Subscriber/Insured ID (72).", peter.Headline);
        Assert.Equal("Check the member ID against the card: a letter O for a zero, a missing prefix or suffix, a dependent's suffix.", peter.Notes[0].Fix);
        Assert.Equal("Check the birth date with the patient and against the card, then ask again.", Read("bluewater-271.x12", "ROSE").Notes[0].Fix);
        Assert.StartsWith("The payer can't find this person", Read("summit-271.x12", "SIMONE").Notes[0].Fix);
        Assert.Equal("The payer's system couldn't answer just now. Ask again later.", Interpreter.Fix("42"));
        Assert.Equal("Correct what the payer's reason names, and ask again.", Interpreter.Fix("ZZ"));
    }

    [Fact]
    public void Medicaid_limits_and_a_pcp_are_read_and_a_visit_date_outside_coverage_is_caught()
    {
        var ana = Read("prairie-271.x12", "ANA");
        Assert.Equal("Mental Health: 8 of 20 visits left (per calendar year)", Assert.Single(ana.Limits).ToString());
        Assert.Equal(["Professional (Physician) Visit - Office: copay $0.00 in network", "Mental Health: copay $0.00 in network"], ana.Costs.Select(c => c.ToString()));

        var mateo = Read("prairie-271.x12", "MATEO");
        Assert.Equal("Primary Care Provider: LUCIA REYES (XX 1888120940), TE 2175550188", Words.Describe(mateo.PrimaryCareProvider!));
        Assert.Equal(["VISITS TO A PROVIDER OTHER THAN THE ASSIGNED PCP REQUIRE A REFERRAL"], mateo.Messages);

        var before = Read("prairie-271.x12", "ANA", new DateOnly(2026, 2, 15));
        Assert.Equal((Verdict.Act, "Coverage begins 2026-03-01, after the visit on 2026-02-15."), (before.Verdict, before.Headline));
        Assert.Equal(Verdict.Ready, Read("prairie-271.x12", "ANA", new DateOnly(2026, 12, 2)).Verdict);
    }

    [Fact]
    public void A_limit_used_up_or_nearly_is_flagged()
    {
        var text = File.ReadAllText(Samples.Path("prairie-271.x12")).Replace("MC**29***VS*8", "MC**29***VS*0");
        var ana = Interpreter.Read(EligibilityReader.Parse(text).People.Single(p => p.First == "ANA"));
        Assert.Equal((Verdict.Act, "Mental Health: none left (Mental Health: 0 of 20 visits left (per calendar year))."), (ana.Verdict, ana.Headline));
        var two = Interpreter.Read(EligibilityReader.Parse(text.Replace("VS*0", "VS*2")).People.Single(p => p.First == "ANA"));
        Assert.Equal((Verdict.Check, "Mental Health: 2 of 20 visits left (per calendar year)."), (two.Verdict, two.Headline));
    }
}
