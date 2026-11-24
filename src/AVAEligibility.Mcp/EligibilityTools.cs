using System.ComponentModel;
using System.Globalization;
using System.Text.Json.Nodes;
using AVAEligibility.Core.Eligibility;
using AVAEligibility.Core.Inquiries;
using AVAEligibility.Core.Privacy;
using AVAEligibility.Core.Reports;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AVAEligibility.Mcp;

/// <summary>
/// The MCP tools: what an agent can do with eligibility files, an appointment list and the worklist, through the same
/// readings the window shows. People are known by handles (P01… in the 270s and 271s, V01… on the appointment list).
/// Unless the person allows values, every string an answer carries passes through <see cref="Masker"/> on the way out:
/// names, IDs and addresses to their shape, dates to their year. Writing a 270 needs values allowed; asking the
/// clearinghouse needs the open window and the person's yes. Failures come back as MCP tool errors.
/// </summary>
/// <remarks>The parameter names are the JSON names agents see, hence snake_case.</remarks>
public sealed partial class EligibilityTools(IExplorerHost host)
{
    private readonly Workspace _files = new(() => host.Root);

    public const int MaxResults = 300;
    private const string PersonHelp = "A person in the open 270s and 271s, by handle: P01 (list_people lists them).";
    private const string VisitHelp = "A visit on the appointment list, by handle: V01 (get_worklist lists them).";

    // ───────────────────────────── handles ─────────────────────────────

    internal static IReadOnlyList<(string Handle, Person Person)> People(IExplorerSession s) =>
        [.. s.Files.SelectMany(f => f.People).Select((p, i) => ($"P{i + 1:D2}", p))];

    internal static string Handle(IExplorerSession s, Person p) => People(s).FirstOrDefault(x => x.Person == p).Handle ?? "";

    internal static string Handle(Appointment a) => $"V{a.Row:D2}";

    private static Person PersonOf(IExplorerSession s, string handle) =>
        People(s).FirstOrDefault(x => x.Handle.Equals(handle.Trim(), StringComparison.OrdinalIgnoreCase)).Person
        ?? throw new ArgumentException($"There's no person {handle}: list_people lists the people in the open files.");

