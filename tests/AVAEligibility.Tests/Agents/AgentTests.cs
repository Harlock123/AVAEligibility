using AVAEligibility.Core.Authorizations;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Threading;
using AVAEligibility.Agents;
using AVAEligibility.Agents.Chat;
using AVAEligibility.Core.Eligibility;
using AVAEligibility.Core.Inquiries;
using AVAEligibility.Mcp;
using ModelContextProtocol.Server;
using static AVAEligibility.Tests.Agents.FakeModelServer;

namespace AVAEligibility.Tests.Agents;

/// <summary>
/// Before any test runs: the AI side's settings, keys, token and saved chats go to a temporary folder, so no test
/// reads or writes the person's own (and none ever finds a real API key).
/// </summary>
internal static class AgentSettingsIsolation
{
    [ModuleInitializer]
    internal static void Isolate() =>
        AgentSettings.FolderOverride = Directory.CreateTempSubdirectory("avaeligibility-agents-").FullName;
}

internal sealed class AvaloniaThread : IUiThread
{
    public Task<T> InvokeAsync<T>(Func<Task<T>> work) => Dispatcher.UIThread.InvokeAsync(work);
}

/// <summary>
/// The MCP server and the AI Chat, driven as agents drive them: through an in-process MCP client over the real server,
/// on Avalonia's headless platform, on a copy of the samples, with people's details masked unless a test says otherwise.
/// The chat's models are scripted fakes of both APIs; no real model or key is ever used.
/// </summary>
public sealed class AgentTests
{
    /// <summary>Every tool the server offers. A new tool goes here, with descriptions on it and its arguments.</summary>
    internal static readonly string[] ToolNames =
    [
        "list_files", "open_file", "close_file", "list_open", "list_people", "get_answer", "get_benefits", "get_segments", "explain_code", "search_codes",
        "get_appointments", "check_appointments", "service_types_for", "write_inquiry", "get_authorizations", "write_auth_request", "get_worklist", "get_visit", "estimate", "compare_answers",
        "export_worklist", "ask_clearinghouse", "get_view", "show_in_view", "render_view",
        "batch", "get_recording", "save_steps", "run_steps", "checkpoint", "diff", "render_checkpoint", "restore_checkpoint",
    ];

    /// <summary>A server on a copy of the samples, with no window round it.</summary>
    internal sealed class Rig : IDisposable
    {
        private readonly Window _window;

        public HeadlessDesk Desk { get; }
        public string Root { get; } = Samples.Copy();
        public McpToolBridge Bridge { get; }
        public HeadlessSession Session => (HeadlessSession)Desk.Session;

        public Rig(bool real = false)
        {
            Desk = new HeadlessDesk(null, real, InquiryTests.SampleSettings, () => InquiryTests.SampleTime);
            var stage = new Panel();
            _window = new Window { Width = 20, Height = 20, Content = stage };
            _window.Show();
            Bridge = Wait(McpToolBridge.StartAsync(new ExplorerHost(new AvaloniaThread(), Desk, new ViewPainter(stage), Root)));
        }

        public ToolOutcome Call(string tool, object? args = null) =>
            Wait(Bridge.CallAsync(tool, JsonSerializer.SerializeToElement(args ?? new { }).EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone())));

        public JsonElement Ok(string tool, object? args = null)
        {
            var o = Call(tool, args);
            Assert.False(o.IsError, $"{tool} failed: {o.Text}");
            return o.Text.StartsWith('{') ? JsonDocument.Parse(o.Text).RootElement.Clone() : default;
        }

        public string Error(string tool, object? args = null)
        {
            var o = Call(tool, args);
            Assert.True(o.IsError, $"{tool} should have failed: {o.Text}");
            return o.Text;
        }

        /// <summary>Opens the appointment list and the four payers' answers.</summary>
        public void OpenDay()
        {
            Ok("open_file", new { file = "appointments.csv" });
            foreach (var f in WorklistTests.AnswerFiles) Ok("open_file", new { file = f });
        }

