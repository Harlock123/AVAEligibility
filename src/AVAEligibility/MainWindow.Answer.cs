using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using AVAEligibility.Core.Eligibility;
using AVAEligibility.Core.Inquiries;

namespace AVAEligibility;

/// <summary>
/// The Coverage tab's reading of an answer: the verdict, the notes behind it with what to do, then the benefits grouped
/// as a front desk reads them (what the patient pays, deductibles, limits, authorizations, the PCP, other payers), and
/// last every line the payer sent. Each part highlights its segments when clicked.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>The date of service to read an answer for, when a later part knows it (the appointment list).</summary>
    private partial DateOnly? VisitDate(Person p);

    /// <summary>The worklist's item for a person, when an appointment's answer is theirs.</summary>
    private partial WorkItem? WorkItemFor(Person p);

    /// <summary>The reading of a person's answer, as the window shows it.</summary>
    public Interpretation? CurrentReading { get; private set; }

    internal static IBrush BrushOf(Verdict v) => v switch
    {
        Verdict.Ready => Look.Clean,
        Verdict.Check => Look.Warn,
        Verdict.Act => Look.Deny,
        Verdict.Rejected => Look.Reject,
        Verdict.NoAnswer => Look.Deny,
        _ => Look.Accent,
    };

    private partial (string, IBrush)? MarkOf(Person p)
    {
        if (WorkItemFor(p) is { } item) return (item.Word, BrushOf(item.Verdict));
        var r = Interpreter.Read(p);
        return (r.Word, BrushOf(r.Verdict));
    }

    private partial bool ShowAnswer(Person p)
    {
        if (!p.Transaction.IsResponse)
        {
            CurrentReading = null;
            return false;
        }
        var r = Interpreter.Read(p, VisitDate(p));
        CurrentReading = r;
        var item = WorkItemFor(p);
        var verdict = item?.Verdict ?? r.Verdict;
        var word = item?.Word ?? r.Word;
        var headline = item?.Headline ?? r.Headline;
        var notes = item?.Notes ?? r.Notes;
        var decider = notes.FirstOrDefault(n => n.Level == verdict && n.Text == headline);
        var banner = new Border
        {
            Background = new SolidColorBrush(((ISolidColorBrush)BrushOf(verdict)).Color, 0.14),
            BorderBrush = BrushOf(verdict), BorderThickness = new Thickness(3, 0, 0, 0), Padding = new Thickness(12, 8), Margin = new Thickness(0, 10, 0, 4),
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = (item != null ? $"{word} for the visit on {item.Appointment.Date:yyyy-MM-dd}" : word).ToUpperInvariant(), Foreground = BrushOf(verdict), FontWeight = FontWeight.Bold, FontSize = 13 },
                    new TextBlock { Text = headline, Foreground = Look.Text, FontSize = 15, TextWrapping = TextWrapping.Wrap },
                    new TextBlock { Text = decider?.Fix ?? "", IsVisible = decider != null, Foreground = Look.Dim, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) },
                },
            },
        };
        if (decider != null) banner.PointerPressed += (_, _) => Highlight([.. decider.Segments.Select(x => x.Index)]);
        CoveragePanel.Children.Add(banner);
        foreach (var n in notes.Where(n => n != decider).OrderByDescending(n => n.Level))
        {
            var note = new StackPanel { Spacing = 1 };
            note.Children.Add(new TextBlock { Text = n.Text, Foreground = BrushOf(n.Level), TextWrapping = TextWrapping.Wrap });
            note.Children.Add(new TextBlock { Text = n.Fix, Foreground = Look.Dim, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(16, 0, 0, 0) });
            CoveragePanel.Children.Add(ClickableBlock(note, n.Segments));
        }

        if (item?.Estimate is { } estimate)
        {
            CoveragePanel.Children.Add(Section($"Estimate for the visit ({item.Appointment.Visit} {item.Appointment.Procedure}, charge {(item.Appointment.Charge is { } ch ? Words.Money(ch) : "not given")})"));
            CoveragePanel.Children.Add(Line(estimate.ToString(), Look.Accent, []));
            CoveragePanel.Children.Add(Dim($"An estimate: it assumes {estimate.Assumes}."));
        }
        if (Changes.Earlier(p, _open) is { } earlier)
        {
            var changes = Changes.Between(earlier, p);
            CoveragePanel.Children.Add(Section($"Since the answer of {earlier.Transaction.Created:yyyy-MM-dd}"));
            if (changes.Count == 0) CoveragePanel.Children.Add(Dim("Nothing has changed."));
            foreach (var c in changes) CoveragePanel.Children.Add(Line(c, Look.Text, []));
        }
        if (r.Active != null || r.Begins != null)
        {
            CoveragePanel.Children.Add(Section("Coverage"));
            var plan = string.Join(", ", new[] { r.Active == true ? "Active" : r.Active == false ? "Inactive" : "", r.Plan, r.InsuranceType.Length > 0 ? Codes.InsuranceType.Say(r.InsuranceType) : "" }.Where(x => x.Length > 0));
            CoveragePanel.Children.Add(Line(plan, r.Active == false ? Look.Deny : Look.Text, []));
            if (r.Begins != null || r.Ends != null)
                CoveragePanel.Children.Add(Dim($"From {r.Begins?.ToString("yyyy-MM-dd") ?? "(not said)"}" + (r.Ends is { } e ? $" to {e:yyyy-MM-dd}" : "")));
            foreach (var reference in p.References) CoveragePanel.Children.Add(Dim($"{reference.Kind}: {reference.Value}" + (reference.Description.Length > 0 ? $" ({reference.Description})" : "")));
        }
        if (r.Costs.Count > 0)
        {
            CoveragePanel.Children.Add(Section("What the patient pays"));
            CoveragePanel.Children.Add(CostTable(r.Costs));
        }
        if (r.Accumulators.Count > 0)
        {
            CoveragePanel.Children.Add(Section("Deductible and out-of-pocket"));
            foreach (var a in r.Accumulators) CoveragePanel.Children.Add(AccumulatorLine(a));
        }
        if (r.Limits.Count > 0)
        {
            CoveragePanel.Children.Add(Section("Limits"));
            foreach (var l in r.Limits) CoveragePanel.Children.Add(Line(l.ToString(), Look.Text, l.Segments));
        }
        if (r.Authorizations.Count > 0)
        {
            CoveragePanel.Children.Add(Section("Needs authorization"));
            foreach (var a in r.Authorizations) CoveragePanel.Children.Add(Line(a.Service + (a.Contact.Length > 0 ? $": {a.Contact}" : ""), Look.Warn, a.Segments));
        }
        if (r.PrimaryCareProvider is { } pcp)
        {
            CoveragePanel.Children.Add(Section("Primary care provider"));
            CoveragePanel.Children.Add(Line(Words.Describe(pcp), Look.Text, pcp.Segments));
        }
        if (r.OtherPayers.Count > 0)
        {
            CoveragePanel.Children.Add(Section("Other payers"));
            foreach (var o in r.OtherPayers) CoveragePanel.Children.Add(Line(o.ToString(), Look.Text, o.Segments));
        }
        if (r.NotCovered.Count > 0)
        {
            CoveragePanel.Children.Add(Section("Not covered"));
            CoveragePanel.Children.Add(Dim(string.Join("; ", r.NotCovered)));
        }
        if (r.Messages.Count > 0)
        {
            CoveragePanel.Children.Add(Section("The payer's words"));
            foreach (var m in r.Messages) CoveragePanel.Children.Add(new TextBlock { Text = $"“{m}”", Foreground = Look.Dim, FontStyle = FontStyle.Italic, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6, 0, 0, 0) });
        }
        if (p.Benefits.Count > 0)
        {
            CoveragePanel.Children.Add(Section($"Every line the payer sent ({p.Benefits.Count})"));
            foreach (var b in p.Benefits) CoveragePanel.Children.Add(BenefitLine(b));
        }
        return true;
    }

    private Control CostTable(IReadOnlyList<CostRow> rows)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,*,*,*,*"), Margin = new Thickness(6, 0, 0, 0) };
        void Cell(int row, int col, string text, IBrush brush, bool bold = false)
        {
            var t = new TextBlock { Text = text, Foreground = brush, FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 12, 2) };
            Grid.SetRow(t, row);
            Grid.SetColumn(t, col);
            grid.Children.Add(t);
        }
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        Cell(0, 0, "Care", Look.Heading, true);
        Cell(0, 1, "Kind", Look.Heading, true);
        Cell(0, 2, "In network", Look.Heading, true);
        Cell(0, 3, "Out of network", Look.Heading, true);
        Cell(0, 4, "Either / other", Look.Heading, true);
        var i = 1;
        foreach (var c in rows)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            Cell(i, 0, c.Service + (c.Messages.Count > 0 ? $"  “{string.Join(" ", c.Messages)}”" : ""), Look.Text);
            Cell(i, 1, c.Kind, Look.Dim);
            Cell(i, 2, c.InNetwork, Look.Text);
            Cell(i, 3, c.OutOfNetwork, Look.Text);
            Cell(i, 4, c.Either, Look.Text);
            i++;
        }
        return grid;
    }

    private Control AccumulatorLine(Accumulator a)
    {
        var panel = new StackPanel { Spacing = 2 };
        panel.Children.Add(new TextBlock { Text = a.ToString(), Foreground = Look.Text, TextWrapping = TextWrapping.Wrap });
        if (a.Total is { } t && t > 0 && a.Remaining is { } left)
        {
            var used = (double)Math.Clamp((t - left) / t, 0, 1);
            var bar = new Grid { ColumnDefinitions = new ColumnDefinitions($"{used.ToString(System.Globalization.CultureInfo.InvariantCulture)}*,{(1 - used).ToString(System.Globalization.CultureInfo.InvariantCulture)}*"), Height = 6, Width = 320, HorizontalAlignment = HorizontalAlignment.Left };
            var fill = new Border { Background = Look.Accent, CornerRadius = new CornerRadius(3, 0, 0, 3) };
            var rest = new Border { Background = Look.Border, CornerRadius = new CornerRadius(0, 3, 3, 0) };
            Grid.SetColumn(rest, 1);
            bar.Children.Add(fill);
            bar.Children.Add(rest);
            panel.Children.Add(bar);
        }
        return ClickableBlock(panel, a.Segments);
    }

    private Control ClickableBlock(Control inner, IReadOnlyList<Core.X12.Segment> segments) =>
        new Border { Child = inner, Padding = new Thickness(6, 3), Background = Brushes.Transparent }.Also(b =>
        {
            if (segments.Count > 0) b.PointerPressed += (_, _) => Highlight([.. segments.Select(s => s.Index)]);
        });
}

internal static class ControlExtensions
{
    public static T Also<T>(this T value, Action<T> action)
    {
        action(value);
        return value;
    }
}
