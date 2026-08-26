# Technical Process Flow

This document traces what happens when an image is submitted to the local demo API, where Microsoft Agent Framework is used, and where the main objects live in the codebase.

## High-Level Flow

```text
browser upload
  -> ASP.NET Minimal API endpoint
  -> upload validation and buffering
  -> top-level MAF workflow
     -> image preprocessing for classification
     -> model classification
     -> image preprocessing for extraction (supported documents only)
     -> labelled conditional route
        -> bound document-specific child workflow
           -> extract typed data
           -> validate typed data
           -> optional one-shot repair extraction
           -> optional policy/review evaluation
           -> DocumentProcessingResult
        -> or unsupported result
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

Classification is the first executor in the top-level MAF workflow graph.

Code:

- `DocumentClassificationExecutor`
- `DocumentWorkflowFactory.BuildDocumentRoutingWorkflow`
- `ModelDocumentClassifier`
- `ModelResponseParsers.ParseClassification`

The executor prepares the classification image, calls the classifier once, records metadata and model usage, and prepares a separate extraction image for supported categories. It produces a typed `ClassifiedDocument` message. Labelled MAF conditional edges inspect its category and send it to exactly one destination.

This keeps the route visible in the workflow while allowing each supported type to retain its own extraction, validation, result semantics, and optional policy behavior.

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

## Composite Capture Source Detection (E3)

The composite-capture endpoint and UI use the source-processing boundary implemented for the E3 workflow:

```text
CompositeCaptureSource
  -> CaptureSourceImageDecoder
     -> check declared type, extension, bytes, decoded format, and dimensions
     -> decode pixels once and apply EXIF orientation
     -> retain OrientedCaptureSourceImage in request-scoped memory
  -> CaptureDetectionImagePreparer
     -> clone and resize a model-facing JPEG derivative
  -> ModelDocumentRegionDetector
     -> one DocumentRegionDetection model call
     -> boxes are asked to include a margin of background rather than a tight text crop
  -> DocumentRegionResponseParser
     -> typed, still-untrusted DocumentRegionProposal values
  -> CaptureSourceDetectionOutput
```

The names above are project code except for ImageSharp's decoding and orientation operations. `DocumentRegionDetection` is a project configuration role; it is not a MAF or .NET feature.

`CaptureSourceDetectionExecutor` is the MAF adapter around this boundary. It is a project-owned class derived from MAF's `Executor<CaptureSourceDetectionInput, CaptureSourceDetectionOutput>`. It emits project-owned `CaptureSourceDecodedEvent` and `CaptureSourceDetectionCompletedEvent` records with trace, capture, caller-source, and source-item identifiers. The bounded parent graph that will run several of these executors is deferred to the orchestration task.

The detector does not decide whether a rectangle is usable. Its output uses `ProposedNormalizedBounds`, which can represent an out-of-range model answer. Deterministic validation then rejects bad coordinates, duplicates, empty pixel crops, or negligible regions and converts accepted proposals into the stricter `NormalizedBounds` type. This is why detection JSON can be parsed successfully without being trusted.

Expected source-level failures are data rather than workflow crashes:

- an invalid source produces `invalid_capture_source` and no model call;
- invalid model JSON preserves the call's known usage and produces `model_response_invalid`;
- detector timeout or provider failure produces a source error;
- missing model configuration still fails the whole request; and
- cancellation propagates immediately.

## Composite Capture Region Validation (E3)

Detected proposals are untrusted until project-owned geometry and policy code accept them:

```text
CaptureSourceDetectionOutput
  -> CaptureRegionValidationService
     -> trust finite in-range bounds above the useful-region thresholds
     -> map opposite edges independently onto the oriented source
     -> reject empty pixel crops, near-duplicates, and member-limit overflow
     -> order remaining regions top-to-bottom, then left-to-right
     -> expand accepted boxes by RegionEdgePadding so headers and edges are less likely to clip
     -> crop accepted regions from OrientedCaptureSourceImage as PNG FileRequest values
  -> CaptureRegionValidationOutput
