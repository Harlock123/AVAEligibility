using AVAEligibility.Core.Eligibility;
using AVAEligibility.Core.X12;

namespace AVAEligibility.Core.Inquiries;

/// <summary>What the patient may owe for a visit, how it was worked out, and what it assumes. Always an estimate.</summary>
public sealed record Estimate(decimal Amount, string Basis, string Assumes)
{
    public override string ToString() => $"About {Words.Money(Amount)}: {Basis}";
}

/// <summary>One appointment on the worklist: its answer, what it means for this visit, and what to do.</summary>
public sealed record WorkItem(Appointment Appointment, Answer Answer, Interpretation? Reading, Verdict Verdict, IReadOnlyList<Note> Notes, Estimate? Estimate)
{
    /// <summary>The verdict in a word; a self-pay visit says so.</summary>
    public string Word => Appointment.SelfPay ? "self-pay" : Verdict switch
    {
        Verdict.Ready => "ready",
        Verdict.Check => "check",
        Verdict.Act => "act first",
        Verdict.Rejected => "rejected",
        Verdict.NoAnswer => "no answer",
        _ => "asked",
    };

    /// <summary>The note that decided the verdict.</summary>
    public string Headline => Notes.Where(n => n.Level == Verdict).Select(n => n.Text).FirstOrDefault() ?? Reading?.Headline ?? Word;

    /// <summary>The kind of care the visit is, as a service type (98 an office visit, 62 an MRI).</summary>
    public string? Care => ServiceTypes.Care(Appointment.Procedure);
}

/// <summary>
/// The day's worklist: every appointment with the answer about it, read for that visit's date and care. On top of
/// <see cref="Interpreter"/>'s notes it says what only the visit can tell: the visit's care needs authorization or isn't
/// covered, it uses one of a limited number of visits, it's with someone other than the PCP, or the payer has the
/// patient's details differently from the registration. And it estimates what the patient owes.
/// </summary>
public static class Worklist
{
    public static IReadOnlyList<WorkItem> Build(IReadOnlyList<Appointment> appointments, IReadOnlyList<EligibilityFile> answers, IReadOnlyDictionary<Appointment, string>? traces = null, string? originator = null) =>
        [.. appointments.Select(a => Item(a, Matcher.For(a, answers, traces != null && traces.TryGetValue(a, out var t) ? t : null, originator)))];

    public static WorkItem Item(Appointment a, Answer answer)
    {
        var notes = new List<Note>();
        if (a.SelfPay)
        {
            notes.Add(new Note(Verdict.Ready, "Self-pay: there's no insurance to check.", a.Charge is { } c ? $"Collect {Words.Money(c)} or as agreed." : "Collect at the visit.", []));
            return new WorkItem(a, answer, null, Verdict.Ready, notes, a.Charge is { } charge ? new Estimate(charge, "the whole charge (self-pay)", "the clinic's charge") : null);
        }
        if (answer.Refusal is { } refusal)
        {
            var r = refusal.Payer!.Rejections[0];
            notes.Add(new Note(Verdict.NoAnswer, $"{refusal.Payer.Name} couldn't answer: {r.Meaning} ({r.Reason}).", Interpreter.Fix(r.Reason)
                + string.Concat(refusal.Payer.Contacts.SelectMany(c => c.Numbers).Where(n => n.Kind == "TE").Take(1).Select(n => $" Or call {Interpreter.Phone(n.Number)}.")), [r.Segment]));
            return new WorkItem(a, answer, null, Verdict.NoAnswer, notes, null);
        }
        if (answer.Person is not { } p)
        {
            notes.Add(new Note(Verdict.NoAnswer, "No answer yet.", a.Denied
                ? "Send the 270 for this list (Write the 270, or Ask Now): it asks about the date of service, then open the payer's 271."
                : "Send the 270 for this list (Write the 270), then open the payers' 271s.", []));
            return new WorkItem(a, answer, null, Verdict.NoAnswer, notes, null);
        }

        var reading = Interpreter.Read(p, a.Date);
        notes.AddRange(reading.Notes);
        if (reading.Verdict is not (Verdict.Rejected or Verdict.NoAnswer))
        {
            Mismatches(a, p, notes);
            if (a.Denied) AfterDenial(a, reading, notes);
            else ForTheVisit(a, reading, notes);
        }
        var verdict = notes.Count == 0 ? Verdict.Ready : notes.Max(n => n.Level);
        return new WorkItem(a, answer, reading, verdict, notes, verdict is Verdict.Rejected or Verdict.NoAnswer || a.Denied ? null : Estimate(a, reading));
    }

