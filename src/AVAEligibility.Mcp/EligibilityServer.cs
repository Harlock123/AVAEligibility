using System.Reflection;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace AVAEligibility.Mcp;

/// <summary>
/// The MCP server: <see cref="EligibilityTools"/>' methods as MCP tools (the SDK's attribute model, read by reflection), with
/// instructions that tell an agent AVA Eligibility's conventions. The same server runs over stdio headless, over a
/// pipe for agents attached to the open window, over the network with a token, and in-process for the AI Chat.
/// </summary>
public static class EligibilityServer
{
    public const string Name = "AVAEligibility";

    /// <summary>What every agent is told on connecting: the conventions the tools' descriptions build on.</summary>
    public const string Instructions = """
        AVA Eligibility reads HIPAA 270 eligibility inquiries and the payers' 271 answers (005010X279A1) and says what they mean for each patient and visit: whether they're covered, what to do first, and what they may owe. A clinic's appointment list (CSV or Excel) is checked and written as a 270; each 271 is matched back to its question; the worklist says, for every visit: ready, check, act first, rejected or no answer, and why. A visit whose care needs authorization has a 278 (005010X217): get_authorizations says where each stands, write_auth_request writes the requests (values allowed only; nothing is sent), and a 278 answer opened with open_file puts the decision on the worklist (certified, with the number for the claim). A list from AVA Remittance's Check Eligibility Again is denied claims (its rows have a denial and an account): the visit date is the date of service, and each visit's first note says what the 271 means for the denial (it agrees, it doesn't: reprocess or appeal, or it shows why the claim didn't match).

        People's details (it matters): unless the person lets agents see values, every name, member ID, identifier and address is masked to its shape (BWH40010O512 is B**99999*999: a letter among digits still shows; a longer ID shows longer), addresses are hidden, and every date keeps only its year. Every answer says "values_masked". Refer to people by their handle: P01, P02… for people in the open 270s and 271s, V01, V02… for visits on the appointment list. Payers, providers, codes and amounts aren't people's details and are shown.

        Reading an answer (it matters):
        - A verdict comes from notes, and each note says what to do and rests on segments the payer sent. Never guess beyond them: what a 271 doesn't say (a copay it didn't send) isn't known.
        - get_visit is the visit's reading: on top of the answer, what only the visit tells (its care needs authorization or isn't covered, a limit, a PCP elsewhere means a referral, the payer has the ID or name differently), and an estimate. Estimates assume in network and the full charge allowed; always call them estimates.
        - Rejections (AAA) name what the payer couldn't match; explain_code gives any code's meaning and what to do.

        A good way to work: list_files; open_file the appointment list and the 271s (or open the folder's files one by one); get_worklist for the day; get_visit for a visit that needs something; get_answer and get_benefits for a person; explain_code for a code; check_appointments before writing a 270; compare_answers when two answers about someone are open. render_view shows the person an answer and show_in_view selects it in their window. batch runs several calls at once.

        Writing a 270 or a report holds people's details, so write_inquiry needs values allowed, and export_worklist writes masked when they aren't. ask_clearinghouse sends the inquiries in real time: only in the open window, and only when the person says yes. Never claim something was sent, written or checked unless a tool said so.

        The server's prompts are saved recipes (check the day's schedule, explain a rejection, a visit's next step, what the patient owes, what changed, fix and ask again, other payers, the day's report): step-by-step plans with the exact tool calls.
        """;




    /// <summary>The MCP tools, bound to a host.</summary>
    public static IEnumerable<McpServerTool> Tools(IExplorerHost host) => Tools(new EligibilityTools(host));

    /// <summary>The MCP tools of one set of handlers (one session's: its checkpoints and its recording).</summary>
    public static IEnumerable<McpServerTool> Tools(EligibilityTools target)
    {
        foreach (var method in typeof(EligibilityTools).GetMethods(BindingFlags.Public | BindingFlags.Instance))
            if (method.GetCustomAttribute<McpServerToolAttribute>() != null)
                yield return McpServerTool.Create(method, target);
    }

