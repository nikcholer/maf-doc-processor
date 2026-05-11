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

## Principles

- Keep V1 Semantic Kernel behavior available as a reference while V2 is built.
- Prove one vertical slice before adding advanced orchestration.
- Prefer deterministic executors for deterministic work.
- Use agents where reasoning, extraction, classification, or review actually benefits from model behavior.
- Treat structured output as a contract, but still validate, retry, and fail clearly.
- Pin package versions rather than floating on latest.
- Separate stable 1.x framework features from preview-adjacent integrations.

## Open Decisions

| ID | Decision | Options | Current Lean |
| --- | --- | --- | --- |
| D1 | Repository strategy | New V2 repo, branch in existing repo, side-by-side project | New V2 repo if we want a clean portfolio artifact; branch/side-by-side if we want easier comparison |
| D2 | First document type | Current strongest use case, simplest sample, resume/CV, tax/finance document | Pick the use case with the clearest expected output schema |
| D3 | Output schema | Summary only, extraction model, classification plus extraction, full report | Start with classification plus extraction/summary |
| D4 | Model/provider | Azure OpenAI, OpenAI, Foundry Agent Service, multiple providers | Use one provider first behind `Microsoft.Extensions.AI` |
| D5 | Hosting model | Console/CLI, web API, worker service, Azure Functions Durable | Console/API first, Durable later |
| D6 | Human review trigger | Fixed confidence threshold, validation failures, user-configured policy | Validation failures plus confidence threshold once confidence is defined |
| D7 | Multi-agent review | In MVP, post-MVP, optional quality mode | Post-MVP unless quality demands it early |
| D8 | Durable backend | None initially, local emulator, Durable Task Scheduler, Azure Functions storage | Defer until workflow contracts are stable |

## Backlog

### P0 - Foundation

- [ ] Create or choose the V2 working location.
- [ ] Preserve V1 Semantic Kernel repo/history and document its maintenance-mode status.
- [ ] Confirm baseline build and test status before migration changes.
- [ ] Inventory Semantic Kernel usage:
  - [ ] `Kernel`
  - [ ] `ChatCompletionAgent` or SK agent types
  - [ ] `[KernelFunction]`
  - [ ] prompt templates
  - [ ] `PromptExecutionSettings`
  - [ ] JSON parsing / structured response logic
  - [ ] DI registrations
- [ ] Decide target .NET version.
- [ ] Pin initial Agent Framework package versions.

### P1 - Working Vertical Slice

- [ ] Remove SK package references from the V2 project.
- [ ] Add Agent Framework package references:
  - [ ] `Microsoft.Agents.AI`
  - [ ] `Microsoft.Agents.AI.Workflows`
  - [ ] `Microsoft.Extensions.AI`
  - [ ] selected provider package
- [ ] Define core domain records:
  - [ ] `FileRequest`
  - [ ] `DocumentText`
  - [ ] `DocumentClassification`
  - [ ] `DocumentExtraction`
  - [ ] `DocumentSummary`
  - [ ] `ValidationResult`
- [ ] Port deterministic processing into executors:
  - [ ] text extraction executor
  - [ ] document classification executor
  - [ ] document analysis/extraction executor
  - [ ] validation executor
  - [ ] persistence/output executor
- [ ] Build a first linear workflow with `WorkflowBuilder`.
- [ ] Run one sample document end to end.
- [ ] Add tests for the sample document workflow.
- [ ] Add structured output validation and clear failure messages.

### P2 - Workflow Maturity

- [ ] Add conditional routing by document type.
- [ ] Add retry policy for transient model/provider failures.
- [ ] Add validation-based repair or re-run flow.
- [ ] Add workflow event logging.
- [ ] Add token, latency, and model-call telemetry.
- [ ] Compare V1 and V2 outputs on representative fixtures.
- [ ] Document migration differences from Semantic Kernel to Agent Framework.

### P3 - Long-Running Processing

- [ ] Evaluate Durable Task integration against actual workflow needs.
- [ ] Choose durable hosting/backend.
- [ ] Add checkpointing for long-running document jobs.
- [ ] Add resume/retry behavior for interrupted jobs.
- [ ] Add operational documentation for durable runs.

### P4 - Human Review

- [ ] Define confidence scoring or review policy.
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
