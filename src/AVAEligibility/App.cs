using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace AVAEligibility;

public sealed class App : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        RequestedThemeVariant = ThemeVariant.Dark;
        // Denser lists than Fluent's defaults: a day's schedule has dozens of people.
        Styles.Add(new Style(x => x.OfType<TreeViewItem>()) { Setters = { new Setter(TreeViewItem.MinHeightProperty, 26.0) } });
        Styles.Add(new Style(x => x.OfType<ListBoxItem>()) { Setters = { new Setter(ListBoxItem.PaddingProperty, new Thickness(10, 4)), new Setter(ListBoxItem.MinHeightProperty, 26.0) } });
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // AVAEligibility [files…] [--real | --mask]: opens 270s, 271s and appointment lists; the flags say what AI agents see.
            var args = desktop.Args ?? [];
            desktop.MainWindow = new MainWindow(args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)))
            {
                RealFromCommandLine = args.Contains("--real"),
                ForceDeidentify = args.Contains("--mask"),
            };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
