using AVAEligibility.Core.Eligibility;
using AVAEligibility.Core.Inquiries;
using AVAEligibility.Core.Reports;
using ClosedXML.Excel;

namespace AVAEligibility.Tests;

/// <summary>The sample day's worklist: each visit's verdict, what decided it, and what the patient may owe.</summary>
public sealed class WorklistTests
{
    internal static readonly string[] AnswerFiles = ["bluewater-271.x12", "prairie-271.x12", "summit-271.x12", "cascade-271.x12"];

    internal static IReadOnlyList<WorkItem> SampleWork() =>
        Worklist.Build(AppointmentList.Load(Samples.Path("appointments.csv")), [.. AnswerFiles.Select(Samples.Load)]);

    [Fact]
    public void Every_visit_on_the_sample_day_gets_its_verdict_and_what_decided_it()
    {
        Assert.Equal(
        [
            "HARTWELL, DANIEL: ready | Active: GOLD PPO 1500",
            "HARTWELL, EMMA: ready | Active: GOLD PPO 1500",
            "OYELARAN, GRACE: act first | Coverage ended 2026-10-31.",
            "KOWALCZYK, PETER: rejected | The payer couldn't answer about the subscriber: Invalid/Missing Subscriber/Insured ID (72).",
            "BRANNIGAN, ROSE: rejected | The payer couldn't answer about the subscriber: Patient Birth Date Does Not Match That for the Patient on the Database (71).",
            "DELACROIX, MARCUS: act first | MEDICARE PART B is primary; this plan pays second.",
            "NAKAMURA, KENJI: act first | This visit (MRI/CAT Scan, 72148) needs authorization, and the plan says so.",
            "FLORES, ANA: ready | This visit uses one of the 8 visits left (Mental Health: 8 of 20 visits left (per calendar year)).",
            "FLORES, MATEO: act first | The PCP on file is LUCIA REYES, not ANJALI PATEL, and the plan needs a referral for other providers.",
            "ABERNATHY, LOUISE: check | The payer has member ID SMH5512001; the registration has SMH55120.",
            "VANTERPOOL, SIMONE: rejected | The payer couldn't answer about the subscriber: Subscriber/Insured Not Found (75).",
            "ODUYA, SAMUEL: no answer | CASCADE MUTUAL couldn't answer: Unable to Respond at Current Time (42).",
            "PRICE, NOLAN: self-pay | Self-pay: there's no insurance to check.",
        ], SampleWork().Select(w => $"{w.Appointment.SortName}: {w.Word} | {w.Headline}"));
    }

    [Fact]
    public void What_to_do_names_who_to_call_and_the_kenji_general_note_becomes_the_visits()
    {
        var work = SampleWork().ToDictionary(w => w.Appointment.First);
        Assert.Equal("Get it from BLUEWATER UTILIZATION REVIEW at 800-555-0147 before the visit, and put its number on the claim.", work["KENJI"].Notes.Single().Fix);
        Assert.Equal("Get a referral from LUCIA REYES's office before the visit (217-555-0188), or change the patient's PCP with the plan.", work["MATEO"].Notes.Single().Fix);
        Assert.Equal("Update the registration to SMH5512001, or the claim will be rejected.", work["LOUISE"].Notes.Single().Fix);
        Assert.Equal("The payer's system couldn't answer just now. Ask again later. Or call 800-555-0199.", work["SAMUEL"].Notes.Single().Fix);
        Assert.Equal(Verdict.Ready, work["ANA"].Notes.Single().Level);
        var unanswered = Worklist.Build(AppointmentList.Load(Samples.Path("appointments.csv")), []);
        Assert.All(unanswered.Where(w => !w.Appointment.SelfPay), w => Assert.Equal(("no answer", "No answer yet."), (w.Word, w.Headline)));
    }

