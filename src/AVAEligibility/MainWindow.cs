using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using AVAEligibility.Core.Eligibility;
using AVAEligibility.Core.X12;

namespace AVAEligibility;

/// <summary>An entry in the list on the left: a file, a payer's transaction in it, or a person.</summary>
public sealed record ListEntry(EligibilityFile File, EligibilityTransaction? Transaction, Person? Person)
{
    public override string ToString() => Person?.SortName ?? Transaction?.Payer?.Name ?? File.Name;
}

/// <summary>
/// The window: the open 270s and 271s on the left, by payer and person, each marked with the answer; the Coverage tab
/// (the person's answer in plain words, with the segments behind it below, the ones behind the selected line
/// highlighted). It shows real values. Built in code; its controls are reachable from tests.
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly List<EligibilityFile> _open = [];
    private Person? _person;
    private EligibilityTransaction? _transaction;
    private bool _filling;

    public ListBox FileList { get; } = new() { Background = Look.Bar };
    public TabControl Tabs { get; } = new();
    public StackPanel CoveragePanel { get; } = new() { Margin = new Thickness(20, 14), Spacing = 4 };
    public SelectableTextBlock Segments { get; } = new() { FontFamily = Look.Mono, FontSize = 12.5, Foreground = Look.Code, Margin = new Thickness(8) };
    public ScrollViewer SegmentScroll { get; } = new() { HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, Background = Look.Sheet };
    public TextBlock txtStatusBar { get; } = new() { Foreground = Look.Heading, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };

    /// <summary>Asks for files to open; tests answer it themselves.</summary>
    public Func<Task<IReadOnlyList<string>>> PickFiles { get; set; }

    /// <summary>Asks where to save, given a title and the name to suggest; tests answer it themselves.</summary>
    public Func<string, string, Task<string?>> PickSaveFile { get; set; }

    public IReadOnlyList<EligibilityFile> OpenFiles => _open;
    public Person? CurrentPerson => _person;
    public EligibilityTransaction? CurrentTransaction => _transaction;

    public MainWindow() : this(null) { }

    public MainWindow(IEnumerable<string>? open)
    {
        Title = "AVA Eligibility";
        Width = 1440;
        Height = 880;
        MinWidth = 800;
        MinHeight = 500;
        Background = Look.Window;
        PickFiles = AskForFiles;
        PickSaveFile = AskWhereToSave;

        var root = new DockPanel();
        var menu = BuildMenu();
        DockPanel.SetDock(menu, Dock.Top);
        root.Children.Add(menu);
        var status = new Border { Child = txtStatusBar, Background = Look.Bar, BorderBrush = Look.Border, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(12, 5) };
        DockPanel.SetDock(status, Dock.Bottom);
        root.Children.Add(status);
        AddExtraDocks(root);

        var main = new Grid { ColumnDefinitions = new ColumnDefinitions("300,4,*") };
        var left = new DockPanel { Background = Look.Bar };
        var leftTitle = new TextBlock { Text = "FILES AND PEOPLE", Foreground = Look.Heading, FontSize = 11, Margin = new Thickness(10, 8, 10, 4) };
        DockPanel.SetDock(leftTitle, Dock.Top);
        left.Children.Add(leftTitle);
        left.Children.Add(FileList);
        FileList.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<ListEntry>((e, _) => e == null ? new TextBlock() : EntryLabel(e));
        FileList.SelectionChanged += (_, _) =>
        {
            if (_filling || FileList.SelectedItem is not ListEntry e) return;
            if (e.Person != null) ShowPerson(e.Person);
            else if (e.Transaction != null) ShowTransaction(e.Transaction);
            else if (e.File.Transactions.FirstOrDefault() is { } t) ShowTransaction(t);
        };
        main.Children.Add(left);
        var split = new GridSplitter { Background = Look.Border, ResizeDirection = GridResizeDirection.Columns };
        Grid.SetColumn(split, 1);
        main.Children.Add(split);
        Grid.SetColumn(Tabs, 2);
        main.Children.Add(Tabs);
        Tabs.ItemsSource = BuildTabs();
        root.Children.Add(main);
        Content = root;

        AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            foreach (var f in e.Data.GetFiles() ?? []) if (f.TryGetLocalPath() is { } p) Open(p);
        });
        DragDrop.SetAllowDrop(this, true);
        ShowNothing();
        Say("Open 270 and 271 files (File › Open, Ctrl+O, or drop them here). Try the samples folder.");
        AfterBuild();
        foreach (var path in open ?? []) Open(path);
    }

    /// <summary>The tabs, in order. Later parts add theirs.</summary>
    private List<TabItem> BuildTabs() =>
    [
        new TabItem { Header = "Coverage", Content = BuildCoveragePage(), FontSize = 14 },
        .. MoreTabs(),
    ];

    partial void AddExtraDocks(DockPanel root);
    partial void AfterBuild();
    private partial IEnumerable<TabItem> MoreTabs();
    private partial IEnumerable<object> MoreMenus(Func<string, Action, Key?, KeyModifiers, MenuItem> item);
    private partial IEnumerable<object> MoreFileItems(Func<string, Action, Key?, KeyModifiers, MenuItem> item);
    private partial IEnumerable<object> MoreViewItems(Func<string, Action, Key?, KeyModifiers, MenuItem> item);

    private Menu BuildMenu()
    {
        MenuItem Item(string header, Action click, Key? key = null, KeyModifiers mods = KeyModifiers.Control)
        {
            var m = new MenuItem { Header = header };
            if (key is { } k) m.InputGesture = new KeyGesture(k, mods);
            m.Click += (_, _) => click();
            return m;
        }
        KeyBindings.Add(new KeyBinding { Gesture = new KeyGesture(Key.O, KeyModifiers.Control), Command = new Command(() => _ = OpenPicked()) });
        var file = new MenuItem
        {
            Header = "_File",
            ItemsSource = (object[])
            [
                Item("_Open…", () => _ = OpenPicked(), Key.O, KeyModifiers.Control),
                Item("_Close File", CloseCurrent, Key.W, KeyModifiers.Control),
                new Separator(),
                .. MoreFileItems(Item),
                Item("E_xit", Close, null, KeyModifiers.None),
            ],
        };
        var view = new MenuItem
        {
            Header = "_View",
            ItemsSource = (object[])
            [
                Item("_Coverage", () => Tabs.SelectedIndex = 0, Key.D1, KeyModifiers.Control),
                .. MoreViewItems(Item),
            ],
        };
        return new Menu { Background = Look.Bar, ItemsSource = (object[])[file, view, .. MoreMenus(Item)] };
    }

    // ----- Opening -----

    /// <summary>Opens a file (or every X12 file in a folder). Later parts take files that aren't X12 (appointment lists).</summary>
    public void Open(string path)
    {
        if (Directory.Exists(path))
        {
            foreach (var f in Directory.EnumerateFiles(path).Order(StringComparer.Ordinal).Where(f => X12File.LooksLikeX12(f) || Takes(f))) Open(f);
            return;
        }
        try
        {
            var full = System.IO.Path.GetFullPath(path);
            if (Takes(full))
            {
                OpenOther(full);
                return;
            }
            var file = EligibilityReader.Load(full);
            if (file.Transactions.Count == 0)
            {
                Say($"{file.Name} has no 270 or 271 in it" + (file.Problems.Count > 0 ? $": {file.Problems[0]}" : "."));
                return;
            }
            _open.RemoveAll(o => o.Path == full);
            _open.Add(file);
            FileOpened(file);
            FillList();
            var first = file.People.FirstOrDefault();
            if (first != null) ShowPerson(first);
            else ShowTransaction(file.Transactions[0]);
            Say($"Opened {file.Name}: {Summary(file)}" + (file.Problems.Count > 0 ? $" {file.Problems.Count} problem{(file.Problems.Count == 1 ? "" : "s")} reading it: {file.Problems[0]}" : ""));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Say($"Couldn't open {System.IO.Path.GetFileName(path)}: {e.Message}");
        }
    }

    /// <summary>Whether a later part opens this file itself (an appointment list).</summary>
    private partial bool Takes(string path);
    private partial void OpenOther(string path);
    partial void FileOpened(EligibilityFile file);

    /// <summary>"a 271 from 1 payer about 9 people".</summary>
    public static string Summary(EligibilityFile f)
    {
        var payers = f.Transactions.Select(t => t.Payer?.Name).Distinct().Count();
        var people = f.People.Count();
        return $"a {f.Kind} from {payers} payer{(payers == 1 ? "" : "s")} about {people} {(people == 1 ? "person" : "people")}.";
    }

    private async Task OpenPicked()
    {
        foreach (var p in await PickFiles()) Open(p);
    }

    private void CloseCurrent()
    {
        var file = _person?.Transaction is { } t ? _open.FirstOrDefault(f => f.Transactions.Contains(t)) : _transaction is { } tt ? _open.FirstOrDefault(f => f.Transactions.Contains(tt)) : null;
        if (file == null) return;
        _open.Remove(file);
        _person = null;
        _transaction = null;
        FillList();
        ShowNothing();
        Say($"Closed {file.Name}.");
    }

    // ----- The list -----

    private void FillList()
    {
        _filling = true;
        var entries = new List<ListEntry>();
        foreach (var f in _open)
        {
            entries.Add(new ListEntry(f, null, null));
            foreach (var t in f.Transactions)
            {
                entries.Add(new ListEntry(f, t, null));
                entries.AddRange(t.People.Select(p => new ListEntry(f, t, p)));
            }
        }
        FileList.ItemsSource = entries;
        _filling = false;
    }

    private Control EntryLabel(ListEntry e)
    {
        if (e.Person is { } p)
        {
            var (word, brush) = Mark(p);
            var row = new DockPanel { Margin = new Thickness(p.IsDependent ? 34 : 20, 0, 0, 0) };
            var badge = new TextBlock { Text = word, Foreground = brush, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            DockPanel.SetDock(badge, Dock.Right);
            row.Children.Add(badge);
            row.Children.Add(new TextBlock { Text = p.SortName, Foreground = Look.Text, TextTrimming = TextTrimming.CharacterEllipsis });
            return row;
        }
        if (e.Transaction is { } t)
        {
            var refused = t.Payer?.Rejections.Count > 0;
            return new TextBlock
            {
                Text = $"{(t.IsResponse ? "answer from" : "asking")} {t.Payer?.Name ?? "no payer"}",
                Foreground = refused ? Look.Reject : Look.Heading, FontSize = 12, Margin = new Thickness(10, 2, 0, 0),
            };
        }
        return new TextBlock { Text = $"{e.File.Name}  ·  {e.File.Kind}", Foreground = Look.Accent, FontWeight = FontWeight.SemiBold };
    }

    /// <summary>The person's mark in the list. Later parts give the verdict; until then, the answer's first word.</summary>
    internal (string Word, IBrush Brush) Mark(Person p) => MarkOf(p) ?? (!p.Transaction.IsResponse ? ("asked", Look.Accent)
        : p.Rejections.Count > 0 ? ("rejected", Look.Reject)
        : p.Benefits.Any(b => b.Information is "6" or "7" or "8") ? ("inactive", Look.Deny)
        : p.Benefits.Any(b => b.Information is "1" or "2" or "3" or "4" or "5") ? ("active", Look.Clean)
        : ("no answer", Look.Warn));

    private partial (string, IBrush)? MarkOf(Person p);

    // ----- The Coverage tab -----

    private Control BuildCoveragePage()
    {
        var grid = new Grid { RowDefinitions = new RowDefinitions("3*,4,2*") };
        grid.Children.Add(new ScrollViewer { Content = CoveragePanel });
        var split = new GridSplitter { Background = Look.Border, ResizeDirection = GridResizeDirection.Rows };
        Grid.SetRow(split, 1);
        grid.Children.Add(split);
        SegmentScroll.Content = Segments;
        Grid.SetRow(SegmentScroll, 2);
        grid.Children.Add(SegmentScroll);
        return grid;
    }

    private void ShowNothing()
    {
        CoveragePanel.Children.Clear();
        CoveragePanel.Children.Add(Heading("No one chosen"));
        CoveragePanel.Children.Add(Dim("Open a 271 (the payer's answer) or a 270 (the questions) and choose a person on the left."));
        ShowSegments([], []);
    }

    /// <summary>Shows a payer's transaction: who asked whom, and the payer's own rejection when it couldn't answer at all.</summary>
    public void ShowTransaction(EligibilityTransaction t)
    {
        _transaction = t;
        _person = null;
        CoveragePanel.Children.Clear();
        CoveragePanel.Children.Add(Heading(t.Payer?.Name ?? "No payer"));
        CoveragePanel.Children.Add(Dim($"{(t.IsResponse ? "A 271: the payer's answer" : "A 270: the questions")} · {t.Payer?.IdQualifier} {t.Payer?.Id} · reference {t.Reference}"
            + (t.Created is { } c ? $" · made {c:yyyy-MM-dd HH:mm}" : "")));
        if (t.Provider is { } prov) CoveragePanel.Children.Add(Dim($"Provider: {prov.Name} ({prov.IdQualifier} {prov.Id})"));
        foreach (var r in t.Payer?.Rejections ?? []) CoveragePanel.Children.Add(Line(Words.Describe(r), Look.Reject, [r.Segment]));
        foreach (var r in t.Provider?.Rejections ?? []) CoveragePanel.Children.Add(Line(Words.Describe(r), Look.Reject, [r.Segment]));
        foreach (var who in t.Payer?.Contacts ?? []) CoveragePanel.Children.Add(Dim($"Contact: {who.Name} {string.Join(", ", who.Numbers.Select(n => $"{n.Kind} {n.Number}"))}".Trim()));
        CoveragePanel.Children.Add(Section($"{t.People.Count} {(t.People.Count == 1 ? "person" : "people")}"));
        foreach (var p in t.People) CoveragePanel.Children.Add(Line($"{p.SortName}: {Mark(p).Word}", Mark(p).Brush, p.Segments));
        ShowSegments([.. t.Source.Body], [.. (t.Payer?.Segments ?? []).Concat(t.Provider?.Segments ?? [])]);
        ShowingTransaction(t);
    }

    partial void ShowingTransaction(EligibilityTransaction t);

    /// <summary>Shows one person's question or answer, in words.</summary>
    public void ShowPerson(Person p)
    {
        _person = p;
        _transaction = p.Transaction;
        CoveragePanel.Children.Clear();
        CoveragePanel.Children.Add(Heading(p.Name.Length > 0 ? p.Name : "(no name)"));
        var facts = new List<string> { p.Transaction.Payer?.Name ?? "no payer" };
        if (p.MemberId.Length > 0) facts.Add($"member {p.MemberId}");
        if (p.IsDependent && p.Subscriber is { } s) facts.Add($"dependent of {s.Name}");
        if (p.BirthDate is { } born) facts.Add($"born {born:yyyy-MM-dd}");
        if (p.Gender.Length > 0) facts.Add(p.Gender switch { "M" => "male", "F" => "female", _ => "gender unknown" });
        if (p.Relationship.Length > 0) facts.Add(Codes.Relationship.Say(p.Relationship).ToLowerInvariant());
        if (p.Trace is { } trace) facts.Add($"trace {trace}");
        CoveragePanel.Children.Add(Dim(string.Join(" · ", facts)));
        if (p.Address.Length > 0) CoveragePanel.Children.Add(Dim(p.Address));
        if (!ShowAnswer(p)) ShowLines(p);
        ShowSegments(p.Segments, []);
    }

    /// <summary>A later part's view of the answer (the interpretation); false shows the lines as they are.</summary>
    private partial bool ShowAnswer(Person p);

    /// <summary>The person's lines, each in words, in the file's order.</summary>
    private void ShowLines(Person p)
    {
        foreach (var r in p.Rejections) CoveragePanel.Children.Add(Line(Words.Describe(r), Look.Reject, [r.Segment]));
        if (p.References.Count > 0)
        {
            CoveragePanel.Children.Add(Section("Identifiers"));
            foreach (var r in p.References) CoveragePanel.Children.Add(Dim($"{r.Kind}: {r.Value}" + (r.Description.Length > 0 ? $" ({r.Description})" : "")));
        }
        if (p.Dates.Count > 0)
        {
            CoveragePanel.Children.Add(Section("Dates"));
            foreach (var d in p.Dates) CoveragePanel.Children.Add(Line($"{d.Kind}: {d}", Look.Text, [d.Segment]));
        }
        if (p.Inquiries.Count > 0)
        {
            CoveragePanel.Children.Add(Section("Asked about"));
            foreach (var q in p.Inquiries) CoveragePanel.Children.Add(Line(Words.Describe(q), Look.Text, [q.Segment]));
        }
        if (p.Benefits.Count > 0)
        {
            CoveragePanel.Children.Add(Section($"The answer: {p.Benefits.Count} line{(p.Benefits.Count == 1 ? "" : "s")}"));
            foreach (var b in p.Benefits) CoveragePanel.Children.Add(BenefitLine(b));
        }
        else if (p.Transaction.IsResponse && p.Rejections.Count == 0) CoveragePanel.Children.Add(Line("The payer sent no benefit lines.", Look.Warn, []));
    }

    /// <summary>A benefit in words, with its messages and entities under it; clicking it highlights its segments.</summary>
    internal Control BenefitLine(Benefit b)
    {
        var brush = b.Information switch
        {
            "1" or "2" or "3" or "4" or "5" => Look.Clean,
            "6" or "7" or "8" or "I" or "V" => Look.Deny,
            _ => Look.Text,
        };
        var panel = new StackPanel { Spacing = 1 };
        panel.Children.Add(new TextBlock { Text = Words.Describe(b), Foreground = brush, TextWrapping = TextWrapping.Wrap });
        foreach (var m in b.Messages) panel.Children.Add(new TextBlock { Text = $"“{m}”", Foreground = Look.Dim, FontStyle = FontStyle.Italic, Margin = new Thickness(16, 0, 0, 0), TextWrapping = TextWrapping.Wrap });
        foreach (var e in b.Entities) panel.Children.Add(new TextBlock { Text = Words.Describe(e), Foreground = Look.Accent, Margin = new Thickness(16, 0, 0, 0), TextWrapping = TextWrapping.Wrap });
        foreach (var d in b.Dates) panel.Children.Add(new TextBlock { Text = $"{d.Kind}: {d}", Foreground = Look.Dim, Margin = new Thickness(16, 0, 0, 0) });
        foreach (var r in b.References) panel.Children.Add(new TextBlock { Text = $"{r.Kind}: {r.Value}", Foreground = Look.Dim, Margin = new Thickness(16, 0, 0, 0) });
        foreach (var r in b.Rejections) panel.Children.Add(new TextBlock { Text = Words.Describe(r), Foreground = Look.Reject, Margin = new Thickness(16, 0, 0, 0), TextWrapping = TextWrapping.Wrap });
        return Clickable(panel, b.Segments);
    }

    internal Control Line(string text, IBrush brush, IReadOnlyList<Segment> segments) =>
        Clickable(new TextBlock { Text = text, Foreground = brush, TextWrapping = TextWrapping.Wrap }, segments);

    private Control Clickable(Control inner, IReadOnlyList<Segment> segments)
    {
        var border = new Border { Child = inner, Padding = new Thickness(6, 3), CornerRadius = new CornerRadius(3), Background = Brushes.Transparent, Cursor = segments.Count > 0 ? new Cursor(StandardCursorType.Hand) : null };
        if (segments.Count > 0)
            border.PointerPressed += (_, _) => Highlight([.. segments.Select(s => s.Index)]);
        return border;
    }

    // ----- Segments -----

    private IReadOnlyList<Segment> _shown = [];

    private void ShowSegments(IReadOnlyList<Segment> segments, IReadOnlyList<Segment> highlighted)
    {
        _shown = segments;
        Highlight([.. highlighted.Select(s => s.Index)]);
    }

    /// <summary>Highlights the segments at these file positions, among those shown.</summary>
    public void Highlight(HashSet<int> indexes)
    {
        Segments.Inlines ??= [];
        Segments.Inlines.Clear();
        var delimiters = _open.FirstOrDefault(f => f.X12.Segments.Any(s => _shown.Count > 0 && ReferenceEquals(s, _shown[0])))?.X12.Delimiters ?? Delimiters.Standard;
        foreach (var s in _shown)
        {
            var hit = indexes.Contains(s.Index);
            Segments.Inlines.Add(new Run($"{s.Index + 1,5}  ") { Foreground = Look.Gutter });
            Segments.Inlines.Add(new Run(s.Id) { Foreground = Look.SegmentId, Background = hit ? Look.Highlight : null });
            Segments.Inlines.Add(new Run(s.Text[s.Id.Length..] + delimiters.Shown) { Background = hit ? Look.Highlight : null });
            Segments.Inlines.Add(new LineBreak());
        }
    }

    /// <summary>The text of the highlighted segments, for tests.</summary>
    public string Highlighted => string.Concat(Segments.Inlines?.OfType<Run>().Where(r => r.Background != null).Select(r => r.Text) ?? []);

    // ----- Helpers -----

    internal static TextBlock Heading(string text) => new() { Text = text, FontSize = 22, FontWeight = FontWeight.SemiBold, Foreground = Look.Text, TextWrapping = TextWrapping.Wrap };

    internal static TextBlock Section(string text) => new() { Text = text.ToUpperInvariant(), FontSize = 11, Foreground = Look.Heading, Margin = new Thickness(0, 14, 0, 4) };

    internal static TextBlock Dim(string text) => new() { Text = text, Foreground = Look.Dim, TextWrapping = TextWrapping.Wrap };

    private async Task<IReadOnlyList<string>> AskForFiles()
    {
        var picked = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Open 270s, 271s and appointment lists", AllowMultiple = true });
        return picked.Select(p => p.TryGetLocalPath()).OfType<string>().ToList();
    }

    private async Task<string?> AskWhereToSave(string title, string suggested)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = title, SuggestedFileName = suggested });
        return file?.TryGetLocalPath();
    }

    public void Say(string text) => txtStatusBar.Text = text;

    private sealed class Command(Action run) : System.Windows.Input.ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => run();
    }
}
