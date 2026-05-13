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
- A small local `IModelChatClient` abstraction for the MVP model boundary. `Microsoft.Extensions.AI` remains a possible later migration target, but is not used while TogetherAI-specific protocol options are required.
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
| D4 | Model/provider | Decided for MVP | Keep model selection in config. Use TogetherAI with Qwen3.5 9B for document classification and extraction, with Gemma 4 retained for reserved text testing initially. |
| D5 | Hosting model | Decided for MVP | All local. Revisit hosting only if/when external access becomes useful. |
| D6 | Human review trigger | Decided for MVP | Review is a quality/ownership state, not an API failure. Low or missing classification confidence, policy exceptions, validation issues, and future user-owned submissions require review or attestation according to `docs/human-review-policy.md`. |
| D7 | Multi-agent review | Decided | Post-MVP quality layer, not part of the first vertical slice. |
| D8 | Durability question | Decided for MVP | Defer durable pause/resume for the local demo. Current processing is bounded, cancellable foreground HTTP work. Revisit when background jobs, human review waits, hosting, or restart-safe workflow history become real requirements. |
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
  - [x] document classification model, initially Qwen3.5 9B
  - [x] document extraction model, initially Qwen3.5 9B
  - [x] text/test model, initially Gemma 4
  - [x] provider endpoints/keys outside source control
- [x] Document that the V1 test project must currently be run directly because it is not included in the old solution.

Initial package pins:

- `Microsoft.Agents.AI` `1.4.0`
- `Microsoft.Agents.AI.Workflows` `1.4.0`
- `Microsoft.Agents.AI.OpenAI` `1.4.0`
- `OpenAI` `2.10.0`
- Target framework: `net8.0`

### P1 - Working Vertical Slice

- [x] Remove SK package references from the V2 project. No SK references were added to V2.
- [x] Add Agent Framework package references:
  - [x] `Microsoft.Agents.AI`
  - [x] `Microsoft.Agents.AI.Workflows`
  - [x] selected provider client: `OpenAI`
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

- [x] Add conditional routing by document type.
- [x] Add shopping list as a candidate second document type if a non-receipt type is needed.
- [x] Add retry policy for transient model/provider failures.
- [x] Add validation-based repair or re-run flow.
  - Added one bounded re-extraction attempt after validation failures for receipts and shopping lists.
  - Repair prompts carry validation reasons back to the extractor and model usage includes both extraction calls.
- [x] Add workflow event logging.
- [x] Add token, latency, and model-call telemetry:
  - [x] Capture provider-reported input, output, and total token counts per model call.
  - [x] Estimate per-run USD cost from configurable per-role pricing.
  - [x] Capture per-model-call latency.
  - [x] Emit structured workflow/model-call telemetry events.
- [x] Resolve the unused `DocumentClassificationExecutor`: deleted because classification intentionally remains outside the MAF graph before routing to document-specific workflows.
- [x] Decide whether `Microsoft.Extensions.AI` is a real abstraction target: deferred for now; V2 keeps the custom `IModelChatClient` abstraction because TogetherAI-specific protocol options are required.
- [x] Reuse/cache OpenAI-compatible `ChatClient` instances per model settings key.
- [x] Thread request-scoped correlation or operation IDs through workflow and model-call logs.
- [x] Clarify the `TextTesting` model role by documenting it as reserved config.
- [x] Add a repo `README.md` for setup, running, test commands, config, and demo scope.

### P2.5 - Hardening

- [x] Add API integration tests with `WebApplicationFactory`.
- [x] Add cancellation propagation tests from HTTP request through workflow/model calls.
- [x] Formalize API error contract documentation, including error codes and status codes.
- [x] Define success/failure semantics per document type, especially validation warnings vs errors.

### P3 - Long-Running Processing

- [x] Decide whether local receipt/shopping-list processing actually needs durable pause/resume.
- [x] If durability is needed, compare:
  - [x] MAF Durable Task in a local setup
  - [x] lightweight local job store/checkpoint files
  - [x] deferring durability until external hosting exists
- [x] Defer checkpointing for local foreground document jobs until durability reopen criteria are met.
- [x] Defer resume/retry behavior for interrupted local jobs; failed or canceled requests are safe to resubmit.
- [x] Add operational documentation for durability decisions and reopen criteria.

### P4 - Human Review

- [x] Define confidence scoring or review policy for categorization and key extracted fields.
- [x] Define document-type ownership/attestation rules, especially for expense claims where the user owns the submission.
- [x] Add workflow pause/resume for human approval: deferred until durability/background review exists; current local demo returns review state immediately.
- [x] Add reviewer input model.
- [x] Add timeout/escalation behavior: deferred with pause/resume until a review queue exists.
- [x] Log review decisions for auditability: added the audit record model; persistence waits for a review endpoint or queue.

### P5 - Multi-Agent Quality Layer

- [x] Prototype AnalystAgent and CriticAgent workflow.
- [ ] Measure quality improvement against baseline single-agent output.
- [x] Measure added cost and latency.
- [x] Decide whether multi-agent review is default, optional, or rejected.
- [x] Add tests for disagreement and hallucination-detection scenarios.

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
