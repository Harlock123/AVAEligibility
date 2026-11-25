using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using AVAEligibility.Core.Eligibility;
using AVAEligibility.Core.Inquiries;
using AVAEligibility.Core.Reports;

namespace AVAEligibility;

/// <summary>
/// The Worklist tab: every appointment, by day, with its answer read for that visit (ready, check, act first, rejected,
/// no answer), what to do, and what the patient may owe; and the worklist exported as a page or a workbook.
/// </summary>
public sealed partial class MainWindow
{
    private IReadOnlyList<WorkItem> _work = [];

    public StackPanel WorklistPanel { get; } = new() { Margin = new Thickness(20, 14), Spacing = 2 };

    /// <summary>The worklist as it stands: the open appointment list against the open answers.</summary>
    public IReadOnlyList<WorkItem> Work => _work;

    private partial IEnumerable<TabItem> LaterTabs() =>
    [
        new TabItem { Header = "Worklist", Content = new ScrollViewer { Content = WorklistPanel }, FontSize = 14 },
        .. EvenLaterTabs(),
    ];

    private partial IEnumerable<TabItem> EvenLaterTabs();

    private partial IEnumerable<object> LaterViewItems(Func<string, Action, Key?, KeyModifiers, MenuItem> item) =>
    [
        item("_Worklist", () => Tabs.SelectedIndex = 2, Key.D3, KeyModifiers.Control),
        new Separator(),
        item("_Export the Worklist…", () => _ = ExportWorklist(), Key.E, KeyModifiers.Control),
    ];

    partial void AppointmentsOpened()
    {
        FillWorklist();
        FillList();
    }

    partial void AnswersChanged() => FillWorklist();

    private partial DateOnly? VisitDate(Person p) => WorkItemFor(p)?.Appointment.Date;

    private partial WorkItem? WorkItemFor(Person p) => _work.FirstOrDefault(w => w.Answer.Person == p);

    private void FillWorklist()
    {
        _work = _appointments.Count == 0 ? [] : Worklist.Build(_appointments, _open, _built?.Traces, Settings.Inquiry.TraceOriginator is { Length: > 0 } o ? o : null);
        WorklistPanel.Children.Clear();
        WorklistPanel.Children.Add(Heading("Worklist"));
        if (_work.Count == 0)
        {
            WorklistPanel.Children.Add(Dim("Open an appointment list and the payers' 271s: each visit then shows whether it's ready, what to do first, and what the patient may owe."));
            return;
        }
        var answerFiles = _open.Count(f => f.Transactions.Any(t => t.IsResponse));
        WorklistPanel.Children.Add(Dim(_work.All(w => w.Appointment.Denied)
            ? $"{System.IO.Path.GetFileName(_appointmentsPath)}: denied claims, each answer read for its denial, against {answerFiles} answer file(s)."
            : $"{System.IO.Path.GetFileName(_appointmentsPath)} against {answerFiles} answer file(s). Estimates are estimates."));
        var counts = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 18, Margin = new Thickness(0, 8, 0, 4) };
        foreach (var g in _work.GroupBy(w => w.Word).OrderBy(g => g.Min(w => (int)w.Verdict)))
            counts.Children.Add(new TextBlock { Text = $"{g.Count()} {g.Key}", Foreground = BrushOf(g.First().Verdict), FontWeight = FontWeight.SemiBold });
        WorklistPanel.Children.Add(counts);
        var export = new Button { Content = "Export the Worklist…", Margin = new Thickness(0, 4, 0, 4) };
        export.Click += async (_, _) => await ExportWorklist();
        WorklistPanel.Children.Add(export);
        foreach (var day in _work.GroupBy(w => w.Appointment.Date))
        {
            WorklistPanel.Children.Add(Section(day.Key?.ToString("dddd d MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture) ?? "No date"));
            foreach (var w in day) WorklistPanel.Children.Add(WorkRow(w));
        }
    }

    private Control WorkRow(WorkItem w)
    {
        var a = w.Appointment;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("60,200,90,*,110") };
        void Put(Control c, int col)
        {
            Grid.SetColumn(c, col);
            grid.Children.Add(c);
        }
        Put(new TextBlock { Text = a.Time, Foreground = Look.Dim }, 0);
        Put(new TextBlock { Text = a.SortName, Foreground = Look.Text, TextTrimming = TextTrimming.CharacterEllipsis }, 1);
        Put(new TextBlock { Text = w.Word, Foreground = BrushOf(w.Verdict), FontWeight = FontWeight.SemiBold }, 2);
        var what = new StackPanel();
        what.Children.Add(new TextBlock { Text = $"{w.Headline}", Foreground = Look.Text, TextWrapping = TextWrapping.Wrap });
        var fix = w.Notes.Where(n => n.Level == w.Verdict).Select(n => n.Fix).FirstOrDefault();
        if (fix is { Length: > 0 } && w.Verdict != Verdict.Ready) what.Children.Add(new TextBlock { Text = fix, Foreground = Look.Dim, TextWrapping = TextWrapping.Wrap });
        what.Children.Add(new TextBlock { Text = $"{(a.SelfPay ? "self-pay" : a.Payer)} · {a.Visit} {a.Procedure}", Foreground = Look.Gutter, FontSize = 12 });
        if (a.Denied) what.Children.Add(new TextBlock { Text = $"Denied{(a.Account.Length > 0 ? $" ({a.Account})" : "")}: {a.Denial}", Foreground = Look.Gutter, FontSize = 12, TextWrapping = TextWrapping.Wrap });
        Put(what, 3);
        Put(new TextBlock { Text = w.Estimate is { } e ? Words.Money(e.Amount) : "", Foreground = Look.Accent, HorizontalAlignment = HorizontalAlignment.Right }, 4);
        var border = new Border { Child = grid, Padding = new Thickness(6, 5), Background = Brushes.Transparent, BorderBrush = Look.Border, BorderThickness = new Thickness(0, 0, 0, 1) };
        if (w.Answer.Person is { } p)
        {
            border.Cursor = new Cursor(StandardCursorType.Hand);
            border.PointerPressed += (_, _) => { ShowPerson(p); Tabs.SelectedIndex = 0; };
        }
        else if (w.Answer.Refusal is { } t)
        {
            border.Cursor = new Cursor(StandardCursorType.Hand);
            border.PointerPressed += (_, _) => { ShowTransaction(t); Tabs.SelectedIndex = 0; };
        }
        return border;
    }

    /// <summary>Writes the worklist as an .html page or an .xlsx workbook, by the name the person picks.</summary>
    public async Task ExportWorklist()
    {
        if (_work.Count == 0)
        {
            Say("There's no worklist yet: open an appointment list and the payers' answers.");
            return;
        }
        if (await PickSaveFile("Export the worklist (.html or .xlsx)", $"worklist-{Now():yyyy-MM-dd}.html") is not { } path) return;
        var title = $"Worklist: {System.IO.Path.GetFileNameWithoutExtension(_appointmentsPath)}";
        if (path.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)) WorklistReport.Excel(path, _work);
        else await File.WriteAllTextAsync(path, WorklistReport.Html(_work, title, Now()));
        Say($"Wrote {System.IO.Path.GetFileName(path)}: {_work.Count} visits.");
    }
}
