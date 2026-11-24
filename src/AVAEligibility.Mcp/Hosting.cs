using System.Diagnostics;
using AVAEligibility.Core.Eligibility;
using AVAEligibility.Core.Inquiries;
using AVAEligibility.Core.Privacy;

namespace AVAEligibility.Mcp;

/// <summary>
/// What the window shows: the tab on show (coverage, inquiries or worklist) and the person or visit selected, by the
/// handle agents know them by (P03 a person in an answer, V07 a visit on the appointment list).
/// </summary>
public sealed record ViewState(string Tab, string? Handle)
{
    public static readonly ViewState Empty = new("coverage", null);
}

/// <summary>What a picture of the view shows: a title, a line under it, and lines, some highlighted.</summary>
public sealed record ViewPicture(string Title, string Subtitle, IReadOnlyList<string> Lines, int FirstLine, int HighlightFrom, int HighlightTo, bool Numbered, IReadOnlyList<string> Notes);

/// <summary>What the server needs from the program that it can't do itself: drawing the view, which needs Avalonia.</summary>
public interface IViewPainter
{
    /// <summary>The view drawn as a PNG, at <paramref name="scale"/>.</summary>
    byte[] Picture(ViewPicture view, double scale);
}

/// <summary>The window (or the headless server's stand-in for one) as an agent works with it. Used on the host's thread only.</summary>
public interface IExplorerSession
{
    /// <summary>What the window shows now.</summary>
    ViewState View { get; }

    /// <summary>
    /// Whether the person lets agents see people's details (AI › Let AI Agents See Values, or <c>--real</c>). Off unless
    /// turned on: names, IDs, addresses and dates are masked (<see cref="Masker"/>) in everything an agent reads.
    /// </summary>
    bool SeeRealValues { get; }

    /// <summary>The 270s and 271s open.</summary>
    IReadOnlyList<EligibilityFile> Files { get; }

    /// <summary>The appointment list open (empty when none), and its file.</summary>
    IReadOnlyList<Appointment> Appointments { get; }
    string? AppointmentsPath { get; }

    /// <summary>The traces the appointments were sent under, when this session wrote or sent the 270.</summary>
    IReadOnlyDictionary<Appointment, string>? Traces { get; }

    /// <summary>Who's asking, and the envelope's IDs.</summary>
    InquirySettings Inquiry { get; }

    /// <summary>The worklist: the appointments against the answers open.</summary>
    IReadOnlyList<WorkItem> Work { get; }

    /// <summary>Today, as the checks see it.</summary>
    DateTime Now { get; }

    /// <summary>Opens a 270, a 271 or an appointment list; throws saying why when the file is none of them.</summary>
    void Open(string path);

    /// <summary>Closes an open file (or the appointment list).</summary>
    void Close(string path);

    /// <summary>Selects in the window (headless: just remembers it).</summary>
    void Show(ViewState view);

    /// <summary>Remembers the 270 just written from the appointment list, so its answers match by trace.</summary>
    void Remember(BuiltInquiry built);

    /// <summary>
    /// Asks the clearinghouse about the appointment list in real time, after the person says yes in the window; says what
    /// happened. Null headless: sending is the person's, in their window.
    /// </summary>
    Func<Task<string>>? AskClearinghouse { get; }
}

/// <summary>
/// Where the MCP tools' work runs. The handlers (<see cref="EligibilityTools"/>) touch the window only through
/// <see cref="RunAsync{T}"/>, so the same handlers serve the headless server and the open window.
/// </summary>
public interface IExplorerHost
{
    /// <summary>The working folder: files are read from it, and new files written only inside it.</summary>
    string Root { get; }

    IViewPainter Painter { get; }

    /// <summary>Working in the open window, with the person watching.</summary>
    bool Live { get; }

    /// <summary>Runs <paramref name="work"/> on the thread that owns the window, one call at a time.</summary>
    Task<T> RunAsync<T>(string action, Func<IExplorerSession, Task<T>> work, CancellationToken cancel = default);
}

public static class ExplorerHostExtensions
{
    public static Task<T> RunAsync<T>(this IExplorerHost host, string action, Func<IExplorerSession, T> work, CancellationToken cancel = default) =>
        host.RunAsync(action, s => Task.FromResult(work(s)), cancel);
}

/// <summary>Runs work on a UI thread (the program's Avalonia dispatcher, or a test's).</summary>
public interface IUiThread
{
    Task<T> InvokeAsync<T>(Func<Task<T>> work);
}

/// <summary>The program as the host sees it: the session, whether it can take a call now, and the status bar.</summary>
public interface IExplorerDesk
{
    IExplorerSession Session { get; }

    /// <summary>Why the program can't take a call just now ("a dialog box is open"), or null. The call waits.</summary>
    string? Busy { get; }

