# MAF Workflow Evolution Backlog

## Purpose

This is the active backlog for extending the document processor beyond its initial Microsoft Agent Framework migration. The completed [migration backlog](maf-migration-backlog.md) remains the historical record for the current receipt, shopping-list, and Sujiko slices.

The next stage should strengthen the application architecture, not simply add another extraction prompt. Each MAF capability must correspond to a concrete workflow requirement and preserve the existing API behaviour unless a contract change is explicitly approved.

## Delivery Principles

- Preserve the existing document slices as regression baselines.
- Prefer deterministic C# for validation, arithmetic, policy, and other predictable rules.
- Use model-backed stages for classification, extraction, interpretation, and evidence-based review.
- Introduce framework capabilities incrementally and measure their operational cost.
- Keep optional or experimental paths out of the default API flow until their value is demonstrated.
- Treat checkpoints, human input, and persisted state as trust boundaries requiring explicit design decisions.

## Phase Summary

| Phase | Outcome | Gate |
| --- | --- | --- |
| E0 | Select the next document scenario and its workflow requirements | Required |
| E1 | Refresh dependencies and capture a protected baseline | Required |
| E2 | Route all document types through a top-level MAF graph | Required |
| E3 | Add multi-source composite capture and independent member processing | Required |
| E4 | Deliver the expense-report vertical slice | Required |
| E5 | Add external input and checkpointing | Only if the workflow crosses a process or human boundary |
| E6 | Integrate quality-review agents | Only if evaluation demonstrates sufficient benefit |
| E7 | Harden, document, measure, and release the new baseline | Required |

## Phase E0: Select the Next Document Scenario

**Objective:** Choose a document type whose processing requirements justify a richer workflow.

- [x] Compare candidate document types against the current receipt, shopping-list, and Sujiko coverage.
- [x] Prefer a scenario that requires at least one meaningful orchestration capability beyond a linear pipeline.
- [x] Define the document's structured output and validation rules.
- [x] Identify which decisions are deterministic and which genuinely require a model.
- [x] Define unsupported, invalid, repairable, and review-required outcomes.
- [x] Select the primary MAF capabilities the slice will exercise; limit the initial selection to the smallest coherent set.
- [x] Record the decision and rejected alternatives in a short design document.
- [x] Assemble representative sample inputs, including success, repair, review, and failure cases.

**Candidate capabilities:** conditional routing, reusable sub-workflows, parallel fan-out/fan-in, custom workflow events, request/response handling, checkpointing, or agent collaboration.

**Exit criteria:** The document scenario, output contract, acceptance examples, orchestration requirements, and non-goals are agreed before implementation begins.

Candidate comparison: [Document scenario evaluation](document-scenario-evaluation.md).

Accepted direction: [Batch capture and expense report sequencing decision](batch-capture-expense-report-decision.md).

Model and result boundary: [Capture and expense report model boundaries](capture-expense-model-boundaries.md).

Selected workflow capabilities: [Capture and expense report MAF capability selection](capture-expense-maf-capabilities.md).

Versioned implementation fixtures: [Composite capture and expense report sample set](next-scenario-sample-set.md).

## Phase E1: Refresh and Protect the Baseline

**Objective:** Start the architectural work from a current, measurable, and secure baseline.

- [x] Resolve the vulnerable transitive test dependencies reported through the current xUnit package chain.
- [x] Review available .NET, MAF, OpenAI SDK, ImageSharp, and test-tooling updates.
- [x] Choose and record the MAF version required by the selected workflow capabilities.
- [x] Apply dependency updates in bounded groups, with regression tests after each group.
- [x] Record baseline test count, representative model latency, token usage, and estimated cost.
- [x] Build a small versioned golden set for the existing supported and unsupported document paths.
- [x] Add or confirm regression coverage for existing API response and error contracts.

**Exit criteria:** The solution builds without warnings, the full offline suite passes, dependency audit findings are resolved or explicitly accepted, and baseline measurements are recorded.

Selected versions and deferrals: [Dependency baseline decision](dependency-baseline-decision.md).

Recorded comparison point: [Current workflow baseline measurements](baseline-measurements.md).