    [Fact]
    public void Estimates_use_the_copay_or_the_deductible_and_coinsurance_and_say_so()
    {
        var est = SampleWork().ToDictionary(w => w.Appointment.First, w => w.Estimate?.ToString());
        Assert.Equal("About $25.00: the $25.00 copay for Professional (Physician) Visit - Office", est["DANIEL"]);
        Assert.Equal("About $0.00: 0% coinsurance on $210.00 (“WELL CHILD CARE COVERED IN FULL IN NETWORK”)", est["EMMA"]);
        Assert.Equal("About $140.00: the whole charge, unless the patient has other insurance (this coverage isn't active)", est["GRACE"]);
        Assert.Equal("About $538.00: $310.00 of deductible left, then 20% coinsurance on $1,140.00", est["KENJI"]);
        Assert.Equal("About $0.00: the $0.00 copay for Mental Health", est["ANA"]);
        Assert.Equal("About $30.00: the $30.00 copay for Professional (Physician) Visit - Office", est["LOUISE"]);
        Assert.Null(est["MARCUS"]);
        Assert.Null(est["PETER"]);
        Assert.Null(est["SAMUEL"]);
        Assert.Equal("About $140.00: the whole charge (self-pay)", est["NOLAN"]);
        Assert.Contains("allows the full charge", SampleWork().First().Estimate!.Assumes);
    }

    [Fact]
    public void An_estimate_stops_at_whats_left_of_the_out_of_pocket_maximum()
    {
        var text = File.ReadAllText(Samples.Path("bluewater-271.x12")).Replace("EB*G*IND*30*PR**29*5210*****Y~", "EB*G*IND*30*PR**29*300*****Y~");
        var daniel = EligibilityReader.Parse(text).People.Single(p => p.First == "DANIEL");
        var a = AppointmentList.Load(Samples.Path("appointments.csv")).Single(x => x.First == "DANIEL") with { Procedure = "72148", Charge = 1450m };
        var e = Worklist.Estimate(a, Interpreter.Read(daniel))!;
        Assert.Equal("About $300.00: $1,120.00 of deductible left, then 20% coinsurance on $330.00, capped at the $300.00 left of the out-of-pocket maximum", e.ToString());
    }

    [Fact]
    public void A_later_answer_says_what_changed_since_the_earlier_one()
    {
        var files = AnswerFiles.Select(Samples.Load).Append(Samples.Load("recheck/bluewater-271-dec15.x12")).ToList();
        var later = files[^1].People.ToDictionary(p => p.First);
        string[] Said(string first) => [.. Changes.Between(Changes.Earlier(later[first], files)!, later[first])];
        Assert.Equal(
        [
            "Deductible, individual, in network: $845.00 left (was $1,120.00; $275.00 used since).",
            "Deductible, family, in network: $2,125.00 left (was $2,400.00; $275.00 used since).",
            "Out-of-pocket maximum, individual, in network: $4,935.00 left (was $5,210.00; $275.00 used since).",
        ], Said("DANIEL"));
        Assert.Equal(["Was rejected, now ready: Active: GOLD PPO 1500"], Said("ROSE"));
        Assert.Equal(["Was act first, now ready: Active: GOLD PPO 1500", "The plan was \"GOLD PPO 1500 SECONDARY\", now \"GOLD PPO 1500\".", "MEDICARE PART B no longer shows as primary."], Said("MARCUS"));
        Assert.Empty(Said("KENJI"));
        Assert.Null(Changes.Earlier(files[0].People.First(), files));
    }

    [Fact]
    public void The_worklist_report_is_a_page_and_a_workbook()
    {
        var work = SampleWork();
        var html = WorklistReport.Html(work, "Worklist: appointments", new DateTime(2026, 11, 25, 9, 0, 0));
        Assert.Contains("<b>3</b> ready", html.Replace("<span>", ""));
        Assert.Contains("Get a referral from LUCIA REYES&#39;s office", html);
        Assert.Contains("$538.00", html);
        var masked = WorklistReport.Html(work, "W", DateTime.Now, s => new string('*', s.Length));
        Assert.DoesNotContain("HARTWELL", masked);
        var path = Path.Combine(Directory.CreateTempSubdirectory("elig-report-").FullName, "w.xlsx");
        WorklistReport.Excel(path, work);
        using var book = new XLWorkbook(path);
        var sheet = book.Worksheet(1);
        Assert.Equal(("Patient", "NAKAMURA, KENJI", "act first", 538m), (sheet.Cell(1, 3).GetString(), sheet.Cell(8, 3).GetString(), sheet.Cell(8, 8).GetString(), sheet.Cell(8, 11).GetValue<decimal>()));
    }
}
