# Capture and Expense Report MAF Capability Selection

## Status

Accepted E0 decision on 25 August 2026. This decision supplies the workflow-capability input to dependency review in issue #16; it does not implement the E2-E4 workflows.

## Context

The accepted sequence introduces a reusable top-level document route, then multi-source composite capture, then expense-report processing. The implementation needs inspectable category routing, reuse of existing document workflows, bounded parallel capture processing, deterministic aggregation, and observable progress. It does not yet need durable state, human input, or additional collaborating agents.

The selected MAF surface is deliberately limited to capabilities required by those behaviours.

## Decision

Use the .NET graph-based workflow API with project-owned typed executors and hand-offs. Select these MAF capabilities for the first implementation:

1. Typed conditional edges for the top-level document route.
2. Workflows bound as executors for reusable document-specific sub-workflows.
3. Fixed, configured worker lanes connected by fan-out and fan-in edges for capture processing.
4. Concurrent in-process execution for independent worker lanes.
5. Custom workflow events plus standard executor, output, and error events.
6. Workflow visualization for topology review and route tests.

Deterministic C# continues to own validation, partitioning, concurrency limits, aggregation, status, disposition, ordering, and error conversion. MAF coordinates when those executors run and how typed messages move between them.

## Capability Mapping

| Requirement | Selected MAF capability | Project-owned responsibility |
| --- | --- | --- |
| Route each classified document to exactly one destination | `WorkflowBuilder.AddEdge<T>` with mutually exclusive predicates | Parse the classification, map `DocumentCategory`, and define the supported/default predicates |
| Reuse receipt, shopping-list, Sujiko, and later expense-report processing | Bind a built `Workflow` as an executor using the supported sub-workflow binding API | Build each typed document workflow and preserve its result, error, cancellation, and usage contracts |
| Process several sources and members without unbounded work | `AddFanOutEdge<T>` across a fixed set of worker executors | Partition ordered work into configured lanes and enforce request, source, member, memory, and provider limits |
| Continue only after every lane has reported | `AddFanInBarrierEdge` into a deterministic aggregation executor | Make every lane emit one typed lane result, including an empty result, and calculate the final ordered outcome |
| Run independent lanes concurrently | `InProcessExecution.Concurrent` | Select a conservative lane count, keep work within each lane sequential, and propagate cancellation |
| Expose classification, routing, detection, member completion, and aggregation progress | `WorkflowEvent` through `IWorkflowContext.AddEventAsync`, plus standard MAF events | Define safe event payloads containing correlation and outcome metadata, never source images or confidential model responses |
| Make the route inspectable and testable | `WorkflowVisualizer.ToMermaidString` or `ToDotString` | Assert destination coverage, exclusive predicates, expected fan-out/fan-in nodes, and named workflow stages |

## Top-Level Document Route

E2 replaces the application-level category switch with one typed MAF graph:

```text
request
  -> classify
  -> prepare classified document
  -> conditional category edges
       -> receipt workflow
       -> shopping-list workflow
       -> Sujiko workflow
       -> unsupported-result executor
       -> expense-report workflow (added in E4)
```

The parsed category activates the edge; the model never selects an executor. Predicates must be mutually exclusive and the unsupported predicate must cover every unregistered category. Each supported destination is a reusable document workflow rather than duplicated capture-specific logic.

Expense report therefore adds a destination and sub-workflow in E4. It does not require a new orchestration pattern.

## Bounded Capture Topology

E3 uses two bounded parallel sections:

```text
validated capture request
  -> source partitioner
  -> fan-out to configured source lanes
  -> source fan-in and deterministic region aggregation
  -> member partitioner
  -> fan-out to configured member lanes
  -> member fan-in and deterministic capture aggregation
  -> capture result
```

The graph contains a fixed configured number of source and member worker lanes, not one node per uploaded source or detected region. A partitioner emits exactly one partition to every lane, including empty partitions, so each fan-in barrier has a known contributor set and cannot wait for a lane that received no work.

Each source lane processes its assigned sources sequentially. Each member lane processes its assigned crops sequentially through the same reusable top-level document workflow used by individual uploads. Distinct lanes may run concurrently. This makes maximum concurrent detection and document-processing work explicit while still allowing a request to contain more items than the lane count.

Lane results retain source and member identifiers. The project aggregation executors restore multipart and region order, isolate non-cancellation failures, sum model usage exactly once, and calculate the documented source and capture statuses. A MAF fan-in barrier is synchronization; it does not replace those domain rules.

## Events and Result Delivery

Custom events should cover at least:

- classification completed and category route selected;
- source detection started and completed;
- member processing started and completed; and
- source aggregation and capture aggregation completed.

Event payloads carry the operation, `captureId`, `sourceItemId`, `memberId`, route or disposition where applicable, and timing or usage references. They do not contain image bytes, extracted fields, prompts, or raw provider responses.

The initial HTTP APIs still return one foreground response. Events support logs, tests, diagnostics, and a future streaming transport; this decision does not add server-sent events, background jobs, or persisted progress.

## Package Compatibility Input

The currently pinned `Microsoft.Agents.AI.Workflows` 1.4.0 package exposes the required API families. Issue #16 must select a deliberate baseline and prove the chosen version supports:

- typed conditional `AddEdge<T>` predicates;
- workflow-to-executor binding;
- selected `AddFanOutEdge<T>` routing and `AddFanInBarrierEdge` synchronization;
- concurrent in-process execution with cancellation;
- custom `WorkflowEvent` emission and standard output/error events; and
- Mermaid or DOT topology generation.

The dependency review must compile a small representative topology and run route, fan-out/fan-in, cancellation, error, and event tests. Release notes and known issues for the candidate version remain part of #16; API presence alone is not sufficient.

## Deferred Capabilities

The first implementation does not select:

- checkpointing, persisted workflow state, or resume;
- request/response human input or reviewer queues;
- agent collaboration or the Analyst/Critic prototype;
- agent wrappers or conversation-oriented orchestration;
- runtime creation of one workflow node per source or member;
- unbounded parallel tasks or model calls; or
- streaming progress over a public API.

E5 and E6 retain their existing evidence gates. Persistence, receipt matching, and external claim submission remain separate future decisions.

## Consequences

- E2 establishes the reusable route before capture depends on it.
- E3 exercises MAF branching, sub-workflows, bounded fan-out/fan-in, concurrent execution, events, and topology inspection for concrete application requirements.
- E4 adds expense-report business behaviour without inventing batch-only processing.
- Worker-lane counts become explicit configuration and test inputs.
- Parallel speed-up is bounded by the slowest lane and the MAF superstep barrier; E3 must compare it with the sequential baseline.
- Deterministic aggregation remains independently unit-testable and does not depend on model judgement or implicit framework collection semantics.
