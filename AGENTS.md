# AGENTS.md

Notes for anyone, human or AI agent, changing this code. `README.md` covers what the program does; this file covers
how the code is put together and the rules that keep it working.

## What this is

A HIPAA eligibility workbench in C# on .NET 10 and Avalonia 11.3: it reads 270 inquiries and 271 answers (005010X279A1)
and says what each answer means for each patient and visit, in plain words; it checks an appointment list and writes the
270, asks a clearinghouse in real time (CAQH CORE, off until turned on), and keeps the day's worklist. For visits whose
care needs authorization it writes the 278 request and reads the answer (005010X217). It has an AI Chat
(F12) and an MCP server (`--mcp`, `--mcp --attach`, `--http`, port 8779); agents see people's details masked unless the
person allows otherwise.

## Commands

```bash
dotnet build AVAEligibility.slnx
dotnet test AVAEligibility.slnx
dotnet run --project src/AVAEligibility [files or folders] [--real | --mask]
dotnet run --project src/AVAEligibility -- --mcp --root samples     # the MCP server on stdio (--attach: the open window)
ELIG_LIVE_SERVER=http://localhost:11434 ELIG_LIVE_MODEL=qwen3:1.7b dotnet test --filter LiveModelTests
cd samples && python3 make_samples.py        # remakes the samples (deterministic: tests pin what each says)
ELIG_SHOTS=Screenshots dotnet test --filter Screenshots     # the README's pictures, headless
```

`dotnet test | head` stalls: write the output to a file and grep it.

## Layout

