using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using AVAEligibility.Core.Eligibility;
using AVAEligibility.Core.Inquiries;

namespace AVAEligibility;

/// <summary>
/// The Inquiries tab: the appointment list with each row's checks, who's asking (the inquiry settings), Write the 270,
/// and every question in the open 270s with the answer the open 271s give it.
/// </summary>
public sealed partial class MainWindow
{
    private IReadOnlyList<Appointment> _appointments = [];
    private string? _appointmentsPath;
    private BuiltInquiry? _built;

    public AppSettings Settings { get; } = AppSettings.Load();
    public StackPanel InquiriesPanel { get; } = new() { Margin = new Thickness(20, 14), Spacing = 4 };
    public TextBox ProviderNameBox { get; } = new() { Width = 300 };
    public TextBox ProviderNpiBox { get; } = new() { Width = 140 };
    public TextBox SenderIdBox { get; } = new() { Width = 160 };
    public TextBox ReceiverIdBox { get; } = new() { Width = 160 };
    public TextBox TraceOriginatorBox { get; } = new() { Width = 140 };
    public CheckBox ProductionBox { get; } = new() { Content = "Production (ISA15 P; otherwise T, test)" };

    public IReadOnlyList<Appointment> Appointments => _appointments;
    public string? AppointmentsPath => _appointmentsPath;

    /// <summary>The 270 last written from the list, with the trace each appointment went under.</summary>
    public BuiltInquiry? LastBuilt => _built;

    /// <summary>Today, as the checks see it; tests set it.</summary>
    public Func<DateTime> Now { get; set; } = () => DateTime.Now;

    private partial IEnumerable<TabItem> MoreTabs() =>
    [
        new TabItem { Header = "Inquiries", Content = new ScrollViewer { Content = InquiriesPanel }, FontSize = 14 },
        .. LaterTabs(),
    ];

    private partial IEnumerable<TabItem> LaterTabs();

    private partial IEnumerable<object> MoreFileItems(Func<string, Action, Key?, KeyModifiers, MenuItem> item) =>
    [
        item("_Write the 270…", () => _ = WriteInquiry(), Key.S, KeyModifiers.Control | KeyModifiers.Shift),
        new Separator(),
    ];

    private partial IEnumerable<object> MoreViewItems(Func<string, Action, Key?, KeyModifiers, MenuItem> item) =>
    [
        item("_Inquiries", () => Tabs.SelectedIndex = 1, Key.D2, KeyModifiers.Control),
        .. LaterViewItems(item),
    ];

    private partial IEnumerable<object> LaterViewItems(Func<string, Action, Key?, KeyModifiers, MenuItem> item);

    private partial bool Takes(string path) => AppointmentList.LooksLikeOne(path);

    private partial void OpenOther(string path)
    {
        try
        {
            _appointments = AppointmentList.Load(path);
            _appointmentsPath = path;
            _built = null;
            FillInquiries();
            Tabs.SelectedIndex = 1;
            var checks = InquiryBuilder.Check(_appointments, DateOnly.FromDateTime(Now()));
            Say($"Opened {System.IO.Path.GetFileName(path)}: {_appointments.Count} appointments; {checks.Count(c => c.Level == CheckLevel.Error)} can't be asked as they are, {checks.Count(c => c.Level == CheckLevel.Warning)} warnings.");
            AppointmentsOpened();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            Say($"Couldn't read {System.IO.Path.GetFileName(path)}: {e.Message}");
        }
    }

    partial void AppointmentsOpened();

    partial void AddSendingSection(StackPanel panel);

    partial void FileOpened(EligibilityFile file)
    {
        FillInquiries();
        AnswersChanged();
    }

    partial void AnswersChanged();

    /// <summary>The settings as the boxes have them, saved.</summary>
    public InquirySettings ReadSettings()
    {
        var s = Settings.Inquiry with
        {
            ProviderName = ProviderNameBox.Text?.Trim() ?? "",
            ProviderNpi = ProviderNpiBox.Text?.Trim() ?? "",
            SenderId = SenderIdBox.Text?.Trim() ?? "",
            ReceiverId = ReceiverIdBox.Text?.Trim() ?? "",
            TraceOriginator = TraceOriginatorBox.Text?.Trim() ?? "",
            Usage = ProductionBox.IsChecked == true ? "P" : "T",
        };
        Settings.Inquiry = s;
        Settings.Save();
        return s;
    }

    /// <summary>Writes the 270 for the appointment list to a file the person picks, and opens it.</summary>
    public async Task WriteInquiry()
    {
        if (_appointments.Count == 0)
        {
            Say("Open an appointment list first (a CSV or Excel file with the patients, payers and member IDs).");
            return;
        }
        var settings = ReadSettings();
        BuiltInquiry built;
        try
        {
            built = InquiryBuilder.Build(_appointments, settings, Now());
        }
        catch (InvalidOperationException e)
        {
            Say(e.Message);
            Tabs.SelectedIndex = 1;
            return;
        }
        var suggested = $"inquiry-{Now():yyyyMMdd-HHmm}.x12";
        if (await PickSaveFile("Write the 270", suggested) is not { } path) return;
        await File.WriteAllTextAsync(path, built.Text);
        Settings.Inquiry = settings with { ControlNumber = settings.ControlNumber + 1 };
        Settings.Save();
        _built = built;
        Open(path);
        FillInquiries();
        Tabs.SelectedIndex = 1;
        var left = built.Checks.Where(c => c.Level == CheckLevel.Error).Select(c => c.Appointment).Distinct().Count();
        Say($"Wrote {System.IO.Path.GetFileName(path)}: {built.Traces.Count} questions to {built.Transactions} payer{(built.Transactions == 1 ? "" : "s")}" + (left > 0 ? $"; {left} left out for errors." : "."));
    }

