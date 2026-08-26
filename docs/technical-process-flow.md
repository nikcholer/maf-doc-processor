# Technical Process Flow

This document traces what happens when an image is submitted to the local demo API, where Microsoft Agent Framework is used, and where the main objects live in the codebase.

## High-Level Flow

```text
browser upload
  -> ASP.NET Minimal API endpoint
  -> upload validation and buffering
  -> image preprocessing for classification
  -> model classification
  -> route to document-specific MAF workflow
  -> image preprocessing for extraction
  -> extract typed data
  -> validate typed data
  -> optional one-shot repair extraction
  -> optional policy/review evaluation
  -> DocumentProcessingResult
  -> API response mapper
  -> demo UI summary and raw JSON
```

## API Entry Point

The browser posts multipart form data to:

- `POST /api/documents/process`
- Endpoint code: `src/MafDocumentProcessor.Api/Endpoints/DocumentProcessingEndpoints.cs`
- Static UI: `src/MafDocumentProcessor.Api/wwwroot/`

The endpoint:

- checks the request is multipart;
- validates the uploaded image field, content type, extension, and size;
- checks required model API key environment variables are visible;
- buffers the image into a `FileRequest`;
- calls `DocumentProcessingWorkflow.RunAsync`;
- maps the domain result into `DocumentProcessingResponse`;
- converts known model/config/provider failures into the API error contract.

The API host wires services in:

- `src/MafDocumentProcessor.Api/Program.cs`

Important registrations:

- `IDocumentClassifier` -> `ModelDocumentClassifier`
- `IReceiptExtractor` -> `ModelReceiptExtractor`
- `IShoppingListExtractor` -> `ModelShoppingListExtractor`
- `ISujikoPuzzleExtractor` -> `ModelSujikoPuzzleExtractor`
- `DocumentProcessingWorkflow`
- `IModelChatClient` -> `OpenAICompatibleModelChatClient`

## Model Boundary

The app deliberately keeps a small local model abstraction:

- `IModelChatClient`: `src/MafDocumentProcessor/Services/IModelChatClient.cs`
- Request object: `ModelChatRequest`
- Message object: `ModelChatMessage`
- Response object: `ModelChatResponse`
- Usage object: `ModelTokenUsage`

The concrete implementation is:

- `src/MafDocumentProcessor/Services/OpenAICompatibleModelChatClient.cs`

That client is responsible for:

- reading provider API keys from environment variables;
- using OpenAI-compatible chat/image requests;
- sending TogetherAI-specific protocol options such as disabled Qwen thinking mode;
- retrying transient provider failures;
- collecting input/output/total tokens;
- estimating cost from configured pricing;
- timing each model call.

Model roles and pricing are configured in:

- `src/MafDocumentProcessor.Api/appsettings.json`
- `src/MafDocumentProcessor/Configuration/AiModelSettings.cs`
- `src/MafDocumentProcessor/Configuration/AiModelSettingsDefaults.cs`

## Classification

Classification happens before the MAF workflow graph.

Code:

- `DocumentProcessingWorkflow.RunAsync`
- `ModelDocumentClassifier`
- `ModelResponseParsers.ParseClassification`

Reason:

- the app needs to choose a document-specific workflow graph after classification;
- each supported type has different extraction, validation, result semantics, and optional policy behavior.

Current categories:

- `Receipt`
- `ShoppingList`
- `SujikoPuzzle`
- `Invoice` as recognized but unsupported
- `Unknown`

The classifier returns:

- `DocumentClassification.Category`
- optional confidence;
- confidence reasoning;
- human document-type description.

Unsupported categories still return a normal workflow result with `IsSuccess=false`, rather than an API/provider error.

## Image Preprocessing

Image preprocessing happens twice:

- once for classification;
- once for extraction.

Code:

- `IModelImagePreprocessor`
- `ModelImagePreprocessor`
- `ModelImagePreprocessingPurpose`
- `ModelImagePreprocessingSettings`

The original upload remains intact at intake. The model-facing image can be resized/downsampled per purpose to reduce latency and token/image cost.

## MAF Workflow Usage

The current production path uses Microsoft Agent Framework workflows for document-specific processing after classification.

Package:

- `Microsoft.Agents.AI.Workflows`

Main MAF concepts used:

- `Executor<TInput, TOutput>`: a workflow step with typed input and output.
- `WorkflowBuilder`: builds a graph by connecting executors with edges.
- `InProcessExecution.RunAsync`: runs the graph locally in-process.
- `WorkflowOutputEvent`: used to retrieve the final `DocumentProcessingResult`.
- `WorkflowErrorEvent`: used to unwrap and rethrow executor failures cleanly.

Shared orchestration code:

- `src/MafDocumentProcessor/Workflow/DocumentProcessingWorkflow.cs`
- `src/MafDocumentProcessor/Workflow/DocumentWorkflowFactory.cs`

`DocumentProcessingWorkflow` currently classifies the image, selects a route with a C# `switch`, runs the selected MAF graph, and interprets its output or error events. `DocumentWorkflowFactory` owns the reusable graph definitions for receipts, shopping lists, and Sujiko puzzles. It creates and connects the document-specific executors but does not classify documents or choose which graph to run.

The app currently uses local in-process workflows only. Durable pause/resume is deliberately deferred; see `docs/durability-decision.md`.

## Document-Specific Workflows

Each supported document type follows the same broad pattern:

```text
ClassifiedDocument
  -> extraction executor
  -> validation executor
  -> validation repair executor
  -> optional policy executor
  -> result executor
```

The three graphs are built by the project-owned `DocumentWorkflowFactory` using MAF's `WorkflowBuilder`. They can be executed on their own and are ready to be bound as child workflows when the top-level routing graph is introduced.

