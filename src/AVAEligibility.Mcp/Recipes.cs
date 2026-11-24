using System.ComponentModel;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace AVAEligibility.Mcp;

/// <summary>
/// Saved recipes, served as MCP prompts: short step-by-step plans for what agents are often asked to do with eligibility,
/// written with the real tool names and arguments. Each step that calls a tool shows the call on a line of its own,
/// "→ tool {arguments}" (the tests run every recipe through the server, call by call, on the samples, so a recipe that
/// stops working fails the build). The defaults name the samples' files and handles.
/// </summary>
[McpServerPromptType]
public static class Recipes
{
    /// <summary>The marker that starts a step's tool call in a recipe's text.</summary>
    public const string CallMarker = "→ ";

    private static readonly string[] SampleAnswers = ["bluewater-271.x12", "prairie-271.x12", "summit-271.x12", "cascade-271.x12"];

    private static void OpenDay(Recipe r, string? list)
    {
        r.Step("The appointment list.", "open_file", new { file = Or(list, "appointments.csv") });
        foreach (var f in SampleAnswers) r.Step(null, "open_file", new { file = f });
    }

    [McpServerPrompt(Name = "check_the_day", Title = "Check the day's schedule")]
    [Description("Goes through a day's appointments against the payers' answers and lists who's ready, who needs something first, and what.")]
    public static string CheckTheDay([Description("The appointment list (default \"appointments.csv\").")] string? list = null)
    {
        var r = new Recipe("Check the schedule against the payers' answers, and say who needs something before their visit.");
        OpenDay(r, list);
        r.Step("Every visit with its verdict.", "get_worklist", new { });
        r.Step("The visits that need something first.", "get_worklist", new { only = "act first" });
        r.Step("One of them in full.", "get_visit", new { visit = "V07" });
        return r.Text("Start with the counts. Then list the visits to act on, worst first: what's wrong and exactly what to do (whom to call, what to get). Then rejections (fix and ask again) and no answers. Say the visits that are ready in one line. Estimates are estimates.");
    }

    [McpServerPrompt(Name = "explain_a_rejection", Title = "Explain a rejected inquiry")]
    [Description("Explains why a payer couldn't answer about someone (an AAA rejection), what to check, and whether to ask again.")]
    public static string ExplainARejection([Description("The person's handle (default \"P04\").")] string? person = null)
    {
        var p = Or(person, "P04");
        var r = new Recipe($"Explain why the payer couldn't answer about {p}, and what to do.");
        r.Step(null, "open_file", new { file = "bluewater-271.x12" });
        r.Step(null, "get_answer", new { person = p });
        r.Step("The code behind it.", "explain_code", new { list = "reject", code = "72" });
        r.Step("What was sent and what came back.", "get_segments", new { person = p });
        return r.Text("Say in a sentence what the payer couldn't match, what to check (the card, the registration), and whether to ask again (the follow-up code). The values are masked to their shape: a letter among digits in an ID still shows.");
    }

    [McpServerPrompt(Name = "next_step_for_a_visit", Title = "A visit's next step")]
    [Description("Reads one visit's answer for that visit and says what to do before the patient arrives.")]
    public static string NextStepForAVisit([Description("The visit's handle (default \"V09\").")] string? visit = null)
    {
        var v = Or(visit, "V09");
        var r = new Recipe($"Say what to do before visit {v}.");
        OpenDay(r, null);
        r.Step(null, "get_visit", new { visit = v });
        r.Step("Show it to the person.", "show_in_view", new { handle = v });
        return r.Text("Give the verdict and the one thing to do first, with whom to call and the number when the answer gives one. Quote the payer's words when they decide it.");
    }

    [McpServerPrompt(Name = "what_the_patient_owes", Title = "What the patient may owe")]
    [Description("Estimates what a patient may owe for a visit, and for other procedures or charges, from their answer.")]
    public static string WhatThePatientOwes([Description("The visit's handle (default \"V07\").")] string? visit = null)
    {
        var v = Or(visit, "V07");
        var r = new Recipe($"Estimate what the patient may owe for visit {v}.");
        OpenDay(r, null);
        r.Step(null, "get_visit", new { visit = v });
        r.Step("The money in the answer.", "get_answer", new { person = "P07" });
        r.Step("What if it were an office visit instead?", "estimate", new { visit = v, procedure = "99214", charge = 185 });
        return r.Text("Give the estimate and what it rests on (a copay, the deductible left, coinsurance, the out-of-pocket left), and say plainly it's an estimate: in network, the full charge allowed; the payer decides.");
    }