    /// <summary>The saved recipes (<see cref="Recipes"/>) as MCP prompts.</summary>
    public static IEnumerable<McpServerPrompt> Prompts() =>
        typeof(Recipes).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.GetCustomAttribute<McpServerPromptAttribute>() != null)
            .Select(m => McpServerPrompt.Create(m, target: null));

    /// <summary>The tools' MCP definitions (names, descriptions, input schemas), without a host to run them.</summary>
    public static IList<Tool> Definitions() => [.. _definitions.Value];

    private static readonly Lazy<Tool[]> _definitions = new(() => Tools(new NoHost()).Select(t => t.ProtocolTool).ToArray());

    public static Implementation Info => new()
    {
        Name = Name,
        Title = "AVA Eligibility",
        Version = typeof(EligibilityServer).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "1.0",
    };

    public static McpServerOptions Options(IExplorerHost host) => Configure(new McpServerOptions(), host);

    /// <summary>Fills a new session's options (ones the network door's HTTP handler made) as <see cref="Options"/> makes them.</summary>
    public static McpServerOptions Configure(McpServerOptions options, IExplorerHost host)
    {
        options.ServerInfo = Info;
        options.ServerInstructions = Instructions;
        options.ToolCollection = [];
        var target = new EligibilityTools(host);
        options.Filters.Request.CallToolFilters.Add(next => async (request, cancel) =>
        {
            var tool = request.Params is { } p && options.ToolCollection.TryGetPrimitive(p.Name, out var t) ? t.ProtocolTool : null;
            if (tool != null && Arguments.Check(tool, request.Params!.Arguments) is { } problem) return Arguments.Error(problem);
            try
            {
                var result = await next(request, cancel).ConfigureAwait(false);
                // The session's calls, as steps save_steps can keep: every call that worked.
                if (tool != null && result.IsError != true)
                    target.Record(tool.Name, request.Params!.Arguments);
                return result;
            }
            catch (Exception e) when (e is not McpException and not OperationCanceledException)
            {
                // The SDK couldn't bind the arguments to the handler (a value of the wrong kind): say which, not just that it failed.
                return Arguments.Error(tool != null ? $"{tool.Name}: {e.Message} Check the arguments against the tool's input schema." : e.Message);
            }
        });
        foreach (var tool in Tools(target)) options.ToolCollection.Add(tool);
        options.PromptCollection = [];
        foreach (var prompt in Prompts()) options.PromptCollection.Add(prompt);
        return options;
    }

    /// <summary>Serves one MCP session over a transport until the client goes away or <paramref name="cancel"/> fires.</summary>
    public static async Task RunAsync(ITransport transport, IExplorerHost host, CancellationToken cancel = default)
    {
        await using var server = McpServer.Create(transport, Options(host));
        await server.RunAsync(cancel).ConfigureAwait(false);
    }

    /// <summary>Checks a call's arguments against its tool's input schema before the SDK binds them.</summary>
    internal static class Arguments
    {
        public static CallToolResult Error(string message) => new() { IsError = true, Content = [new TextContentBlock { Text = message }] };

        /// <summary>What's wrong with the arguments (a name the tool doesn't take, a value of the wrong kind), or null.</summary>
        public static string? Check(Tool tool, IDictionary<string, JsonElement>? args)
        {
            if (!tool.InputSchema.TryGetProperty("properties", out var props)) return null;
            // A required argument that's missing: say which, and what it is (the SDK's own words are about dictionaries).
            if (tool.InputSchema.TryGetProperty("required", out var required) && required.ValueKind == JsonValueKind.Array)
            {
                var missing = required.EnumerateArray().Select(r => r.GetString() ?? "").Where(r => r.Length > 0 && args?.ContainsKey(r) != true).ToList();
                if (missing.Count > 0)
                    return $"{tool.Name} needs {string.Join(" and ", missing)}: " + string.Join(" ", missing.Select(m =>
                        props.TryGetProperty(m, out var p) && p.TryGetProperty("description", out var d) ? $"{m} is {d.GetString()?.TrimEnd('.')}." : $"{m}."));
            }
            if (args == null) return null;
            foreach (var (name, value) in args)
            {
                if (!props.TryGetProperty(name, out var schema))
                {
                    var names = props.EnumerateObject().Select(x => x.Name).ToList();
                    return $"{tool.Name} has no argument \"{name}\". It takes: {(names.Count == 0 ? "none" : string.Join(", ", names))}.";
                }
                if (Kinds(schema) is { Count: > 0 } kinds && !kinds.Any(k => Fits(k, value)))
                    return $"{tool.Name}: {name} must be {string.Join(" or ", kinds.Where(k => k != "null").Select(Describe))}, not {Describe(value)}.";
            }
            return null;
        }

        private static List<string> Kinds(JsonElement schema) =>
            schema.ValueKind != JsonValueKind.Object || !schema.TryGetProperty("type", out var type) ? []
            : type.ValueKind == JsonValueKind.String ? [type.GetString()!]
            : type.ValueKind == JsonValueKind.Array ? type.EnumerateArray().Select(x => x.GetString() ?? "").ToList() : [];

        private static bool Fits(string kind, JsonElement v) => kind switch
        {
            "string" => v.ValueKind == JsonValueKind.String,
            "number" => v.ValueKind == JsonValueKind.Number,
            "integer" => v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out _),
            "boolean" => v.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "array" => v.ValueKind == JsonValueKind.Array,
            "object" => v.ValueKind == JsonValueKind.Object,
            "null" => v.ValueKind == JsonValueKind.Null,
            _ => true,
        };

        private static string Describe(string kind) => kind switch
        {
            "string" => "text", "number" => "a number", "integer" => "a whole number", "boolean" => "true or false",
            "array" => "a list", "object" => "an object", _ => kind,
        };

        private static string Describe(JsonElement v) => v.ValueKind switch
        {
            JsonValueKind.String => $"the text \"{v.GetString()}\"",
            JsonValueKind.Number => $"the number {v.GetRawText()}",
            JsonValueKind.True or JsonValueKind.False => v.GetRawText(),
            JsonValueKind.Array => "a list",
            JsonValueKind.Object => "an object",
            _ => "null",
        };
    }

    /// <summary>A host for reading the tools' definitions only.</summary>
    private sealed class NoHost : IExplorerHost
    {
        public string Root => Path.GetTempPath();
        public IViewPainter Painter => throw new InvalidOperationException("Not connected.");
        public bool Live => false;
        public Task<T> RunAsync<T>(string action, Func<IExplorerSession, Task<T>> work, CancellationToken cancel = default) =>
            throw new InvalidOperationException("Not connected.");
    }
}