    private static WorkItem VisitOf(IExplorerSession s, string handle)
    {
        if (s.Appointments.Count == 0) throw new InvalidOperationException("No appointment list is open: open_file opens one (a CSV or Excel schedule).");
        return s.Work.FirstOrDefault(w => Handle(w.Appointment).Equals(handle.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"There's no visit {handle}: get_worklist lists them.");
    }

    // ───────────────────────────── files ─────────────────────────────

    [McpServerTool(Name = "list_files", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("Lists the eligibility files (270s, 271s) and appointment lists (CSV or Excel) in the working folder and its subfolders.")]
    public Task<string> ListFiles(
        [Description("A folder inside the working folder to list instead. Optional.")] string? folder = null,
        CancellationToken cancel = default) =>
        Session("listed the files", s =>
        {
            var (list, more) = _files.List(folder);
            var o = new JsonObject
            {
                ["folder"] = _files.Root,
                ["files"] = new JsonArray([.. list.Select(f => (JsonNode)new JsonObject { ["file"] = f, ["kind"] = Kind(_files.Resolve(f)) })]),
            };
            if (more) o["note"] = $"Only the first {Workspace.MaxListed} are listed. Name a folder to see the rest.";
            return o;
        }, cancel);

    private static string Kind(string path)
    {
        if (AppointmentList.LooksLikeOne(path)) return "appointment list";
        try
        {
            var f = EligibilityReader.Load(path);
            return f.Transactions.Count > 0 ? f.Kind : "X12, not eligibility";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return "unreadable";
        }
    }

    [McpServerTool(Name = "open_file", ReadOnly = false, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("Opens a 270, a 271 or an appointment list (CSV or Excel) from the working folder. Opening a list and the payers' 271s makes the worklist.")]
    public Task<string> OpenFile(
        [Description("The file, relative to the working folder.")] string file,
        CancellationToken cancel = default) =>
        Session("opened " + file, s =>
        {
            var full = _files.Existing(file);
            s.Open(full);
            if (s.AppointmentsPath == full)
                return new JsonObject { ["opened"] = _files.Relative(full), ["kind"] = "appointment list", ["visits"] = s.Appointments.Count, ["worklist"] = Counts(s.Work) };
            var f = s.Files.First(x => x.Path == full);
            return new JsonObject
            {
                ["opened"] = _files.Relative(full), ["kind"] = f.Kind,
                ["payers"] = new JsonArray([.. f.Transactions.Select(t => (JsonNode)new JsonObject { ["payer"] = t.Payer?.Name, ["people"] = t.People.Count, ["refused"] = t.Payer?.Rejections.Count > 0 ? Words.Describe(t.Payer.Rejections[0]) : null })]),
                ["people"] = new JsonArray([.. f.People.Select(p => (JsonNode)PersonLine(s, p))]),
                ["problems"] = f.Problems.Count > 0 ? new JsonArray([.. f.Problems.Select(x => (JsonNode)x)]) : null,
            };
        }, cancel);

    [McpServerTool(Name = "close_file", ReadOnly = false, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("Closes an open file or the appointment list.")]
    public Task<string> CloseFile(
        [Description("The file, relative to the working folder.")] string file,
        CancellationToken cancel = default) =>
        Session("closed " + file, s =>
        {
            s.Close(_files.Resolve(file));
            return new JsonObject { ["closed"] = file };
        }, cancel);

    [McpServerTool(Name = "list_open", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("What's open: each 270 and 271 with its payers and how many people, and the appointment list.")]
    public Task<string> ListOpen(CancellationToken cancel = default) =>
        Session("listed what's open", s => new JsonObject
        {
            ["files"] = new JsonArray([.. s.Files.Select(f => (JsonNode)new JsonObject
            {
                ["file"] = f.Path is { } p ? _files.Relative(p) : f.Name, ["kind"] = f.Kind, ["payers"] = string.Join(", ", f.Transactions.Select(t => t.Payer?.Name).Distinct()), ["people"] = f.People.Count(),
            })]),
            ["appointment_list"] = s.AppointmentsPath is { } a ? _files.Relative(a) : null,
            ["visits"] = s.Appointments.Count,
        }, cancel);

    // ───────────────────────────── people and answers ─────────────────────────────

    private static JsonObject PersonLine(IExplorerSession s, Person p)
    {
        var r = Interpreter.Read(p);
        return new JsonObject
        {
            ["person"] = Handle(s, p), ["name"] = p.SortName, ["payer"] = p.Transaction.Payer?.Name, ["kind"] = p.Transaction.IsResponse ? "answer" : "question",
            ["dependent"] = p.IsDependent ? true : null, ["verdict"] = r.Word, ["says"] = p.Transaction.IsResponse ? r.Headline : null,
        };
    }

    [McpServerTool(Name = "list_people", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("Everyone in the open 270s and 271s, by handle: their payer, whether it's a question or an answer, and the answer's verdict (ready, check, act first, rejected, no answer) with what decided it.")]
    public Task<string> ListPeople(
        [Description("Only this file, relative to the working folder. Optional.")] string? file = null,
        CancellationToken cancel = default) =>
        Session("listed the people", s =>
        {
            var only = file == null ? null : _files.Resolve(file);
            return new JsonObject { ["people"] = new JsonArray([.. People(s).Where(x => only == null || s.Files.First(f => f.People.Contains(x.Person)).Path == only).Take(MaxResults).Select(x => (JsonNode)PersonLine(s, x.Person))]) };
        }, cancel);

    [McpServerTool(Name = "get_answer", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("A person's answer made sense of: the verdict, each note with what to do, coverage (active, plan, dates), what the patient pays (copays, coinsurance), deductibles and out-of-pocket with what's left, limits, what needs authorization, the PCP, other payers and who's primary, what isn't covered, and the payer's own words.")]
    public Task<string> GetAnswer(
        [Description(PersonHelp)] string person,
        CancellationToken cancel = default) =>
        Session("read an answer", s =>
        {
            var p = PersonOf(s, person);
            var r = Interpreter.Read(p);
            return new JsonObject
            {
                ["person"] = Handle(s, p), ["name"] = p.Name, ["payer"] = p.Transaction.Payer?.Name, ["member_id"] = p.MemberId.Length > 0 ? p.MemberId : null,
                ["born"] = p.BirthDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), ["dependent_of"] = p.Subscriber is { } sub ? Handle(s, sub) : null,
                ["verdict"] = r.Word, ["headline"] = r.Headline,
                ["notes"] = Notes(r.Notes),
                ["active"] = r.Active, ["plan"] = r.Plan.Length > 0 ? r.Plan : null, ["insurance_type"] = r.InsuranceType.Length > 0 ? Codes.InsuranceType.Say(r.InsuranceType) : null,
                ["begins"] = r.Begins?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), ["ends"] = r.Ends?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["costs"] = Strings(r.Costs.Select(c => c + (c.Messages.Count > 0 ? $" (\"{string.Join(" ", c.Messages)}\")" : ""))),
                ["deductibles_and_out_of_pocket"] = Strings(r.Accumulators.Select(a => a.ToString())),
                ["limits"] = Strings(r.Limits.Select(l => l.ToString())),
                ["authorization_needed"] = Strings(r.Authorizations.Select(a => a.Service + (a.Contact.Length > 0 ? $": {a.Contact}" : ""))),
                ["primary_care_provider"] = r.PrimaryCareProvider is { } pcp ? Words.Describe(pcp) : null,
                ["other_payers"] = Strings(r.OtherPayers.Select(o => o.ToString())),
                ["not_covered"] = Strings(r.NotCovered),
                ["payer_said"] = Strings(r.Messages),
            };
        }, cancel);

    private static JsonArray Notes(IEnumerable<Note> notes) =>
        new([.. notes.OrderByDescending(n => n.Level).Select(n => (JsonNode)new JsonObject { ["level"] = n.Level.ToString().ToLowerInvariant(), ["says"] = n.Text, ["do"] = n.Fix, ["segments"] = string.Join(", ", n.Segments.Select(x => $"{x.Id} at {x.Index + 1}")) })]);

    private static JsonArray? Strings(IEnumerable<string> items)
    {
        var list = items.ToList();
        return list.Count == 0 ? null : new JsonArray([.. list.Select(x => (JsonNode)x)]);
    }

    [McpServerTool(Name = "get_benefits", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("Every line the payer sent about a person (EB), in words and as its codes, with its messages, dates, identifiers and the entities it names (a PCP, another payer, whom to call); or, for a 270, what was asked.")]
    public Task<string> GetBenefits(
        [Description(PersonHelp)] string person,
        CancellationToken cancel = default) =>
        Session("read the benefit lines", s =>
        {
            var p = PersonOf(s, person);
            return new JsonObject
            {
                ["person"] = Handle(s, p),
                ["asked"] = Strings(p.Inquiries.Select(Words.Describe)),
                ["rejections"] = Strings(p.Rejections.Select(Words.Describe)),
                ["lines"] = new JsonArray([.. p.Benefits.Take(MaxResults).Select(b => (JsonNode)new JsonObject
                {
                    ["says"] = Words.Describe(b), ["eb"] = b.Segment.Text,
                    ["messages"] = Strings(b.Messages), ["dates"] = Strings(b.Dates.Select(d => $"{d.Kind}: {d}")),
                    ["identifiers"] = Strings(b.References.Select(r => $"{r.Kind}: {r.Value}")), ["entities"] = Strings(b.Entities.Select(Words.Describe)),
                })]),
            };
        }, cancel);

    [McpServerTool(Name = "get_segments", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("The raw X12 segments about a person (their loops, numbered as in the file), for checking exactly what the payer sent.")]
    public Task<string> GetSegments(
        [Description(PersonHelp)] string person,
        CancellationToken cancel = default) =>
        Session("read the segments", s =>
        {
            var p = PersonOf(s, person);
            return new JsonObject { ["person"] = Handle(s, p), ["segments"] = new JsonArray([.. p.Segments.Take(MaxResults).Select(x => (JsonNode)$"{x.Index + 1}: {x.Text}")]) };
        }, cancel);

    private static readonly Dictionary<string, IReadOnlyDictionary<string, string>> Lists = new()
    {
        ["information"] = Codes.Information, ["coverage_level"] = Codes.CoverageLevel, ["service_type"] = Codes.ServiceType, ["insurance_type"] = Codes.InsuranceType,
        ["time_period"] = Codes.TimePeriod, ["quantity"] = Codes.Quantity, ["reject"] = Codes.Reject, ["follow_up"] = Codes.FollowUp, ["date"] = Codes.DateKind,
        ["reference"] = Codes.Reference, ["entity"] = Codes.Entity, ["relationship"] = Codes.Relationship,
    };

    [McpServerTool(Name = "explain_code", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("What a 270/271 code means, from the 005010X279A1 lists: information (EB01), coverage_level (EB02), service_type (EB03, EQ01), insurance_type (EB04), time_period (EB06), quantity (EB09), reject (AAA03, with what to do), follow_up (AAA04), date (DTP01), reference (REF01), entity (NM101), relationship (INS02).")]
    public Task<string> ExplainCode(
        [Description("The list: information, coverage_level, service_type, insurance_type, time_period, quantity, reject, follow_up, date, reference, entity or relationship.")] string list,
        [Description("The code: 98, AL, 72, C…")] string code,
        CancellationToken cancel = default) =>
        Session("explained a code", s =>
        {
            var l = Lists.GetValueOrDefault(list.Trim().ToLowerInvariant()) ?? throw new ArgumentException($"list is one of {string.Join(", ", Lists.Keys)}.");
            var meaning = l.Meaning(code);
            return new JsonObject
            {
                ["list"] = list, ["code"] = code.Trim(), ["meaning"] = meaning ?? "not in the list",
                ["what_to_do"] = list.Trim().Equals("reject", StringComparison.OrdinalIgnoreCase) && meaning != null ? Interpreter.Fix(code.Trim()) : null,
            };
        }, cancel);

    [McpServerTool(Name = "search_codes", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("Finds codes whose meaning has all the words given (\"mental health\", \"birth date\"), in one list or all of them.")]
    public Task<string> SearchCodes(
        [Description("The words to look for.")] string words,
        [Description("One list (as explain_code names them). Optional: all of them.")] string? list = null,
        CancellationToken cancel = default) =>
        Session("searched the codes", s =>
        {
            var wanted = words.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var lists = list == null ? Lists : Lists.Where(kv => kv.Key.Equals(list.Trim(), StringComparison.OrdinalIgnoreCase)).ToDictionary();
            if (lists.Count == 0) throw new ArgumentException($"list is one of {string.Join(", ", Lists.Keys)}.");
            var hits = lists.SelectMany(kv => kv.Value.Where(c => wanted.All(w => c.Value.Contains(w, StringComparison.OrdinalIgnoreCase))).Select(c => (JsonNode)$"{kv.Key} {c.Key}: {c.Value}")).Take(60);
            return new JsonObject { ["found"] = new JsonArray([.. hits]) };
        }, cancel);

    // ───────────────────────────── appointments and the 270 ─────────────────────────────

    [McpServerTool(Name = "get_appointments", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("The appointment list's visits, by handle: when, the patient, payer and member ID, the visit and its procedure, what the 270 would ask about, and each visit's checks before sending.")]
    public Task<string> GetAppointments(CancellationToken cancel = default) =>
        Session("read the appointment list", s =>
        {
            if (s.Appointments.Count == 0) throw new InvalidOperationException("No appointment list is open: open_file opens one (a CSV or Excel schedule).");
            var checks = InquiryBuilder.Check(s.Appointments, DateOnly.FromDateTime(s.Now));
            return new JsonObject
            {
                ["visits"] = new JsonArray([.. s.Appointments.Take(MaxResults).Select(a => (JsonNode)new JsonObject
                {
                    ["visit"] = Handle(a), ["date"] = a.Date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), ["time"] = a.Time, ["patient"] = a.SortName,
                    ["payer"] = a.SelfPay ? "self-pay" : a.Payer, ["member_id"] = a.MemberId.Length > 0 ? a.MemberId : null, ["dependent_of"] = a.IsDependent ? a.Subscriber : null,
                    ["visit_type"] = a.Visit, ["procedure"] = a.Procedure, ["charge"] = a.Charge,
                    ["asks"] = a.SelfPay ? null : string.Join(", ", ServiceTypes.For(a.Procedure).Select(t => $"{t} {Codes.ServiceType.Say(t)}")),
                    ["checks"] = Strings(checks.Where(c => c.Appointment == a).Select(c => $"{c.Level.ToString().ToLowerInvariant()}: {c.Text}")),
                })]),
            };
        }, cancel);

    [McpServerTool(Name = "check_appointments", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("The checks the appointment list gets before a 270 is written: errors (that visit can't be asked as it is), warnings (it would likely be rejected: a letter O among a member ID's digits, no birth date) and notes (self-pay).")]
    public Task<string> CheckAppointments(CancellationToken cancel = default) =>
        Session("checked the appointment list", s =>
        {
            if (s.Appointments.Count == 0) throw new InvalidOperationException("No appointment list is open: open_file opens one.");
            var checks = InquiryBuilder.Check(s.Appointments, DateOnly.FromDateTime(s.Now));
            return new JsonObject
            {
                ["errors"] = checks.Count(c => c.Level == CheckLevel.Error), ["warnings"] = checks.Count(c => c.Level == CheckLevel.Warning),
                ["checks"] = new JsonArray([.. checks.Select(c => (JsonNode)new JsonObject { ["visit"] = Handle(c.Appointment), ["level"] = c.Level.ToString().ToLowerInvariant(), ["says"] = c.Text })]),
            };
        }, cancel);

    [McpServerTool(Name = "service_types_for", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("What a 270 asks about for a procedure code: always 30 (plan coverage), and the kind of care the code is (98 office visit, BZ well visit, MH mental health, 62 MRI/CT…).")]
    public Task<string> ServiceTypesFor(
        [Description("A CPT, HCPCS or CDT code: 99213, 72148, D1110.")] string procedure,
        CancellationToken cancel = default) =>
        Session("looked up service types", s => new JsonObject { ["procedure"] = procedure, ["asks"] = new JsonArray([.. ServiceTypes.For(procedure).Select(t => (JsonNode)$"{t} {Codes.ServiceType.Say(t)}")]) }, cancel);

    [McpServerTool(Name = "write_inquiry", ReadOnly = false, Idempotent = false, Destructive = false, OpenWorld = false)]
    [Description("Writes the appointment list's 270 as a new .x12 file in the working folder, with the inquiry settings the person set (visits with errors and self-pay ones are left out). It holds people's details, so it needs the person to allow values.")]
    public Task<string> WriteInquiry(
        [Description("The new .x12 file, relative to the working folder.")] string output,
        CancellationToken cancel = default) =>
        Write("wrote a 270", output, "A 270", [".x12", ".edi", ".txt"], (s, full) =>
        {
            if (!s.SeeRealValues) throw new InvalidOperationException("A 270 holds people's names, birth dates and member IDs, and agents see values masked here: the person writes it (File › Write the 270), or lets agents see values (AI menu).");
            if (s.Appointments.Count == 0) throw new InvalidOperationException("No appointment list is open: open_file opens one.");
            var built = InquiryBuilder.Build(s.Appointments, s.Inquiry, s.Now);
            File.WriteAllText(full, built.Text);
            s.Remember(built);
            s.Open(full);
            return new JsonObject { ["questions"] = built.Traces.Count, ["payers"] = built.Transactions, ["left_out"] = built.Checks.Where(c => c.Level == CheckLevel.Error).Select(c => c.Appointment).Distinct().Count() };
        }, cancel);

    // ───────────────────────────── the worklist ─────────────────────────────

    private static JsonObject Counts(IReadOnlyList<WorkItem> work) =>
        new(work.GroupBy(w => w.Word).Select(g => KeyValuePair.Create(g.Key, (JsonNode?)g.Count())));

    [McpServerTool(Name = "get_worklist", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("The worklist: every visit on the appointment list with its answer read for that visit: ready, check, act first, rejected, no answer or self-pay; what decided it and what to do; the estimate; and the person handle of its answer. Days are numbered from the list's first (day 1).")]
    public Task<string> GetWorklist(
        [Description("Only one verdict: ready, check, act first, rejected, no answer or self-pay. Optional.")] string? only = null,
        CancellationToken cancel = default) =>
        Session("read the worklist", s =>
        {
            if (s.Appointments.Count == 0) throw new InvalidOperationException("No appointment list is open: open_file opens one, then the payers' 271s.");
            var work = s.Work;
            var first = work.Select(w => w.Appointment.Date).Where(d => d != null).DefaultIfEmpty(null).Min();
            return new JsonObject
            {
                ["counts"] = Counts(work),
                ["visits"] = new JsonArray([.. work.Where(w => only == null || w.Word.Equals(only.Trim(), StringComparison.OrdinalIgnoreCase)).Select(w =>
                {
                    var decider = w.Notes.FirstOrDefault(n => n.Level == w.Verdict);
                    return (JsonNode)new JsonObject
                    {
                        ["visit"] = Handle(w.Appointment), ["day"] = w.Appointment.Date is { } d && first is { } f ? d.DayNumber - f.DayNumber + 1 : null, ["time"] = w.Appointment.Time,
                        ["patient"] = w.Appointment.SortName, ["payer"] = w.Appointment.SelfPay ? "self-pay" : w.Appointment.Payer, ["visit_type"] = $"{w.Appointment.Visit} {w.Appointment.Procedure}",
                        ["verdict"] = w.Word, ["says"] = w.Headline, ["do"] = w.Verdict == Verdict.Ready ? null : decider?.Fix,
                        ["estimate"] = w.Estimate?.Amount, ["answer"] = w.Answer.Person is { } p ? Handle(s, p) : null,
                    };
                })]),
            };
        }, cancel);

    [McpServerTool(Name = "get_visit", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("One visit in full: its answer read for that visit's date and care, every note with what to do (the visit's own: its care needs authorization or isn't covered, a limit, a PCP elsewhere, the payer has the ID or name differently), how its answer was found, and the estimate with what it rests on and assumes.")]
    public Task<string> GetVisit(
        [Description(VisitHelp)] string visit,
        CancellationToken cancel = default) =>
        Session("read a visit", s =>
        {
            var w = VisitOf(s, visit);
            var a = w.Appointment;
            return new JsonObject
            {
                ["visit"] = Handle(a), ["patient"] = a.SortName, ["date"] = a.Date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), ["time"] = a.Time,
                ["payer"] = a.SelfPay ? "self-pay" : a.Payer, ["provider"] = a.Provider, ["visit_type"] = a.Visit, ["procedure"] = a.Procedure,
                ["care"] = w.Care is { } c ? $"{c} {Codes.ServiceType.Say(c)}" : null, ["charge"] = a.Charge,
                ["verdict"] = w.Word, ["headline"] = w.Headline, ["notes"] = Notes(w.Notes),
                ["answer"] = w.Answer.Person is { } p ? Handle(s, p) : null, ["found_by"] = w.Answer.How.Length > 0 ? w.Answer.How : null,
                ["estimate"] = w.Estimate is { } e ? new JsonObject { ["amount"] = e.Amount, ["basis"] = e.Basis, ["assumes"] = e.Assumes, ["note"] = "An estimate, not what the payer will decide." } : null,
            };
        }, cancel);

    [McpServerTool(Name = "estimate", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("What the patient might owe for a visit if its procedure or charge were different (\"what if it's a 99214 at $185?\"), from the same answer: the copay for that care, or what's left of the deductible then coinsurance, up to the out-of-pocket left. An estimate.")]
    public Task<string> Estimate(
        [Description(VisitHelp)] string visit,
        [Description("Another procedure code. Optional: the visit's.")] string? procedure = null,
        [Description("Another charge, in dollars. Optional: the visit's.")] decimal? charge = null,
        CancellationToken cancel = default) =>
        Session("estimated a visit", s =>
        {
            var w = VisitOf(s, visit);
            if (w.Reading == null) throw new InvalidOperationException($"{Handle(w.Appointment)} has no answer to estimate from ({w.Headline}).");
            var a = w.Appointment with { Procedure = (procedure ?? w.Appointment.Procedure).Trim().ToUpperInvariant(), Charge = charge ?? w.Appointment.Charge };
            var e = Worklist.Estimate(a, w.Reading);
            return new JsonObject
            {
                ["visit"] = Handle(w.Appointment), ["procedure"] = a.Procedure, ["charge"] = a.Charge,
                ["estimate"] = e?.Amount, ["basis"] = e?.Basis ?? "The answer doesn't say enough to estimate (or coverage isn't active, or another payer is primary).", ["assumes"] = e?.Assumes,
            };
        }, cancel);

    [McpServerTool(Name = "compare_answers", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("What changed about a person since an earlier answer from the same payer that's open too: the verdict, coverage, plan, the deductible and out-of-pocket used since, limits, other payers, the PCP, copays.")]
    public Task<string> CompareAnswers(
        [Description(PersonHelp + " The later answer.")] string person,
        CancellationToken cancel = default) =>
        Session("compared two answers", s =>
        {
            var p = PersonOf(s, person);
            var earlier = Changes.Earlier(p, s.Files) ?? throw new InvalidOperationException($"No earlier answer about {Handle(s, p)} from {p.Transaction.Payer?.Name} is open: open_file opens one.");
            return new JsonObject
            {
                ["person"] = Handle(s, p), ["earlier"] = Handle(s, earlier), ["earlier_made"] = earlier.Transaction.Created?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["changes"] = new JsonArray([.. Changes.Between(earlier, p).Select(c => (JsonNode)c)]),
            };
        }, cancel);

    [McpServerTool(Name = "export_worklist", ReadOnly = false, Idempotent = false, Destructive = false, OpenWorld = false)]
    [Description("Writes the worklist as a new .html page or .xlsx workbook in the working folder: the counts, then every visit with its answer, what to do and the estimate. People's details are masked in it unless the person allows values.")]
    public Task<string> ExportWorklist(
        [Description("The new file, relative to the working folder: .html or .xlsx.")] string output,
        CancellationToken cancel = default) =>
        Write("exported the worklist", output, "A worklist", [".html", ".htm", ".xlsx"], (s, full) =>
        {
            if (s.Appointments.Count == 0) throw new InvalidOperationException("No appointment list is open: open_file opens one.");
            Func<string, string>? mask = s.SeeRealValues ? null : new Masker(s.Files, s.Appointments).Text;
            if (full.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)) WorklistReport.Excel(full, s.Work, mask);
            else File.WriteAllText(full, WorklistReport.Html(s.Work, "Worklist", s.Now, mask));
            return new JsonObject { ["visits"] = s.Work.Count };
        }, cancel);

    [McpServerTool(Name = "ask_clearinghouse", ReadOnly = false, Idempotent = false, Destructive = false, OpenWorld = true)]
    [Description("Sends the appointment list's questions to the clearinghouse in real time (CAQH CORE), and opens the answers. Only in the open window, with the clearinghouse the person set up, and only when the person says yes.")]
    public Task<string> AskClearinghouse(CancellationToken cancel = default) =>
        Run("asked the clearinghouse", async s =>
        {
            if (!host.Live || s.AskClearinghouse is not { } ask)
                throw new InvalidOperationException("That's only done in the open window, with the person saying yes (AVAEligibility --mcp --attach, or its AI Chat).");
            var said = await ask();
            return Finish(s, new JsonObject { ["said"] = said, ["worklist"] = Counts(s.Work) });
        }, cancel);

    // ───────────────────────────── the view ─────────────────────────────

    private static readonly string[] Tabs = ["coverage", "inquiries", "worklist"];

    [McpServerTool(Name = "get_view", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("What the window shows: the tab, and the person or visit selected.")]
    public Task<string> GetView(CancellationToken cancel = default) =>
        Session("looked at the view", s => new JsonObject { ["tab"] = s.View.Tab, ["selected"] = s.View.Handle, ["live"] = host.Live }, cancel);

    [McpServerTool(Name = "show_in_view", ReadOnly = false, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("Shows the person something in their window: a person's answer (P01) or a visit (V01) on the coverage tab, or the inquiries or worklist tab. Headless, it only remembers it (render_view draws it).")]
    public Task<string> ShowInView(
        [Description("A person (P01) or a visit (V01). Optional.")] string? handle = null,
        [Description("coverage, inquiries or worklist. Default coverage.")] string tab = "coverage",
        CancellationToken cancel = default) =>
        Session("showed something", s =>
        {
            var t = tab.Trim().ToLowerInvariant();
            if (!Tabs.Contains(t)) throw new ArgumentException("tab is coverage, inquiries or worklist.");
            var h = handle?.Trim().ToUpperInvariant();
            if (h != null) _ = h.StartsWith('V') ? VisitOf(s, h).Appointment : (object)PersonOf(s, h);
            s.Show(new ViewState(t, h));
            return new JsonObject { ["showing"] = t, ["selected"] = h };
        }, cancel);

    [McpServerTool(Name = "render_view", ReadOnly = true, Idempotent = true, Destructive = false, OpenWorld = false)]
    [Description("A picture (PNG) to show the person: a person's answer or a visit, verdict first, with its notes and lines; without one, what the window shows (or the worklist). Masked as everything else is.")]
    public async Task<CallToolResult> RenderView(
        [Description("A person (P01) or a visit (V01). Optional.")] string? handle = null,
        [Description("Width in pixels, from 200 to 2000. Default 1000.")] int width = 1000,
        CancellationToken cancel = default)
    {
        try
        {
            if (width is < 200 or > 2000) throw new ArgumentException("width is from 200 to 2000 pixels.");
            var picture = await host.RunAsync("drew the view", s => PictureOf(s, new ViewState(s.View.Tab, handle?.Trim().ToUpperInvariant() ?? s.View.Handle)), cancel).ConfigureAwait(false);
            var png = await host.RunAsync("drew the view", _ => host.Painter.Picture(picture, width / 1000.0), cancel).ConfigureAwait(false);
            return new CallToolResult { Content = [ImageContentBlock.FromBytes(png, "image/png"), new TextContentBlock { Text = picture.Title + (picture.Subtitle.Length > 0 ? " — " + picture.Subtitle : "") }] };
        }
        catch (Exception e) when (e is not McpException and not OperationCanceledException)
        {
            throw new McpException(Explain(e), e);
        }
    }

    internal ViewPicture PictureOf(IExplorerSession s, ViewState view)
    {
        var mask = s.SeeRealValues ? (Func<string, string>)(x => x) : new Masker(s.Files, s.Appointments).Text;
        ViewPicture Masked(string title, string sub, IEnumerable<string> lines, IEnumerable<string> notes) =>
            new(mask(title), mask(sub), [.. lines.Select(mask)], 1, 0, 0, false, [.. notes.Select(mask)]);
        if (view.Handle is { } h && h.StartsWith('V'))
        {
            var w = VisitOf(s, h);
            return Masked($"{w.Appointment.SortName}: {w.Word}", w.Headline, w.Notes.OrderByDescending(n => n.Level).SelectMany(n => new[] { n.Text, "   " + n.Fix }),
                w.Estimate is { } e ? [e.ToString(), $"An estimate: it assumes {e.Assumes}."] : []);
        }
        if (view.Handle is { } ph)
        {
            var p = PersonOf(s, ph);
            var r = Interpreter.Read(p);
            return Masked($"{p.Name}: {r.Word}", $"{p.Transaction.Payer?.Name} · {r.Headline}",
                [.. r.Notes.Select(n => n.Text), .. p.Benefits.Select(Words.Describe)], [.. r.Accumulators.Select(a => a.ToString())]);
        }
        var work = s.Work;
        return Masked("Worklist", work.Count == 0 ? "No appointment list open" : string.Join(", ", work.GroupBy(w => w.Word).Select(g => $"{g.Count()} {g.Key}")),
            work.Select(w => $"{w.Appointment.Time,-6} {w.Appointment.SortName,-22} {w.Word,-10} {w.Headline}"), []);
    }

    // ───────────────────────────── helpers ─────────────────────────────

    /// <summary>Every string in an answer masked (unless values are allowed), and "values_masked" added.</summary>
    private static string Finish(IExplorerSession s, JsonObject o)
    {
        if (!s.SeeRealValues) MaskStrings(o, new Masker(s.Files, s.Appointments));
        o["values_masked"] = !s.SeeRealValues;
        return o.ToJsonString(Json.Options);
    }

    private static void MaskStrings(JsonNode? node, Masker m)
    {
        switch (node)
        {
            case JsonObject o:
                foreach (var key in o.Select(kv => kv.Key).ToList())
                {
                    if (o[key] is JsonValue v && v.TryGetValue<string>(out var text)) o[key] = m.Text(text);
                    else MaskStrings(o[key], m);
                }
                break;
            case JsonArray a:
                for (var i = 0; i < a.Count; i++)
                {
                    if (a[i] is JsonValue v && v.TryGetValue<string>(out var text)) a[i] = m.Text(text);
                    else MaskStrings(a[i], m);
                }
                break;
        }
    }

    private async Task<string> Session(string action, Func<IExplorerSession, JsonObject> work, CancellationToken cancel)
    {
        try
        {
            return await host.RunAsync(action, s => Finish(s, work(s)), cancel).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not McpException and not OperationCanceledException)
        {
            throw new McpException(Explain(e), e);
        }
    }

    /// <summary>Runs a call that writes one new file in the working folder, and answers with where it went.</summary>
    private async Task<string> Write(string action, string output, string what, string[] extensions, Func<IExplorerSession, string, JsonObject> work, CancellationToken cancel)
    {
        try
        {
            var full = _files.Resolve(output, what, extensions);
            _files.CheckWritable(full);
            return await host.RunAsync(action, s =>
            {
                var o = work(s, full);
                _files.Wrote(full);
                o["wrote"] = _files.Relative(full);
                return Finish(s, o);
            }, cancel).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not McpException and not OperationCanceledException)
        {
            throw new McpException(Explain(e), e);
        }
    }

    private async Task<T> Run<T>(string action, Func<IExplorerSession, T> work, CancellationToken cancel)
    {
        try
        {
            return await host.RunAsync(action, work, cancel).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not McpException and not OperationCanceledException)
        {
            throw new McpException(Explain(e), e);
        }
    }

    private async Task<T> Run<T>(string action, Func<IExplorerSession, Task<T>> work, CancellationToken cancel)
    {
        try
        {
            return await host.RunAsync(action, work, cancel).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not McpException and not OperationCanceledException)
        {
            throw new McpException(Explain(e), e);
        }
    }

    internal static string Explain(Exception e) => e switch
    {
        ArgumentException or InvalidOperationException or FormatException or NotSupportedException or IOException
            or UnauthorizedAccessException or ExplorerBusyException or KeyNotFoundException or InvalidDataException => e.Message,
        _ => $"Something went wrong ({e.GetType().Name}): {e.Message}",
    };
}