    private void FillInquiries()
    {
        InquiriesPanel.Children.Clear();
        InquiriesPanel.Children.Add(Heading("Inquiries"));
        InquiriesPanel.Children.Add(Dim("Open an appointment list (CSV or Excel) to check it and write the 270; open the payers' 271s to see what came back."));

        InquiriesPanel.Children.Add(Section("Who's asking"));
        InquiriesPanel.Children.Add(SettingsPart());
        AddSendingSection(InquiriesPanel);

        if (_appointments.Count > 0)
        {
            var checks = InquiryBuilder.Check(_appointments, DateOnly.FromDateTime(Now()));
            InquiriesPanel.Children.Add(Section($"{System.IO.Path.GetFileName(_appointmentsPath)}: {_appointments.Count} appointments"));
            var write = new Button { Content = "Write the 270…", Margin = new Thickness(0, 4, 0, 8) };
            write.Click += async (_, _) => await WriteInquiry();
            InquiriesPanel.Children.Add(write);
            foreach (var a in _appointments) InquiriesPanel.Children.Add(AppointmentRow(a, checks.Where(c => c.Appointment == a).ToList()));
        }

        var asked = Matcher.All(_open);
        if (asked.Count > 0)
        {
            var answered = asked.Count(x => x.Answer.Person != null);
            InquiriesPanel.Children.Add(Section($"Questions and answers: {asked.Count} asked, {answered} answered"));
            foreach (var (p, answer) in asked)
            {
                var (word, brush, how) = answer switch
                {
                    { Person: { } a } => (Interpreter.Read(a).Word, BrushOf(Interpreter.Read(a).Verdict), $"by {answer.How}"),
                    { Refusal: { } t } => ("no answer", Look.Deny, $"{t.Payer?.Name}: {Words.Describe(t.Payer!.Rejections[0])}"),
                    _ => ("waiting", Look.Dim, "no 271 open answers it"),
                };
                var row = new DockPanel();
                var badge = new TextBlock { Text = word, Foreground = brush, Width = 90 };
                DockPanel.SetDock(badge, Dock.Left);
                row.Children.Add(badge);
                row.Children.Add(new TextBlock { Text = $"{p.SortName} · {p.Transaction.Payer?.Name} · trace {p.Trace} · {how}", Foreground = Look.Text, TextWrapping = TextWrapping.Wrap });
                var border = new Border { Child = row, Padding = new Thickness(6, 3), Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand) };
                var target = answer.Person ?? p;
                border.PointerPressed += (_, _) => { ShowPerson(target); Tabs.SelectedIndex = 0; };
                InquiriesPanel.Children.Add(border);
            }
        }
    }

    private StackPanel? _settingsPart;

    /// <summary>The settings boxes, made once and kept (they hold what the person typed), filled from the saved settings.</summary>
    private StackPanel SettingsPart()
    {
        if (_settingsPart != null) return _settingsPart;
        var s = Settings.Inquiry;
        ProviderNameBox.Text = s.ProviderName;
        ProviderNpiBox.Text = s.ProviderNpi;
        SenderIdBox.Text = s.SenderId;
        ReceiverIdBox.Text = s.ReceiverId;
        TraceOriginatorBox.Text = s.TraceOriginator;
        ProductionBox.IsChecked = s.Usage == "P";
        _settingsPart = new StackPanel { Spacing = 2 };
        _settingsPart.Children.Add(Field("Provider (2100B)", ProviderNameBox, "NPI", ProviderNpiBox));
        _settingsPart.Children.Add(Field("Sender ID (ISA06)", SenderIdBox, "Receiver ID (ISA08)", ReceiverIdBox));
        _settingsPart.Children.Add(Field("Trace originator (TRN03)", TraceOriginatorBox, null, null));
        _settingsPart.Children.Add(ProductionBox);
        return _settingsPart;
    }

    private static Control Field(string label, Control box, string? label2, Control? box2)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 2) };
        row.Children.Add(new TextBlock { Text = label, Foreground = Look.Heading, Width = 170, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(box);
        if (label2 != null && box2 != null)
        {
            row.Children.Add(new TextBlock { Text = label2, Foreground = Look.Heading, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(box2);
        }
        return row;
    }

    private Control AppointmentRow(Appointment a, IReadOnlyList<InquiryCheck> checks)
    {
        var worst = checks.Count == 0 ? (CheckLevel?)null : checks.Max(c => c.Level);
        var panel = new StackPanel { Spacing = 1 };
        var asks = a.SelfPay ? "nothing to ask" : string.Join(", ", ServiceTypes.For(a.Procedure).Select(t => Codes.ServiceType.Say(t)));
        var trace = _built != null && _built.Traces.TryGetValue(a, out var t) ? $" · sent as trace {t}" : "";
        panel.Children.Add(new TextBlock
        {
            Text = $"{a.Date:yyyy-MM-dd} {a.Time}  {a.SortName}  ·  {(a.SelfPay ? "self-pay" : $"{a.Payer} {a.MemberId}")}  ·  {a.Visit} {a.Procedure}  ·  asks: {asks}{trace}",
            Foreground = worst switch { CheckLevel.Error => Look.Reject, CheckLevel.Warning => Look.Warn, _ => Look.Text },
            TextWrapping = TextWrapping.Wrap,
        });
        foreach (var c in checks)
            panel.Children.Add(new TextBlock { Text = c.Text, Foreground = c.Level switch { CheckLevel.Error => Look.Reject, CheckLevel.Warning => Look.Warn, _ => Look.Dim }, Margin = new Thickness(16, 0, 0, 0), TextWrapping = TextWrapping.Wrap });
        return new Border { Child = panel, Padding = new Thickness(6, 3) };
    }
}