- `src/AVAEligibility.Core/`: no Avalonia.
  - `X12/`: `Segment`, `Delimiters`, `X12File`: X12 Explorer's (`~/RiderProjects/X12_820Explorer`), by way of
    AVAClaimScrubber, with the namespace changed. Fix them together.
  - `Eligibility/`: `Codes` (the 005010X279A1 code lists, worded as X12 publishes them: EB01 to EB12, service types,
    AAA reasons and follow-ups, DTP, REF and NM1 qualifiers), `EligibilityModel` (`EligibilityFile`,
    `EligibilityTransaction`, `Party`, `Person`, `Benefit`, `Rejection`, `Inquiry`…; every item keeps the segments
    behind it), `EligibilityReader` (HL levels 20, 21, 22, 23; a person's loop up to its first EQ or EB; 2110 loops;
    2120 LS…LE), `Words` (each line in plain words, the same everywhere), `Interpretation` (`Interpreter.Read`: a
    person's answer made sense of: a `Verdict` (ready, check, act first, rejected, no answer), `Note`s each with what
    to do and the segments it rests on, and the benefits grouped as a front desk reads them: `CostRow`s, `Accumulator`s,
    `Limit`s, `Authorization`s, `OtherPayer`s, the PCP, what isn't covered; `Interpreter.Fix` says what to do about
    each AAA reason).
  - `Inquiries/`: `Appointments` (`AppointmentList`: a CSV or Excel schedule read by its headers, in any order;
    Account and Denial when the list is AVA Remittance's Check Eligibility Again),
    `ServiceTypes` (what to ask for a visit: 30, and the care its procedure code falls in), `InquiryBuilder` (the checks
    before sending, then the 270: one transaction per payer, dependents under their subscriber, a trace per
    appointment; `Npi` check digits), `Matcher` (each question's answer: by trace, then by payer, member ID and name;
    a payer that refused the whole batch answers everyone asked of it), `Worklist` (each appointment's `WorkItem`: its
    answer read for the visit's date and care, plus what only the visit tells: its care needs authorization or isn't
    covered, it uses one of a limited number of visits, it's with someone other than the PCP, the payer has the
    patient's ID, name or birth date differently; a 278 review's decision in place of "needs authorization"; and an
    `Estimate`; for a denied claim, `AfterDenial` instead of the
    visit's notes and the estimate: whether the 271 for the date of service agrees with the denial, contradicts it, or
    shows why the claim didn't match), `Changes` (what changed between two answers about
    the same person).
  - `Authorizations/`: `AuthModel` (`AuthCodes`: HCR01 actions, UM01 categories, UM02 certification types, from
    X217; HCR03 reasons are an external list, shown as codes; `AuthFile`, `AuthReview`, `AuthService`), `AuthReader`
    (278 requests and answers by HL level 20, 21, 22, 23, EV, SS; an AAA above the event applies to every review under
    it), `AuthRequests` (`Needed`: the visits whose care the 271 says needs authorization, with the 2120 X3 reviewer;
    `Check`: a 278 needs a diagnosis; `Build`: one 278 transaction per visit, HS review, initial, the care's service
    type, office, the date, diagnosis, provider and procedure; `For`: a visit's review among the open 278s, answers
    first; `Read`: what the decision means for the visit, as a note).
  - `Reports/WorklistReport.cs`: the worklist as an HTML page or an Excel workbook.
  - `Privacy/Masker.cs`: people's details as agents see them: every name, ID, identifier and address in the open files
    and the appointment list masked to its shape wherever it turns up, addresses hidden, every date to its year.
  - `Sending/`: `CoreConnectivity` (`Clearinghouse`, off until turned on, HTTPS unless on this computer; `CoreClient`,
    the CAQH CORE real-time envelope over HTTP MIME multipart or SOAP with WS-Security; `PasswordStore`, the password in
    a file of its own, owner-only), `RealTime` (one 270 per appointment, under the traces the whole list's 270 gives,
    each 271 saved beside the list).
- `src/AVAEligibility/`: the window, built in code (`MainWindow.cs`: the list, the Coverage tab and the segments;
  `MainWindow.Answer.cs`: the reading of an answer, verdict first; `MainWindow.Inquiries.cs`: the Inquiries tab, the
  settings and Write the 270; `MainWindow.Worklist.cs`: the Worklist tab and its export;
  `MainWindow.Sending.cs`: the Clearinghouse section and Ask Now (asks first); `MainWindow.Authorizations.cs`: the
  Authorizations tab, Write the 278 and the open 278s; `Settings.cs`: the settings file, in `MainWindow.SettingsFolder`, which tests point at a
  temporary folder;
  `MainWindow.Parts.cs`: the hooks later parts fill).
- `src/AVAEligibility.Mcp/`: the MCP server, no Avalonia. `EligibilityTools.cs` (the tools; people by handle, P01…
  and V01…; `Finish` masks every string of every answer unless values are allowed), `EligibilityTools.Orchestration.cs`
  (batch, steps files, checkpoints of what's open and the view), `Hosting.cs` (`ViewState`, `IExplorerSession`,
  `HeadlessSession`), `EligibilityServer.cs` (the instructions), `Recipes.cs` (9). `LiveListener`, `LiveBridge`,
  `NetworkListener`, `McpCommandLine`, `Workspace` and `Json` are AVAClaimScrubber's with the names changed.
- `src/AVAEligibility/`'s AI side: `MainWindow.AI.cs` (the AI menu, the chat's dock, the view as agents see and set it),
  `MainWindow.Agents.cs` (the doors, `AgentsSeeRealValues`), `Agents/` (`Chat/` and `ChatPanel.cs`: copies of AVA
  Modeler's chat, by way of AVAClaimScrubber, with this app's wording; `LiveAgents.cs`, `AgentMain.cs`,
  `AgentSettings.cs`), `ViewPainter.cs`.
- `tests/AVAEligibility.Tests/`: xUnit v3 on Avalonia.Headless. `Agents/AgentTests` (every tool, the no-leak test, every
  recipe, the chat on `FakeModelServer` and `FakeMessagesServer`), `LiveAgentTests` (the doors), `LiveModelTests`
  (opt-in), `MockClearinghouse` (the only clearinghouse tests talk to).
