# Multi-Agent Quality Prototype

## Current Prototype

The codebase includes an opt-in `DocumentQualityReviewWorkflow` prototype. It is not wired into the default API or demo UI.

The prototype exists to test whether a second model-review layer catches mistakes that the main extraction workflow misses. It should be treated as an experiment harness, not as part of the current production path.

## Object Map

- `DocumentProcessingResult`
  - The structured output from the normal document workflow. It includes classification, extracted data, validation, human-review state, and model usage.
- `DocumentQualityReviewWorkflow`
  - The opt-in wrapper that runs the quality review steps over an existing `DocumentProcessingResult`.
- `QualityAnalystExecutor`
  - A MAF-style executor that asks the model for a concise risk analysis of the structured result.
- `QualityAnalysis`
  - The intermediate output from the analyst step. It carries the original document result, the analyst summary, and analyst model usage.
- `QualityCriticExecutor`
  - A MAF-style executor that reads the analyst summary and document result, then returns a structured decision.
- `QualityReviewResult`
  - The final quality-layer result. It includes `Accept`, `NeedsHumanReview`, or `Reject`, a list of findings, and the quality-layer model usage.
- `QualityReviewFinding`
  - One critic finding with `Info`, `Warning`, or `Error` severity.

The prototype runs two model-backed workflow executors over an existing structured `DocumentProcessingResult`:

1. `QualityAnalystExecutor`
   - Summarizes quality risks, missing fields, contradictions, and confidence concerns.
   - Returns concise plain text.
2. `QualityCriticExecutor`
   - Reads the analyst summary and structured result.
   - Returns JSON with `Accept`, `NeedsHumanReview`, or `Reject`, plus findings.

The output is `QualityReviewResult`, including its own `DocumentModelUsage` so added token count, estimated cost, and latency can be measured separately from the main extraction path.

## Default Decision

Do not run multi-agent quality review by default yet.

Reasons:

- The current Qwen receipt and shopping-list path is fast and cheap without the extra layer.
- The prototype adds at least two extra model calls per document.
- We do not yet have a golden evaluation set to prove quality improvement.
- The local demo should stay reassuringly responsive.

Treat the prototype as an experiment harness. Wire it into the API only after measuring real benefit on representative documents.

## Invocation

The default image-submission path does not invoke this prototype.

To use it in future code, the expected shape is:

```csharp
var documentResult = await documentProcessingWorkflow.RunAsync(fileRequest, cancellationToken);
var qualityWorkflow = new DocumentQualityReviewWorkflow(chatClient, modelSettings.TextTesting);
var qualityResult = await qualityWorkflow.RunAsync(documentResult, cancellationToken);
```

The model role is deliberately a caller decision. Use `TextTesting` or a future dedicated quality-review role until measurement proves this should become product behavior.

## Measurement Plan

Before enabling by default, compare baseline vs quality-review output on a small sample set:

- Normal receipts.
- Large receipts.
- Shopping lists.
- Low-confidence classifications.
- Unsupported documents.
- Deliberately corrupted or incomplete model outputs.

Track:

- Whether the critic catches real extraction or categorization issues.
- False-positive review rate.
- Added latency.
- Added token usage and estimated cost.
- Whether findings are useful to a human reviewer.

## Revisit Criteria

Do not evaluate or integrate this prototype before **November 2026**. The current conversion path is fast and cheap enough that two extra calls are not justified as a product gap.

From November 2026, look again **only** if there is reason to believe models have made a step change in quality, speed, or price. Then run the measurement plan above on a defined set (including expense reports) and record the result even if the prototype stays rejected.

A later human-review surface in a surrounding workflow system is not, by itself, a reason to enable critic calls on this converter.