    /// <summary>Where the payer has the patient differently from the registration: claims would be rejected for it.</summary>
    private static void Mismatches(Appointment a, Person p, List<Note> notes)
    {
        var name = p.Segments.Where(s => s.Id == "NM1").Take(1).ToList();
        if (!a.IsDependent && p.MemberId.Length > 0 && !p.MemberId.Equals(InquiryBuilder.Clean(a.MemberId), StringComparison.OrdinalIgnoreCase))
            notes.Add(new Note(Verdict.Check, $"The payer has member ID {p.MemberId}; the registration has {a.MemberId}.", $"Update the registration to {p.MemberId}, or the claim will be rejected.", name));
        if (!p.Last.Equals(a.Last, StringComparison.OrdinalIgnoreCase) || (p.First.Length > 0 && !p.First.Equals(a.First, StringComparison.OrdinalIgnoreCase)))
            notes.Add(new Note(Verdict.Check, $"The payer has the name {p.SortName}; the registration has {a.SortName}.", "Use the payer's spelling on the claim, or confirm it with the patient.", name));
        if (p.BirthDate is { } born && a.BirthDate is { } ours && born != ours)
            notes.Add(new Note(Verdict.Check, $"The payer has the birth date {born:yyyy-MM-dd}; the registration has {ours:yyyy-MM-dd}.", "Confirm it with the patient and correct the one that's wrong.", [.. p.Segments.Where(s => s.Id == "DMG")]));
    }

    private static void ForTheVisit(Appointment a, Interpretation reading, List<Note> notes)
    {
        var care = ServiceTypes.Care(a.Procedure);
        var careName = care == null ? null : Codes.ServiceType.Say(care);
        var p = reading.Person;
        if (care != null)
        {
            foreach (var b in p.Benefits.Where(b => b.Authorization == "Y" && b.ServiceTypes.Contains(care)))
            {
                var auth = reading.Authorizations.First(x => x.Segments.Contains(b.Segment));
                // The visit is the care that needs it: the general note becomes something to do now.
                notes.RemoveAll(n => n.Segments.Contains(b.Segment) && n.Level == Verdict.Check);
                notes.Add(new Note(Verdict.Act, $"This visit ({careName}, {a.Procedure}) needs authorization, and the plan says so.",
                    auth.Contact.Length > 0 ? $"Get it from {auth.Contact} before the visit, and put its number on the claim." : "Get it from the payer before the visit, and put its number on the claim.", b.Segments));
            }
            if (p.Benefits.FirstOrDefault(b => b.Information == "I" && b.ServiceTypes.Contains(care)) is { } excluded)
                notes.Add(new Note(Verdict.Act, $"This visit's care ({careName}) isn't covered.", "Tell the patient before the visit: they'd pay for it.", excluded.Segments));
            foreach (var l in reading.Limits.Where(l => l.Service.Split(", ").Contains(careName)))
                if (l.Remaining is { } left && left > 2)
                    notes.Add(new Note(Verdict.Ready, $"This visit uses one of the {left:0.##} {l.Unit} left ({l}).", "Nothing to do; the count goes down by one.", l.Segments));
        }
        if (reading.PrimaryCareProvider is { } pcp && a.ProviderNpi.Length > 0 && pcp.Id.Length > 0 && pcp.Id != a.ProviderNpi)
        {
            var pcpBenefit = p.Benefits.First(b => b.Entities.Contains(pcp));
            var gatekeeper = reading.InsuranceType is "HM" or "HN" or "PS" || pcpBenefit.Messages.Any(m => m.Contains("REFERRAL", StringComparison.OrdinalIgnoreCase));
            notes.Add(new Note(gatekeeper ? Verdict.Act : Verdict.Check,
                $"The PCP on file is {pcp.Name}, not {Person(a.Provider)}" + (gatekeeper ? ", and the plan needs a referral for other providers." : "."),
                gatekeeper ? $"Get a referral from {pcp.Name}'s office before the visit" + string.Concat(pcp.Contacts.SelectMany(c => c.Numbers).Where(n => n.Kind == "TE").Take(1).Select(n => $" ({Interpreter.Phone(n.Number)})")) + ", or change the patient's PCP with the plan."
                           : "Check whether the plan needs a referral.", pcpBenefit.Segments));
        }
    }

