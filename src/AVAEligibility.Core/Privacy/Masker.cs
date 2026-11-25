using System.Text;
using System.Text.RegularExpressions;
using AVAEligibility.Core.Eligibility;
using AVAEligibility.Core.Inquiries;

namespace AVAEligibility.Core.Privacy;

/// <summary>
/// People's details as AI agents see them unless the person allows otherwise. Every name, member ID, identifier,
/// address and birth date in the open files and the appointment list is known, so it's masked wherever it turns up (a
/// note, a payer's message, a raw segment): to its shape, the first character kept, other letters as *, digits as 9
/// (BWH40010O512 is B**99999*999, so a letter among the digits still shows). Addresses are hidden. Every date keeps only
/// its year. Payers, providers, codes and amounts aren't people's details and are kept.
/// </summary>
public sealed class Masker
{
    private static readonly Regex IsoDate = new(@"\b((?:19|20)\d\d)-\d\d-\d\d\b", RegexOptions.CultureInvariant);
    private static readonly Regex CompactDate = new(@"(?<![0-9])((?:19|20)\d\d)(?:0[1-9]|1[0-2])(?:0[1-9]|[12]\d|3[01])(?![0-9])", RegexOptions.CultureInvariant);
    private static readonly Regex SlashDate = new(@"\b\d{1,2}/\d{1,2}/((?:19|20)\d\d)\b", RegexOptions.CultureInvariant);
    private static readonly Regex RangeDate = new(@"(?<![0-9])((?:19|20)\d\d)(?:0[1-9]|1[0-2])(?:0[1-9]|[12]\d|3[01])-((?:19|20)\d\d)(?:0[1-9]|1[0-2])(?:0[1-9]|[12]\d|3[01])(?![0-9])", RegexOptions.CultureInvariant);

    private readonly List<(Regex Pattern, string Masked)> _tokens;

    public Masker(IEnumerable<EligibilityFile> files, IEnumerable<Appointment> appointments)
    {
        var values = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase); // value → hidden entirely (an address)
        void Add(string? v, bool hide = false)
        {
            v = v?.Trim();
            if (v is { Length: >= 2 }) values[v] = hide || (values.TryGetValue(v, out var h) && h);
        }
        void Name(string last, string first, string middle)
        {
            Add(last);
            Add(first);
            Add(middle);
            foreach (var part in $"{last} {first} {middle}".Split([' ', '-', ',', '\''], StringSplitOptions.RemoveEmptyEntries)) if (part.Length >= 3) Add(part);
        }
        foreach (var p in files.SelectMany(f => f.People))
        {
            Name(p.Last, p.First, p.Middle);
            Add(p.MemberId);
            foreach (var r in p.References) Add(r.Value);
            foreach (var r in p.Benefits.SelectMany(b => b.References)) Add(r.Value);
            if (p.Address.Length > 0)
            {
                Add(p.Address, true);
                foreach (var s in p.Segments.Where(s => s.Id is "N3" or "N4"))
                    for (var i = 1; i < s.Elements.Length; i++) Add(s[i], true);
            }
        }
        foreach (var a in appointments)
        {
            Name(a.Last, a.First, a.Middle);
            Add(a.MemberId);
            Add(InquiryBuilder.Clean(a.MemberId));
            Add(a.Account);
            if (a.IsDependent) Name(a.SubscriberName.Last, a.SubscriberName.First, "");
        }
        _tokens = [.. values.OrderByDescending(kv => kv.Key.Length).Select(kv => (new Regex($@"(?<![A-Za-z0-9]){Regex.Escape(kv.Key)}(?![A-Za-z0-9])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), kv.Value ? "(address hidden)" : Shape(kv.Key)))];
    }

    /// <summary>A value's shape: the first character kept, other letters as *, digits as 9, the rest kept.</summary>
    public static string Shape(string value)
    {
        var b = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            b.Append(i == 0 ? c : char.IsLetter(c) ? '*' : char.IsDigit(c) ? '9' : c);
        }
        return b.ToString();
    }

    /// <summary>Text with every person's detail masked and every date reduced to its year.</summary>
    public string Text(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        foreach (var (pattern, masked) in _tokens) text = pattern.Replace(text, masked);
        text = RangeDate.Replace(text, m => $"{m.Groups[1].Value}****-{m.Groups[2].Value}****");
        text = IsoDate.Replace(text, m => $"{m.Groups[1].Value}-**-**");
        text = CompactDate.Replace(text, m => $"{m.Groups[1].Value}****");
        text = SlashDate.Replace(text, m => $"**/**/{m.Groups[1].Value}");
        return text;
    }

    /// <summary>A date as agents see it: its year.</summary>
    public static string Year(DateOnly? d) => d is { } x ? $"{x.Year}-**-**" : "";
}
