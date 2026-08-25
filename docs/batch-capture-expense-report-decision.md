# Batch Capture and Expense Report Sequencing Decision

## Status

Accepted on 25 August 2026.

## Context

The current API accepts one image and assumes it contains one document. Adding an expense report while retaining that interaction would make supporting-receipt ingestion unnecessarily repetitive. A user may instead have several separate receipt images, one desk photo containing several physical documents, or a mixture of both.

The selected direction must add meaningful MAF orchestration without making persistence, claim submission, or human approval an accidental prerequisite.

## Decision

Deliver the next two substantive capabilities in this order:

1. **Multi-source composite capture.** One request accepts one or more image files. Each source may contain zero, one, or several physical documents. The capture layer detects and crops document regions, then every valid member follows the same classification and document-specific workflow it would follow as an individual upload. Member outcomes are aggregated with partial-success semantics and annotated source previews.
2. **Expense report document processing.** Expense report becomes the next distinct business `DocumentCategory`. It receives its own extraction, validation, review, result, API, UI, and test coverage and can be processed either individually or as a member of a batch capture.

Persistent receipt-to-expense-report linking is a later capability. The initial capture and expense-report slices are request-scoped and do not create retrievable document identities.

```text
batch request
  -> source image(s)
  -> detect and validate region(s)
  -> crop each region
  -> classify each crop independently
  -> run the existing document workflow for its category
  -> aggregate member outcomes and annotated previews

then add:
  ExpenseReport category -> extraction -> validation -> review -> result

later, by separate decision:
  persist document results -> stable identities -> link receipts to expense reports
```

## Options Considered

| Option | Decision | Rationale |
| --- | --- | --- |
| Add expense reports before batch capture | Rejected | It would expose avoidable one-receipt-at-a-time ingestion friction immediately |
| Support only several documents within one image | Rejected | Separate receipt files are equally common and fit the same bounded batch envelope |
| Support repeated image files, each with zero or more document regions, then add expense reports | Selected | It handles both capture patterns, reuses every existing document workflow, and creates genuine fan-out/fan-in requirements |
| Add persistence and receipt linking with the first expense-report slice | Deferred | Stable identity, ownership, retention, deletion, and lookup rules are separate trust and lifecycle decisions |
| Treat a batch as a business document category | Rejected | Composite capture is an intake shape; each member owns its real document category |

## Consequences

### API and domain

- The existing `POST /api/documents/process` contract remains unchanged.
- A separate `POST /api/document-captures/process` endpoint accepts repeated `images` fields.
- Capture, source, and member identifiers are request-scoped and explicitly non-persistent.
- Expense report is added later as a normal document category, not as special batch logic.
- The [composite capture contract](composite-capture-contract.md) defines source isolation, region coordinates, partial success, model usage, dispositions, and annotated previews.

### Workflow architecture

- The top-level MAF route remains the only document-category dispatch path.
- Batch capture adds bounded source and member fan-out, reusable document sub-workflows, deterministic fan-in, and progress events.
- Cropped members cannot bypass or duplicate the individual classification and processing path.
- Detection, classification, extraction, repair, and usage accounting remain independently observable.

### Delivery sequence

- E2 first introduces the reusable top-level document-routing graph.
- E3 then delivers multi-source composite capture and its bounded parallel member processing.
- E4 adds the expense-report vertical slice to the same routing and batch infrastructure.
- E5 checkpointing and E6 agent collaboration remain gated and are not implied by either feature.

### Product and UI

- The capture UI accepts several source images and shows one annotated preview per source.
- Green tick, amber question-mark, and red cross overlays reflect deterministic accepted, review, and rejected member dispositions.
- An expense report is useful independently before persistent receipt linking exists.
- Later linking may reuse batch-ingested receipt results only after a durable identity and ownership model is approved.

### Operational

- Aggregate upload size, source count, decoded dimensions, region count, and concurrency require explicit limits.
- Batch work increases model-call count, latency, and cost, so capture-level and member-level usage must be measured.
- One invalid source or member does not discard trustworthy sibling outcomes; request cancellation still cancels the whole operation.

## Follow-Up Work

- #11 defines model-backed versus deterministic responsibilities and result semantics.
- #12 selects the smallest MAF capability set and records non-goals.
- #14 supplies representative single-source, multi-source, overlap, partial-failure, and expense-report samples.
- #16 selects a compatible MAF and dependency baseline after #12.
- E3 implementation work delivers the capture endpoint, workflow, UI, and tests.
- E4 implementation work delivers the expense-report vertical slice.
- Persistent document identity and receipt linking require a separate future decision and work items.