    /// <summary>What a denial's codes said, in a few words for a sentence.</summary>
    private static string Said(string reason) => reason switch
    {
        "22" => "another payer's to pay first",
        "26" => "before coverage began",
        "27" => "after coverage ended",
        "31" => "not the payer's member",
        _ => $"reason {reason}",
    };

    /// <summary>
    /// A denied claim asked about again: what the payer's 271 for the date of service says about the denial. It agrees
    /// (coverage wasn't active then, another payer is primary), or it doesn't (coverage was active: ask the payer to
    /// reprocess, or appeal with the 271), or it shows why the payer couldn't match the claim (its member ID, name or
    /// birth date differ). Goes first among the notes, so it's the headline. Visit notes (authorizations, referrals,
    /// estimates) don't apply to a claim already sent.
    /// </summary>
    private static void AfterDenial(Appointment a, Interpretation r, List<Note> notes)
    {
        var p = r.Person;
        var codes = a.DenialCodes;
        var named = codes.Count > 0 ? string.Join(", ", codes.Select(c => $"{c.Group}-{c.Reason}")) : "the denial";
        var reasons = codes.Select(c => c.Reason).ToHashSet();
        var day = a.Date;
        var on = day is { } d ? $" on {d:yyyy-MM-dd}" : "";
        var coverage = p.Benefits.Where(b => b.ServiceTypes.Count == 0 || b.ServiceTypes.Contains("30")).Where(b => b.Information is "1" or "2" or "3" or "4" or "5" or "6" or "7" or "8")
            .SelectMany(b => b.Segments).Concat(p.Dates.Select(x => x.Segment)).Distinct().ToList();
        var notActive = r.Active == false || day is { } v && (r.Ends is { } end && v > end || r.Begins is { } begin && v < begin);
        var primary = r.OtherPayers.FirstOrDefault(o => o.Order == "Primary");
        var differs = new List<string>();
        if (!a.IsDependent && p.MemberId.Length > 0 && !p.MemberId.Equals(InquiryBuilder.Clean(a.MemberId), StringComparison.OrdinalIgnoreCase)) differs.Add($"member ID {p.MemberId} (the claim had {a.MemberId})");
        if (!p.Last.Equals(a.Last, StringComparison.OrdinalIgnoreCase) || p.First.Length > 0 && !p.First.Equals(a.First, StringComparison.OrdinalIgnoreCase)) differs.Add($"the name {p.SortName} (the claim had {a.SortName})");
        if (p.BirthDate is { } born && a.BirthDate is { } ours && born != ours) differs.Add($"the birth date {born:yyyy-MM-dd} (the claim had {ours:yyyy-MM-dd})");

        Note note;
        if (notActive)
        {
            var when = r.Active == false ? r.Ends is { } e ? $"coverage ended {e:yyyy-MM-dd}" : "coverage isn't active"
                : r.Ends is { } e2 && day > e2 ? $"coverage ended {e2:yyyy-MM-dd}" : $"coverage began {r.Begins:yyyy-MM-dd}";
            note = new Note(Verdict.Act, $"The 271 agrees with the denial ({named}): {when}, so the plan didn't cover the claim{on}.",
                "Ask the patient which plan covered them" + (on.Length > 0 ? on : " then") + " and bill it" + (r.OtherPayers.Count > 0 ? $" (the 271 names {string.Join(", ", r.OtherPayers.Select(o => o.ToString()))})" : "") + "; with none, bill the patient.",
                coverage);
        }
        else if (reasons.Contains("22") && primary != null)
            note = new Note(Verdict.Act, $"The 271 agrees with the denial ({named}): {primary.Name} is primary.", $"Bill {primary.Name} first, then this plan with its remittance.", primary.Segments);
        else if (r.Active != true)
            note = new Note(Verdict.Check, $"The 271 doesn't say whether coverage was active{on}, so it neither confirms nor answers the denial ({named}).", "Call the payer about the claim, with the date of service.", coverage);
        else if (differs.Count > 0)
            note = new Note(Verdict.Act, $"The payer finds the patient with {string.Join(", ", differs)}: why it couldn't match the claim ({named}).",
                "Correct the claim to the payer's details and send it again as a replacement claim (frequency 7), and correct the registration.", [.. p.Segments.Where(x => x.Id is "NM1" or "DMG").Take(2)]);
        else if (reasons.Contains("22"))
            note = new Note(Verdict.Act, $"The 271 shows no other payer ahead of this plan{on}, yet the claim was denied as {Said("22")} ({named}).",
                "Ask the payer to update the patient's coordination of benefits (the patient may need to tell it there's no other coverage), then to reprocess the claim.", coverage);
        else
            note = new Note(Verdict.Act, $"The 271 says coverage was active{on}" + (r.Begins is { } b ? $" (from {b:yyyy-MM-dd})" : "") + $", yet the claim was denied as {string.Join(" and ", reasons.Select(Said).DefaultIfEmpty("not covered"))} ({named}).",
                "Ask the payer to reprocess the claim, or appeal with this 271: it's the payer's own answer for the date of service. Keep its date and trace number.", coverage);
        notes.Insert(0, note);
        // Notes written for a visit still to come ("before the visit") are about the past here: the denial's note says what to do.
        notes.RemoveAll(n => n != note && n.Fix.Contains("before the visit", StringComparison.Ordinal));
    }