- `samples/`: an invented clinic's 270, its payers' 271s and its appointment list, from `make_samples.py`; each patient
  has one situation planted (see `PATIENTS` there). `recheck/` holds Bluewater's answer when asked again on 15
  December (kept out of `samples/` itself so opening the folder opens one day's answers). `denied/` holds AVA
  Remittance's Check Eligibility Again list (a copy of its `samples/recheck/eligibility-recheck.csv`, another invented
  clinic's patients) and Tallgrass's 271 for their dates of service (see `DENIED_LIST`). `authorizations/` holds the
  278 the app writes for Kenji's MRI and the reviewer's answer (see `auth_request` and `auth_answer`).

## Rules that keep it working

- **Core stays free of Avalonia.**
- **The samples are pinned.** `ReaderTests` (and later tests) say what each sample patient's answer is: plant a new
  situation in `make_samples.py` and pin it together.
- **Words come from `Words` and `Codes`**, so the window, the reports and agents say the same thing. A code a list hasn't
  got is shown as "code X (not in the list)", never guessed.
- **A window test that fails leaves its window open, and Avalonia's reset then fails with `fonts:SystemFonts`,** which
  hides the real failure: close windows in `finally`.
- **The interpretation never guesses.** A note rests on segments the payer sent and says which; what a 271 doesn't
  say (a copay it didn't send) stays empty. An estimate is labelled an estimate.
- **Code lists:** the service types were checked against the table in CAQH CORE's Eligibility & Benefits Data Content
  Rule appendix (vEB.2.1); `ReaderTests` pins their number. Change a list only from a published source.
- **Estimates are labelled.** `Estimate.Assumes` says what each assumes (in network, the full charge allowed); every
  place that shows one says it's an estimate. No estimate when coverage isn't active, another payer is primary, or the
  answer doesn't say enough.
- **Nothing is sent without the person.** Sending is off until they turn it on; Ask Now asks first, naming how many
  questions and where; the address must be HTTPS unless it's this computer. Tests send only to `MockClearinghouse`,
  on 127.0.0.1; never add a test that reaches a real clearinghouse. The password never goes in `settings.json`.
- **AVA Remittance writes the denied list.** `samples/denied/eligibility-recheck.csv` is its output, copied; the
  columns are shared (fix both apps together), and `DeniedTests` pins what each answer means for its denial.
- **The samples' 278 is what the app writes.** `AuthTests` builds it with the samples' settings (control number 1002)
  and compares it byte for byte with `authorizations/clinic-278.x12`: change `AuthRequests.Build` and
  `make_samples.py` together. Nothing sends a 278.
- **The samples' 270 is what the app writes.** `InquiryTests` builds it from `appointments.csv` with the samples'
  settings and compares it byte for byte with `clinic-270.x12`: change the builder and `make_samples.py` together.
- **Waiting on the UI thread:** a window test that awaits work started on the UI thread uses `HeadlessApp.Wait`, which
  runs the UI thread's jobs while it waits; `.GetResult()` there deadlocks.
- **The no-leak test is the contract.** `AgentTests.No_tool_result_…` calls every reading tool on everyone in the
  samples and fails if any answer, or any file an agent wrote, holds a name, member ID, identifier, address, birth date
  or visit date from the samples (`Phi()`). A new tool goes into it; a new place people's details come from goes into
  `Masker` and `Phi()`.
- **Agents never send and never write people's details unasked.** `ask_clearinghouse` runs only in the open window and
  goes through Ask Now's question; `write_inquiry` and `write_auth_request` need values allowed; reports are masked unless allowed.
- **New tools** need `[Description]` on the tool and every parameter (snake_case names), a place in
  `AgentTests.ToolNames`, a test, and a line in the README. Recipes are run call by call.
- **No real data in the repository.** Every person, payer, member ID and NPI in the samples is invented.

## Making a change

- Run `dotnet test` after any change.
- Pushes to `main` publish builds to the rolling `latest` pre-release; a `v*` tag makes a proper release.