    /// <summary>Tells the person what an agent just did.</summary>
    void Report(string text);
}

/// <summary>
/// The host: calls run against the desk's session on its UI thread, one at a time. While the person is busy (a dialog
/// open) a call waits up to <see cref="BusyWait"/>.
/// </summary>
public sealed class ExplorerHost(IUiThread ui, IExplorerDesk desk, IViewPainter painter, Func<string> root) : IExplorerHost
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ExplorerHost(IUiThread ui, IExplorerDesk desk, IViewPainter painter, string root)
        : this(ui, desk, painter, () => root) { }

    public string Root => Path.GetFullPath(root());

    public IViewPainter Painter { get; } = painter;

    public bool Live { get; init; }

    public TimeSpan BusyWait { get; init; } = TimeSpan.FromSeconds(20);

    public TimeSpan BusyPoll { get; init; } = TimeSpan.FromMilliseconds(200);

    public async Task<T> RunAsync<T>(string action, Func<IExplorerSession, Task<T>> work, CancellationToken cancel = default)
    {
        await _gate.WaitAsync(cancel).ConfigureAwait(false);
        try
        {
            var clock = Stopwatch.StartNew();
            while (true)
            {
                cancel.ThrowIfCancellationRequested();
                var (ran, result, busy) = await ui.InvokeAsync(async () =>
                {
                    if (desk.Busy is { } why) return (false, default(T), why);
                    try
                    {
                        var r = await work(desk.Session);
                        desk.Report($"AI agent: {action}.");
                        return (true, r, (string?)null);
                    }
                    catch (Exception e) when (e is not OperationCanceledException)
                    {
                        desk.Report($"AI agent: {action} failed: {e.Message}");
                        throw;
                    }
                }).ConfigureAwait(false);
                if (ran) return result!;
                if (clock.Elapsed >= BusyWait)
                    throw new ExplorerBusyException($"AVA Eligibility is busy: {busy}. The call waited {BusyWait.TotalSeconds:0} seconds; ask the person to finish, then try again.");
                await Task.Delay(BusyPoll, cancel).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}

public sealed class ExplorerBusyException(string message) : Exception(message);

/// <summary>A session with no window round it, as <c>AVAEligibility --mcp</c> serves it.</summary>
public sealed class HeadlessSession(bool seeRealValues = false, InquirySettings? inquiry = null, Func<DateTime>? now = null) : IExplorerSession
{
    private readonly List<EligibilityFile> _files = [];

    public ViewState View { get; private set; } = ViewState.Empty;
    public bool SeeRealValues { get; set; } = seeRealValues;
    public IReadOnlyList<EligibilityFile> Files => _files;
    public IReadOnlyList<Appointment> Appointments { get; private set; } = [];
    public string? AppointmentsPath { get; private set; }
    public IReadOnlyDictionary<Appointment, string>? Traces { get; set; }
    public InquirySettings Inquiry { get; set; } = inquiry ?? new InquirySettings();
    public IReadOnlyList<WorkItem> Work => Appointments.Count == 0 ? [] : Worklist.Build(Appointments, _files, Traces, Inquiry.TraceOriginator is { Length: > 0 } o ? o : null);
    public DateTime Now => (now ?? (() => DateTime.Now))();
    public Func<Task<string>>? AskClearinghouse => null;

    public void Open(string path)
    {
        var full = Path.GetFullPath(path);
        if (AppointmentList.LooksLikeOne(full))
        {
            Appointments = AppointmentList.Load(full);
            AppointmentsPath = full;
            Traces = null;
            return;
        }
        var file = EligibilityReader.Load(full);
        if (file.Transactions.Count == 0)
            throw new NotSupportedException($"{Path.GetFileName(full)} isn't a 270, a 271 or an appointment list" + (file.Problems.Count > 0 ? $": {file.Problems[0]}" : "."));
        Close(full);
        _files.Add(file);
    }

    public void Close(string path)
    {
        var full = Path.GetFullPath(path);
        _files.RemoveAll(f => f.Path == full);
        if (AppointmentsPath == full)
        {
            Appointments = [];
            AppointmentsPath = null;
            Traces = null;
        }
    }

    public void Show(ViewState view) => View = view;

    public void Remember(BuiltInquiry built)
    {
        Traces = built.Traces;
        Inquiry = Inquiry with { ControlNumber = Inquiry.ControlNumber + 1 };
    }
}

public sealed class HeadlessDesk(TextWriter? log = null, bool seeRealValues = false, InquirySettings? inquiry = null, Func<DateTime>? now = null) : IExplorerDesk
{
    public IExplorerSession Session { get; } = new HeadlessSession(seeRealValues, inquiry, now);

    public string? Busy => null;

    public void Report(string text) => log?.WriteLine(text);
}