        public void Dispose()
        {
            Wait(Bridge.DisposeAsync().AsTask().ContinueWith(_ => true));
            _window.Close();
        }
    }

    internal static T Wait<T>(Task<T> task, int seconds = 30) => HeadlessApp.Wait(task, seconds);

    private static void WithRig(Action<Rig> test, bool real = false) => HeadlessApp.Run(() =>
    {
        using var rig = new Rig(real);
        test(rig);
    });

    private static IEnumerable<string> Strings(JsonElement e, string name) => e.GetProperty(name).EnumerateArray().Select(x => x.GetString()!);

    [Fact]
    public void The_tools_are_the_ones_listed_and_each_says_what_it_and_its_arguments_are() => WithRig(rig =>
    {
        var tools = Wait(rig.Bridge.ListToolsAsync());
        Assert.Equal(ToolNames.Order(), tools.Select(t => t.Name).Order());
        foreach (var t in tools)
        {
            Assert.False(string.IsNullOrWhiteSpace(t.Description), t.Name);
            if (t.InputSchema.TryGetProperty("properties", out var props))
                foreach (var p in props.EnumerateObject())
                {
                    Assert.True(p.Value.TryGetProperty("description", out _), $"{t.Name}.{p.Name} has no description.");
                    Assert.Matches("^[a-z_]+$", p.Name);
                }
        }
        Assert.Contains("masked to its shape", EligibilityServer.Instructions);
        Assert.Contains("has no argument \"nope\"", rig.Error("list_files", new { nope = 1 }));
    });

    [Fact]
    public void The_day_is_opened_and_read_with_peoples_details_masked() => WithRig(rig =>
    {
        var files = rig.Ok("list_files").GetProperty("files").EnumerateArray().ToDictionary(f => f.GetProperty("file").GetString()!, f => f.GetProperty("kind").GetString());
        Assert.Equal(("appointment list", "271", "270"), (files["appointments.csv"], files["bluewater-271.x12"], files["clinic-270.x12"]));
        Assert.Equal("271", files["recheck/bluewater-271-dec15.x12"]);
        rig.OpenDay();
        var work = rig.Ok("get_worklist");
        Assert.True(work.GetProperty("values_masked").GetBoolean());
        Assert.Equal(4, work.GetProperty("counts").GetProperty("act first").GetInt32());
        var kenji = work.GetProperty("visits").EnumerateArray().Single(v => v.GetProperty("visit").GetString() == "V07");
        Assert.Equal(("N*******, K****", "act first", 538m, 2), (kenji.GetProperty("patient").GetString(), kenji.GetProperty("verdict").GetString(), kenji.GetProperty("estimate").GetDecimal(), kenji.GetProperty("day").GetInt32()));
        Assert.Equal("Get it from BLUEWATER UTILIZATION REVIEW at 800-555-0147 before the visit, and put its number on the claim.", kenji.GetProperty("do").GetString());

        var visit = rig.Ok("get_visit", new { visit = "V10" });
        Assert.Equal("The payer has member ID S**9999999; the registration has S**99999.", visit.GetProperty("headline").GetString());
        Assert.Equal("2026-**-**", visit.GetProperty("date").GetString());
        var people = rig.Ok("list_people").GetProperty("people").EnumerateArray().ToList();
        Assert.Equal(("P04", "K********, P****"), (people[3].GetProperty("person").GetString(), people[3].GetProperty("name").GetString()));
        var peter = rig.Ok("get_answer", new { person = "P04" });
        Assert.Equal("B**99999*999", peter.GetProperty("member_id").GetString());
        Assert.False(peter.TryGetProperty("born", out var none) && none.ValueKind != JsonValueKind.Null);
        Assert.Equal("1965-**-**", rig.Ok("get_answer", new { person = "P05" }).GetProperty("born").GetString());
        var marcus = rig.Ok("get_answer", new { person = "P06" });
        Assert.Equal(["MEDICARE PART B (primary), Medicare Part B, policy 4**9**9**99, since 2022-**-**"], Strings(marcus, "other_payers"));
        Assert.Contains("(address hidden)", rig.Ok("get_segments", new { person = "P01" }).GetProperty("segments").EnumerateArray().Select(x => x.GetString()).Single(x => x!.Contains("N3*")));
        var checks = rig.Ok("check_appointments");
        Assert.Contains("Member ID B**99999*999 has a letter O, I or l among its digits", checks.GetProperty("checks")[0].GetProperty("says").GetString());
    });

    [Fact]
    public void Codes_estimates_and_changes_are_explained() => WithRig(rig =>
    {
        var code = rig.Ok("explain_code", new { list = "reject", code = "72" });
        Assert.Equal(("Invalid/Missing Subscriber/Insured ID", "Check the member ID against the card: a letter O for a zero, a missing prefix or suffix, a dependent's suffix."), (code.GetProperty("meaning").GetString(), code.GetProperty("what_to_do").GetString()));
        Assert.Contains("service_type MH: Mental Health", Strings(rig.Ok("search_codes", new { words = "mental health" }), "found"));
        Assert.Contains("is one of", rig.Error("explain_code", new { list = "nope", code = "1" }));
        Assert.Equal(["30 Health Benefit Plan Coverage", "62 MRI/CAT Scan"], Strings(rig.Ok("service_types_for", new { procedure = "72148" }), "asks"));
        rig.OpenDay();
        var e = rig.Ok("estimate", new { visit = "V01", procedure = "99285", charge = 900 });
        Assert.Equal((250m, "the $250.00 copay for Emergency Services"), (e.GetProperty("estimate").GetDecimal(), e.GetProperty("basis").GetString()));
        rig.Ok("open_file", new { file = "recheck/bluewater-271-dec15.x12" });
        var changes = rig.Ok("compare_answers", new { person = "P12" });
        Assert.Equal(("P01", "2026-**-**"), (changes.GetProperty("earlier").GetString(), changes.GetProperty("earlier_made").GetString()));
        Assert.Contains("Deductible, individual, in network: $845.00 left (was $1,120.00; $275.00 used since).", Strings(changes, "changes"));
        Assert.Contains("No earlier answer", rig.Error("compare_answers", new { person = "P01" }));
    });

    [Fact]
    public void Writing_a_270_needs_values_allowed_and_sending_needs_the_window() => WithRig(rig =>
    {
        rig.Ok("open_file", new { file = "appointments.csv" });
        Assert.Contains("holds people's names", rig.Error("write_inquiry", new { output = "out/day.x12" }));
        Assert.Contains("only done in the open window", rig.Error("ask_clearinghouse"));
        rig.Ok("export_worklist", new { output = "out/worklist.html" });
        var html = File.ReadAllText(Path.Combine(rig.Root, "out", "worklist.html"));
        Assert.DoesNotContain("HARTWELL", html);
        Assert.Contains("H*******, D*****", html);
        File.WriteAllText(Path.Combine(rig.Root, "theirs.html"), "the person's own file");
        Assert.Contains("already exists", rig.Error("export_worklist", new { output = "theirs.html" }));
        Assert.Equal("the person's own file", File.ReadAllText(Path.Combine(rig.Root, "theirs.html")));
        Assert.Contains("outside", rig.Error("export_worklist", new { output = "../x.html" }));
    });

    [Fact]
    public void The_MRI_needs_authorization_and_its_278_is_written_then_answered() => WithRig(rig =>
    {
        rig.Ok("open_file", new { file = "appointments.csv" });
        rig.Ok("write_inquiry", new { output = "out/day.x12" });
        foreach (var f in WorklistTests.AnswerFiles) rig.Ok("open_file", new { file = f });
        var visit = rig.Ok("get_authorizations").GetProperty("visits").EnumerateArray().Single();
        Assert.Equal(("V07", "not asked", "BLUEWATER UTILIZATION REVIEW", "800-555-0147", "M54.16"),
            (visit.GetProperty("visit").GetString(), visit.GetProperty("status").GetString(), visit.GetProperty("reviewer").GetString(), visit.GetProperty("phone").GetString(), visit.GetProperty("diagnosis").GetString()));
        var w = rig.Ok("write_auth_request", new { output = "out/auth.x12" });
        Assert.Equal((1, 0), (w.GetProperty("requests").GetInt32(), w.GetProperty("left_out").GetInt32()));
        Assert.Equal(File.ReadAllText(Samples.Path("authorizations/clinic-278.x12")), File.ReadAllText(Path.Combine(rig.Root, "out", "auth.x12")));
        Assert.Equal("asked", rig.Ok("get_authorizations").GetProperty("visits")[0].GetProperty("status").GetString());
        Assert.Equal("278 answer", rig.Ok("open_file", new { file = "authorizations/bluewater-278.x12" }).GetProperty("kind").GetString());
        var done = rig.Ok("get_authorizations").GetProperty("visits")[0];
        Assert.Equal(("certified", "BWUR2611250042", "2026-12-02 to 2027-01-01", "Certified in total"),
            (done.GetProperty("status").GetString(), done.GetProperty("number").GetString(), done.GetProperty("runs").GetString(), done.GetProperty("decision").GetString()));
        Assert.Contains("No visit needs a 278", rig.Error("write_auth_request", new { output = "out/again.x12" }));
    }, real: true);

    [Fact]
    public void Writing_a_278_needs_values_allowed() => WithRig(rig =>
    {
        rig.Ok("open_file", new { file = "appointments.csv" });
        foreach (var f in WorklistTests.AnswerFiles) rig.Ok("open_file", new { file = f });
        Assert.Contains("holds people's names", rig.Error("write_auth_request", new { output = "out/auth.x12" }));
        Assert.False(File.Exists(Path.Combine(rig.Root, "out", "auth.x12")));
    });

    [Fact]
    public void With_values_allowed_the_270_is_written_and_its_answers_match_by_trace() => WithRig(rig =>
    {
        rig.Ok("open_file", new { file = "appointments.csv" });
        var w = rig.Ok("write_inquiry", new { output = "out/day.x12" });
        Assert.Equal((12, 4), (w.GetProperty("questions").GetInt32(), w.GetProperty("payers").GetInt32()));
        Assert.False(w.GetProperty("values_masked").GetBoolean());
        Assert.Equal(File.ReadAllText(Samples.Path("clinic-270.x12")), File.ReadAllText(Path.Combine(rig.Root, "out", "day.x12")));
        foreach (var f in WorklistTests.AnswerFiles) rig.Ok("open_file", new { file = f });
        Assert.Equal("trace BW0007", rig.Ok("get_visit", new { visit = "V07" }).GetProperty("found_by").GetString());
        Assert.Equal("NAKAMURA, KENJI", rig.Ok("get_visit", new { visit = "V07" }).GetProperty("patient").GetString());
    }, real: true);

    [Fact]
    public void The_view_is_drawn_shown_and_batches_steps_and_checkpoints_work() => WithRig(rig =>
    {
        rig.OpenDay();
        foreach (var h in new[] { "P07", "V09", (string?)null })
        {
            var shot = rig.Call("render_view", new { handle = h });
            Assert.False(shot.IsError, shot.Text);
            Assert.Single(shot.Images);
        }
        Assert.StartsWith("N*******, K****: act first", rig.Call("render_view", new { handle = "V07" }).Text);
        rig.Ok("show_in_view", new { handle = "V07", tab = "coverage" });
        Assert.Equal(new ViewState("coverage", "V07"), rig.Session.View);
        Assert.Contains("There's no visit", rig.Error("show_in_view", new { handle = "V99" }));
        var batch = rig.Ok("batch", new { steps = new object[] { new { tool = "get_visit", args = new { visit = "V01" } }, new { tool = "get_visit", args = new { visit = "V02" } } } });
        Assert.Equal(2, batch.GetProperty("answers").GetArrayLength());
        rig.Ok("checkpoint", new { name = "before" });
        rig.Ok("close_file", new { file = "cascade-271.x12" });
        Assert.Contains("showed", rig.Ok("diff", new { since = "before" }).ToString() + "showed");
        Assert.Single(rig.Call("render_checkpoint", new { name = "before" }).Images);
        rig.Ok("restore_checkpoint", new { name = "before" });
        Assert.Equal(4, rig.Session.Files.Count);
        rig.Ok("save_steps", new { path = "steps/visit", steps = new object[] { new { tool = "get_visit", args = new { visit = "${visit}" } } }, parameters = new { visit = "V01" } });
        Assert.Equal("V05", rig.Ok("run_steps", new { path = "steps/visit.avasteps.json", values = new { visit = "V05" } }).GetProperty("answers")[0].GetProperty("answer").GetProperty("visit").GetString());
    });

    // ───────────────────────────── people's details ─────────────────────────────

    /// <summary>Every person's detail in the samples: each part of each name, every member ID and identifier, birth and visit dates, addresses.</summary>
    internal static HashSet<string> Phi()
    {
        var phi = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = Directory.EnumerateFiles(Samples.Folder, "*.x12", SearchOption.AllDirectories).Select(EligibilityReader.Load).ToList();
        foreach (var p in files.SelectMany(f => f.People))
        {
            foreach (var part in $"{p.Last} {p.First}".Split([' ', '-'], StringSplitOptions.RemoveEmptyEntries).Where(x => x.Length >= 3)) phi.Add(part);
            if (p.MemberId.Length > 0) phi.Add(p.MemberId);
            if (p.BirthDate is { } b) { phi.Add(b.ToString("yyyy-MM-dd")); phi.Add(b.ToString("yyyyMMdd")); }
            foreach (var s in p.Segments.Where(s => s.Id == "N3")) phi.Add(s[1]);
            foreach (var r in p.Benefits.SelectMany(b => b.References)) phi.Add(r.Value);
        }
        foreach (var r in Directory.EnumerateFiles(Samples.Path("authorizations")).Select(AuthReader.Load).SelectMany(f => f.Reviews))
            if (r.Number.Length > 0) phi.Add(r.Number);
        foreach (var a in AppointmentList.Load(Samples.Path("appointments.csv")).Concat(AppointmentList.Load(Samples.Path("denied/eligibility-recheck.csv"))))
        {
            if (a.Account.Length > 0) phi.Add(a.Account);
            phi.Add(a.MemberId is { Length: > 0 } m ? m : "-");
            if (a.BirthDate is { } b) phi.Add(b.ToString("yyyy-MM-dd"));
            if (a.Date is { } d) { phi.Add(d.ToString("yyyy-MM-dd")); phi.Add(d.ToString("yyyyMMdd")); }
        }
        phi.Remove("-");
        return phi;
    }

    [Fact]
    public void The_list_of_details_the_no_leak_test_looks_for_covers_every_kind()
    {
        var phi = Phi();
        foreach (var real in new[] { "HARTWELL", "KOWALCZYK", "BWH40010O512", "SMH5512001", "1971-03-14", "19650218", "100 OAK ST", "4TQ8NJ2KR63", "2026-12-02", "VANTERPOOL", "RB26119", "TG55090123", "TG55090132", "2026-11-04", "SANTOS", "BWUR2611250042" }) Assert.Contains(real, phi);
        Assert.True(phi.Count > 60, $"{phi.Count}");
    }

    [Fact]
    public void No_tool_result_carries_a_persons_detail_when_values_are_masked() => WithRig(rig =>
    {
        var phi = Phi();
        var seen = new List<(string Call, string Text)>();
        void Check(string tool, object? args = null)
        {
            var o = rig.Call(tool, args);
            Assert.False(o.IsError, $"{tool} {JsonSerializer.Serialize(args)}: {o.Text}");
            seen.Add(($"{tool} {JsonSerializer.Serialize(args)}", o.Text));
        }
        Check("list_files");
        Check("open_file", new { file = "appointments.csv" });
        foreach (var f in WorklistTests.AnswerFiles.Append("clinic-270.x12").Append("recheck/bluewater-271-dec15.x12")) Check("open_file", new { file = f });
        Check("list_open");
        Check("list_people");
        var handles = rig.Ok("list_people").GetProperty("people").EnumerateArray().Select(p => p.GetProperty("person").GetString()!).ToList();
        foreach (var h in handles)
        {
            Check("get_answer", new { person = h });
            Check("get_benefits", new { person = h });
            Check("get_segments", new { person = h });
            Check("render_view", new { handle = h });
        }
        Check("compare_answers", new { person = handles[^7] });
        Check("get_appointments");
        Check("check_appointments");
        Check("get_authorizations");
        Check("open_file", new { file = "authorizations/clinic-278.x12" });
        Check("open_file", new { file = "authorizations/bluewater-278.x12" });
        Check("list_open");
        Check("get_authorizations");
        Check("get_worklist");
        foreach (var w in rig.Session.Work)
        {
            var v = EligibilityTools.Handle(w.Appointment);
            Check("get_visit", new { visit = v });
            Check("render_view", new { handle = v });
            if (w.Reading != null) Check("estimate", new { visit = v, procedure = "99214", charge = 185 });
        }
        Check("export_worklist", new { output = "leak/worklist.html" });
        Check("export_worklist", new { output = "leak/worklist.xlsx" });
        Check("show_in_view", new { handle = "V07" });
        Check("get_view");
        Check("batch", new { steps = new object[] { new { tool = "get_visit", args = new { visit = "V04" } }, new { tool = "get_answer", args = new { person = "P05" } } } });
        Check("get_recording");
        Check("checkpoint", new { name = "x" });
        Check("diff", new { since = "x" });

        // A denied claims list from AVA Remittance, and the 271 for the dates of service.
        Check("open_file", new { file = "denied/eligibility-recheck.csv" });
        Check("open_file", new { file = "denied/tallgrass-271.x12" });
        Check("get_appointments");
        Check("check_appointments");
        Check("get_worklist");
        Assert.Equal(3, rig.Session.Work.Count(w => w.Appointment.Denied));
        foreach (var w in rig.Session.Work)
        {
            var v = EligibilityTools.Handle(w.Appointment);
            Check("get_visit", new { visit = v });
            Check("render_view", new { handle = v });
        }
        foreach (var h in rig.Ok("list_people").GetProperty("people").EnumerateArray().Select(p => p.GetProperty("person").GetString()!)) Check("get_answer", new { person = h });
        Check("export_worklist", new { output = "leak/denied.html" });
        Check("export_worklist", new { output = "leak/denied.xlsx" });

        var html = File.ReadAllText(Path.Combine(rig.Root, "leak", "worklist.html")) + File.ReadAllText(Path.Combine(rig.Root, "leak", "denied.html"));
        using var book = new ClosedXML.Excel.XLWorkbook(Path.Combine(rig.Root, "leak", "worklist.xlsx"));
        using var denied = new ClosedXML.Excel.XLWorkbook(Path.Combine(rig.Root, "leak", "denied.xlsx"));
        var xlsx = string.Join("\n", book.Worksheet(1).CellsUsed().Concat(denied.Worksheet(1).CellsUsed()).Select(c => c.GetFormattedString()));
        var words = phi.Select(real => (real, new Regex($"(?<![A-Za-z0-9]){Regex.Escape(real)}(?![A-Za-z0-9])", RegexOptions.IgnoreCase))).ToList();
        var leaks = new List<string>();
        foreach (var (call, text) in seen.Append(("file worklist.html", html)).Append(("file worklist.xlsx", xlsx)))
            foreach (var (real, word) in words)
                if (word.IsMatch(text))
                    leaks.Add($"{call}: \"{real}\"");
        Assert.True(seen.Count > 80, $"Only {seen.Count} calls were made.");
        Assert.All(seen.Where(s => s.Text.StartsWith('{')), s => Assert.DoesNotContain("\"values_masked\":false", s.Text));
        Assert.True(leaks.Count == 0, $"{leaks.Count} leaks:\n" + string.Join("\n", leaks.Distinct().Take(60)));
    });

    // ───────────────────────────── recipes ─────────────────────────────

    [Fact]
    public void Every_recipe_runs_call_by_call_with_values_masked() => HeadlessApp.Run(() =>
    {
        var recipes = typeof(Recipes).GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.GetCustomAttribute<McpServerPromptAttribute>() != null).ToList();
        Assert.Equal(9, recipes.Count);
        foreach (var recipe in recipes)
        {
            using var rig = new Rig();
            var text = (string)recipe.Invoke(null, recipe.GetParameters().Select(_ => (object?)null).ToArray())!;
            var calls = text.Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith(Recipes.CallMarker, StringComparison.Ordinal)).ToList();
            Assert.True(calls.Count >= 3, recipe.Name);
            foreach (var call in calls)
            {
                var rest = call[Recipes.CallMarker.Length..];
                var space = rest.IndexOf(' ');
                var tool = rest[..space];
                Assert.Contains(tool, ToolNames);
                var args = JsonDocument.Parse(rest[(space + 1)..]).RootElement;
                var outcome = Wait(rig.Bridge.CallAsync(tool, args.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone())));
                Assert.False(outcome.IsError, $"{recipe.Name}: {tool} failed: {outcome.Text}");
            }
        }
    });

    // ───────────────────────────── the chat ─────────────────────────────

    [Fact]
    public void A_chat_on_a_local_openai_compatible_model_reads_shows_and_its_reply_can_be_undone() => WithRig(rig =>
    {
        var server = new FakeModelServer("http://localhost:1234/v1")
            .Reply(Say("I'll open the day."), Call("c1", "open_file", new { file = "appointments.csv" }), Call("c2", "open_file", new { file = "bluewater-271.x12" }))
            .Reply(Call("c3", "get_visit", new { visit = "V07" }), Call("c4", "show_in_view", new { handle = "V07" }))
            .Reply(Say("V07's MRI needs authorization first: call Bluewater Utilization Review at 800-555-0147."));
        var chat = new ChatSession(rig.Bridge) { Provider = ChatProvider.OpenAICompatible, Server = server.Client(), ServerModel = "qwen/qwen3-8b" };
        Wait(chat.SendAsync("who needs something before their visit?").ContinueWith(t => { t.GetAwaiter().GetResult(); return true; }));

        Assert.Equal(["open_file", "open_file", "get_visit", "show_in_view"], chat.Entries.OfType<ToolEntry>().Select(t => t.Name));
        Assert.All(chat.Entries.OfType<ToolEntry>(), t => Assert.False(t.IsError, t.Result));
        var system = (string?)server.Requests[0]["messages"]![0]!["content"];
        Assert.Contains("AVA Eligibility", system);
        Assert.Contains("masked", system);
        Assert.Contains("\"values_masked\":true", (string?)server.Requests[1]["messages"]!.AsArray().Last()!["content"]);
        Assert.Equal("V07", rig.Session.View.Handle);

        var undo = Assert.Single(chat.Entries.OfType<ReplyUndoEntry>());
        Assert.True(Wait(chat.UndoReplyAsync(undo)));
        Assert.Null(rig.Session.View.Handle);
        Assert.Empty(rig.Session.Files);
    });

    [Fact]
    public void A_chat_on_a_local_server_that_speaks_the_messages_api_uses_the_core_tools() => WithRig(rig =>
    {
        var claude = new FakeMessagesServer()
            .Reply(new FakeMessagesServer.ToolUse("t1", "explain_code", new { list = "reject", code = "75" }))
            .Reply(new FakeMessagesServer.Words("75 means the payer can't find the subscriber."));
        var client = new Anthropic.AnthropicClient { ApiKey = "local", BaseUrl = "http://localhost:1234", HttpClient = new HttpClient(claude) };
        var chat = new ChatSession(rig.Bridge) { Provider = ChatProvider.Anthropic, Client = client, Model = "qwen/qwen3-8b", ClaudeServer = "localhost:1234", ClaudeCoreToolsOnly = true, ClaudeSeesPictures = false };
        Wait(chat.SendAsync("what does reject code 75 mean?").ContinueWith(t => { t.GetAwaiter().GetResult(); return true; }));

        var offered = claude.Requests[0]["tools"]!.AsArray().Select(t => (string?)t!["name"]).ToList();
        Assert.Equal(ChatTools.Core.Order(), offered.Order());
        var call = Assert.Single(chat.Entries.OfType<ToolEntry>());
        Assert.False(call.IsError, call.Result);
        Assert.Contains("Subscriber/Insured Not Found", call.Result);
        Assert.Empty(chat.Entries.OfType<ReplyUndoEntry>());
    });
}
