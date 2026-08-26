# Beginner's Guide: Following One Document Slice

This guide follows a receipt from an HTTP upload to the final JSON response. Receipts are the best slice to learn first because they use every important pattern in the project: classification, MAF executors, typed hand-offs, validation, bounded repair, policy evaluation, result mapping, and tests.

You do not need to understand the whole repository before starting. Read the files in the order shown here and keep this question in mind:

> What type enters this stage, what work happens, and what type leaves it?

That question makes a MAF workflow much easier to understand.

## Know Which Layer You Are Looking At

This project targets modern **.NET 10** (`net10.0`). People sometimes still say “.NET Core,” but the product name has been simply **.NET** since .NET 5. **ASP.NET Core** remains the name of the web framework used by the API project.

The code path combines three main layers. Their namespaces are the quickest way to tell them apart:

| Layer | Namespace or source | Examples in this guide | What it contributes |
| --- | --- | --- | --- |
| .NET / C# | `System.*` | `Task`, `CancellationToken`, records, exceptions, LINQ | Language and runtime building blocks |
| ASP.NET Core and .NET extensions | `Microsoft.AspNetCore.*`, `Microsoft.Extensions.*` | `WebApplication`, `HttpRequest`, `IResult`, dependency injection, configuration, `ILogger` | HTTP hosting and general application infrastructure |
| Microsoft Agent Framework (MAF) | `Microsoft.Agents.AI.Workflows` from the `Microsoft.Agents.AI.Workflows` NuGet package | `Executor<TInput,TOutput>`, `IWorkflowContext`, `WorkflowBuilder`, `AddEdge`, `InProcessExecution`, workflow events | The typed workflow graph and its execution model |
| OpenAI .NET SDK | `OpenAI`, `OpenAI.Chat` from the `OpenAI` NuGet package | `ChatClient` inside `OpenAICompatibleModelChatClient` | The OpenAI-compatible HTTP/model client used with TogetherAI |
| ImageSharp | `SixLabors.ImageSharp.*` | Image loading, orientation, resizing, and JPEG encoding | Model-image preprocessing |
| This project | namespaces beginning `MafDocumentProcessor` | `FileRequest`, `IModelChatClient`, `DocumentProcessingWorkflow`, receipt executors and result records | Document-processing decisions and the glue between the other layers |

The project references MAF packages in `src/MafDocumentProcessor/MafDocumentProcessor.csproj`. The workflow API used by this slice is imported in source files with:

```csharp
using Microsoft.Agents.AI.Workflows;
```

That import is why code can say `WorkflowBuilder` rather than the fully qualified `Microsoft.Agents.AI.Workflows.WorkflowBuilder`.

Two names are especially easy to misclassify:

- `IModelChatClient` is a **project-owned interface** in `MafDocumentProcessor.Services`; it is not a MAF interface.
- `DocumentProcessingWorkflow` and all `Receipt...Executor` classes are **project-owned types**. They use MAF base types and APIs, but their document behavior belongs to this repository.

When unsure about a symbol, check the file's `using` directives or use “Go to Definition” in the IDE. A `Microsoft.Agents.AI.Workflows` definition is MAF; a `MafDocumentProcessor.*` definition is local project code.

## The Path at a Glance

```text
Browser or API client
  -> POST /api/documents/process
  -> validate and buffer the uploaded image
  -> create FileRequest
  -> start the top-level MAF workflow
  -> DocumentClassificationExecutor
     -> preprocess a classification image
     -> classify the document
     -> preprocess an extraction image
  -> labelled Receipt edge
  -> bound receipt child workflow
     -> ReceiptExtractionExecutor
     -> ReceiptValidationExecutor
     -> ReceiptValidationRepairExecutor
     -> ReceiptPolicyExecutor
     -> ReceiptResultExecutor
  -> DocumentProcessingResult
  -> map to DocumentProcessingResponse
  -> JSON response
```

Only classification and extraction call a model in the normal receipt path. Validation, routing, policy checks, and response construction are ordinary deterministic C#.

## 1. Start at Dependency Registration

Open `src/MafDocumentProcessor.Api/Program.cs`.

This is the application's composition root: the place where concrete implementations are connected to interfaces. `WebApplication`, `builder.Services`, and the dependency-injection lifetimes (`AddSingleton`, `AddScoped`) are **ASP.NET Core/.NET infrastructure**, not MAF. Look for these registrations:

