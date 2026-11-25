using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using AVAEligibility.Core.Authorizations;
using AVAEligibility.Core.Eligibility;
using AVAEligibility.Core.Inquiries;

namespace AVAEligibility;

/// <summary>
/// The Authorizations tab: the visits whose care the payers' 271s say needs authorization, each with its 278: not asked,
/// asked and waiting, or the reviewer's decision (certified with the number for the claim, pended, refused); Write the
/// 278 for those not answered; and the open 278 files. Opening a 278 answer updates the worklist.
/// </summary>
public sealed partial class MainWindow
{
    private readonly List<AuthFile> _reviews = [];

    public StackPanel AuthPanel { get; } = new() { Margin = new Thickness(20, 14), Spacing = 4 };

    /// <summary>The open 278s, requests and answers.</summary>
    public IReadOnlyList<AuthFile> Reviews => _reviews;

    /// <summary>The visits on the worklist that need authorization.</summary>
    public IReadOnlyList<AuthNeed> AuthNeeds => AuthRequests.Needed(_work);

    /// <summary>An agent wrote a 278 with the inquiry settings: the next control number, saved.</summary>
    internal void WroteAuthForAgent()
    {
        Settings.Inquiry = Settings.Inquiry with { ControlNumber = Settings.Inquiry.ControlNumber + 1 };
        Settings.Save();
    }

    private TabItem AuthTab() => new() { Header = "Authorizations", Content = new ScrollViewer { Content = AuthPanel }, FontSize = 14 };

    private void OpenAuth(string path)
    {
        var file = AuthReader.Load(path);
        if (file.Reviews.Count == 0)
        {
            Say($"{file.Name} has no review in it" + (file.Problems.Count > 0 ? $": {file.Problems[0]}" : "."));
            return;
        }
        _reviews.RemoveAll(f => f.Path == file.Path);
        _reviews.Add(file);
        FillWorklist();
        Say($"Opened {file.Name}: a 278 {(file.IsResponse ? "answer" : "request")} about {file.Reviews.Count} visit{(file.Reviews.Count == 1 ? "" : "s")}"
            + (file.IsResponse ? $": {string.Join(", ", file.Reviews.GroupBy(r => r.Word).Select(g => $"{g.Count()} {g.Key}"))}." : "."));
    }

    public void CloseAuth(AuthFile file)
    {
        _reviews.Remove(file);
        FillWorklist();
        Say($"Closed {file.Name}.");
    }

    /// <summary>Writes the 278 requests for the visits needing authorization that no open 278 answers, and opens it.</summary>
    public async Task WriteAuth()
    {
        var needs = AuthNeeds.Where(n => AuthRequests.For(n.Appointment, _reviews) is not { IsResponse: true }).ToList();
        if (needs.Count == 0)
        {
            Say(AuthNeeds.Count == 0 ? "No visit on the worklist needs authorization: open the appointment list and the payers' 271s first." : "Every visit that needs authorization has its 278 answer open.");
            return;
        }
        var settings = ReadSettings();
        BuiltAuth built;
        try
        {
            built = AuthRequests.Build(needs, settings, Now());
        }
        catch (InvalidOperationException e)
        {
            Say(e.Message);
            return;
        }
        if (await PickSaveFile("Write the 278", $"authorization-278-{Now():yyyyMMdd-HHmm}.x12") is not { } path) return;
        await File.WriteAllTextAsync(path, built.Text);
        Settings.Inquiry = settings with { ControlNumber = settings.ControlNumber + 1 };
        Settings.Save();
        OpenAuth(path);
        Tabs.SelectedIndex = 3;
        var left = built.Checks.Where(c => c.Level == CheckLevel.Error).Select(c => c.Appointment).Distinct().Count();
        Say($"Wrote {System.IO.Path.GetFileName(path)}: {built.Transactions} authorization request{(built.Transactions == 1 ? "" : "s")}" + (left > 0 ? $"; {left} left out for errors (see Authorizations)." : ". Send it through the clearinghouse, then open the answer."));
    }

    private void FillAuth()
    {
        AuthPanel.Children.Clear();
        AuthPanel.Children.Add(Heading("Authorizations"));
        AuthPanel.Children.Add(Dim("The visits whose care the payer's 271 says needs authorization. Write the 278 asks the reviewer (a 278 request, 005010X217); open the answer when it comes. Payers support the 278 unevenly: where one takes it only by phone or on its portal, use the number shown."));
        var needs = AuthNeeds;
        if (needs.Count == 0)
        {
            AuthPanel.Children.Add(Dim(_work.Count == 0 ? "Open an appointment list and the payers' 271s." : "No visit on the worklist needs authorization."));
        }
        else
        {
            var write = new Button { Content = "Write the 278…", Margin = new Thickness(0, 6, 0, 6) };
            write.Click += async (_, _) => await WriteAuth();
            AuthPanel.Children.Add(write);
            var checks = AuthRequests.Check(needs);
            foreach (var n in needs) AuthPanel.Children.Add(AuthRow(n, checks.Where(c => c.Appointment == n.Appointment).ToList()));
        }
        if (_reviews.Count > 0)
        {
            AuthPanel.Children.Add(Section("Open 278s"));
            foreach (var f in _reviews)
            {
                var row = new DockPanel();
                var close = new Button { Content = "Close", FontSize = 12 };
                close.Click += (_, _) => CloseAuth(f);
                DockPanel.SetDock(close, Dock.Right);
                row.Children.Add(close);
                row.Children.Add(new TextBlock { Text = $"{f.Name}: {(f.IsResponse ? "answer" : "request")}, {string.Join(", ", f.Reviews.Select(r => $"{r.SortName} {r.Word}"))}", Foreground = Look.Text, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
                AuthPanel.Children.Add(row);
            }
        }
    }

    private Control AuthRow(AuthNeed n, IReadOnlyList<InquiryCheck> checks)
    {
        var a = n.Appointment;
        var review = AuthRequests.For(a, _reviews);
        var note = review != null ? AuthRequests.Read(review, n) : null;
        var word = review?.Word ?? "not asked";
        var brush = note == null ? Look.Deny : BrushOf(note.Level);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("200,110,*") };
        void Put(Control c, int col)
        {
            Grid.SetColumn(c, col);
            grid.Children.Add(c);
        }
        Put(new TextBlock { Text = a.SortName, Foreground = Look.Text }, 0);
        Put(new TextBlock { Text = word, Foreground = brush, FontWeight = FontWeight.SemiBold }, 1);
        var what = new StackPanel();
        what.Children.Add(new TextBlock { Text = $"{a.Date:yyyy-MM-dd} · {a.Visit} {a.Procedure} ({Codes.ServiceType.Say(n.Care)}) · {a.Diagnosis}", Foreground = Look.Text, TextWrapping = TextWrapping.Wrap });
        what.Children.Add(new TextBlock { Text = note != null ? $"{note.Text} {note.Fix}" : $"Ask {n.UmoName}" + (n.UmoPhone.Length > 0 ? $" ({Interpreter.Phone(n.UmoPhone)})" : "") + ": write the 278, or call.", Foreground = Look.Dim, TextWrapping = TextWrapping.Wrap });
        foreach (var c in checks) what.Children.Add(new TextBlock { Text = c.Text, Foreground = c.Level == CheckLevel.Error ? Look.Reject : Look.Warn, FontSize = 12, TextWrapping = TextWrapping.Wrap });
        Put(what, 2);
        return new Border { Child = grid, Padding = new Thickness(6, 5), BorderBrush = Look.Border, BorderThickness = new Thickness(0, 0, 0, 1) };
    }
}