```

`CaptureRegionValidationExecutor` is the MAF adapter around this boundary. It emits a project-owned `CaptureRegionValidationCompletedEvent` with proposal, accepted, and rejected counts, then disposes the oriented source once the crops exist. Overlapping but distinct documents continue with the `detected regions overlap` warning; a successful detection that yields no accepted crop becomes `no_usable_document_region`. Crops may include a little neighbouring paper. Classification and extraction prompts tell the model to use the main document occupying most of the image, including its centre.

## Composite Capture Orchestration (E3)

Accepted crops are processed through the reusable document workflow with a fixed number of MAF worker lanes:

```text
CompositeCaptureRequest
  -> source partitioner
  -> fan-out to MaxConcurrentSources lanes
     -> detect, validate, and crop each assigned source sequentially
  -> source fan-in
  -> member partitioner
  -> fan-out to MaxConcurrentMembers lanes
     -> run DocumentProcessingWorkflow for each assigned crop sequentially
  -> member fan-in
  -> CompositeCaptureResult
```

The graph never grows a node per upload. Empty lanes still report so the fan-in barrier has a known contributor set. Ordinary source and member failures become result data; request cancellation still aborts the capture. `CaptureResultComposer` restores source order, assigns capture-wide member indexes, sums model usage once, and calculates `Succeeded` / `PartiallySucceeded` / `Failed` plus member dispositions.

`POST /api/document-captures/process` accepts repeated `images` parts and returns `CompositeCaptureProcessingResponse`. Request-level intake failures use the existing API error contract. Partial success is HTTP 200.

The static UI keeps the original single-document mode and adds an explicit capture-set mode. It retains object URLs for the selected local images only for the lifetime of the current page selection, matches them to response sources by multipart index, and draws each normalized outline or bounds value in an SVG coordinate space over the corresponding preview. The API-provided disposition selects the accepted, review, or rejected treatment; the browser does not recalculate policy. Tick, question-mark, and cross symbols, textual rows, `aria-label` values, and keyboard-selectable overlays provide equivalent non-colour cues. Selecting either an overlay or row updates a member inspector with classification, extracted data, warnings, errors, and disposition reasons, while source failures remain visible alongside successful siblings.

OpenAPI is generated at `GET /openapi/v1.json`.

## MAF Workflow Usage

The production path uses one Microsoft Agent Framework workflow from classification through the final document result.

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
- `src/MafDocumentProcessor/Workflow/DocumentClassificationExecutor.cs`
- `src/MafDocumentProcessor/Workflow/UnsupportedDocumentResultExecutor.cs`

`DocumentProcessingWorkflow` builds the request-scoped graph, executes it once, logs its events, unwraps workflow errors, and returns its `DocumentProcessingResult`. `DocumentWorkflowFactory.BuildDocumentRoutingWorkflow` owns the graph shape. It connects `DocumentClassificationExecutor` to four destinations with typed conditional edges:

```text
DocumentClassificationExecutor
  -> receipt-workflow       -> bound receipt child workflow
  -> shopping-list-workflow -> bound shopping-list child workflow
  -> sujiko-workflow        -> bound Sujiko child workflow
  -> UnsupportedDocumentResult -> Invoice or Unknown result
```

`BindAsExecutor` makes each complete document workflow appear as one executor-shaped node in the parent graph. The child workflow still emits its own events, so it remains separately inspectable without flattening every document-specific step into the routing graph.

The project emits `DocumentClassifiedEvent` and `DocumentRouteSelectedEvent` with the category, filename, and optional source ID. MAF's normal executor events show classification and the selected bound workflow completing. All of these events are observed during the same in-process run and remain inside the HTTP request's logging scope.

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

The three graphs are built by the project-owned `DocumentWorkflowFactory` using MAF's `WorkflowBuilder`. They can be executed on their own and are also bound as child nodes in the top-level routing graph.

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
- `tests/MafDocumentProcessor.Tests/DocumentWorkflowFactoryTests.cs`
- `tests/MafDocumentProcessor.Tests/DocumentRoutingExecutorTests.cs`

Parser/model service tests:

- `tests/MafDocumentProcessor.Tests/ModelResponseParsersTests.cs`
- `tests/MafDocumentProcessor.Tests/ModelDocumentServicesTests.cs`
- `tests/MafDocumentProcessor.Tests/CaptureSourceDetectionTests.cs`
- `tests/MafDocumentProcessor.Tests/CaptureRegionValidationTests.cs`
- `tests/MafDocumentProcessor.Tests/CaptureWorkflowTests.cs`

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