- `IModelChatClient` -> `OpenAICompatibleModelChatClient`
- `IDocumentClassifier` -> `ModelDocumentClassifier`
- `IReceiptExtractor` -> `ModelReceiptExtractor`
- `IModelImagePreprocessor` -> `ModelImagePreprocessor`
- `DocumentProcessingWorkflow`

The API endpoint asks dependency injection for `DocumentProcessingWorkflow`; it does not construct model clients or extractors itself. The workflow in turn receives the classifier, extractors, policy options, image preprocessor, and logger that it needs.

This separation is why tests can replace real model-backed services with small fakes.

The same file also maps the two HTTP entry points:

- `/health`
- `/api/documents/process`

## 2. Follow the HTTP Request into the Domain

Open `src/MafDocumentProcessor.Api/Endpoints/DocumentProcessingEndpoints.cs` and find `ProcessDocumentAsync`.

Before MAF is involved, the endpoint performs normal web-application work:

1. Check that the request is multipart form data.
2. Read the image from the configured `image` form field.
3. Validate its filename, content type, and size.
4. Check that the required model API keys are available.
5. Buffer the uploaded image.
6. Normalize the optional `sourceId`.
7. Create a `FileRequest`.
8. Call `DocumentProcessingWorkflow.RunAsync`.

`FileRequest`, in `src/MafDocumentProcessor/Domain/FileRequest.cs`, is the boundary between the API layer and the workflow library. It carries the image bytes and metadata needed by later stages:

- content
- filename
- content type
- file size
- upload time
- optional source ID

This is an important architectural boundary. Everything after `FileRequest` can be tested without constructing an HTTP request.

The endpoint also converts known exceptions into the API error contract. A normal unsupported document is different: it completes the workflow and returns a regular response with `IsSuccess=false`.

## 3. Classification Starts the Top-Level MAF Graph

Open `src/MafDocumentProcessor/Workflow/DocumentProcessingWorkflow.cs` and begin at `RunAsync`.

`RunAsync` asks the project-owned `DocumentWorkflowFactory` to build one request-scoped top-level MAF graph. It then runs that graph once, passing the original `FileRequest` as its input.

The graph's first node is the project-owned `DocumentClassificationExecutor`. Because it inherits MAF's `Executor<FileRequest, ClassifiedDocument>`, MAF knows that it accepts a `FileRequest` and produces a `ClassifiedDocument`.

The executor first prepares a smaller image for classification by calling the image preprocessor with `ModelImagePreprocessingPurpose.Classification`. It then calls `IDocumentClassifier.ClassifyAsync`.

The concrete classifier is `ModelDocumentClassifier` in `src/MafDocumentProcessor/Services/ModelDocumentClassifier.cs`. It:

1. Builds the classification prompt and image message.
2. Sends a `ModelChatRequest` through `IModelChatClient`.
3. Parses the model response into `DocumentClassification`.
4. Returns `ModelResult<DocumentClassification>`, which keeps the parsed value and that call's token/cost/latency usage together.

Its typed answer decides which destination should run:

```csharp
DocumentCategory.Receipt      -> receipt workflow
DocumentCategory.ShoppingList -> shopping-list workflow
DocumentCategory.SujikoPuzzle -> Sujiko workflow
anything else                 -> unsupported result
```

The selection is expressed with MAF's labelled conditional `AddEdge<ClassifiedDocument>` connections. The category checks and the rule that exactly one destination must match are **project decisions**; `AddEdge` and conditional graph execution are **MAF features**.

For a supported category, the classification executor preprocesses the original image again using `ModelImagePreprocessingPurpose.Extraction` before returning its typed message. Classification can use a smaller image, while extraction retains more detail. Invoice and Unknown skip this second preparation because no extractor will run.

It then creates `ClassifiedDocument`, found in `src/MafDocumentProcessor/Workflow/ClassifiedDocument.cs`. This record packages the extraction-ready request, original metadata, classification, classification usage, and original request for the selected slice.

The executor also emits project-owned `DocumentClassifiedEvent` and `DocumentRouteSelectedEvent` records. They make the category, destination, filename, and source ID visible in the same MAF event stream as the selected child workflow.

`UnsupportedDocumentResultExecutor` is the fourth destination. It is a **project-owned MAF executor** that produces the normal unsupported response for `Invoice` and `Unknown` without calling an extraction model.

## 4. See How the Receipt Graph Is Built

