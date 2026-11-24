using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using AVAEligibility.Core.Inquiries;
using AVAEligibility.Core.Sending;

namespace AVAEligibility;

/// <summary>
/// Sending in real time: the clearinghouse the person sets up (off until they turn it on), and Ask Now, which asks
/// first, sends each appointment's 270 in a CORE envelope, saves each 271 beside the appointment list and opens them.
/// </summary>
public sealed partial class MainWindow
{
    public CheckBox SendEnabledBox { get; } = new() { Content = "Send inquiries to a clearinghouse in real time (CAQH CORE)" };
    public TextBox EndpointBox { get; } = new() { Width = 420, Watermark = "https://…" };
    public ComboBox EnvelopeBox { get; } = new() { ItemsSource = new[] { "mime", "soap" }, SelectedIndex = 0, Width = 100 };
    public TextBox CoreSenderBox { get; } = new() { Width = 160 };
    public TextBox CoreReceiverBox { get; } = new() { Width = 160 };
    public TextBox UserBox { get; } = new() { Width = 200 };
    public TextBox PasswordBox { get; } = new() { Width = 200, PasswordChar = '•' };

    /// <summary>Asks the person to confirm; tests answer it themselves.</summary>
    public Func<string, Task<bool>> AskSure { get; set; } = null!;

    /// <summary>Makes the HTTP client that talks to the clearinghouse; tests give their own.</summary>
    public Func<HttpClient> MakeHttp { get; set; } = () => new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

    /// <summary>The last real-time run.</summary>
    public IReadOnlyList<Asked> LastAsked { get; private set; } = [];

    private PasswordStore Passwords => new(SettingsFolder);
    private StackPanel? _sendingPart;

    partial void AddSendingSection(StackPanel panel)
    {
        AskSure ??= AskYesNo;
        panel.Children.Add(Section("Clearinghouse"));
        panel.Children.Add(SendingPart());
    }

    private StackPanel SendingPart()
    {
        if (_sendingPart != null) return _sendingPart;
        var c = Settings.Clearinghouse;
        SendEnabledBox.IsChecked = c.Enabled;
        EndpointBox.Text = c.Endpoint;
        EnvelopeBox.SelectedItem = c.Envelope;
        CoreSenderBox.Text = c.SenderId;
        CoreReceiverBox.Text = c.ReceiverId;
        UserBox.Text = c.Username;
        PasswordBox.Text = Passwords.Read() ?? "";
        var details = new StackPanel { Spacing = 2, IsVisible = c.Enabled };
        SendEnabledBox.IsCheckedChanged += (_, _) => details.IsVisible = SendEnabledBox.IsChecked == true;
        details.Children.Add(Field("Address", EndpointBox, "Envelope", EnvelopeBox));
        details.Children.Add(Field("CORE sender ID", CoreSenderBox, "CORE receiver ID", CoreReceiverBox));
        details.Children.Add(Field("User name", UserBox, "Password", PasswordBox));
        var ask = new Button { Content = "Ask Now…", Margin = new Thickness(0, 6, 0, 0) };
        ask.Click += async (_, _) => await AskNow();
        details.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 12,
            Children = { ask, new TextBlock { Text = "One request per appointment; each answer is saved as a 271 beside the appointment list and opened.", Foreground = Look.Dim, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap } },
        });
        _sendingPart = new StackPanel { Spacing = 4, Children = { SendEnabledBox, new TextBlock { Text = "Off unless you turn it on. The password is kept in a file of its own that only you can read; the address must be HTTPS.", Foreground = Look.Dim, TextWrapping = TextWrapping.Wrap }, details } };
        return _sendingPart;
    }

    /// <summary>The clearinghouse as the boxes have it, saved with its password.</summary>
    public Clearinghouse ReadClearinghouse()
    {
        var c = new Clearinghouse
        {
            Enabled = SendEnabledBox.IsChecked == true,
            Endpoint = EndpointBox.Text?.Trim() ?? "",
            Envelope = EnvelopeBox.SelectedItem as string ?? "mime",
            SenderId = CoreSenderBox.Text?.Trim() ?? "",
            ReceiverId = CoreReceiverBox.Text?.Trim() ?? "",
            Username = UserBox.Text?.Trim() ?? "",
        };
        Settings.Clearinghouse = c;
        Settings.Save();
        if (PasswordBox.Text is { Length: > 0 } pw) Passwords.Save(pw);
        return c;
    }

    /// <summary>Asks the clearinghouse about every appointment that can be asked, after the person says yes.</summary>
    public async Task AskNow()
    {
        if (_appointments.Count == 0)
        {
            Say("Open an appointment list first.");
            return;
        }
        var ch = ReadClearinghouse();
        if (ch.Problem() is { } problem)
        {
            Say(problem);
            return;
        }
        var settings = ReadSettings();
        int count;
        try
        {
            count = InquiryBuilder.Build(_appointments, settings, Now()).Traces.Count;
        }
        catch (InvalidOperationException e)
        {
            Say(e.Message);
            return;
        }
        if (!await AskSure($"Send {count} eligibility questions to {new Uri(ch.Endpoint).Host}?\n\nEach holds a patient's name, birth date and member ID. The answers are saved beside the appointment list."))
        {
            Say("Nothing was sent.");
            return;
        }
        var folder = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(_appointmentsPath!)!, $"{System.IO.Path.GetFileNameWithoutExtension(_appointmentsPath)}-answers-{Now():yyyyMMdd-HHmm}");
        using var http = MakeHttp();
        IReadOnlyList<Asked> asked;
        try
        {
            asked = await RealTime.AskAsync(_appointments, settings, ch, Passwords.Read() ?? "", new CoreClient(http), folder, Now(), new Progress<string>(Say));
        }
        catch (InvalidOperationException e)
        {
            Say(e.Message);
            return;
        }
        LastAsked = asked;
        Settings.Inquiry = settings with { ControlNumber = RealTime.NextControl(settings.ControlNumber, asked.Count) };
        Settings.Save();
        _built = InquiryBuilder.Build(_appointments, settings, Now());
        foreach (var a in asked.Where(a => a.Saved != null)) Open(a.Saved!);
        FillInquiries();
        FillWorklist();
        FillList();
        Tabs.SelectedIndex = 2;
        var failed = asked.Where(a => !a.Response.Ok).ToList();
        Say($"Asked {asked.Count}: {asked.Count - failed.Count} answered" + (failed.Count > 0 ? $", {failed.Count} not ({failed[0].Appointment.SortName}: {failed[0].Response.ErrorCode} {failed[0].Response.ErrorMessage})." : "."));
    }

    private async Task<bool> AskYesNo(string question)
    {
        var answer = false;
        var yes = new Button { Content = "Yes", MinWidth = 90, IsDefault = true };
        var no = new Button { Content = "No", MinWidth = 90, IsCancel = true };
        var dialog = new Window
        {
            Title = "AVA Eligibility", Width = 520, SizeToContent = SizeToContent.Height, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Look.Window,
            Content = new StackPanel
            {
                Margin = new Thickness(20), Spacing = 14,
                Children = { new TextBlock { Text = question, TextWrapping = TextWrapping.Wrap, Foreground = Look.Text }, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { yes, no } } },
            },
        };
        yes.Click += (_, _) => { answer = true; dialog.Close(); };
        no.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(this);
        return answer;
    }
}
