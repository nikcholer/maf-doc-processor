# Forward Planning: A Structured-Data Service In A Larger Workflow System

## Status

Planning only. This is not a delivery phase of the current repository. It records where pause/resume, case storage, human task lists, and claim submission would live **if** this processor were later embedded in a bigger workflow-management system.

The current effort is [turning images into structured data](durability-decision.md). That work is complete through the [extended workflow baseline](extended-workflow-release-baseline.md).

## What This Application Is For

This repository accepts document images and returns structured, validated data in one foreground request:

```text
images
  -> detect and crop physical documents when needed
  -> classify each document
  -> extract typed fields
  -> validate, optionally repair once, apply deterministic policy
  -> JSON (and a local demo UI)
```

That is the product. Review flags such as “ownership attestation required” are part of the structured result. They describe the parse. They do not keep a server-side job open.

The application is not a case manager, claim system, reviewer inbox, or durable process engine.

## Where A Surrounding System Would Take Over

A later workflow-management product could treat this processor as a **conversion step**:

1. A case is opened in that system (“Alex’s August client-trip claim”).
2. That system collects images (or asks Alex to upload).
3. It calls this API, or an equivalent library, and receives structured members.
4. **It** stores identities, documents, and results.
5. **It** assigns human work: attest the expense report, approve the claim, query a missing receipt.
6. **It** submits to payroll, finance, or another system of record.

In that picture, waiting for Alex is the surrounding engine’s problem. This processor has already finished: bytes in, structured data out. The surrounding engine may retry a failed conversion, or call again after Alex corrects a crop, but those are new conversions—not a paused instance of this graph.

## What Must Not Be Built Here

These belong to that surrounding system, or to an explicit later product decision outside the current effort:

| Concern | Why it is not this repository’s job |
| --- | --- |
| Durable pause/resume of an in-flight MAF graph | The conversion request is allowed to finish or fail. See the [durability decision](durability-decision.md). |
| Checkpointing mid-classification or mid-extraction | A failed local run is safe to resubmit. Crash-resume of unfinished conversion is a hosting concern for a future operator, not a demo feature. |
| Document or case storage | Persistence, ownership, retention, and deletion are deferred. This process is request-scoped. |
| Attestation, approval, or claim submission as a blocked workflow | Those are commands on a **stored** structured result. The parse already exists. |
| Reviewer queues, SLAs, escalation | Human-task management. This app only reports `HumanReview` on the result. |
| Linking receipts to expense lines across requests | Needs stable identities after the request. Deferred in the [model boundaries](capture-expense-model-boundaries.md). |

Lightweight domain records already sketch a future review command (`ReviewerInput`, `ReviewDecisionLogEntry`). They are unused on the live path. If they are ever implemented, they should be new APIs on stored results, or they should live in the surrounding system—not as MAF checkpoints of extraction.

## Implications If This Processor Is Embedded

Callers should assume:

- **Idempotent conversion.** The same images may be sent again. This app does not remember the previous run.
- **No pending-work protocol.** There is no “resume workflow X with answer Y” on the current API.
- **The result is the hand-off.** Category, fields, validation, policy, human-review reasons, and model usage are what a workflow engine should persist if it needs history.
- **Corrections are new requests.** Region overrides and re-upload are how humans improve extraction without pausing the original graph.

A future hosted operator might still add queues, timeouts, and retries **around** this process. That wrapping is outside the conversion graph.

## Relationship To Gated Phases

[E5](https://github.com/nikcholer/maf-doc-processor/issues/6) described pause/resume inside this application. That is **out of scope** for the current effort. Do not move it to Ready as a way to implement case management here.

[E6](https://github.com/nikcholer/maf-doc-processor/issues/7) (extra model review of an already-extracted result) is a separate quality question about the conversion itself. It does not require a workflow engine.

Icebox items such as alternative detectors remain optional conversion-quality work, not workflow-system work.