Follow `DocumentProcessingWorkflow.RunAsync` to `DocumentWorkflowFactory.BuildDocumentRoutingWorkflow`, then open `src/MafDocumentProcessor/Workflow/DocumentWorkflowFactory.cs`.

`DocumentWorkflowFactory` is a **project-owned** class. Its top-level builder creates the classification and unsupported executors, builds the three document-specific workflows, and uses MAF's `BindAsExecutor` to place each complete child workflow behind one parent-graph node.

The top-level graph connects classification to its four destinations with labelled conditional edges. `WithOutputFrom` declares that any one of those destinations can produce the final result. Production topology tests prove that every defined category matches exactly one edge.

The same factory provides one reusable builder method for each supported document type. The receipt method constructs five project-owned executors and connects them with MAF's `Microsoft.Agents.AI.Workflows.WorkflowBuilder`. `WorkflowBuilder`, `AddEdge`, `WithOutputFrom`, `BindAsExecutor`, and `Build` all come from the **MAF Workflows NuGet package**. The inner receipt graph remains:

```csharp
var workflow = new WorkflowBuilder(extractionExecutor)
    .AddEdge(extractionExecutor, validationExecutor)
    .AddEdge(validationExecutor, repairExecutor)
    .AddEdge(repairExecutor, policyExecutor)
    .AddEdge(policyExecutor, resultExecutor)
    .WithOutputFrom(resultExecutor)
    .Build();
```

In MAF, `AddEdge(source, target)` adds a directed connection to the workflow graph. When the source executor produces its output, MAF delivers that message to the target executor. The compiler-visible generic types on the executors make these hand-offs easier to reason about than an untyped bag of workflow state.

The executor instances and their document rules are project code. The graph builder and edge semantics are framework code.

Keeping graph construction in `DocumentWorkflowFactory` means the receipt workflow can be tested and visualized on its own, while the running application reuses it as a child of the top-level route. There is no application-level category `switch` around separate workflow runs.

The graph is linear, but it is still useful MAF practice: execution, events, typed stages, and workflow output are all real framework behavior. The repair executor contains the conditional decision about whether a second extraction is necessary.

## 5. Walk Through Each Receipt Executor

Read these files in this order.

### 5.1 `ReceiptExtractionExecutor`

File: `src/MafDocumentProcessor/Workflow/ReceiptExtractionExecutor.cs`

Input: `ClassifiedDocument`  
Output: `ReceiptExtraction`

This class inherits MAF's `Executor<ClassifiedDocument, ReceiptExtraction>` and overrides its `HandleAsync` method. The base class, `HandleAsync` workflow contract, and `IWorkflowContext` parameter are MAF. The receipt category check and extraction call are project behavior.

The executor checks that the routed category is actually `Receipt`, calls `IReceiptExtractor.ExtractReceiptAsync`, and combines the extracted receipt with the classification context.

The concrete extractor is `ModelReceiptExtractor` in `src/MafDocumentProcessor/Services/ModelReceiptExtractor.cs`. Like the classifier, it builds a model request, calls `IModelChatClient`, parses structured JSON, and returns the value together with model usage.

The parsed domain type is `ReceiptData` in `src/MafDocumentProcessor/Domain/ReceiptData.cs`.

MAF lesson: an executor should do one stage's work and emit a clear type for the next stage. It should not also apply receipt policy or build the API response.

### 5.2 `ReceiptValidationExecutor`

File: `src/MafDocumentProcessor/Workflow/ReceiptValidationExecutor.cs`

Input: `ReceiptExtraction`  
Output: `ValidatedReceiptExtraction`

This is deterministic code. It checks whether the model-produced receipt is structurally usable, including the store name, non-negative total, and currency format.

The validation result is data, not an exception. That lets the workflow decide what to do next.

MAF lesson: model output should be treated as untrusted input. Structured JSON parsing is necessary, but domain validation still belongs in a separate stage.

### 5.3 `ReceiptValidationRepairExecutor`

File: `src/MafDocumentProcessor/Workflow/ReceiptValidationRepairExecutor.cs`

Input: `ValidatedReceiptExtraction`  
Output: `ValidatedReceiptExtraction`

If validation succeeded, this executor passes the message onward without another model call. If validation failed, it makes one bounded re-extraction attempt and supplies the validation reasons as repair instructions.

It then validates the repaired value again and includes both extraction calls in accumulated model usage.

The important word is **bounded**. The workflow does not keep asking the model until it happens to return something acceptable. One repair attempt gives a useful recovery path without creating an uncontrolled loop in latency or cost.

