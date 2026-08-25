# Current Document Golden Set

## Purpose

The golden set is a small, versioned corpus for detecting changes to the existing document paths. Each case records a non-confidential source, the model-boundary outputs supplied to the offline workflow, and the complete stable result semantics expected from project-owned processing.

The set protects classification hand-off, routing, extraction hand-off, deterministic validation and policy, human-review state, model-call accounting, supported payloads, and unsupported-document behaviour. It deliberately separates those repeatable checks from live-provider quality: the offline runner does not claim that a configured model will reproduce the recorded outputs.

## Cases

| Case | Source | Expected route | Expected result |
| --- | --- | --- | --- |
| `receipt-approved` | Synthetic North Star Cafe receipt | Receipt | Valid, approved, no review |
| `shopping-list-valid` | Synthetic weekly shopping list | Shopping list | Valid, no review |
| `sujiko-rotated-known-answer` | Versioned rotated puzzle photograph | Sujiko puzzle | Valid known starting state, no review |
| `unsupported-event-ticket` | Synthetic event ticket | Unknown | Normal unsupported failure, review required |

Synthetic PNGs are rendered in memory from the manifest's text using a small deterministic bitmap font. The Sujiko photograph contains only a puzzle and is already used by the opt-in provider regression test. No receipts, lists, tickets, names, account details, or other personal source documents are included.

## Files and Runner

The case manifest is [`tests/MafDocumentProcessor.Tests/golden-set/current-document-paths.json`](../tests/MafDocumentProcessor.Tests/golden-set/current-document-paths.json). `GoldenSetTests` loads it, creates each image, supplies the recorded classifier and extractor outputs through the project interfaces, runs `DocumentProcessingWorkflow`, and compares a stable result snapshot with the expected record.

The golden-set test is part of the normal offline suite. It can also be run alone:

```powershell
dotnet test .\MafDocumentProcessor.sln --filter FullyQualifiedName~GoldenSetTests
```

The live Sujiko assertion remains separately opt-in because it calls TogetherAI and tests provider behaviour rather than deterministic workflow semantics. See the [current workflow baseline](baseline-measurements.md) for its command and latest recorded observation.

## Changing the Set

When adding or changing a case:

1. Use synthetic content or a source asset whose provenance and non-confidential status are clear.
2. Record model-boundary outputs separately from the expected result. The former drives the workflow; the latter states the contract being protected.
3. Include expected classification, success, validation, review, policy, model-operation order, typed payload, errors, and warnings.
4. Run the focused golden-set test and the full offline suite.
5. Review changed expected results as contract changes. Do not refresh them mechanically merely to make a failure pass.

Add repair, review, and failure variants only when they protect an agreed behaviour not already covered by focused tests. Keep this set small enough that its intent remains obvious.
