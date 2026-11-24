using AVAEligibility.Core.Eligibility;

namespace AVAEligibility.Core.Inquiries;

/// <summary>A question and its answer: the 271 person, or the payer's refusal to answer at all, or nothing yet.</summary>
public sealed record Answer(Person? Person, EligibilityTransaction? Refusal, string How)
{
    public static readonly Answer None = new(null, null, "");

    public bool Found => Person != null || Refusal != null;
}

/// <summary>
/// Finds the 271 answer to a question: first by trace number (the 271 sends the 270's TRN back as TRN01 2, with the same
/// originator), then by payer, member ID and name (and birth date when both sides have one). A payer that couldn't answer
/// at all (an AAA at its own level, no people) answers everyone asked of it with that refusal.
/// </summary>
public static class Matcher
{
    /// <summary>The answer to a 270's person among the open 271s.</summary>
    public static Answer For(Person asked, IEnumerable<EligibilityFile> answers) =>
        Find(answers, asked.Transaction.Payer?.Id ?? "", asked.Trace, asked.Traces.FirstOrDefault(t => t.Type == "1")?.Originator,
            asked.IsDependent ? asked.Subscriber?.MemberId ?? "" : asked.MemberId, asked.Last, asked.First, asked.BirthDate, asked.IsDependent);

    /// <summary>The answer about an appointment, by the trace it was sent under (when known) or by who it is.</summary>
    public static Answer For(Appointment a, IEnumerable<EligibilityFile> answers, string? trace = null, string? originator = null) =>
        Find(answers, a.PayerId, trace, originator, InquiryBuilder.Clean(a.MemberId), a.Last, a.First, a.BirthDate, a.IsDependent);

    public static Answer Find(IEnumerable<EligibilityFile> answers, string payerId, string? trace, string? originator, string memberId, string last, string first, DateOnly? born, bool dependent)
    {
        if (payerId.Length == 0) return Answer.None;
        var fromPayer = answers.SelectMany(f => f.Transactions).Where(t => t.IsResponse && string.Equals(t.Payer?.Id, payerId, StringComparison.OrdinalIgnoreCase)).ToList();
        if (trace is { Length: > 0 })
        {
            var byTrace = fromPayer.SelectMany(t => t.People).FirstOrDefault(p => p.Traces.Any(t => t.Type == "2" && t.Number == trace && (originator == null || t.Originator == originator)));
            if (byTrace != null) return new Answer(byTrace, null, "trace " + trace);
        }
        var byWho = fromPayer.SelectMany(t => t.People).FirstOrDefault(p =>
            p.Last.Equals(last, StringComparison.OrdinalIgnoreCase) && p.First.Equals(first, StringComparison.OrdinalIgnoreCase)
            && (dependent
                ? (p.IsDependent && (p.Subscriber?.MemberId ?? "").Equals(memberId, StringComparison.OrdinalIgnoreCase)) || p.MemberId.Length == 0
                : p.MemberId.Equals(memberId, StringComparison.OrdinalIgnoreCase))
            && (born == null || p.BirthDate == null || p.BirthDate == born || p.Rejections.Count > 0));
        if (byWho != null) return new Answer(byWho, null, "member ID and name");
        var refusal = fromPayer.FirstOrDefault(t => t.People.Count == 0 && t.Payer?.Rejections.Count > 0);
        return refusal != null ? new Answer(null, refusal, "the payer's refusal") : Answer.None;
    }

    /// <summary>Every person in the open 270s, with their answer.</summary>
    public static IReadOnlyList<(Person Asked, Answer Answer)> All(IEnumerable<EligibilityFile> files)
    {
        var list = files.ToList();
        return [.. list.SelectMany(f => f.Transactions).Where(t => !t.IsResponse).SelectMany(t => t.People).Select(p => (p, For(p, list)))];
    }
}