MAF lesson: a workflow stage can make a local conditional decision without turning the entire graph into a branch. More complex alternatives could instead be expressed with conditional edges.

### 5.4 `ReceiptPolicyExecutor`

File: `src/MafDocumentProcessor/Workflow/ReceiptPolicyExecutor.cs`

Input: `ValidatedReceiptExtraction`  
Output: `ReceiptPolicyEvaluation`

Policy is deliberately separate from structural validation. A receipt can be structurally valid and still need human review because, for example:

- its total is above the configured review threshold; or
- its payment method is missing.

This executor uses `ReceiptPolicyOptions` from configuration and produces `ReceiptPolicyResult`.

MAF lesson: separating validation from business policy makes both rules easier to test and change. “Can this data be used?” and “May this pass automatically?” are different questions.

### 5.5 `ReceiptResultExecutor`

File: `src/MafDocumentProcessor/Workflow/ReceiptResultExecutor.cs`

Input: `ReceiptPolicyEvaluation`  
Output: `DocumentProcessingResult`

The final executor converts the receipt-specific intermediate state into the shared result used by every document type. It brings together:

- classification and metadata
- receipt data
- validation
- receipt policy
- model usage from classification, extraction, and any repair call
- human-review status and reasons
- success, errors, and warnings

For receipts, a parsed receipt can still be a successful processing result while carrying validation or policy warnings. See `docs/document-result-semantics.md` for the deliberate differences between document types.

MAF lesson: have one explicit output stage. Downstream callers should receive a stable result rather than needing to understand all internal workflow records.

## 6. Understand the Intermediate Records

The small records in `src/MafDocumentProcessor/Workflow` are the typed envelopes passed between executors:

- `ClassifiedDocument`
- `ReceiptExtraction`
- `ValidatedReceiptExtraction`
- `ReceiptPolicyEvaluation`

They may initially look like extra ceremony, but they provide useful guarantees:

- A policy executor cannot accidentally run before validation because it requires `ValidatedReceiptExtraction`.
- Context such as metadata and usage moves with the receipt rather than living in global mutable state.
- Each stage can be tested with a directly constructed input.
- Adding a field to a hand-off is an explicit code change rather than a hidden dictionary convention.

When learning a new slice, inspect these records before studying every line inside the executors. They reveal the intended data flow quickly.

## 7. See How MAF Returns the Result

Back in `DocumentProcessingWorkflow.RunWorkflowAsync`, MAF's `Microsoft.Agents.AI.Workflows.InProcessExecution.RunAsync` executes the complete top-level workflow in the current process.

MAF defines and returns the workflow event stream, including `WorkflowErrorEvent` and `WorkflowOutputEvent`. This project's surrounding method interprets those framework events as follows:

1. Logs the emitted event types from the parent and selected child.
2. Looks for a `WorkflowErrorEvent` and rethrows its underlying exception.
3. Finds the last `WorkflowOutputEvent` containing `DocumentProcessingResult`.
4. Returns that result to the API endpoint.

This event-oriented boundary is worth noticing. The workflow is not invoked like a normal function that directly returns the last executor's value; the execution produces events, one of which carries the declared workflow output.

`InProcessExecution`, `WorkflowErrorEvent`, and `WorkflowOutputEvent` are MAF types. The choice to log them, unwrap errors, select a `DocumentProcessingResult`, and run request-scoped without durability is project-level policy. The reasons for the durability decision are in `docs/durability-decision.md`.

## 8. Map the Domain Result Back to HTTP JSON

The API endpoint passes the workflow result to `DocumentProcessingResponseMapper` in:

`src/MafDocumentProcessor.Api/Services/DocumentProcessingResponseMapper.cs`

The mapper selects the correct document data for the returned category and constructs `DocumentProcessingResponse`. Keeping this mapping in the API project prevents HTTP response concerns from leaking into the workflow library.

The response exposes the classification, metadata, model usage, human-review state, typed document data at runtime, errors, and warnings. The browser code in `src/MafDocumentProcessor.Api/wwwroot/app.js` renders that JSON for the local demo.

## 9. Read the Tests as Executable Examples

After following the production path, open these tests.

### Workflow behavior

`tests/MafDocumentProcessor.Tests/ReceiptProcessingWorkflowTests.cs`

Start with:

