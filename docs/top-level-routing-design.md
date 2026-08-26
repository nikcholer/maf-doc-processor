# Top-Level Document Routing Design

## The Problem This Change Will Solve

The application already uses Microsoft Agent Framework (MAF) workflows to process receipts, shopping lists, and Sujiko puzzles. However, the decision about which workflow to use still happens outside MAF.

Today the application follows these steps:

1. Prepare a smaller copy of the uploaded image for classification.
2. Ask the classification model what kind of document it is.
3. Use a normal C# `switch` statement to choose a document workflow.
4. Start a separate MAF workflow for the chosen document type.

This works, but MAF can see only the processing steps after the `switch`. It cannot show or report the complete journey from classification to the selected workflow. That becomes more important when composite capture needs to send many detected documents through the same reusable route.

E2 will move classification and routing into one top-level MAF workflow without changing what the API returns or how many model calls it makes.

## Proposed Shape

The top-level workflow will look like this:

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

`WorkflowBuilder`, `AddEdge<T>`, `BindAsExecutor`, `InProcessExecution`, and workflow events are MAF APIs. `DocumentProcessingWorkflow`, `ClassifiedDocument`, the document categories, and all receipt, shopping-list, Sujiko, and unsupported behaviour belong to this project.

## Selected MAF Pattern

The pinned `Microsoft.Agents.AI.Workflows` 1.19.0 package directly supports the required shape:

- `AddEdge<T>(source, target, condition, label)` adds a typed connection that is followed only when its condition is true.
- `Workflow.BindAsExecutor(id)` places a complete child workflow behind one executor-shaped node in the parent graph.
- `WithOutputFrom(...)` allows every valid final branch to produce the top-level workflow result.

The selected implementation will therefore:

1. Use the project-owned `DocumentClassificationExecutor`, which is now implemented and tested but not yet connected to the production path. It prepares the classification image, calls `IDocumentClassifier` once, records classification usage and metadata, prepares the extraction image when needed, and produces a `ClassifiedDocument`.
2. Use `DocumentWorkflowFactory`, which now builds each existing document graph through a reusable project-owned method. These methods connect the same extraction, validation, repair, policy, and result executors used by the current path.
3. Bind the receipt, shopping-list, and Sujiko workflows as child workflow executors in the top-level graph.
4. Use the project-owned `UnsupportedDocumentResultExecutor`, which is now implemented and tested but not yet a destination in the production graph, for `Invoice` and `Unknown` classifications.
5. Connect classification to those four destinations using labelled, typed conditional edges.
6. Run the top-level workflow once and obtain the final `DocumentProcessingResult` from its output event.

The compatibility test `DocumentCategoryRoute_UsesExactlyOneWorkflowDestination` proves this pattern against MAF 1.19.0 without changing the application path. It covers every category currently defined by `DocumentCategory`, confirms that exactly one outer destination completes, confirms that child workflow events reach the parent run, and confirms that all destinations appear in Mermaid and DOT visualizations.

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

The production topology tests must therefore prove both rules:

1. Every defined document category reaches a destination.
2. Every defined document category reaches exactly one destination.

The outer workflow visualization will show each child workflow as one named node. The internal receipt, shopping-list, and Sujiko graphs remain separately inspectable; binding them does not flatten every internal step into the parent diagram.

The top-level workflow should be built for each processing run initially. The current extraction and repair executors can link a request cancellation token supplied at construction time with the token supplied by MAF during execution. Keeping the same lifetime avoids an unrelated cancellation refactor during routing migration. A later cleanup may remove the captured token if tests prove that the execution token alone covers every path.

## Alternatives Considered

### Keep the C# `switch`

This is the current working implementation, but it leaves classification and routing outside the graph. MAF cannot then visualize or emit workflow events for the complete route, and composite capture would have to call an application-level dispatcher rather than a reusable top-level workflow.

### Wrap methods that start separate workflows

A parent executor could call the existing `RunReceiptWorkflowAsync`-style methods. That would preserve behaviour, but the child workflow would be hidden inside ordinary project code rather than composed through MAF. `BindAsExecutor` exists specifically to represent the child workflow in the parent graph, so a wrapper would add indirection without a benefit.

### Flatten every document step into one large graph

This would make every executor visible in one diagram, but it would mix routing with the internal details of every document type. Reusable child workflows give the top-level graph a clearer job: select one document process. They also provide the boundary that composite capture will need when it sends each detected document through the same route.

### Use `AddSwitch`

MAF 1.19.0 also provides an `AddSwitch` builder. The project already selected typed conditional edges, and the route has a small fixed set of clearly named category checks. `AddEdge<ClassifiedDocument>` keeps the message type and each labelled destination visible at the point where the graph is built. The required exclusivity and coverage tests are still necessary whichever conditional builder is used.

## Delivery Sequence After This Proof

The production work should remain split into reviewable changes:

1. **Completed:** extract `DocumentWorkflowFactory` builders for the existing document workflows without changing routing.
2. **Completed:** add the classification and unsupported-result executors with focused tests, without enabling the new route.
3. Build and enable the top-level graph with conditional edges and bound child workflows.
4. Add full topology, cancellation, event, golden-set, API contract, and model-call-count regression coverage.
5. Update the code-path guides to describe the implemented graph rather than this proposed design.

This sequence keeps behaviour-preserving refactoring separate from the point where application routing changes.
