# Current Document Golden Set

## Purpose

A golden set is a collection of examples whose correct answers have already been agreed.

For example, the sample receipt should be recognised as a receipt, its total should be £21.02, and it should be approved without asking a person to review it. The sample event ticket should be recognised as an unsupported document and rejected with a helpful message.

Whenever the application changes, the tests run these examples again and compare the results with the agreed answers. If a result changes, the test fails. A developer can then decide whether the change is intentional or whether a bug has been introduced.

The individual-document golden set has four examples: one receipt, one shopping list, one Sujiko puzzle, and one unsupported document. Composite capture has a separate corpus in the [next-scenario sample set](next-scenario-sample-set.md), exercised offline by `CaptureGoldenSetTests`. Expense-report fixtures live in that same sample set and are exercised offline by `ExpenseReportProcessingWorkflowTests`.

## Cases

| Case | Example | How it should be identified | Expected result |
| --- | --- | --- | --- |
| `receipt-approved` | Fictional North Star Cafe receipt | Receipt | Correct details, approved, no review |
| `shopping-list-valid` | Fictional weekly shopping list | Shopping list | Correct items, no review |
| `sujiko-rotated-known-answer` | Rotated puzzle photograph | Sujiko puzzle | Correct totals and given numbers, no review |
| `unsupported-event-ticket` | Fictional event ticket | Unsupported | Rejected with a helpful message |

The receipt, shopping list, and event ticket are made from fictional text when the tests run. The Sujiko photograph contains only a number puzzle. The set contains no real receipts, names, account details, or other personal information.

## How the Test Works

For each example, the test:

1. Creates or loads the sample image.
2. Supplies a saved AI answer, such as “this is a receipt” and “the total is £21.02”.
3. Runs the application's real workflow using that saved answer.
4. Checks the final result against the agreed result.

Saved AI answers make this test fast, free, and repeatable. It does not need an API key and does not call TogetherAI. It checks what the application does with an AI answer: which workflow it chooses, how it validates the extracted information, whether policy allows it, whether a person should review it, and what it returns to the caller.

This test does **not** prove that the live AI service will read every image correctly. That is a separate question because AI answers can vary. The opt-in live Sujiko test sends the real puzzle image to TogetherAI and compares its answer with the known one. The [current workflow baseline](baseline-measurements.md) records the latest result.

## Files and Running the Test

The examples, saved AI answers, and expected application results are listed in [`current-document-paths.json`](../tests/MafDocumentProcessor.Tests/golden-set/current-document-paths.json). Keeping them in a normal text file makes every change visible in Git history.

The golden-set test is part of the normal offline suite. It can also be run alone:

```powershell
dotnet test .\MafDocumentProcessor.sln --filter FullyQualifiedName~GoldenSetTests
```

The composite-capture corpus is:

```powershell
dotnet test .\MafDocumentProcessor.sln --filter FullyQualifiedName~CaptureGoldenSetTests
```

## Adding or Changing an Example

When adding or changing a case:

1. Use fictional content, or a real source whose origin is known and which contains nothing confidential.
2. Record the answer that the AI is assumed to have returned.
3. Record what the application should return after processing that answer.
4. Run the golden-set test and the full offline test suite.
5. Read any changed result carefully. Update an agreed answer only when the application's intended behaviour has genuinely changed.

Do not replace expected results automatically just to make a failed test pass. The failure is the warning that the golden set is designed to provide.
