using Avalonia.LogicalTree;
using AVAEligibility.Core.Eligibility;

namespace AVAEligibility.Tests;

/// <summary>The window: opening files, the list by payer and person, a person's answer in words, and its segments.</summary>
public sealed class WindowTests
{
    [Fact]
    public void Opening_the_samples_lists_each_payer_and_person_with_the_answer() => HeadlessApp.Run(() =>
    {
        var w = new MainWindow();
        w.Show();
        try
        {
            w.Open(Samples.Path("bluewater-271.x12"));
            Assert.Equal("Opened bluewater-271.x12: a 271 from 1 payer about 7 people.", w.txtStatusBar.Text);
            var entries = w.FileList.Items.OfType<ListEntry>().ToList();
            Assert.Equal(9, entries.Count);
            Assert.Equal(["ready", "ready", "act first", "rejected", "rejected", "act first", "check"], entries.Where(e => e.Person != null).Select(e => w.Mark(e.Person!).Word));
            Assert.Equal("DANIEL", w.CurrentPerson!.First);
            w.Open(Samples.Path("cascade-271.x12"));
            w.FileList.SelectedItem = w.FileList.Items.OfType<ListEntry>().Single(e => e.Transaction?.Payer?.Name == "CASCADE MUTUAL" && e.Person == null);
            HeadlessApp.Settle();
            Assert.Contains("Unable to Respond at Current Time (42). Resubmission Allowed.", Texts(w));
        }
        finally { w.Close(); }
    });

    [Fact]
    public void A_person_shows_in_words_and_a_line_highlights_its_segments() => HeadlessApp.Run(() =>
    {
        var w = new MainWindow();
        w.Show();
        try
        {
            w.Open(Samples.Path("bluewater-271.x12"));
            var kenji = w.OpenFiles[0].People.Single(p => p.First == "KENJI");
            w.FileList.SelectedItem = w.FileList.Items.OfType<ListEntry>().Single(e => e.Person == kenji);
            HeadlessApp.Settle();
            Assert.Same(kenji, w.CurrentPerson);
            var texts = Texts(w);
            Assert.Contains("KENJI NAKAMURA", texts);
            Assert.Contains("BLUEWATER HEALTH PLAN · member BWH400100815 · born 1979-12-19 · male · trace BW0007", texts);
            Assert.Contains("“PRIOR AUTHORIZATION REQUIRED FOR ADVANCED IMAGING”", texts);
            Assert.Contains("CHECK", texts);
            Assert.Contains("Get it from BLUEWATER UTILIZATION REVIEW at 800-555-0147 before scheduling it.", texts);
            Assert.Equal(Verdict.Check, w.CurrentReading!.Verdict);
            w.Highlight([.. kenji.Benefits[1].Segments.Select(s => s.Index)]);
            Assert.StartsWith("EB*1*IND*62*PR*******Y*Y~MSG*PRIOR AUTHORIZATION", w.Highlighted);
        }
        finally { w.Close(); }
    });

    [Fact]
    public void A_folder_opens_every_eligibility_file_in_it() => HeadlessApp.Run(() =>
    {
        var w = new MainWindow();
        w.Show();
        try
        {
            w.Open(Samples.Folder);
            Assert.Equal(["bluewater-271.x12", "cascade-271.x12", "clinic-270.x12", "prairie-271.x12", "summit-271.x12"], w.OpenFiles.Select(f => f.Name).Order());
        }
        finally { w.Close(); }
    });

    internal static List<string> Texts(MainWindow w) =>
        w.CoveragePanel.GetLogicalDescendants().OfType<Avalonia.Controls.TextBlock>().Select(t => t.Text ?? "").ToList();
}

