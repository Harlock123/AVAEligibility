# AGENTS.md

Notes for anyone, human or AI agent, changing this code. `README.md` covers what the program does; this file covers
how the code is put together and the rules that keep it working.

## What this is

A HIPAA eligibility workbench in C# on .NET 10 and Avalonia 11.3: it reads 270 inquiries and 271 answers (005010X279A1)
and says what each answer means for each patient, in plain words.

## Commands

```bash
dotnet build AVAEligibility.slnx
dotnet test AVAEligibility.slnx
dotnet run --project src/AVAEligibility [files or folders]
cd samples && python3 make_samples.py        # remakes the samples (deterministic: tests pin what each says)
ELIG_SHOTS=Screenshots dotnet test --filter Screenshots     # the README's pictures, headless
```

`dotnet test | head` stalls: write the output to a file and grep it.

## Layout

- `src/AVAEligibility.Core/`: no Avalonia.
  - `X12/`: `Segment`, `Delimiters`, `X12File`, `Deidentifier`: X12 Explorer's (`~/RiderProjects/X12_820Explorer`), by
    way of AVAClaimScrubber, with the namespace changed. Fix them together.
  - `Eligibility/`: `Codes` (the 005010X279A1 code lists, worded as X12 publishes them: EB01 to EB12, service types,
    AAA reasons and follow-ups, DTP, REF and NM1 qualifiers), `EligibilityModel` (`EligibilityFile`,
    `EligibilityTransaction`, `Party`, `Person`, `Benefit`, `Rejection`, `Inquiry`…; every item keeps the segments
    behind it), `EligibilityReader` (HL levels 20, 21, 22, 23; a person's loop up to its first EQ or EB; 2110 loops;
    2120 LS…LE), `Words` (each line in plain words, the same everywhere), `Interpretation` (`Interpreter.Read`: a
    person's answer made sense of: a `Verdict` (ready, check, act first, rejected, no answer), `Note`s each with what
    to do and the segments it rests on, and the benefits grouped as a front desk reads them: `CostRow`s, `Accumulator`s,
    `Limit`s, `Authorization`s, `OtherPayer`s, the PCP, what isn't covered; `Interpreter.Fix` says what to do about
    each AAA reason).
  - `Inquiries/`: `Appointments` (`AppointmentList`: a CSV or Excel schedule read by its headers, in any order),
    `ServiceTypes` (what to ask for a visit: 30, and the care its procedure code falls in), `InquiryBuilder` (the checks
    before sending, then the 270: one transaction per payer, dependents under their subscriber, a trace per
    appointment; `Npi` check digits), `Matcher` (each question's answer: by trace, then by payer, member ID and name;
    a payer that refused the whole batch answers everyone asked of it).
- `src/AVAEligibility/`: the window, built in code (`MainWindow.cs`: the list, the Coverage tab and the segments;
  `MainWindow.Answer.cs`: the reading of an answer, verdict first; `MainWindow.Inquiries.cs`: the Inquiries tab, the
  settings and Write the 270; `Settings.cs`: the settings file, in `MainWindow.SettingsFolder`, which tests point at a
  temporary folder;
  `MainWindow.Parts.cs`: the hooks later parts fill).
- `tests/AVAEligibility.Tests/`: xUnit v3 on Avalonia.Headless.
- `samples/`: an invented clinic's 270, its payers' 271s and its appointment list, from `make_samples.py`; each patient
  has one situation planted (see `PATIENTS` there).

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
- **The samples' 270 is what the app writes.** `InquiryTests` builds it from `appointments.csv` with the samples'
  settings and compares it byte for byte with `clinic-270.x12`: change the builder and `make_samples.py` together.
- **Waiting on the UI thread:** a window test that awaits work started on the UI thread uses `HeadlessApp.Wait`, which
  runs the UI thread's jobs while it waits; `.GetResult()` there deadlocks.
- **No real data in the repository.** Every person, payer, member ID and NPI in the samples is invented.

## Making a change

- Run `dotnet test` after any change.
- Pushes to `main` publish builds to the rolling `latest` pre-release; a `v*` tag makes a proper release.
