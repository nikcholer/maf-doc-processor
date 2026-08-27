# Human Review Policy

## Decision

Human review is a quality and ownership state, not an API failure.

The workflow should return the best structured result it can, then mark review reasons when the user or a future reviewer needs to inspect, confirm, or own the submission.

## Review Triggers

| Trigger | Applies to | Result behavior |
| --- | --- | --- |
| Low or missing classification confidence | All supported document types | Return parsed data when possible and add a review reason. |
| Unsupported document type | Unsupported documents | Return `IsSuccess=false` with a human-readable unsupported-type message. |
| Structural validation failure after repair | All parsed document types | Receipt: return parsed data with warnings. Shopping list, Sujiko, and expense report: return `IsSuccess=false` with validation errors. |
| Receipt exceeds review threshold | Receipts | Return parsed data with `PolicyDecision.NeedsReview`. |
| Receipt payment method missing | Receipts | Return parsed data with `PolicyDecision.NeedsReview`. |
| User-owned submission | Expense claims and future claim-like types | User must attest that the parsed document and claim are theirs to submit. The model must not be treated as the submitting party. |

## Confidence Guidance

Use classifier confidence as an advisory signal.

Initial thresholds:

- `>= 0.80`: normal processing.
- `0.50` to `< 0.80`: process if the type is supported, but flag for review.
- `< 0.50` or missing confidence: treat as model doubt; process only if other evidence is strong enough, otherwise return unsupported/unknown with a clear reason.

These values are deliberately conservative starting points. They should be tuned from real examples rather than guessed into permanence.

## Ownership And Attestation

Document extraction is model assistance. It does not transfer responsibility to the model.

Receipts, shopping lists, and Sujiko puzzles in the local demo do not require formal attestation. Expense reports do:

| Document family | Ownership rule |
| --- | --- |
| Receipt reference/demo | User may inspect or discard result. No submission attestation required. |
| Shopping list/demo | User may inspect or discard result. No submission attestation required. |
| Sujiko puzzle/demo | User may inspect, solve, or discard result. No submission attestation required. |
| Expense claim | User must attest that the claim is theirs, that parsed fields are acceptable, and that submission is intentional. |
| Compliance, financial, or legal submission | Require explicit user or reviewer ownership before downstream action. |

## Deferred Workflow Capabilities

Durable pause/resume and reviewer queues are **out of scope** for this processor. They belong to a surrounding workflow-management system if this conversion API is ever embedded in one. See [forward planning](forward-planning-workflow-system.md) and the [durability decision](durability-decision.md).

`HumanReviewResult` on the conversion result is still in scope: it is structured data about the parse, not a blocked job.

The codebase now has lightweight domain records for the future review surface:

- `HumanReviewResult` is returned with every workflow result.
- `ReviewerInput` models a future human decision.
- `ReviewDecisionLogEntry` models the audit event to persist once a review endpoint or queue exists.

When a review surface exists, add:

- Review state transitions.
- Timeout/escalation rules.
- Persistent audit logging for reviewer decisions and user attestations.
