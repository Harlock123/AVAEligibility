using AVAEligibility.Core.Inquiries;

namespace AVAEligibility.Core.Sending;

/// <summary>One appointment asked in real time: the trace, where its 271 was saved, and what the clearinghouse said.</summary>
public sealed record Asked(Appointment Appointment, string Trace, string? Saved, CoreResponse Response);

/// <summary>
/// Asks about an appointment list in real time, one appointment per request as CORE's real-time mode carries one
/// patient: each its own 270, under the trace the whole list's 270 would give it, each answer saved as a 271 file in
/// <c>folder</c> (never over a file that's there). Rows with errors and self-pay visits aren't sent.
/// </summary>
public static class RealTime
{
    public static async Task<IReadOnlyList<Asked>> AskAsync(IReadOnlyList<Appointment> appointments, InquirySettings settings, Clearinghouse ch, string password,
        CoreClient client, string folder, DateTime now, IProgress<string>? progress = null, CancellationToken cancel = default)
    {
        if (ch.Problem() is { } problem) throw new InvalidOperationException(problem);
        var whole = InquiryBuilder.Build(appointments, settings, now);
        Directory.CreateDirectory(folder);
        var asked = new List<Asked>();
        var control = settings.ControlNumber;
        foreach (var a in whole.Sent)
        {
            cancel.ThrowIfCancellationRequested();
            var trace = whole.Traces[a];
            progress?.Report($"Asking {a.Payer} about {a.SortName} ({asked.Count + 1} of {whole.Traces.Count})…");
            var one = InquiryBuilder.Build([a], settings with { ControlNumber = control++ }, now, whole.Traces);
            CoreResponse response;
            try
            {
                response = await client.SendAsync(one.Text, ch, password, now, cancel).ConfigureAwait(false);
            }
            catch (HttpRequestException e)
            {
                response = new CoreResponse("Unreachable", e.Message, "", "", "");
            }
            string? saved = null;
            if (response.Ok)
            {
                saved = Path.Combine(folder, $"{trace}-271.x12");
                for (var n = 2; File.Exists(saved); n++) saved = Path.Combine(folder, $"{trace}-271-{n}.x12");
                await File.WriteAllTextAsync(saved, response.Payload, cancel).ConfigureAwait(false);
            }
            asked.Add(new Asked(a, trace, saved, response));
        }
        return asked;
    }

    /// <summary>The control number after a run that sent <paramref name="count"/> requests from <paramref name="start"/>.</summary>
    public static int NextControl(int start, int count) => start + count;
}