Versioned regression corpus: [Current document golden set](golden-set.md).

## Phase E2: Introduce a Top-Level Routing Workflow

**Objective:** Move classification and document routing into an explicit MAF workflow while preserving current behaviour.

- [x] Confirm the supported MAF pattern for composing document-specific workflows with the pinned package version.
- [x] Reintroduce classification as a typed executor within the top-level workflow.
- [x] Route classifications using MAF conditional edges rather than an application-level switch outside the graph.
- [x] Represent receipt, shopping-list, Sujiko, and unsupported handling as typed workflow destinations.
- [x] Extract existing document graphs behind reusable sub-workflow or adapter boundaries.
- [x] Preserve classification confidence, model usage, correlation data, cancellation, and error propagation.
- [x] Emit observable events for classification, routing, and selected workflow completion.
- [x] Add topology tests proving that every category reaches exactly one intended destination.
- [x] Add compatibility tests proving that existing inputs retain their response semantics.

**Exit criteria:** All current document types run through one inspectable top-level MAF graph with no API contract regression and no additional model calls.

Selected routing and child-workflow pattern: [Top-level document routing design](top-level-routing-design.md).

## Phase E3: Add Multi-Source Composite Capture

**Objective:** Accept one or more source images, detect zero or more physical documents in each, and process every valid member independently through the existing document route.