### Receipt

Files:

- `ReceiptExtractionExecutor`
- `ReceiptValidationExecutor`
- `ReceiptValidationRepairExecutor`
- `ReceiptPolicyExecutor`
- `ReceiptResultExecutor`
- `ReceiptExtraction`
- `ValidatedReceiptExtraction`
- `ReceiptPolicyEvaluation`

Behavior:

- extracts receipt fields;
- validates store name, total amount, and currency code;
- retries extraction once with validation reasons when validation fails;
- applies receipt review policy, such as review threshold and payment method checks;
- returns receipt data even when policy review is needed, using warnings/review state.

### Shopping List

Files:

- `ShoppingListExtractionExecutor`
- `ShoppingListValidationExecutor`
- `ShoppingListValidationRepairExecutor`
- `ShoppingListResultExecutor`
- `ShoppingListExtraction`
- `ValidatedShoppingListExtraction`

Behavior:

- extracts title, items, checked state, notes;
- validates that there is at least one readable item and no blank item names;
- retries extraction once with validation reasons when validation fails;
- returns `IsSuccess=false` if the list is still structurally invalid after repair.

### Sujiko Puzzle

Files:

- `SujikoPuzzleExtractionExecutor`
- `SujikoPuzzleValidationExecutor`
- `SujikoPuzzleValidationRepairExecutor`
- `SujikoPuzzleResultExecutor`
- `SujikoPuzzleExtraction`
- `ValidatedSujikoPuzzleExtraction`

Behavior:

- extracts four quadrant totals: `topLeft`, `topRight`, `bottomLeft`, `bottomRight`;
- extracts zero or more given cells as 1-based `row`, `column`, `value`;
- validates positive totals, row/column range, value range, duplicate locations, and duplicate given values;
- retries extraction once with validation reasons when validation fails;
- returns `IsSuccess=false` if the puzzle is still structurally invalid after repair.

The Sujiko extractor prompt includes explicit deskewing guidance because rotated newspaper photos can cause row/column mistakes. The rotated sample regression fixture lives under:

- `tests/MafDocumentProcessor.Tests/assets/IMG20260513194450.jpg`

## Validation Repair

Validation repair is a bounded model re-run, not an unbounded retry loop.

Pattern:

1. Extraction executor calls the relevant extractor once.
2. Validation executor validates the typed data.
3. Repair executor checks validation:
   - if valid, passes through;
   - if invalid, calls the same extractor once more with the validation reasons;
   - validates the repaired output.
4. Result executor includes all extraction model usages in `DocumentModelUsage`.

This means the UI cost/token totals naturally include repair attempts.

## Result Objects

The main domain result is:

- `DocumentProcessingResult`

It contains:

- category;
- metadata;
- classification;
- model usage;
- nullable document data for each supported type;
- optional receipt policy result;
- validation result;
- human review result;
- success flag;
- errors;
- warnings.

The API response is:

- `DocumentProcessingResponse`
- mapped by `DocumentProcessingResponseMapper`

The UI uses:

- summary metrics for category, decision/review state, token count, model time, estimated cost;
- extracted data rows;
- review reasons;
- raw JSON.

## Human Review State

Human review is represented as a quality/ownership state, not an API failure.

Core objects:

- `HumanReviewResult`
- `HumanReviewStatus`
- `ReviewerInput`
- `ReviewDecisionLogEntry`

Evaluator:

- `HumanReviewEvaluator`

The evaluator turns low/missing confidence, validation errors/warnings, policy review reasons, unsupported documents, and future attestation requirements into a review status:

- `NotRequired`
- `Recommended`
- `Required`

The local demo does not pause for human review. It returns review state immediately.

## P5 Quality Review Prototype

The P5 prototype is opt-in and not wired into the default API path.

Files:

- `DocumentQualityReviewWorkflow`
- `QualityAnalystExecutor`
- `QualityCriticExecutor`
- `QualityAnalysis`
- `QualityReviewResult`
- `QualityReviewFinding`

Purpose:

- run an extra quality layer over an existing `DocumentProcessingResult`;
- have an Analyst step summarize risks and contradictions;
- have a Critic step decide `Accept`, `NeedsHumanReview`, or `Reject`;
- measure the added token count, cost, and latency separately.

Important distinction:

- the document-specific extraction workflows are in the live processing path;
- the P5 quality prototype is an experiment harness for later measurement.

See `docs/multi-agent-quality-prototype.md` for more detail.

## Tests To Look At

Workflow tests:

- `tests/MafDocumentProcessor.Tests/ReceiptProcessingWorkflowTests.cs`

Parser/model service tests:

- `tests/MafDocumentProcessor.Tests/ModelResponseParsersTests.cs`
- `tests/MafDocumentProcessor.Tests/ModelDocumentServicesTests.cs`

API and UI mapping tests:

- `tests/MafDocumentProcessor.Tests/ApiIntegrationTests.cs`
- `tests/MafDocumentProcessor.Tests/ApiDemoTests.cs`

P5 quality prototype tests:

- `tests/MafDocumentProcessor.Tests/QualityReviewWorkflowTests.cs`

Sujiko asset regression:

- `tests/MafDocumentProcessor.Tests/SujikoAssetRegressionTests.cs`

The live asset regression is opt-in:

```powershell
$env:MAF_RUN_LIVE_ASSET_TESTS = "1"
dotnet test .\MafDocumentProcessor.sln --no-restore -p:UseAppHost=false -p:OutDir=.build\test\ --filter "FullyQualifiedName~RunAsync_CanBeLiveCheckedAndMeasuredAgainstRotatedSujikoAsset"
```