- `RunAsync_ProcessesReceiptEndToEnd`
- `RunAsync_ReExtractsReceiptOnceWhenValidationFails`
- `RunAsync_FlagsReceiptForReviewWhenPaymentMethodIsMissing`
- `RunAsync_RecommendsReviewForLowConfidenceSupportedClassification`
- `RunAsync_ReturnsHumanUnsupportedMessageForInvoice`

These tests construct the workflow with fake classifier/extractor implementations. They are the fastest way to see expected results without making provider calls.

For the outer graph itself, read `tests/MafDocumentProcessor.Tests/DocumentWorkflowFactoryTests.cs`. Its routing theory runs every `DocumentCategory`, checks that exactly one parent destination completes, confirms the classification and route events, and verifies that unsupported documents do not make an extraction call.

### Model boundary

`tests/MafDocumentProcessor.Tests/ModelDocumentServicesTests.cs`

Look for the classification and receipt-extraction tests. They show the messages sent to `IModelChatClient`, parsing behavior, model-role selection, and repair instructions.

### Full HTTP boundary

`tests/MafDocumentProcessor.Tests/ApiIntegrationTests.cs`

`ProcessDocument_WithReceiptImage_ReturnsMappedResponse` starts at the HTTP endpoint while replacing model-backed services. It proves that multipart input, dependency injection, workflow execution, and response mapping work together.

## 10. A Practical Debugging Order

When a receipt produces an unexpected result, debug from the outside inward:

1. Check the API response's `traceId`, errors, warnings, and model usage.
2. Confirm upload validation and `FileRequest` creation in the endpoint.
3. Inspect the classification category and confidence.
4. Check whether the extraction model returned the expected receipt fields.
5. Inspect validation reasons and whether the repair executor made a second call.
6. Inspect the policy decision separately from validation.
7. Confirm `ReceiptResultExecutor` placed reasons in the intended errors or warnings collection.
8. Confirm the API mapper selected receipt data.

The logs use the HTTP trace/correlation scope, workflow names, executor events, and model operation names to help connect these stages.

## 11. Where to Make Different Kinds of Changes

Use this rule of thumb:

| Change | First place to look |
| --- | --- |
| Upload limits or accepted image types | `appsettings.json`, `DocumentIntakeSettings`, `DocumentImageValidator` |
| Classification prompt or parsing | `ModelDocumentClassifier`, `ModelResponseParsers` |
| Receipt extraction prompt or parsing | `ModelReceiptExtractor`, `ModelResponseParsers` |
| Required receipt structure | `ReceiptValidationExecutor` |
| Retry after invalid extracted fields | `ReceiptValidationRepairExecutor` |
| Review threshold or payment rule | `ReceiptPolicyOptions`, `ReceiptPolicyExecutor` |
| Shared result semantics | `ReceiptResultExecutor`, `DocumentProcessingResult` |
| HTTP response shape | API contracts and `DocumentProcessingResponseMapper` |
| Top-level routing topology | `BuildDocumentRoutingWorkflow` in `DocumentWorkflowFactory` |
| Receipt child-workflow topology | `BuildReceiptWorkflow` in `DocumentWorkflowFactory` |

## 12. Compare the Other Completed Slices

Once the receipt path is clear, compare its files with the shopping-list and Sujiko equivalents in `src/MafDocumentProcessor/Workflow`.

All three share the same broad pattern:

```text
extract -> validate -> optionally repair -> construct shared result
```

The differences are instructive:

- Shopping lists have their own data model and validation rules, but no receipt policy stage.
- Sujiko extracts quadrant totals and given cells, validates the starting state, and currently stops before solving the puzzle.
- Each result executor applies the success/error semantics appropriate to that document type.

This repetition is intentional. It gives each document type a readable vertical slice while preserving shared classification, model transport, preprocessing, telemetry, and API infrastructure.

## Suggested Learning Exercises

Try these in order:

1. Put a breakpoint in each receipt executor and process one receipt through the UI.
2. Run `RunAsync_ProcessesReceiptEndToEnd` and inspect every intermediate record.
3. Run the repair test and watch model usage grow by one extraction call.
4. Change `ReceiptPolicy:ReviewThreshold` locally and observe that validation is unchanged while policy changes.
5. Add a harmless receipt warning with a test first, placing the rule in the stage where it belongs.
6. Draw the shopping-list graph from its executor generic types without reading `WorkflowBuilder`, then verify it against the code.

After this guide, use `docs/technical-process-flow.md` for a more complete architectural reference and `docs/adding-document-types.md` when you are ready to build a new slice.
