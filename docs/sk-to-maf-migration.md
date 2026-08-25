# Semantic Kernel to MAF Migration Strategy

## Objective

This repository is the Microsoft Agent Framework successor to the original Semantic Kernel document processor. The migration has two objectives:

1. Preserve the proven document-processing behaviour and contracts of the Semantic Kernel implementation.
2. Establish an architecture that can adopt richer orchestration where document workflows require it.

The original implementation remains the behavioural baseline. This makes compatibility explicit while allowing the MAF version to evolve independently rather than carrying forward every earlier implementation detail.

## Phase 1: Establish the Semantic Kernel Baseline

Inventory the original application before changing its architecture:

- Record its API, configuration, prompts, extracted data, validation, policy, model usage, and tests.
- Confirm the build and test baseline.
- Separate behaviour that must survive the migration from implementation details that may change.

The result of this phase is captured in the [V1 Semantic Kernel inventory](v1-semantic-kernel-inventory.md).

## Phase 2: Preserve Behaviour in a MAF Vertical Slice

Recreate one complete document path in MAF while deliberately keeping its initial scope small. Receipts provide the reference slice from upload through classification, extraction, validation, policy, and response mapping.

This phase establishes the core MAF workflow model:

- Typed executors and hand-off records.
- Explicit workflow construction with executors and edges.
- In-process execution and workflow events.
- Clear separation between model-backed and deterministic stages.
- Tests focused on observable behaviour rather than internal class structure.

Not every component needs to become a MAF abstraction. ASP.NET Core hosting, provider integration, parsing, validation, and business policy remain ordinary application code where that is the clearer design.

## Phase 3: Consolidate the Architecture

Document and harden the resulting application so its architectural boundaries are explicit:

- Behaviour retained from the Semantic Kernel implementation.
- MAF types and runtime responsibilities.
- Application-owned abstractions and policy decisions.
- Initial design constraints, such as classification occurring before the document-specific graph.
- Capabilities intentionally deferred because the current foreground workflow does not require them.

The [completed slice guide](slice-guide.md), [technical process flow](technical-process-flow.md), tests, and decision documents provide this architectural record.

## Phase 4: Extend the Workflow Model

The next document type should be chosen for both its application value and its orchestration requirements. It should extend the processor in a credible way while providing a reason to use more of MAF than the initial linear slices require.

Potential capabilities include conditional routing, sub-workflows, parallel fan-out and aggregation, richer workflow events, external input, checkpointing, or agent collaboration. The document scenario and required capabilities will be agreed before implementation and converted into a focused backlog.

The governing question for this phase is:

> What can the next workflow express more clearly or robustly because it is built on MAF?

## Phase 5: Validate the Architecture

Assess the extended workflow against the simpler baseline:

- Does each additional MAF capability solve a concrete workflow requirement?
- Is the graph easier to understand, test, and observe?
- What additional complexity, latency, state, or operational responsibility does it introduce?
- Which patterns should become application conventions, and which should remain optional?

Framework features are not goals by themselves. They should be retained where they improve the application and avoided where straightforward C# remains the more reliable design.

## Current Position

The Semantic Kernel baseline, initial MAF migration, completed document slices, and supporting documentation are in place. The next step is to select the Phase 4 document scenario and define its architectural objectives before creating the implementation backlog.
