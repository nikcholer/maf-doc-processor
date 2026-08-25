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
| E3 | Deliver the new document vertical slice | Required |
| E4 | Add justified branching, parallelism, and aggregation | Required where selected by E0 |
| E5 | Add external input and checkpointing | Only if the workflow crosses a process or human boundary |
| E6 | Integrate quality-review agents | Only if evaluation demonstrates sufficient benefit |
| E7 | Harden, document, measure, and release the new baseline | Required |

## Phase E0: Select the Next Document Scenario

**Objective:** Choose a document type whose processing requirements justify a richer workflow.

- [ ] Compare candidate document types against the current receipt, shopping-list, and Sujiko coverage.
- [ ] Prefer a scenario that requires at least one meaningful orchestration capability beyond a linear pipeline.
- [ ] Define the document's structured output and validation rules.
- [ ] Identify which decisions are deterministic and which genuinely require a model.
- [ ] Define unsupported, invalid, repairable, and review-required outcomes.
- [ ] Select the primary MAF capabilities the slice will exercise; limit the initial selection to the smallest coherent set.
- [ ] Record the decision and rejected alternatives in a short design document.
- [ ] Assemble representative sample inputs, including success, repair, review, and failure cases.

**Candidate capabilities:** conditional routing, reusable sub-workflows, parallel fan-out/fan-in, custom workflow events, request/response handling, checkpointing, or agent collaboration.

**Exit criteria:** The document scenario, output contract, acceptance examples, orchestration requirements, and non-goals are agreed before implementation begins.

Candidate comparison: [Document scenario evaluation](document-scenario-evaluation.md).

## Phase E1: Refresh and Protect the Baseline

**Objective:** Start the architectural work from a current, measurable, and secure baseline.

- [ ] Resolve the vulnerable transitive test dependencies reported through the current xUnit package chain.
- [ ] Review available .NET, MAF, OpenAI SDK, ImageSharp, and test-tooling updates.
- [ ] Choose and pin the MAF version required by the selected workflow capabilities.
- [ ] Apply dependency updates in bounded groups, with regression tests after each group.
- [ ] Record baseline test count, representative model latency, token usage, and estimated cost.
- [ ] Build a small versioned golden set for the existing supported and unsupported document paths.
- [ ] Add or confirm regression coverage for existing API response and error contracts.

**Exit criteria:** The solution builds without warnings, the full offline suite passes, dependency audit findings are resolved or explicitly accepted, and baseline measurements are recorded.

## Phase E2: Introduce a Top-Level Routing Workflow

**Objective:** Move classification and document routing into an explicit MAF workflow while preserving current behaviour.

- [ ] Confirm the supported MAF pattern for composing document-specific workflows with the pinned package version.
- [ ] Reintroduce classification as a typed executor within the top-level workflow.
- [ ] Route classifications using MAF conditional edges rather than an application-level switch outside the graph.
- [ ] Represent receipt, shopping-list, Sujiko, and unsupported handling as typed workflow destinations.
- [ ] Extract existing document graphs behind reusable sub-workflow or adapter boundaries.
- [ ] Preserve classification confidence, model usage, correlation data, cancellation, and error propagation.
- [ ] Emit observable events for classification, routing, and selected workflow completion.
- [ ] Add topology tests proving that every category reaches exactly one intended destination.
- [ ] Add compatibility tests proving that existing inputs retain their response semantics.

**Exit criteria:** All current document types run through one inspectable top-level MAF graph with no API contract regression and no additional model calls.

## Phase E3: Add the New Document Vertical Slice

**Objective:** Implement the selected document type as a complete, independently testable workflow.

- [ ] Add the category, domain records, API mapping, and UI representation.
- [ ] Define a dedicated extractor interface and model-backed implementation.
- [ ] Add a separate model role only if the document requires different model capabilities or operational settings.
- [ ] Implement deterministic structural and semantic validation.
- [ ] Implement one bounded repair path for model-correctable failures.
- [ ] Define policy and human-review evaluation separately from structural validation.
- [ ] Build the document-specific workflow from typed executors and hand-off records.
- [ ] Connect the new workflow to the top-level conditional route.
- [ ] Add parser, extractor, executor, workflow, response-mapping, and HTTP integration tests.
- [ ] Verify at least one representative sample against the configured live provider.
- [ ] Document result semantics and update the guide for adding document types.

**Exit criteria:** The new type processes end to end through the API and UI, has explicit success/failure/review semantics, and passes offline and representative live verification.

## Phase E4: Add Requirement-Driven Branching and Parallelism

**Objective:** Use MAF graph capabilities for work that is genuinely independent or outcome-dependent.

- [ ] Identify validations, enrichment, or review operations that can execute independently.
- [ ] Fan out those operations through typed parallel workflow branches.
- [ ] Aggregate branch outputs into one deterministic decision record.
- [ ] Add conditional edges for accept, repair, reject, and review-required outcomes where applicable.
- [ ] Keep repair attempts bounded and prevent cycles without explicit limits.
- [ ] Define behaviour when one parallel branch fails, times out, or is cancelled.
- [ ] Emit progress events that distinguish branch start, completion, aggregation, and route selection.
- [ ] Add tests for fan-out/fan-in ordering independence, partial failure, cancellation, and every conditional destination.
- [ ] Compare latency with sequential execution and confirm that parallelism produces a measurable benefit.

**Exit criteria:** The graph topology corresponds to real document-processing decisions, all routes are covered by tests, and added concurrency improves either clarity or measured execution time.

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

## Explicit Non-Goals for the Initial Evolution

- Public hosting, authentication, quotas, and abuse prevention unless separately approved.
- Durable infrastructure without a workflow that crosses a real process or human boundary.
- Multi-agent review enabled by default without evaluation evidence.
- Replacing deterministic validation or policy with model judgement.
- Adding several document types before the selected new slice is complete and measured.
