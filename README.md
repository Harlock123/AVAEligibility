# AVA Eligibility

Know who's covered before they arrive. AVA Eligibility reads HIPAA **270** eligibility inquiries and the payers' **271**
answers (005010X279A1) and says what they mean in plain words: whether the patient is active, the copay, how much
deductible is left, what needs authorization, who the primary care provider is, and whether another payer comes first.

*Work in progress: milestone 6 of 7 (AI agents: an MCP server with 31 tools and 8 recipes, and the AI Chat).*

## Running it

```bash
dotnet run --project src/AVAEligibility -- samples
```

`samples/` has an invented clinic's 270 for next week's appointments, the 271s its four payers sent back, and the
appointment list.

## License

MIT