/// <summary>The Inquiries tab: an appointment list opened, checked and written as a 270, and the answers matched.</summary>
public sealed class InquiryWindowTests
{
    [Fact]
    public void An_appointment_list_is_checked_written_as_a_270_and_its_answers_found() => HeadlessApp.Run(() =>
    {
        var folder = Samples.Copy();
        var w = new MainWindow { Now = () => InquiryTests.SampleTime };
        w.Show();
        try
        {
            w.Open(Path.Combine(folder, "appointments.csv"));
            Assert.Equal(13, w.Appointments.Count);
            Assert.Equal("Opened appointments.csv: 13 appointments; 0 can't be asked as they are, 1 warnings.", w.txtStatusBar.Text);
            Assert.Equal(1, w.Tabs.SelectedIndex);
            var s = InquiryTests.SampleSettings;
            w.ProviderNameBox.Text = s.ProviderName;
            w.ProviderNpiBox.Text = "1642031871";
            w.SenderIdBox.Text = s.SenderId;
            w.ReceiverIdBox.Text = s.ReceiverId;
            w.TraceOriginatorBox.Text = s.TraceOriginator;
            w.Settings.Inquiry = w.Settings.Inquiry with { ReferencePrefix = "MSFM", ControlNumber = 1001 };
            var output = Path.Combine(folder, "out.x12");
            w.PickSaveFile = (_, _) => Task.FromResult<string?>(output);
            HeadlessApp.Wait(w.WriteInquiry());
            Assert.Contains("isn't a valid NPI", w.txtStatusBar.Text);
            Assert.False(File.Exists(output));
            w.ProviderNpiBox.Text = s.ProviderNpi;
            HeadlessApp.Wait(w.WriteInquiry());
            Assert.Equal("Wrote out.x12: 12 questions to 4 payers.", w.txtStatusBar.Text);
            Assert.Equal(File.ReadAllText(Samples.Path("clinic-270.x12")), File.ReadAllText(output));
            Assert.Equal(1002, AppSettings.Load().Inquiry.ControlNumber);
            Assert.Equal("1642031870", AppSettings.Load().Inquiry.ProviderNpi);

            foreach (var f in new[] { "bluewater-271.x12", "prairie-271.x12", "summit-271.x12", "cascade-271.x12" }) w.Open(Path.Combine(folder, f));
            var texts = w.InquiriesPanel.GetLogicalDescendants().OfType<Avalonia.Controls.TextBlock>().Select(t => t.Text ?? "").ToList();
            Assert.Contains("QUESTIONS AND ANSWERS: 12 ASKED, 11 ANSWERED", texts);
            Assert.Contains(texts, t => t.StartsWith("ODUYA, SAMUEL · CASCADE MUTUAL · trace CS0001 · CASCADE MUTUAL: Unable to Respond at Current Time (42).", StringComparison.Ordinal));
        }
        finally
        {
            AppSettings.Load().Save();
            w.Close();
        }
    });
}

/// <summary>The Worklist tab and the Coverage tab read for the visit.</summary>
public sealed class WorklistWindowTests
{
    [Fact]
    public void The_worklist_lists_each_visit_and_a_person_shows_the_visits_estimate_and_changes() => HeadlessApp.Run(() =>
    {
        var folder = Samples.Copy();
        var w = new MainWindow { Now = () => InquiryTests.SampleTime };
        w.Show();
        try
        {
            w.Open(Path.Combine(folder, "appointments.csv"));
            Assert.All(w.Work.Where(x => !x.Appointment.SelfPay), x => Assert.Equal(Verdict.NoAnswer, x.Verdict));
            foreach (var f in WorklistTests.AnswerFiles) w.Open(Path.Combine(folder, f));
            Assert.Equal(["ready", "ready", "act first", "rejected", "rejected", "act first", "act first", "ready", "act first", "check", "rejected", "no answer", "self-pay"], w.Work.Select(x => x.Word));
            Assert.Equal("act first", w.Mark(w.Work.Single(x => x.Appointment.First == "KENJI").Answer.Person!).Word);
            var texts = w.WorklistPanel.GetLogicalDescendants().OfType<Avalonia.Controls.TextBlock>().Select(t => t.Text ?? "").ToList();
            Assert.Contains("TUESDAY 1 DECEMBER 2026", texts);
            Assert.Contains("4 act first", texts);
            Assert.Contains("$538.00", texts);

            w.Open(Path.Combine(folder, "recheck", "bluewater-271-dec15.x12"));
            var daniel = w.OpenFiles.Single(f => f.Name == "bluewater-271-dec15.x12").People.First();
            w.ShowPerson(daniel);
            texts = WindowTests.Texts(w);
            Assert.Contains("SINCE THE ANSWER OF 2026-11-25", texts);
            Assert.Contains("Deductible, individual, in network: $845.00 left (was $1,120.00; $275.00 used since).", texts);

            var kenji = w.Work.Single(x => x.Appointment.First == "KENJI").Answer.Person!;
            w.ShowPerson(kenji);
            texts = WindowTests.Texts(w);
            Assert.Contains("ACT FIRST FOR THE VISIT ON 2026-12-02", texts);
            Assert.Contains("About $538.00: $310.00 of deductible left, then 20% coinsurance on $1,140.00", texts);

            var output = Path.Combine(folder, "worklist.xlsx");
            w.PickSaveFile = (_, _) => Task.FromResult<string?>(output);
            HeadlessApp.Wait(w.ExportWorklist());
            Assert.Equal("Wrote worklist.xlsx: 13 visits.", w.txtStatusBar.Text);
            Assert.True(File.Exists(output));
        }
        finally { w.Close(); }
    });
}

