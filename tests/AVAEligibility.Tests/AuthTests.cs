using AVAEligibility.Core.Authorizations;
using AVAEligibility.Core.Eligibility;
using AVAEligibility.Core.Inquiries;

namespace AVAEligibility.Tests;

/// <summary>The 278: which visits need authorization, the request the app writes, and what an answer means for the visit.</summary>
public sealed class AuthTests
{
    internal static readonly InquirySettings Settings = InquiryTests.SampleSettings with { ControlNumber = 1002 };

    internal static IReadOnlyList<WorkItem> Work(params string[] reviews) =>
        Worklist.Build(AppointmentList.Load(Samples.Path("appointments.csv")), [.. WorklistTests.AnswerFiles.Select(Samples.Load)], reviews: [.. reviews.Select(r => AuthReader.Load(Samples.Path(r)))]);

    private static WorkItem Kenji(IReadOnlyList<WorkItem> work) => work.Single(w => w.Appointment.First == "KENJI");

    private static WorkItem WithAnswer(string from, string to)
    {
        var text = File.ReadAllText(Samples.Path("authorizations/bluewater-278.x12"));
        Assert.Contains(from, text);
        var work = Worklist.Build(AppointmentList.Load(Samples.Path("appointments.csv")), [.. WorklistTests.AnswerFiles.Select(Samples.Load)], reviews: [AuthReader.Parse(text.Replace(from, to))]);
        return Kenji(work);
    }

    [Fact]
    public void Only_the_MRI_needs_authorization_and_the_278_is_the_samples()
    {
        var needs = AuthRequests.Needed(Work());
        var n = Assert.Single(needs);
        Assert.Equal(("KENJI", "62", "BLUEWATER UTILIZATION REVIEW", "8005550147", "M54.16"), (n.Appointment.First, n.Care, n.UmoName, n.UmoPhone, n.Appointment.Diagnosis));
        var built = AuthRequests.Build(needs, Settings, InquiryTests.SampleTime);
        Assert.Empty(built.Checks);
        Assert.Equal(File.ReadAllText(Samples.Path("authorizations/clinic-278.x12")), built.Text);
        Assert.Equal("BWA001", built.Traces[n.Appointment]);
    }

    [Fact]
    public void The_request_and_the_answer_read_back()
    {
        var asked = Assert.Single(AuthReader.Load(Samples.Path("authorizations/clinic-278.x12")).Reviews);
        Assert.Equal((false, "asked", "BLUEWATER UTILIZATION REVIEW", "BWHP1", "NAKAMURA, KENJI", "BWA001", "HS", "I", "62", "11"),
            (asked.IsResponse, asked.Word, asked.UmoName, asked.UmoId, asked.SortName, asked.Trace, asked.Category, asked.CertificationType, asked.ServiceType, asked.Facility));
        Assert.Equal(["M5416"], asked.Diagnoses);
        Assert.Equal(("CHIDI OKAFOR", "1733001584", new DateOnly(2026, 12, 2)), (asked.ProviderName, asked.ProviderNpi, asked.EventDate!.Value));
        Assert.Equal(("72148", 1450m), (asked.Services.Single().Procedure, asked.Services.Single().Charge!.Value));

        var file = AuthReader.Load(Samples.Path("authorizations/bluewater-278.x12"));
        Assert.True(file.IsResponse);
        var answer = Assert.Single(file.Reviews);
        Assert.Equal(("certified", "A1", "BWUR2611250042", "2026-12-02 to 2027-01-01", new DateOnly(2026, 11, 25)), (answer.Word, answer.Action, answer.Number, answer.Runs, answer.Issued!.Value));
        Assert.Equal("A1", answer.Services.Single().Action);
        Assert.True(AuthReader.LooksLikeOne(Samples.Path("authorizations/bluewater-278.x12")));
        Assert.False(AuthReader.LooksLikeOne(Samples.Path("bluewater-271.x12")));
        Assert.Equal("Certified in total", AuthCodes.Say(AuthCodes.Action, "A1"));
        Assert.Equal("code ZZ (not in the list)", AuthCodes.Say(AuthCodes.Action, "ZZ"));
    }

