# Document Scenario Evaluation

## Status

This spike recommends a scenario for decision; it does not approve an implementation. The output contract, model boundaries, MAF capability set, and final decision remain separate E0 work items.

## Current Baseline

The application currently assumes that one uploaded image contains one document. It demonstrates three vertical slices:

| Slice | Model-backed work | Deterministic work | Workflow shape |
| --- | --- | --- | --- |
| Receipt | Classification and extraction | Structural validation, bounded repair decision, and policy | Linear graph with a policy stage |
| Shopping list | Classification and extraction | Item validation and bounded repair decision | Linear graph |
| Sujiko | Classification and extraction | Grid and quadrant validation and bounded repair decision | Linear graph |

The next scenario should therefore require orchestration that is useful to the problem, rather than merely adding another extraction prompt to the same graph.

## Evaluation Method

Candidates are scored from 1 (weak) to 5 (strong). Weighted totals are comparative planning aids, not measured product evidence.

| Criterion | Weight | What a strong candidate demonstrates |
| --- | ---: | --- |
| Genuine orchestration need | 30% | Independent work, aggregation, and outcome-dependent routing arise naturally |
| Deterministic validation | 20% | Important correctness rules can be expressed and tested without another model call |
| Architectural progression | 20% | Existing slices or project contracts can be reused rather than bypassed |
| Bounded first delivery | 15% | A coherent initial slice can be delivered without prematurely requiring E5 or E6 |
| Safe representative samples | 15% | Useful examples can be created without personal, confidential, or licensed material |

## Ranked Comparison

| Rank | Scenario | Orchestration | Validation | Progression | Bounded scope | Samples | Weighted total |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1 | Composite document capture | 5 | 4 | 5 | 4 | 5 | 93 |
| 2 | Expense report linked to ingested receipts | 5 | 5 | 5 | 2 | 5 | 91 |
| 3 | Invoice with purchase-order matching | 4 | 5 | 3 | 4 | 5 | 83 |
| 4 | Shipping and customs document package | 5 | 5 | 2 | 2 | 4 | 76 |
| 5 | Multi-page bank statement reconciliation | 4 | 5 | 2 | 3 | 3 | 70 |

### 1. Composite Document Capture — Recommended First Step

A single high-resolution photo or scan may contain several physical documents: for example receipts, train tickets, and other small items arranged on a desk rather than on an A4 page.

The processing path should:

1. analyse a layout-sized derivative of the whole image and identify document regions;
2. validate the returned bounds and crop each region from the original high-resolution image;
3. classify each crop independently;
4. fan supported crops out to their document-specific workflows;
5. preserve an explicit unsupported or failed result for an individual crop without discarding successful siblings; and
6. aggregate the member results into one batch response.

This naturally exercises typed fan-out/fan-in, conditional routing, reusable sub-workflows, aggregation, and progress events. It also improves a real intake problem without requiring persistence or a long-running approval process.

After cropping, every member enters the same classification and document-specific processing path it would have followed as an individual upload. Batch orchestration changes the intake envelope and aggregates the outcomes; it does not create a second implementation of receipt, shopping-list, Sujiko, or unsupported-document processing.

A composite capture is an **intake shape**, not a final business document category. The aggregate result should contain independently classified document members. Whether the first implementation represents composite detection as a dedicated intake analyser or as a special top-level classifier outcome belongs in the model-boundary and capability decisions; downstream receipt, shopping-list, and Sujiko workflows should not need to understand the batch envelope.

Model-produced regions are untrusted input. Deterministic checks should reject or flag out-of-range coordinates, negligible crops, excessive overlap, duplicate regions, and an excessive member count. The batch contract must define zero-region, one-region, partial-success, unreadable-member, and cancellation behaviour.

Image preprocessing is significant. Detection may use a smaller layout image, but classification and extraction should use crops taken from the original image before their normal purpose-specific resizing. Otherwise small receipt text could be lost when the full desk image is reduced.

### 2. Expense Report Linked to Ingested Receipts — Recommended Follow-On

An expense report is a distinct document type that can refer to receipts processed earlier. Its workflow could load the referenced receipt results, validate ownership and currency, reconcile claimed and evidenced totals, detect duplicate evidence, apply deterministic policy, and return an attestation or review requirement.

This is a strong follow-on because composite capture makes ingesting several receipts convenient without forcing a separate interaction for each one. It is not the same feature, however. Linking previously ingested documents requires stable document identifiers, persistence, ownership rules, retention and deletion behaviour, and a way to resolve missing or inaccessible references. Those trust and lifecycle decisions should not be hidden inside the composite-capture slice.

The first expense-report delivery should stop at a structured, reviewable result with explicit user attestation. It should not submit a claim or treat a model as the responsible party. Durable pause/resume becomes justified only if a later product decision introduces an approval response that must survive the original request. Agent collaboration remains subject to the E6 quality gate.

### 3. Invoice with Purchase-Order Matching — Single-Document Alternative

The classifier already recognises invoices as unsupported, and the Semantic Kernel predecessor processed them. Reintroducing invoice extraction with line arithmetic and PO matching would provide clear deterministic reconciliation and conditional match/review/reject routes.

It ranks below composite capture because a useful PO lookup requires either a second document, a real external system, or an explicitly simulated registry. Parallel work is otherwise limited until invoice extraction reveals the PO number. It remains the best alternative if the next baseline must retain both the single-image and single-document assumptions.

### 4. Shipping and Customs Document Package

A commercial invoice, packing list, and bill of lading can be extracted in parallel and reconciled for shipment identifiers, quantities, weights, and totals. The orchestration and validation are strong, but multi-document intake and unfamiliar trade-document rules make the first slice larger and more domain-heavy than composite capture of already supported types.

### 5. Multi-Page Bank Statement Reconciliation

Pages or periods can be processed concurrently and aggregated, with deterministic balance and transaction checks. However, PDF ingestion, page rendering, table extraction, and sensitive financial samples risk making document preprocessing rather than MAF workflow design the dominant work.

## Recommendation and Decision Boundary

Select **composite document capture** as the next scenario. Its smallest coherent MAF capability set is:

- an intake decision between a single document and a composite capture;
- document-region detection and validated cropping;
- parallel per-region classification and reusable document sub-workflows;
- deterministic fan-in to a batch result with partial-success semantics; and
- workflow events for detection, member progress, and aggregation.

Do not include persistent storage, expense-report linking, checkpointing, external submission, or additional review agents in this initial slice.

Retain **expense report linked to ingested receipts** as the recommended follow-on. Composite capture removes the most obvious ingestion friction, while the later feature can introduce persistence and linking deliberately rather than as an incidental implementation detail.

The E0 decision should confirm:

1. that a single upload may contain zero, one, or several independently processed document regions;
2. that the initial batch response is request-scoped and does not persist document results;
3. that partial success is allowed and every detected region receives its own outcome; and
4. that expense-report linking is follow-on scope rather than part of composite capture.

After that decision, issues #10–#14 can define the batch and member contracts, model boundaries, initial MAF capabilities, decision record, and representative desk-photo samples. Issue #16 can then select a package version against the approved capability set.

## Framework Fit

The candidate capability set follows the current [.NET MAF graph workflow model](https://learn.microsoft.com/en-us/agent-framework/concepts/workflows/), which documents typed executors, conditional edges, fan-out/fan-in, workflow events, and sub-workflow composition. Issue #16 remains responsible for selecting a package version that supports the approved E0 capability set.
