# Top-Level Document Routing Design

## The Problem This Change Solves

The application already used Microsoft Agent Framework (MAF) workflows to process receipts, shopping lists, and Sujiko puzzles. Before E2, however, the decision about which workflow to use happened outside MAF. E4 later added expense reports through the same routing pattern.

Before E2, the application followed these steps:

1. Prepare a smaller copy of the uploaded image for classification.
2. Ask the classification model what kind of document it is.
3. Use a normal C# `switch` statement to choose a document workflow.
4. Start a separate MAF workflow for the chosen document type.

That worked, but MAF could see only the processing steps after the `switch`. It could not show or report the complete journey from classification to the selected workflow. That becomes more important when composite capture needs to send many detected documents through the same reusable route.

The implemented E2 route moves classification and routing into one top-level MAF workflow without changing what the API returns or how many model calls it makes.

## Implemented Shape

The production top-level workflow looks like this:

```text
Uploaded image
      |
      v
Classify document once
      |
      +-- Receipt ----------> Receipt workflow
      |
      +-- Shopping list ----> Shopping-list workflow
      |
      +-- Sujiko puzzle ----> Sujiko workflow
      |
      +-- Expense report --> Expense-report workflow
      |
      +-- Invoice/Unknown --> Unsupported-document result
```

Only one branch should run for each document. The selected child workflow will continue to perform extraction, validation, one possible repair, policy where applicable, and result construction exactly as it does now.

## The Framework Terms

Three MAF terms are needed to describe the implementation:

| Term | Plain-language meaning | Source |
| --- | --- | --- |
| Executor | One step that receives a typed message and produces another typed message | `Microsoft.Agents.AI.Workflows` |
| Edge | A connection that carries a message from one executor to another | `Microsoft.Agents.AI.Workflows` |
| Child workflow | A complete workflow placed behind one node in a larger workflow | MAF calls `BindAsExecutor` on a `Workflow` |

`WorkflowBuilder`, `AddEdge<T>`, `BindAsExecutor`, `InProcessExecution`, and workflow events are MAF APIs. `DocumentProcessingWorkflow`, `ClassifiedDocument`, the document categories, and all receipt, shopping-list, Sujiko, expense-report, and unsupported behaviour belong to this project.

## Selected MAF Pattern

The pinned `Microsoft.Agents.AI.Workflows` 1.19.0 package directly supports the required shape:

- `AddEdge<T>(source, target, condition, label)` adds a typed connection that is followed only when its condition is true.
- `Workflow.BindAsExecutor(id)` places a complete child workflow behind one executor-shaped node in the parent graph.
- `WithOutputFrom(...)` allows every valid final branch to produce the top-level workflow result.

The production implementation:

1. Starts with the project-owned `DocumentClassificationExecutor`. It prepares the classification image, calls `IDocumentClassifier` once, records classification usage and metadata, prepares the extraction image when needed, and produces a `ClassifiedDocument`.
2. Uses `DocumentWorkflowFactory` to build each existing document graph through a reusable project-owned method. These methods connect the existing extraction, validation, repair, policy, and result executors.
3. Binds the receipt, shopping-list, Sujiko, and expense-report workflows as child workflow executors in the top-level graph.
4. Uses the project-owned `UnsupportedDocumentResultExecutor` for `Invoice` and `Unknown` classifications.
5. Connects classification to those five destinations using labelled, typed conditional edges.
6. Runs the top-level workflow once and obtains the final `DocumentProcessingResult` from its output event.

The compatibility test `DocumentCategoryRoute_UsesExactlyOneWorkflowDestination` proves the underlying composition pattern against MAF 1.19.0. The production test `BuildDocumentRoutingWorkflow_UsesExactlyOneDestinationAndPreservesContext` then runs the real graph for every current category. Together they confirm that exactly one outer destination completes, child workflow events reach the parent run, route metadata and model calls remain correct, and all destinations appear in Mermaid and DOT visualizations.

`DocumentClassificationExecutor` also emits project-owned `DocumentClassifiedEvent` and `DocumentRouteSelectedEvent` records. MAF's normal `ExecutorCompletedEvent` identifies the selected bound child when it completes. The filename and optional source ID carried by the custom events preserve request correlation within the surrounding HTTP logging scope.

## What Must Stay the Same

Moving the route into MAF is an architectural change, not a product change.

- Classification still happens once before extraction.
- Only the selected document extractor is called.
- A repair call remains possible only when the first extraction fails deterministic validation.
- Classification and extraction continue to use their separately configured model roles.
- Classification confidence, file metadata, model usage, estimated cost, cancellation, errors, warnings, policy, and human-review state must reach the same API fields.
- `Invoice` and `Unknown` continue to return the current normal unsupported-document response.
- The individual upload endpoint and its response contract do not change.

The existing API contract tests and golden set will detect changes to these behaviours.

## Important Limits and Required Tests

MAF runs every conditional edge whose condition is true. It does not understand the business meaning of `DocumentCategory`, so it cannot prove that the project's conditions are mutually exclusive or that every possible category is covered.

The production topology tests therefore prove both rules:

1. Every defined document category reaches a destination.
2. Every defined document category reaches exactly one destination.

The outer workflow visualization will show each child workflow as one named node. The internal receipt, shopping-list, Sujiko, and expense-report graphs remain separately inspectable; binding them does not flatten every internal step into the parent diagram.

The top-level workflow is built for each processing run. The extraction and repair executors link the request cancellation token supplied at construction time with the token supplied by MAF during execution. Keeping the same lifetime avoids an unrelated cancellation refactor during routing migration. A later cleanup may remove the captured token if tests prove that the execution token alone covers every path.

## Alternatives Considered

### Keep the C# `switch`

This was the working implementation before E2, but it left classification and routing outside the graph. MAF could not visualize or emit workflow events for the complete route, and composite capture would have needed an application-level dispatcher rather than a reusable top-level workflow.

### Wrap methods that start separate workflows

A parent executor could call the existing `RunReceiptWorkflowAsync`-style methods. That would preserve behaviour, but the child workflow would be hidden inside ordinary project code rather than composed through MAF. `BindAsExecutor` exists specifically to represent the child workflow in the parent graph, so a wrapper would add indirection without a benefit.

### Flatten every document step into one large graph

This would make every executor visible in one diagram, but it would mix routing with the internal details of every document type. Reusable child workflows give the top-level graph a clearer job: select one document process. They also provide the boundary that composite capture will need when it sends each detected document through the same route.

### Use `AddSwitch`

MAF 1.19.0 also provides an `AddSwitch` builder. The project already selected typed conditional edges, and the route has a small fixed set of clearly named category checks. `AddEdge<ClassifiedDocument>` keeps the message type and each labelled destination visible at the point where the graph is built. The required exclusivity and coverage tests are still necessary whichever conditional builder is used.

## Delivery Sequence

The production work was split into reviewable changes:

1. **Completed:** extract `DocumentWorkflowFactory` builders for the existing document workflows without changing routing.
2. **Completed:** add the classification and unsupported-result executors with focused tests, without enabling the new route.
3. **Completed:** build and enable the top-level graph with conditional edges and bound child workflows.
4. **Completed:** add full topology, cancellation, event, golden-set, API contract, and model-call-count regression coverage.
5. **Completed:** update the code-path guides to describe the implemented graph.

This sequence kept behaviour-preserving refactoring separate from the point where application routing changed.
