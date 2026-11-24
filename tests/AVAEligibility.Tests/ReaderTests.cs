using AVAEligibility.Core.Eligibility;

namespace AVAEligibility.Tests;

/// <summary>The samples read as their guide lays them out: payers, providers, people, questions, answers.</summary>
public sealed class ReaderTests
{
    [Fact]
    public void The_270_asks_four_payers_about_twelve_people_one_a_dependent()
    {
        var f = Samples.Load("clinic-270.x12");
        Assert.Empty(f.Problems);
        Assert.Equal("270", f.Kind);
        Assert.Equal(["BLUEWATER HEALTH PLAN", "PRAIRIE MEDICAID CARE", "SUMMIT HMO", "CASCADE MUTUAL"], f.Transactions.Select(t => t.Payer!.Name));
        Assert.All(f.Transactions, t => Assert.Equal(("MAPLE STREET FAMILY MEDICINE", "XX", "1642031870"), (t.Provider!.Name, t.Provider.IdQualifier, t.Provider.Id)));
        Assert.Equal(12, f.People.Count());
        var emma = f.People.Single(p => p.First == "EMMA");
        Assert.True(emma.IsDependent);
        Assert.Equal("DANIEL", emma.Subscriber!.First);
        Assert.Equal("", emma.MemberId);
        Assert.Equal("BW0002", emma.Trace);
        var kenji = f.People.Single(p => p.First == "KENJI");
        Assert.Equal(["30", "62"], Assert.Single(kenji.Inquiries).ServiceTypes);
        Assert.Equal("Health Benefit Plan Coverage; MRI/CAT Scan", Words.Describe(kenji.Inquiries[0]));
        Assert.Equal(new DateOnly(1979, 12, 19), kenji.BirthDate);
        Assert.Equal(("Plan", new DateOnly(2026, 12, 2)), (kenji.Dates[0].Kind, kenji.Dates[0].From));
        Assert.Equal(new DateTime(2026, 11, 25, 8, 15, 0), f.Transactions[0].Created);
    }

    [Fact]
    public void The_bluewater_answer_reads_every_benefit_with_its_messages_and_entities()
    {
        var f = Samples.Load("bluewater-271.x12");
        Assert.Empty(f.Problems);
        Assert.Equal(7, f.People.Count());
        var daniel = f.People.Single(p => p.First == "DANIEL");
        Assert.Equal(("BWH400100201", "Self", "100 OAK ST, SPRINGFIELD, IL 62704"), (daniel.MemberId, Codes.Relationship.Say(daniel.Relationship), daniel.Address));
        Assert.Equal(["BW0001", "RBW0001"], daniel.Traces.Select(t => t.Number));
        Assert.Equal("BW0001", daniel.Trace);
        Assert.Equal(["Group Number: GRP77015 (MAPLE TOOL AND DIE)", "Plan Number: GOLDPPO1500 ()"], daniel.References.Select(r => $"{r.Kind}: {r.Value} ({r.Description})"));
        Assert.Equal(16, daniel.Benefits.Count);
        Assert.Equal("Active Coverage, Health Benefit Plan Coverage, family, Preferred Provider Organization (PPO), \"GOLD PPO 1500\"", Words.Describe(daniel.Benefits[0]));
        Assert.Equal("Co-Payment: $25.00 per visit, Professional (Physician) Visit - Office, in network", Words.Describe(daniel.Benefits[1]));
        Assert.Equal("Deductible: $1,120.00 remaining, Health Benefit Plan Coverage, in network", Words.Describe(daniel.Benefits[6]));
        Assert.Equal("Co-Insurance: 20%, Health Benefit Plan Coverage, in network", Words.Describe(daniel.Benefits[10]));
        Assert.Equal("Co-Payment: $250.00 per visit, Emergency Services, in or out of network", Words.Describe(daniel.Benefits[3]));
        Assert.Equal(["OFFICE VISIT COPAY ONLY, DEDUCTIBLE WAIVED"], daniel.Benefits[14].Messages);
        Assert.Equal("Non-Covered, Vision (Optometry)", Words.Describe(daniel.Benefits[15]));

        var emma = f.People.Single(p => p.First == "EMMA");
        Assert.True(emma.IsDependent);
        Assert.Same(daniel, emma.Subscriber);
        Assert.Equal("Child", Codes.Relationship.Say(emma.Relationship));
        Assert.Equal("Co-Insurance: 0%, Physician Visit - Office: Well, in network", Words.Describe(emma.Benefits[2]));

        var marcus = f.People.Single(p => p.First == "MARCUS");
        var other = marcus.Benefits.Single(b => b.Information == "R");
        Assert.Equal("Other or Additional Payor, Health Benefit Plan Coverage, Medicare Part B", Words.Describe(other));
        Assert.Equal("Primary Payer: MEDICARE PART B (PI MCRB1)", Words.Describe(Assert.Single(other.Entities)));
        Assert.Equal(("Insurance Policy Number", "4TQ8NJ2KR63"), (other.References[0].Kind, other.References[0].Value));
        Assert.Equal("Coordination of Benefits: 2022-07-01", $"{other.Dates[0].Kind}: {other.Dates[0]}");

        var kenji = f.People.Single(p => p.First == "KENJI");
        var mri = kenji.Benefits[1];
        Assert.Equal("Active Coverage, MRI/CAT Scan, in network, authorization required, Preferred Provider Organization (PPO)", Words.Describe(mri));
        Assert.Equal("Utilization Management Organization: BLUEWATER UTILIZATION REVIEW, TE 8005550147", Words.Describe(Assert.Single(mri.Entities)));
        Assert.Equal(["EB", "MSG", "LS", "NM1", "PER", "LE"], mri.Segments.Select(s => s.Id));
    }

