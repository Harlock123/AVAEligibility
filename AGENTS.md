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
    2120 LS…LE), `Words` (each line in plain words, the same everywhere).
- `src/AVAEligibility/`: the window, built in code (`MainWindow.cs`: the list, the Coverage tab and the segments;
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
- **No real data in the repository.** Every person, payer, member ID and NPI in the samples is invented.

## Making a change

- Run `dotnet test` after any change.
- Pushes to `main` publish builds to the rolling `latest` pre-release; a `v*` tag makes a proper release.
