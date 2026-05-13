# Multi-Agent Quality Prototype

## Current Prototype

The codebase includes an opt-in `DocumentQualityReviewWorkflow` prototype. It is not wired into the default API or demo UI.

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

Revisit default or optional UI integration when the prototype shows a clear quality gain, or when the app gains a human-review surface where critic findings can be acted on directly.