    [Fact]
    public void The_answer_takes_the_place_of_needs_authorization_on_the_worklist()
    {
        Assert.Equal("act first", Kenji(Work()).Word);
        var asked = Kenji(Work("authorizations/clinic-278.x12"));
        Assert.Equal(("act first", "This visit's authorization was asked for (BWA001); no answer is open yet."), (asked.Word, asked.Headline));
        var kenji = Kenji(Work("authorizations/clinic-278.x12", "authorizations/bluewater-278.x12"));
        Assert.DoesNotContain(kenji.Notes, n => n.Text.Contains("needs authorization, and the plan says so"));
        var note = kenji.Notes.Single(n => n.Text.StartsWith("Authorized"));
        Assert.Equal(("Authorized: BWUR2611250042, 2026-12-02 to 2027-01-01.", "Put BWUR2611250042 on the claim (REF*G1).", Verdict.Ready), (note.Text, note.Fix, note.Level));
        Assert.NotEqual("act first", kenji.Word);
        var others = Work("authorizations/bluewater-278.x12").Where(w => w.Appointment.First != "KENJI").Select(w => w.Word);
        Assert.Equal(WorklistTests.SampleWork().Where(w => w.Appointment.First != "KENJI").Select(w => w.Word), others);
    }

    [Fact]
    public void Every_decision_says_what_to_do()
    {
        Assert.Equal("The authorization is pending (BWUR2611250042).", WithAnswer("HCR*A1*BWUR2611250042~\nDTP*AAH", "HCR*A4*BWUR2611250042~\nDTP*AAH").Headline);
        var refused = WithAnswer("HCR*A1*BWUR2611250042~\nDTP*AAH", "HCR*A3*BWUR2611250042*0V~\nDTP*AAH");
        Assert.Equal(("act first", "Authorization was refused (not certified) (reason code 0V)."), (refused.Word, refused.Headline));
        Assert.StartsWith("Tell the patient before the visit: ask BLUEWATER UTILIZATION REVIEW at 800-555-0147 to reconsider", refused.Notes.First(n => n.Level == Verdict.Act).Fix);
        Assert.Equal("Authorization BWUR2611250042 runs 2026-12-05 to 2027-01-01, which doesn't cover the visit on 2026-12-02.",
            WithAnswer("DTP*007*RD8*20261202-20270101", "DTP*007*RD8*20261205-20270101").Headline);
        var rejected = WithAnswer("HCR*A1*BWUR2611250042~\nDTP*AAH", "AAA*N**15*C~\nDTP*AAH");
        Assert.Equal("act first", rejected.Word);
        Assert.StartsWith("The 278 was refused:", rejected.Headline);
        Assert.Contains("(15)", rejected.Headline);
    }

    [Fact]
    public void The_checks_catch_what_a_278_cant_go_without()
    {
        var n = AuthRequests.Needed(Work()).Single();
        var bad = n with { Appointment = n.Appointment with { Diagnosis = "", ProviderNpi = "1234567890" } };
        Assert.Equal(
        [
            "Row 7 (NAKAMURA, KENJI): There's no diagnosis: a 278 needs one (an ICD-10-CM code, in the list's Diagnosis column).",
            "Row 7 (NAKAMURA, KENJI): The visit's provider NPI 1234567890 isn't a valid NPI.",
        ], AuthRequests.Check([bad]).Select(c => c.ToString()));
        Assert.Throws<InvalidOperationException>(() => AuthRequests.Build([bad], Settings, InquiryTests.SampleTime));
        Assert.Contains("isn't shaped like", AuthRequests.Check([n with { Appointment = n.Appointment with { Diagnosis = "72148" } }]).Single().Text);
    }
}