Delivery is split into [shared foundations](https://github.com/nikcholer/maf-doc-processor/issues/43), [source detection](https://github.com/nikcholer/maf-doc-processor/issues/44), [region validation and cropping](https://github.com/nikcholer/maf-doc-processor/issues/45), [bounded orchestration](https://github.com/nikcholer/maf-doc-processor/issues/46), [the capture API](https://github.com/nikcholer/maf-doc-processor/issues/47), [annotated previews](https://github.com/nikcholer/maf-doc-processor/issues/48), and [final hardening and measurement](https://github.com/nikcholer/maf-doc-processor/issues/49). The GitHub Project records which of these is ready or active.

- [x] Add the repeated-file capture endpoint without changing the individual document endpoint.
- [x] Decode and orient each source once, then make one typed document-region detection call.
- [x] Deterministically validate detected document regions before cropping.
- [x] Crop every valid region from its high-resolution source before normal classification and extraction preprocessing.
- [x] Fan sources and members out with explicit concurrency and resource limits.
- [x] Route each member through the reusable top-level document workflow.
- [x] Aggregate source and member outcomes with deterministic success, partial-success, and failure semantics.
- [x] Isolate non-cancellation source and member failures without discarding trustworthy siblings.
- [x] Preserve correlation and account for every detection, classification, extraction, and repair call exactly once.
- [x] Emit progress events for source detection, member routing, completion, and aggregation.
- [x] Add per-source annotated previews with accessible accepted, review, and rejected treatments.
- [ ] Cover single-source, multi-source, overlapping, duplicate, unsupported, partial-failure, timeout, and cancellation paths.
- [ ] Compare bounded parallel execution with a sequential baseline for latency and resource use.

**Exit criteria:** Multi-source and composite images produce independently processed member results through the API and UI, with bounded fan-out, deterministic aggregation, complete route coverage, and no regression to individual uploads.

## Phase E4: Add the Expense Report Vertical Slice

**Objective:** Implement expense report as the next complete, independently testable business document workflow.

- [ ] Add the expense-report category, domain records, API mapping, and UI representation.
- [ ] Define a dedicated extractor interface and model-backed implementation.
- [ ] Add a separate model role only if expense reports require different model capabilities or operational settings.
- [ ] Implement deterministic structural, line-total, claimed-total, date, and currency validation.
- [ ] Implement one bounded repair path for model-correctable failures.
- [ ] Define policy, ownership attestation, and human-review evaluation separately from structural validation.
- [ ] Build the workflow from typed executors and hand-off records.
- [ ] Connect the workflow to individual and batch member routing without special-case batch logic.
- [ ] Add parser, extractor, executor, workflow, response-mapping, HTTP, and annotated-batch integration tests.
- [ ] Verify representative expense-report samples against the configured live provider.
- [ ] Document result semantics and update the guide for adding document types.
- [ ] Keep persistent receipt linking and external claim submission out of the initial slice.

**Exit criteria:** Expense reports process end to end individually and as batch members, have explicit success/failure/review/attestation semantics, and pass offline and representative live verification.

## Phase E5: External Input and Checkpointing — Gated

**Objective:** Add pause/resume only if the selected workflow requires input that cannot be completed within one foreground request.

**Decision gate:** Reopen the [durability decision](durability-decision.md) only when the workflow has a real reviewer wait, user attestation, background job, or restart-survival requirement.

- [ ] Define the external request and typed response contract.
- [ ] Decide whether processing remains HTTP request-scoped or moves to a job model.
- [ ] Select checkpoint storage and document its trust, access-control, retention, and cleanup requirements.
- [ ] Implement MAF request/response handling at the required workflow boundary.
- [ ] Capture and persist checkpoints without storing provider credentials or unnecessary source data.
- [ ] Add endpoints or UI needed to inspect and answer pending requests.
- [ ] Define timeout, rejection, cancellation, expiry, and resubmission behaviour.
- [ ] Test pause, process restart, resume, duplicate response, invalid response, and expired request paths.
- [ ] Update API, operational, human-review, and durability documentation.

**Exit criteria:** A workflow can pause and resume safely across the required boundary, with authenticated ownership deferred unless the application is made externally accessible.

## Phase E6: Quality Review and Agent Collaboration — Gated

**Objective:** Enable additional model review only when evaluation shows a useful quality improvement.

**Decision gate:** The existing Analyst/Critic prototype must outperform the single-extraction baseline on the golden set without an unacceptable false-positive, latency, or cost increase.

- [ ] Define evaluation cases and expected findings before running comparisons.
- [ ] Measure baseline extraction quality on the golden set.
- [ ] Run the quality-review prototype on the same inputs.
- [ ] Record true findings, missed issues, false positives, latency, tokens, and estimated cost.
- [ ] Decide whether review is rejected, opt-in, conditionally routed, or enabled by default.
- [ ] If retained, give quality review a dedicated configured model role.
- [ ] Integrate it as a typed sub-workflow on only the approved routes.
- [ ] Surface critic findings through the document result and any human-review interface.
- [ ] Add regression tests for disagreement, hallucination detection, and reviewer failure.
- [ ] Document the evaluation result regardless of whether integration proceeds.

**Exit criteria:** The decision to retain or reject agent collaboration is supported by repeatable evidence rather than framework availability alone.

## Phase E7: Hardening and Release

**Objective:** Make the extended workflow observable, maintainable, and ready to become the next stable application baseline.

- [ ] Run the full offline, integration, golden-set, cancellation, and selected live-provider checks.
- [ ] Verify correlation IDs and structured telemetry across the top-level graph and every sub-workflow.
- [ ] Confirm model usage and estimated cost include parallel, repair, and optional review calls exactly once.
- [ ] Review upload limits, memory use, concurrency, timeouts, retries, and provider failure handling.
- [ ] Review the API schema for typed document payloads and update the error contract where necessary.
- [ ] Update the README, technical process flow, slice guide, and architecture decisions.
- [ ] Remove superseded experimental paths and close or move remaining items to the icebox.
- [ ] Record before/after architecture, quality, latency, and cost results.
- [ ] Tag the resulting stable milestone after remote verification and a clean build/test run.

**Exit criteria:** Documentation matches the implementation, required checks pass, operational trade-offs are recorded, and the new milestone is reproducible from a clean checkout.

## Icebox

- Evaluate alternative vision models for document-region detection if Qwen's tight boxes remain a quality limit after the current padding and main-document prompts. Tracked as [#53](https://github.com/nikcholer/maf-doc-processor/issues/53).

## Explicit Non-Goals for the Initial Evolution

- Public hosting, authentication, quotas, and abuse prevention unless separately approved.
- Durable infrastructure without a workflow that crosses a real process or human boundary.
- Multi-agent review enabled by default without evaluation evidence.
- Replacing deterministic validation or policy with model judgement.
- Adding several document types before the selected new slice is complete and measured.