    /// <summary>"PATEL, ANJALI" as ANJALI PATEL, as names in a 271 read.</summary>
    private static string Person(string lastFirst) =>
        lastFirst.Split(',', 2) is [var last, var first] ? $"{first.Trim()} {last.Trim()}" : lastFirst;

    /// <summary>
    /// What the patient may owe: the visit's copay; or what's left of the deductible and then the coinsurance, up to
    /// what's left of the out-of-pocket maximum; on the clinic's charge, in network. Null when the answer doesn't say
    /// enough, when coverage isn't active, or when another payer pays first.
    /// </summary>
    public static Estimate? Estimate(Appointment a, Interpretation r)
    {
        const string assumes = "the provider is in network, and the payer allows the full charge (it usually allows less)";
        if (a.Charge is not { } charge) return null;
        if (r.Active == false) return new Estimate(charge, "the whole charge, unless the patient has other insurance (this coverage isn't active)", "the clinic's charge");
        if (r.Active != true || r.OtherPayers.Any(o => o.Order == "Primary")) return null;
        var care = ServiceTypes.Care(a.Procedure);
        var p = r.Person;
        Benefit? For(string info, string? service) => p.Benefits.FirstOrDefault(b => b.Information == info && b.InNetwork is "Y" or "W" or "" && (service == null ? b.ServiceTypes.Count == 0 || b.ServiceTypes.Contains("30") : b.ServiceTypes.Contains(service)));
        if (care != null && For("B", care) is { Amount: { } copay } cp)
            return new Estimate(Math.Min(copay, charge), $"the {Words.Money(copay)} copay for {Codes.ServiceType.Say(care)}" + (cp.Messages.Count > 0 ? $" (“{cp.Messages[0]}”)" : ""), assumes);
        var coinsurance = (care != null ? For("A", care) : null) ?? For("A", null);
        var deductibleLeft = r.Accumulators.FirstOrDefault(x => x.Kind == "Deductible" && x.Level == "individual" && x.Network is "in network" or "in or out of network" or "")?.Remaining;
        var careWaivesDeductible = care != null && For("A", care) is { } own && own.Messages.Any(m => m.Contains("DEDUCTIBLE WAIVED", StringComparison.OrdinalIgnoreCase));
        if (coinsurance?.Percent is not { } pct) return null;
        var fromDeductible = careWaivesDeductible ? 0 : Math.Min(deductibleLeft ?? 0, charge);
        var owed = fromDeductible + Math.Round(pct * (charge - fromDeductible), 2);
        var cap = r.Accumulators.FirstOrDefault(x => x.Kind == "Out-of-pocket maximum" && x.Level == "individual" && x.Network is "in network" or "")?.Remaining;
        if (cap is { } c && owed > c) owed = c;
        var said = coinsurance.Messages.Count > 0 ? $" (“{coinsurance.Messages[0]}”)" : "";
        var basis = (fromDeductible > 0 ? $"{Words.Money(fromDeductible)} of deductible left, then " : "") + $"{Words.Percent(pct)} coinsurance on {Words.Money(charge - fromDeductible)}"
            + (cap is { } capped && owed == capped ? $", capped at the {Words.Money(capped)} left of the out-of-pocket maximum" : "")
            + said + (deductibleLeft == null && !careWaivesDeductible && pct > 0 ? " (the answer doesn't say what's left of the deductible)" : "");
        return new Estimate(owed, basis, assumes);
    }
}