    [McpServerPrompt(Name = "what_changed", Title = "What changed since the last check")]
    [Description("Compares a later answer about someone with an earlier one from the same payer.")]
    public static string WhatChanged([Description("The later answer's person handle (default \"P08\").")] string? person = null)
    {
        var p = Or(person, "P08");
        var r = new Recipe("Say what changed since the earlier answer.");
        r.Step(null, "open_file", new { file = "bluewater-271.x12" });
        r.Step("The later answer.", "open_file", new { file = "recheck/bluewater-271-dec15.x12" });
        r.Step(null, "list_people", new { file = "recheck/bluewater-271-dec15.x12" });
        r.Step(null, "compare_answers", new { person = p });
        return r.Text("List the changes that matter at the front desk first (coverage, who's primary), then the money (deductible used since). If nothing changed, say so.");
    }

    [McpServerPrompt(Name = "fix_and_ask_again", Title = "Fix the list and ask again")]
    [Description("Finds what's wrong in the appointment list before a 270 is sent, and what the rejections say to fix.")]
    public static string FixAndAskAgain([Description("The appointment list (default \"appointments.csv\").")] string? list = null)
    {
        var r = new Recipe("Find what to fix in the appointment list before asking (again).");
        OpenDay(r, list);
        r.Step("What's wrong before sending.", "check_appointments", new { });
        r.Step("What the payers rejected.", "get_worklist", new { only = "rejected" });
        r.Step("Each rejection in full.", "get_visit", new { visit = "V04" });
        return r.Text("List each fix with its visit: the field to correct and what to check it against. Say which can be asked again now and which need the patient. The person writes the corrected 270 (File › Write the 270).");
    }

    [McpServerPrompt(Name = "other_payers", Title = "Find other payers")]
    [Description("Finds the patients whose answers name another payer, and who pays first.")]
    public static string OtherPayers()
    {
        var r = new Recipe("Find everyone with another payer, and say who pays first.");
        r.Step(null, "open_file", new { file = "bluewater-271.x12" });
        r.Step(null, "list_people", new { });
        r.Step("One with another payer.", "get_answer", new { person = "P06" });
        r.Step("What the code means.", "explain_code", new { list = "entity", code = "PRP" });
        return r.Text("For each, say which payer is primary and what that means for billing (bill the primary first). If the answer doesn't say which is primary, say to ask the patient.");
    }

    [McpServerPrompt(Name = "day_report", Title = "The day's report")]
    [Description("Writes the worklist as a report for the front desk, and summarizes it.")]
    public static string DayReport([Description("The report to write (default \"worklist.html\"; .xlsx for a workbook).")] string? output = null)
    {
        var o = Or(output, "worklist.html");
        var r = new Recipe($"Write the day's worklist report, {o}, and summarize it.");
        OpenDay(r, null);
        r.Step(null, "get_worklist", new { });
        r.Step(null, "export_worklist", new { output = o });
        return r.Text("Summarize: how many visits are ready, what needs doing and by whom, and the rejections to fix. Name the report file; people's details in it are masked unless the person allowed values.");
    }

    private static string Or(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private sealed class Recipe(string intro)
    {
        private readonly StringBuilder _text = new(intro + "\n\nFollow these steps in order. A line starting with \"" + CallMarker.Trim() + "\" is one tool call, with its arguments as JSON. If a step fails, read the error, fix the cause and carry on. File names and handles in these calls are the samples': use the person's own.\n");
        private int _number;

        public void Step(string? says, string tool, object args)
        {
            _number++;
            _text.Append('\n').Append(_number).Append('.').Append(says != null ? " " : "");
            if (says != null) _text.Append(says);
            _text.Append("\n   ").Append(CallMarker).Append(tool).Append(' ').Append(JsonSerializer.Serialize(args, Json.Options));
        }

        public string Text(string closing) => _text.Append("\n\n").Append(closing).ToString();
    }
}
