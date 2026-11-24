using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;

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
        w.Show();
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
}
