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
            Assert.Equal(["ready", "ready", "act first", "rejected", "rejected", "act first", "check"], entries.Where(e => e.Person != null).Select(e => MainWindow.Mark(e.Person!).Word));
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