    [Fact]
    public void Rejections_are_read_at_the_level_they_were_sent()
    {
        var peter = Samples.Person("bluewater-271.x12", "PETER");
        var r = Assert.Single(peter.Rejections);
        Assert.Equal((false, "72", "C", "the subscriber"), (r.Valid, r.Reason, r.FollowUp, r.Where));
        Assert.Equal("Invalid/Missing Subscriber/Insured ID (72). Please Correct and Resubmit.", Words.Describe(r));
        Assert.Empty(peter.Benefits);
        Assert.Equal("Patient Birth Date Does Not Match That for the Patient on the Database (71). Please Correct and Resubmit.", Words.Describe(Samples.Person("bluewater-271.x12", "ROSE").Rejections[0]));
        Assert.Equal("Subscriber/Insured Not Found (75). Please Correct and Resubmit.", Words.Describe(Samples.Person("summit-271.x12", "SIMONE").Rejections[0]));

        var cascade = Samples.Load("cascade-271.x12");
        Assert.Empty(cascade.Problems);
        Assert.Empty(cascade.People);
        var payer = cascade.Transactions[0].Payer!;
        Assert.Equal("Unable to Respond at Current Time (42). Resubmission Allowed.", Words.Describe(Assert.Single(payer.Rejections)));
        Assert.Equal("the payer", payer.Rejections[0].Where);
        Assert.Equal(("CASCADE PROVIDER SERVICES", "TE", "8005550199"), (payer.Contacts[0].Name, payer.Contacts[0].Numbers[0].Kind, payer.Contacts[0].Numbers[0].Number));
    }

    [Fact]
    public void Limits_inactive_coverage_and_a_pcp_read_in_words()
    {
        var ana = Samples.Person("prairie-271.x12", "ANA");
        Assert.Equal("Limitations: 20 visits per calendar year, Mental Health, in network", Words.Describe(ana.Benefits[3]));
        Assert.Equal("Limitations: 8 visits remaining, Mental Health, in network", Words.Describe(ana.Benefits[4]));
        Assert.Equal("Co-Payment: $0.00 per visit, Mental Health, in network", Words.Describe(ana.Benefits[2]));

        var mateo = Samples.Person("prairie-271.x12", "MATEO");
        var pcp = mateo.Benefits.Single(b => b.Information == "L");
        Assert.Equal("Primary Care Provider: LUCIA REYES (XX 1888120940), TE 2175550188", Words.Describe(Assert.Single(pcp.Entities)));
        Assert.Equal(["VISITS TO A PROVIDER OTHER THAN THE ASSIGNED PCP REQUIRE A REFERRAL"], pcp.Messages);

        var grace = Samples.Person("bluewater-271.x12", "GRACE");
        Assert.Equal("Inactive, Health Benefit Plan Coverage, Preferred Provider Organization (PPO), \"GOLD PPO 1500\"", Words.Describe(grace.Benefits[0]));
        Assert.Equal(["Plan Begin: 2025-01-01", "Plan End: 2026-10-31"], grace.Dates.Select(d => $"{d.Kind}: {d}"));
    }

    [Fact]
    public void Codes_not_in_a_list_say_so_and_other_transactions_are_left_out()
    {
        Assert.Equal("code ZZ (not in the list)", Codes.ServiceType.Say("ZZ"));
        var f = EligibilityReader.Parse("ISA*00*          *00*          *ZZ*A              *ZZ*B              *261125*0815*^*00501*000000001*0*T*:~GS*HP*A*B*20261125*0815*1*X*005010X221A1~ST*835*0001~BPR*I~SE*3*0001~GE*1*1~IEA*1*000000001~");
        Assert.Empty(f.Transactions);
        Assert.Equal(["Transaction 0001 is a 835, not a 270 or 271: it's left out."], f.Problems);
        Assert.Equal(["There's no ST segment: this isn't an X12 file, or it's empty."], EligibilityReader.Parse("hello").Problems);
    }

    [Fact]
    public void Every_service_type_and_code_list_entry_is_unique_and_worded()
    {
        foreach (var list in new[] { Codes.Information, Codes.ServiceType, Codes.InsuranceType, Codes.TimePeriod, Codes.Reject, Codes.FollowUp, Codes.DateKind, Codes.Reference, Codes.Entity })
            Assert.All(list, kv => Assert.False(string.IsNullOrWhiteSpace(kv.Value), kv.Key));
        Assert.Equal(187, Codes.ServiceType.Count);
    }
}
