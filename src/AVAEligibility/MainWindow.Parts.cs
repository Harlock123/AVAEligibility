using Avalonia.Controls;
using Avalonia.Input;
using AVAEligibility.Core.Eligibility;

namespace AVAEligibility;

/// <summary>The hooks later parts of the window fill: what each milestone adds stays out of MainWindow.cs.</summary>
public sealed partial class MainWindow
{
    private partial IEnumerable<object> MoreMenus(Func<string, Action, Key?, KeyModifiers, MenuItem> item) => [];
    private partial IEnumerable<TabItem> EvenLaterTabs() => [];
}
