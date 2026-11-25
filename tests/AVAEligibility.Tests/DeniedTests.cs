using AVAEligibility.Core.Eligibility;
using AVAEligibility.Core.Inquiries;

namespace AVAEligibility.Tests;

/// <summary>Denied claims asked about again: AVA Remittance's Check Eligibility Again list and the 271 for the dates of service.</summary>
public sealed class DeniedTests
{
    internal static IReadOnlyList<Appointment> List() => AppointmentList.Load(Samples.Path("denied/eligibility-recheck.csv"));

    internal static Dictionary<string, WorkItem> Work() =>
        Worklist.Build(List(), [Samples.Load("denied/tallgrass-271.x12")]).ToDictionary(w => w.Appointment.Last);

    [Fact]
    public void The_list_reads_with_its_accounts_and_denials()
    {
        Assert.True(AppointmentList.LooksLikeOne(Samples.Path("denied/eligibility-recheck.csv")));
        var list = List();
        Assert.Equal(["RB26118", "RB26119", "RB26120"], list.Select(a => a.Account));
        Assert.All(list, a => Assert.True(a.Denied));
        Assert.Equal([("CO", "27")], list[0].DenialCodes);
        Assert.Equal("CO-31: Patient cannot be identified as our insured.", list[1].Denial);
        Assert.Equal((new DateOnly(2026, 11, 4), "TGM01", "TG55090123", "99214", 220m), (list[1].Date!.Value, list[1].PayerId, list[1].MemberId, list[1].Procedure, list[1].Charge!.Value));
        Assert.False(AppointmentList.Load(Samples.Path("appointments.csv")).Any(a => a.Denied));
    }

    [Fact]
    public void The_270_asks_about_the_dates_of_service()
    {
        var settings = new InquirySettings { ProviderName = "RIVERBEND FAMILY MEDICINE", ProviderNpi = "1588667638", SenderId = "RIVERBEND", ReceiverId = "CLEARHOUSE01", TraceOriginator = "9RIVERBND1" };
        var list = List();
        var built = InquiryBuilder.Build(list, settings, new DateTime(2026, 11, 25, 8, 15, 0));
        Assert.Empty(built.Checks);
        Assert.Contains("DTP*291*D8*20261103~", built.Text);
        Assert.Equal(["TG0001", "TG0002", "TG0003"], built.Traces.Values);
        var work = Worklist.Build(list, [Samples.Load("denied/tallgrass-271.x12")], built.Traces, settings.TraceOriginator);
        Assert.All(work, w => Assert.StartsWith("trace", w.Answer.How));
    }

    [Fact]
    public void Each_answer_says_what_it_means_for_the_denial()
    {
        var work = Work();
        Assert.All(work.Values, w => Assert.Equal(("act first", (Estimate?)null), (w.Word, w.Estimate)));
        Assert.Equal("The 271 agrees with the denial (CO-27): coverage ended 2026-10-31, so the plan didn't cover the claim on 2026-11-03.", work["SANTOS"].Headline);
        Assert.Equal("Ask the patient which plan covered them on 2026-11-03 and bill it; with none, bill the patient.", work["SANTOS"].Notes[0].Fix);
        Assert.DoesNotContain(work["SANTOS"].Notes, n => n.Fix.Contains("before the visit"));
        Assert.Equal("The payer finds the patient with member ID TG55090132 (the claim had TG55090123): why it couldn't match the claim (CO-31).", work["TRAN"].Headline);
        Assert.StartsWith("Correct the claim to the payer's details and send it again", work["TRAN"].Notes[0].Fix);
        Assert.Equal("The 271 says coverage was active on 2026-11-05 (from 2026-11-01), yet the claim was denied as before coverage began (CO-26).", work["VEGA"].Headline);
        Assert.StartsWith("Ask the payer to reprocess the claim, or appeal with this 271", work["VEGA"].Notes[0].Fix);
        Assert.All(work.Values, w => Assert.NotEmpty(w.Notes[0].Segments));
    }

    [Fact]
    public void With_no_answer_yet_it_says_to_ask_about_the_date_of_service()
    {
        var w = Worklist.Build(List(), []).First();
        Assert.Equal("no answer", w.Word);
        Assert.StartsWith("Send the 270 for this list (Write the 270, or Ask Now): it asks about the date of service", w.Notes[0].Fix);
    }

    [Fact]
    public void Another_payer_first_is_confirmed_or_questioned()
    {
        var a = List()[2] with { Denial = "OA-22: This care may be covered by another payer per coordination of benefits." };
        var none = Worklist.Item(a, Matcher.For(a, [Samples.Load("denied/tallgrass-271.x12")]));
        Assert.StartsWith("The 271 shows no other payer ahead of this plan on 2026-11-05, yet the claim was denied as another payer's to pay first (OA-22).", none.Headline);
    }
}
