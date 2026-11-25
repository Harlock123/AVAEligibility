using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using AVAEligibility.Agents.Chat;
using AVAEligibility.Tests.Agents;
using static AVAEligibility.Tests.Agents.FakeModelServer;

namespace AVAEligibility.Tests;

/// <summary>The README's pictures (ELIG_SHOTS set to a folder), headless. Off unless ELIG_SHOTS is set; run with --filter Screenshots.</summary>
public sealed class Screenshots
{
    internal static void Save(Window w, string path)
    {
        for (var i = 0; i < 30; i++)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(path);
    }

    private static string? Folder() => Environment.GetEnvironmentVariable("ELIG_SHOTS") is { Length: > 0 } dir ? Directory.CreateDirectory(dir).FullName : null;

    [Fact]
    public void Coverage() => HeadlessApp.Run(() =>
    {
        if (Folder() is not { } dir) return;
        var w = new MainWindow { Width = 1500, Height = 900 };
        w.AgentSettings.ChatOpen = false;
        w.Show();
        try
        {
            w.Open(Samples.Path("bluewater-271.x12"));
            Save(w, Path.Combine(dir, "coverage.png"));
            var kenji = w.OpenFiles[0].People.Single(p => p.First == "KENJI");
            w.FileList.SelectedItem = w.FileList.Items.OfType<ListEntry>().Single(e => e.Person == kenji);
            w.Highlight([.. kenji.Benefits[1].Segments.Select(s => s.Index)]);
            Save(w, Path.Combine(dir, "authorization.png"));
        }
        finally { w.Close(); }
    });

    [Fact]
    public void Worklist() => HeadlessApp.Run(() =>
    {
        if (Folder() is not { } dir) return;
        var folder = Samples.Copy();
        var w = new MainWindow { Width = 1500, Height = 900, Now = () => InquiryTests.SampleTime };
        w.AgentSettings.ChatOpen = false;
        w.Settings.Inquiry = InquiryTests.SampleSettings;
        w.Show();
        if (w.ChatHolder.IsVisible) w.ToggleChat();
        try
        {
            w.Open(Path.Combine(folder, "appointments.csv"));
            foreach (var f in WorklistTests.AnswerFiles) w.Open(Path.Combine(folder, f));
            w.Tabs.SelectedIndex = 2;
            Save(w, Path.Combine(dir, "worklist.png"));
            w.ShowPerson(w.Work.Single(x => x.Appointment.First == "KENJI").Answer.Person!);
            w.Tabs.SelectedIndex = 0;
            Save(w, Path.Combine(dir, "visit.png"));
            w.Tabs.SelectedIndex = 1;
            Save(w, Path.Combine(dir, "inquiries.png"));
        }
        finally { w.Close(); }
    });

    [Fact]
    public void Denied() => HeadlessApp.Run(() =>
    {
        if (Folder() is not { } dir) return;
        var folder = Samples.Copy();
        var w = new MainWindow { Width = 1500, Height = 760, Now = () => InquiryTests.SampleTime };
        w.AgentSettings.ChatOpen = false;
        w.Show();
        if (w.ChatHolder.IsVisible) w.ToggleChat();
        try
        {
            w.Open(Path.Combine(folder, "denied"));
            w.Tabs.SelectedIndex = 2;
            Save(w, Path.Combine(dir, "denied.png"));
        }
        finally { w.Close(); }
    });

    [Fact]
    public void Ai_chat() => HeadlessApp.Run(() =>
    {
        if (Folder() is not { } dir) return;
        var root = Samples.Copy();
        var w = new MainWindow { Width = 1700, Height = 950, Now = () => InquiryTests.SampleTime };
        w.ChatKeys = (new ApiKeyStore(null, null), new ApiKeyStore(null, null));
        w.AgentSettings.ChatProvider = "OpenAICompatible";
        w.AgentSettings.ChatServerUrl = "http://localhost:1234/v1";
        w.AgentSettings.ChatServerModel = "qwen3-8b";
        w.AgentSettings.WorkingFolder = root;
        w.Show();
        try
        {
            w.Open(Path.Combine(root, "appointments.csv"));
            foreach (var f in WorklistTests.AnswerFiles) w.Open(Path.Combine(root, f));
            if (!w.ChatHolder.IsVisible) w.ToggleChat();
            var server = new FakeModelServer("http://localhost:1234/v1")
                .Reply(Say("I'll read the worklist."), Call("c1", "get_worklist", new { }))
                .Reply(Call("c2", "get_visit", new { visit = "V07" }), Call("c3", "get_visit", new { visit = "V09" }))
                .Reply(Say("Showing you V07."), Call("c4", "show_in_view", new { handle = "V07" }))
                .Reply(Say("Of 13 visits, 3 are ready and 1 is self-pay. Before the patients arrive:\n\n" +
                           "• V07: the lumbar MRI needs prior authorization. Call Bluewater Utilization Review at 800-555-0147 and put the number on the claim.\n" +
                           "• V09: his PCP on file is Lucia Reyes, so the plan needs a referral from her office (217-555-0188).\n" +
                           "• V06: Medicare Part B is primary: bill it first.\n" +
                           "• V03: coverage ended in October: ask for current insurance.\n" +
                           "• V10: the payer has a longer member ID (S**9999999 against S**99999): update the registration.\n\n" +
                           "Three were rejected: V04's member ID has a letter among its digits (B**99999*999: likely an O for a zero), V05's birth date doesn't match, and V11 wasn't found. Cascade Mutual couldn't answer for V12: try again later. Names and IDs here are masked."));
            var panel = w.Chat!;
            panel.ServerFactory = (_, key) => server.Client(key);
            panel.Input.Text = "Who needs something before their visit, and what?";
            panel.Send();
            AgentTests.Wait(panel.Running.ContinueWith(_ => true));
            Save(w, Path.Combine(dir, "ai-chat.png"));
            panel.SetupOpen = true;
            Save(w, Path.Combine(dir, "ai-chat-setup.png"));
        }
        finally { w.Close(); }
    });
}