/// <summary>Ask Now in the window: nothing is sent without the person's yes.</summary>
public sealed class SendingWindowTests
{
    [Fact]
    public void Ask_now_asks_first_then_sends_saves_and_opens_the_answers() => HeadlessApp.Run(() =>
    {
        using var mock = new MockClearinghouse();
        var folder = Samples.Copy();
        var w = new MainWindow { Now = () => InquiryTests.SampleTime };
        w.Show();
        try
        {
            w.Open(Path.Combine(folder, "appointments.csv"));
            var s = InquiryTests.SampleSettings;
            (w.ProviderNameBox.Text, w.ProviderNpiBox.Text, w.SenderIdBox.Text, w.ReceiverIdBox.Text, w.TraceOriginatorBox.Text) = (s.ProviderName, s.ProviderNpi, s.SenderId, s.ReceiverId, s.TraceOriginator);
            w.Settings.Inquiry = w.Settings.Inquiry with { ControlNumber = 1001 };
            HeadlessApp.Wait(w.AskNow());
            Assert.Equal("Sending to a clearinghouse is off (Inquiries › Clearinghouse).", w.txtStatusBar.Text);
            w.SendEnabledBox.IsChecked = true;
            (w.EndpointBox.Text, w.CoreSenderBox.Text, w.CoreReceiverBox.Text, w.UserBox.Text, w.PasswordBox.Text) = (mock.Address, "MAPLESTFM", "MOCKCH", MockClearinghouse.User, MockClearinghouse.Password);
            string? question = null;
            w.AskSure = q => { question = q; return Task.FromResult(false); };
            HeadlessApp.Wait(w.AskNow());
            Assert.StartsWith("Send 12 eligibility questions to 127.0.0.1?", question);
            Assert.Empty(mock.Requests);
            w.AskSure = _ => Task.FromResult(true);
            HeadlessApp.Wait(w.AskNow());
            Assert.Equal("Asked 12: 12 answered.", w.txtStatusBar.Text);
            Assert.Equal(12, mock.Requests.Count);
            Assert.Equal(WorklistTests.SampleWork().Select(x => x.Word), w.Work.Select(x => x.Word));
            Assert.Equal(2, w.Tabs.SelectedIndex);
            Assert.DoesNotContain(MockClearinghouse.Password, File.ReadAllText(AppSettings.Path));
            Assert.Equal(1013, AppSettings.Load().Inquiry.ControlNumber);
        }
        finally
        {
            new Core.Sending.PasswordStore(MainWindow.SettingsFolder).Forget();
            new AppSettings().Save();
            w.Close();
        }
    });

    [Fact]
    public void A_denied_claims_list_from_AVA_Remittance_says_what_the_271_means_for_each_denial() => HeadlessApp.Run(() =>
    {
        var folder = Samples.Copy();
        var w = new MainWindow { Now = () => InquiryTests.SampleTime };
        w.Show();
        try
        {
            w.Open(Path.Combine(folder, "denied"));
            Assert.Equal(3, w.Work.Count);
            Assert.All(w.Work, x => Assert.Equal("act first", x.Word));
            var texts = w.WorklistPanel.GetLogicalDescendants().OfType<Avalonia.Controls.TextBlock>().Select(t => t.Text ?? "").ToList();
            Assert.Contains("Denied (RB26120): CO-26: Expenses incurred prior to coverage.", texts);
            Assert.Contains("The 271 says coverage was active on 2026-11-05 (from 2026-11-01), yet the claim was denied as before coverage began (CO-26).", texts);
            Assert.Contains("TUESDAY 3 NOVEMBER 2026", texts);
        }
        finally { w.Close(); }
    });
}