/// <summary>What changed about a person between two answers.</summary>
public static class Changes
{
    /// <summary>The differences from <paramref name="before"/> to <paramref name="after"/>, in words; empty when nothing changed.</summary>
    public static IReadOnlyList<string> Between(Person before, Person after)
    {
        var a = Interpreter.Read(before);
        var b = Interpreter.Read(after);
        var said = new List<string>();
        if (a.Verdict != b.Verdict) said.Add($"Was {a.Word}, now {b.Word}: {b.Headline}");
        if (a.Active != b.Active && a.Verdict == b.Verdict) said.Add(b.Active == true ? "Coverage is active now." : "Coverage isn't active any more.");
        if (a.Plan != b.Plan && a.Plan.Length > 0 && b.Plan.Length > 0) said.Add($"The plan was \"{a.Plan}\", now \"{b.Plan}\".");
        if (a.Ends != b.Ends) said.Add($"Coverage ends {b.Ends?.ToString("yyyy-MM-dd") ?? "(not said)"} (was {a.Ends?.ToString("yyyy-MM-dd") ?? "not said"}).");
        if (!before.MemberId.Equals(after.MemberId, StringComparison.OrdinalIgnoreCase) && before.MemberId.Length > 0 && after.MemberId.Length > 0)
            said.Add($"The member ID was {before.MemberId}, now {after.MemberId}.");
        foreach (var x in b.Accumulators)
        {
            var was = a.Accumulators.FirstOrDefault(y => y.Kind == x.Kind && y.Level == x.Level && y.Network == x.Network);
            if (was?.Remaining is { } r0 && x.Remaining is { } r1 && r0 != r1)
                said.Add($"{x.Kind}, {x.Level}{(x.Network.Length > 0 ? ", " + x.Network : "")}: {Words.Money(r1)} left (was {Words.Money(r0)}; {Words.Money(r0 - r1)} {(r1 < r0 ? "used" : "restored")} since).");
        }
        foreach (var x in b.Limits)
            if (a.Limits.FirstOrDefault(y => y.Service == x.Service && y.Unit == x.Unit) is { Remaining: { } r0 } && x.Remaining is { } r1 && r0 != r1)
                said.Add($"{x.Service}: {r1:0.##} {x.Unit} left (was {r0:0.##}).");
        foreach (var o in a.OtherPayers.Where(o => !b.OtherPayers.Any(n => n.Name == o.Name))) said.Add($"{(o.Name.Length > 0 ? o.Name : "Another payer")} no longer shows as {(o.Order.Length > 0 ? o.Order.ToLowerInvariant() : "another payer")}.");
        foreach (var o in b.OtherPayers.Where(o => !a.OtherPayers.Any(n => n.Name == o.Name))) said.Add($"{o} shows now.");
        if (a.PrimaryCareProvider?.Id != b.PrimaryCareProvider?.Id) said.Add($"The PCP is {(b.PrimaryCareProvider?.Name ?? "not said")} (was {a.PrimaryCareProvider?.Name ?? "not said"}).");
        foreach (var c in b.Costs)
            if (a.Costs.FirstOrDefault(x => x.Service == c.Service && x.Kind == c.Kind) is { } old && old.ToString() != c.ToString())
                said.Add($"{c} (was {old.InNetwork}{(old.OutOfNetwork.Length > 0 ? $", {old.OutOfNetwork} out" : "")}).");
        return said;
    }

    /// <summary>The same person in another answer from the same payer: by member ID and name.</summary>
    public static Person? Earlier(Person p, IEnumerable<EligibilityFile> files) =>
        files.SelectMany(f => f.Transactions).Where(t => t.IsResponse && t != p.Transaction && t.Payer?.Id == p.Transaction.Payer?.Id && t.Created < p.Transaction.Created)
            .OrderByDescending(t => t.Created)
            .SelectMany(t => t.People)
            .FirstOrDefault(q => q.Last == p.Last && q.First == p.First && (q.MemberId == p.MemberId || q.IsDependent && p.IsDependent));
}
