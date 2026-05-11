# Microsoft Agent Framework Migration Backlog

## Context

We are planning a V2 migration of the existing Semantic Kernel document processor to Microsoft Agent Framework.

Microsoft announced Agent Framework 1.0 on April 3, 2026 for both .NET and Python, describing it as production-ready with stable APIs and long-term support. Current NuGet packages are now stable 1.x releases, so the core adoption risk is lower than it would have been during the preview/RC period.

The plan is still to migrate incrementally: first preserve a working document-processing path, then add graph branching, durability, human review, and multi-agent review where each capability proves useful.

## Architecture Decision

Adopt Microsoft Agent Framework for the V2 document processor.

The target architecture should use:

- `Microsoft.Agents.AI` for the core agent abstraction.
- `Microsoft.Agents.AI.Workflows` for graph-based workflows and executors.
- `Microsoft.Extensions.AI` as the shared AI abstraction layer.
- Provider-specific packages selected after the model/provider decision.
- Durable Task integration only after the in-memory workflow is working end to end.

V2 will live in a separate repository so the Semantic Kernel implementation can be culled or archived later without affecting this codebase.

## Principles

- Keep V1 Semantic Kernel behavior available as a reference while V2 is built.
- Prove one vertical slice before adding advanced orchestration.
- Prefer deterministic executors for deterministic work.
- Use agents where reasoning, extraction, classification, or review actually benefits from model behavior.
- Treat structured output as a contract, but still validate, retry, and fail clearly.
- Pin package versions rather than floating on latest.
- Separate stable 1.x framework features from preview-adjacent integrations.

## Open Decisions

| ID | Decision | Status | Notes |
| --- | --- | --- | --- |
| D1 | Repository strategy | Decided | Separate V2 repository. The SK repository must be disposable/archivable without impact. |
| D2 | First document type | Decided | Receipts, matching the old repository. Shopping lists are the candidate second document type if a new type is needed. |
| D3 | Output schema | Decided for MVP | Match what the old repository currently extracts from receipts. Inventory the old repo before finalizing record names. |
| D4 | Model/provider | Decided for MVP | Keep model selection in config. Use TogetherAI with Gemma 4 for both image recognition and text/testing initially, while preserving separate role settings so tasks can point to different models later. |
| D5 | Hosting model | Decided for MVP | All local. Revisit hosting only if/when external access becomes useful. |
| D6 | Human review trigger | Partly decided | Human review is required when the model is in doubt on categorization or other key fields. Some document types may require user ownership/attestation after parsing, e.g. an expense claim is submitted by the user, not by the model. |
| D7 | Multi-agent review | Decided | Post-MVP quality layer, not part of the first vertical slice. |
| D8 | Durability question | Open | The question is not "which cloud backend?" yet. It is: do we need durable pause/resume for local long-running jobs, and if so should we use MAF Durable Task locally, a lightweight local job store, or defer entirely until hosting exists? |
| D9 | Frontend scope | Decided for MVP | Port or recreate the old static web app only as a reassuring local demo for human consumption with an arbitrary new image. A proper user-facing frontend is a separate future effort. |

## Backlog

### P0 - Foundation

- [x] Create or choose the V2 working location.
- [x] Locate V1 Semantic Kernel repository for reference: `C:\data\repo\csharp-semantic-document-processor`.
- [x] Preserve V1 Semantic Kernel repo/history and document its maintenance-mode status. The V1 repo remains a separate GitHub-backed reference/maintenance repository; V2 work happens here and culling/archive can wait until V2 covers the needed behavior.
- [x] Confirm baseline build and test status before migration changes.
- [x] Inventory Semantic Kernel usage:
  - [x] `Kernel`
  - [x] SK agent types: none found
  - [x] `[KernelFunction]`
  - [x] prompt templates
  - [x] `PromptExecutionSettings`
  - [x] JSON parsing / structured response logic
  - [x] DI registrations
- [x] Inventory current receipt extraction fields from the old repository.
- [x] Decide target .NET version.
- [x] Pin initial Agent Framework package versions.
- [x] Define config shape for model selection:
  - [x] image recognition model, initially Gemma 4
  - [x] text/test model, initially a GPT mini model
  - [x] provider endpoints/keys outside source control
- [x] Document that the V1 test project must currently be run directly because it is not included in the old solution.

Initial package pins:

- `Microsoft.Agents.AI` `1.4.0`
- `Microsoft.Agents.AI.Workflows` `1.4.0`
- `Microsoft.Agents.AI.OpenAI` `1.4.0`
- `Microsoft.Extensions.AI` `10.5.2`
- `OpenAI` `2.10.0`
- Target framework: `net8.0`

### P1 - Working Vertical Slice

- [x] Remove SK package references from the V2 project. No SK references were added to V2.
- [x] Add Agent Framework package references:
  - [x] `Microsoft.Agents.AI`
  - [x] `Microsoft.Agents.AI.Workflows`
  - [x] `Microsoft.Extensions.AI`
  - [x] selected provider package: `Microsoft.Agents.AI.OpenAI`
