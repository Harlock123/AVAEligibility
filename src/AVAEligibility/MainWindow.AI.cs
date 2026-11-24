using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using AVAEligibility.Mcp;

namespace AVAEligibility;

/// <summary>The AI side's place in the window: the chat's dock, the AI menu, and the view as agents see and set it.</summary>
public sealed partial class MainWindow
{
    public Border ChatHolder { get; } = new() { Width = 440, IsVisible = false, BorderBrush = Look.Border, BorderThickness = new Thickness(1, 0, 0, 0) };

    /// <summary>Where pictures of the view are drawn for agents, out of sight.</summary>
    public Panel AgentStage { get; } = new() { Width = 0, Height = 0, ClipToBounds = true, IsHitTestVisible = false };

    public MenuItem ChatMenuItem { get; } = new() { Header = "AI _Chat", InputGesture = new KeyGesture(Key.F12), ToggleType = MenuItemToggleType.CheckBox };
    public MenuItem SeeRealMenuItem { get; } = new() { Header = "Let AI Agents _See Values", ToggleType = MenuItemToggleType.CheckBox };
    public MenuItem AllowAgentsMenuItem { get; } = new() { Header = "_Allow AI Agents (MCP)", ToggleType = MenuItemToggleType.CheckBox };

    /// <summary>Started with <c>--mask</c>: agents see people's details masked whatever the setting says.</summary>
    public bool ForceDeidentify { get; init; }

    /// <summary>Started with <c>--real</c>: agents see people's details (unless <see cref="ForceDeidentify"/>).</summary>
    public bool RealFromCommandLine { get; init; }

    partial void AddExtraDocks(DockPanel root)
    {
        DockPanel.SetDock(ChatHolder, Dock.Right);
        root.Children.Add(ChatHolder);
        DockPanel.SetDock(AgentStage, Dock.Right);
        root.Children.Add(AgentStage);
    }

    private partial IEnumerable<object> MoreMenus(Func<string, Action, Key?, KeyModifiers, MenuItem> item)
    {
        ChatMenuItem.Click += OnToggleChatClick;
        SeeRealMenuItem.Click += OnSeeRealClick;
        AllowAgentsMenuItem.Click += OnAllowAgentsClick;
        return
        [
            new MenuItem
            {
                Header = "_AI",
                ItemsSource = (object[])
                [
                    ChatMenuItem,
                    new Separator(),
                    SeeRealMenuItem,
                    item("_Working Folder…", () => OnWorkingFolderClick(null, new RoutedEventArgs()), null, KeyModifiers.None),
                    new Separator(),
                    AllowAgentsMenuItem,
                    item("C_onnect an Agent…", () => _ = ShowConnect(), null, KeyModifiers.None),
                ],
            },
            new MenuItem
            {
                Header = "_Help",
                ItemsSource = (object[])
                [
                    item("_About", () => Say($"AVA Eligibility {typeof(MainWindow).Assembly.GetName().Version?.ToString(3)}: reads 270 eligibility inquiries and 271 answers, and says who's ready for their visit. MIT licence, Lonnie Watson."), null, KeyModifiers.None),
                ],
            },
        ];
    }

    private static readonly string[] TabNames = ["coverage", "inquiries", "worklist"];

    /// <summary>What the window shows, as agents see it: the tab, and the person shown (by handle).</summary>
    public ViewState CurrentView
    {
        get
        {
            var tab = Tabs.SelectedIndex >= 0 && Tabs.SelectedIndex < TabNames.Length ? TabNames[Tabs.SelectedIndex] : "coverage";
            var session = Agents?.Session;
            var handle = _person != null && session != null ? EligibilityTools.Handle(session, _person) : null;
            return new ViewState(tab, handle is { Length: > 0 } ? handle : null);
        }
    }

    /// <summary>Shows what an agent asked: a person's answer, a visit's (its answer, or its payer's refusal), or a tab.</summary>
    public void ShowView(ViewState view)
    {
        if (view.Handle is { } h && Agents?.Session is { } session)
        {
            if (h.StartsWith('V') && _work.FirstOrDefault(w => EligibilityTools.Handle(w.Appointment) == h) is { } w)
            {
                if (w.Answer.Person is { } p) ShowPerson(p);
                else if (w.Answer.Refusal is { } t) ShowTransaction(t);
            }
            else if (EligibilityTools.People(session).FirstOrDefault(x => x.Handle == h).Person is { } p) ShowPerson(p);
        }
        Tabs.SelectedIndex = Math.Max(0, Array.IndexOf(TabNames, view.Tab));
    }

    /// <summary>Opens a file for an agent, saying why when it isn't one the program reads.</summary>
    internal void OpenForAgent(string path)
    {
        var full = Path.GetFullPath(path);
        Open(full);
        if (_open.All(o => o.Path != full) && _appointmentsPath != full)
            throw new NotSupportedException(txtStatusBar.Text is { Length: > 0 } said ? said : $"{Path.GetFileName(full)} isn't a 270, a 271 or an appointment list.");
    }

    /// <summary>Closes an open file (or the appointment list) for an agent.</summary>
    internal void CloseForAgent(string path)
    {
        var full = Path.GetFullPath(path);
        if (_appointmentsPath == full)
        {
            _appointments = [];
            _appointmentsPath = null;
            _built = null;
        }
        if (_open.FirstOrDefault(o => o.Path == full) is { } f)
        {
            _open.Remove(f);
            if (_person != null && f.People.Contains(_person)) ShowNothing();
        }
        FillInquiries();
        FillWorklist();
        FillList();
    }

    /// <summary>A 270 an agent wrote, remembered as if the person had written it.</summary>
    internal void RememberForAgent(Core.Inquiries.BuiltInquiry built)
    {
        _built = built;
        Settings.Inquiry = Settings.Inquiry with { ControlNumber = Settings.Inquiry.ControlNumber + 1 };
        Settings.Save();
        FillInquiries();
        FillWorklist();
    }

    /// <summary>Ask Now for an agent: the person is asked first, as always; says what happened.</summary>
    internal async Task<string> AskForAgent()
    {
        await AskNow();
        return txtStatusBar.Text ?? "";
    }

    partial void AgentsReady()
    {
        SetUpAgents();
        KeyDown += (_, e) =>
        {
            if (e.Key != Key.F12) return;
            ToggleChat();
            e.Handled = true;
        };
    }
}