- [x] Define core domain records:
  - [x] `FileRequest`
  - [x] `DocumentClassification`
  - [x] receipt extraction record matching the old repository
  - [x] `ValidationResult`
  - [x] model usage and receipt processing result records
- [x] Port deterministic processing into executors:
  - [x] document classification executor
  - [x] receipt extraction executor
  - [x] receipt validation executor
  - [x] receipt policy executor
  - [x] receipt result/output executor
- [x] Build a first linear workflow with `WorkflowBuilder`.
- [x] Run one sample receipt end to end.
- [x] Add tests for the sample receipt workflow.
- [x] Add structured output validation and clear failure messages.
- [x] Keep the workflow local-only with no external hosting dependency.
- [x] Wire real configured model clients behind `IDocumentClassifier` and `IReceiptExtractor`.

### P1.5 - Local API and Demo UI

- [x] Add a V2 ASP.NET Minimal API host project over the workflow library.
- [x] Serve static demo assets, using the old `wwwroot` app as the reference point.
- [x] Keep the UI scope deliberately small: upload one arbitrary PNG/JPEG, submit it, and show the parsed result.
- [x] Add `/health` with local readiness and configured model/provider visibility.
- [x] Add `/api/documents/process` as an adapter from multipart upload to `ReceiptProcessingWorkflow`.
- [x] Map V2 workflow output into a demo response that shows:
  - [x] document category
  - [x] extracted receipt fields
  - [x] policy/review decision and reasons
  - [x] model usage
  - [x] validation errors and warnings
- [x] Preserve upload validation from the old API where still relevant.
- [x] Avoid production frontend concerns for this stage: no auth UI, no persistence UI, no workflow history UI, and no polished product IA.
- [x] Return human-readable unsupported-document messages, e.g. "This appears to be a car registration document. This demo can only process receipts right now."
- [x] Verify the local demo shell, `/health`, upload validation, and missing-key response.
- [x] Verify live model extraction with a known sample image and at least one ad hoc new image once `TOGETHER_API_KEY` is visible to the server. Verified with a real supermarket receipt and a non-receipt technical infographic.

### P2 - Workflow Maturity

- [ ] Add conditional routing by document type.
- [ ] Add shopping list as a candidate second document type if a non-receipt type is needed.
- [ ] Add retry policy for transient model/provider failures.
- [ ] Add validation-based repair or re-run flow.
- [ ] Add workflow event logging.
- [ ] Add token, latency, and model-call telemetry:
  - [x] Capture provider-reported input, output, and total token counts per model call.
  - [x] Estimate per-run USD cost from configurable per-role pricing.
  - [ ] Capture per-model-call latency.
  - [ ] Emit structured workflow/model-call telemetry events.
- [ ] Compare V1 and V2 outputs on representative fixtures.
- [ ] Document migration differences from Semantic Kernel to Agent Framework.

### P3 - Long-Running Processing

- [ ] Decide whether local receipt/shopping-list processing actually needs durable pause/resume.
- [ ] If durability is needed, compare:
  - [ ] MAF Durable Task in a local setup
  - [ ] lightweight local job store/checkpoint files
  - [ ] deferring durability until external hosting exists
- [ ] Add checkpointing for long-running document jobs.
- [ ] Add resume/retry behavior for interrupted jobs.
- [ ] Add operational documentation for durable runs.

### P4 - Human Review

- [ ] Define confidence scoring or review policy for categorization and key extracted fields.
- [ ] Define document-type ownership/attestation rules, especially for expense claims where the user owns the submission.
- [ ] Add workflow pause/resume for human approval.
- [ ] Add reviewer input model.
- [ ] Add timeout/escalation behavior.
- [ ] Log review decisions for auditability.

### P5 - Multi-Agent Quality Layer

- [ ] Prototype AnalystAgent and CriticAgent workflow.
- [ ] Measure quality improvement against baseline single-agent output.
- [ ] Measure added cost and latency.
- [ ] Decide whether multi-agent review is default, optional, or rejected.
- [ ] Add tests for disagreement and hallucination-detection scenarios.

## Stable vs Preview-Aware Scope

Use stable core features for the migration:

- Core agents.
- Middleware.
- Workflows.
- Multi-agent orchestration patterns.
- MCP integration where useful.
- YAML/declarative definitions if they simplify configuration.

Treat these as optional or later until verified for our use case:

- DevUI.
- Foundry hosted agent integration.
- AG-UI / frontend adapters.
- Skills.
- Agent harness integrations.

## References

- Microsoft Agent Framework Version 1.0: https://devblogs.microsoft.com/agent-framework/microsoft-agent-framework-version-1-0/
- Agent Framework documentation: https://learn.microsoft.com/en-us/agent-framework/
- Workflow Builder & Execution: https://learn.microsoft.com/en-us/agent-framework/workflows/workflows
- Durable Task extension for Microsoft Agent Framework: https://learn.microsoft.com/en-us/azure/durable-task/sdks/durable-agents-microsoft-agent-framework
