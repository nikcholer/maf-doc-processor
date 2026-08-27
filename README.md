# MAF Document Processor

Local Microsoft Agent Framework (MAF) demo that turns document images into structured data. It processes receipt, shopping-list, Sujiko puzzle, and expense-report images. It is not a case-management or durable workflow engine.

Upload one PNG or JPEG through the web UI or HTTP API. One top-level MAF workflow classifies the image and routes it to a document-specific child workflow, which extracts structured data, validates it, makes one bounded repair attempt when needed, and returns model usage, latency, estimated cost, human-review state, and raw JSON.

## Project Status

The local vertical slice is complete and covered by unit and API integration tests. It supports:

- Receipts, including policy checks for payment method and review threshold.
- Shopping lists, including item validation.
- Sujiko puzzles, including quadrant-total and given-cell validation. The current scope extracts the starting state; it does not solve the puzzle.
- Expense reports, including line-total arithmetic, currency and date checks, high-value and missing-receipt-reference policy, and ownership attestation. Persistent receipt linking and claim submission are out of scope.
- Human-review recommendations returned with the response. There is no reviewer queue or pause/resume flow yet.
- A two-mode local UI: direct single-document processing and composite capture with multi-source previews, annotated document regions, and accessible member inspection.
- An opt-in Analyst/Critic quality-review prototype. It is not part of the default API path because its quality benefit has not yet been measured against a representative sample set.

The [initial migration backlog](docs/maf-migration-backlog.md) records the completed milestones. Forward architectural work is tracked in the [MAF workflow evolution backlog](docs/maf-workflow-evolution-backlog.md).

## Prerequisites

- .NET SDK `10.0.400` or a compatible later .NET 10 feature band. The repository pins this in `global.json`.
- A TogetherAI API key in `TOGETHER_API_KEY` for live processing.
- Node.js 18 or later only when running the dependency-free browser UI model tests.

Set the API key for your Windows user:

```powershell
[Environment]::SetEnvironmentVariable("TOGETHER_API_KEY", "<your-key>", "User")
```

Or set it for the current terminal only:

```powershell
$env:TOGETHER_API_KEY = "<your-key>"
```

## Run Locally

From the repository root:

```powershell
dotnet restore .\MafDocumentProcessor.sln
dotnet run --project .\src\MafDocumentProcessor.Api\MafDocumentProcessor.Api.csproj
```

Then open <http://127.0.0.1:5095/>. The launch profile binds to that address by default. Choose **Single document** for the original direct route or **Capture set** to submit up to five source images and inspect every detected document on its source preview.

If the API is already running, stop it before rebuilding so Windows does not keep the output executable locked.

## API

- `GET /health` reports API-key readiness and configured model information.
- `GET /openapi/v1.json` is the generated OpenAPI document. Its individual-document response schema describes the receipt, shopping-list, Sujiko, and expense-report variants of `document.data` with `oneOf`.
- `POST /api/documents/process` accepts `multipart/form-data` with an image in the `image` field and an optional `sourceId` value.
- `POST /api/document-captures/process` accepts one or more PNG or JPEG files in a repeated `images` field, an optional request-level `sourceId`, and optional per-source normalized rectangle corrections in the `regionOverrides` JSON field. It returns a capture aggregate with source and member outcomes. Corrected sources skip region detection; uncorrected siblings still use the detector.

The individual-document upload limit is 5 MiB. A capture request may include up to five images totalling 25 MiB. Accepted types are PNG and JPEG with `.png`, `.jpg`, or `.jpeg` extensions.

Example, one document:

```powershell
curl.exe -F "image=@C:\path\to\receipt.jpg" -F "sourceId=manual-test" http://127.0.0.1:5095/api/documents/process
```

Example, composite capture:

```powershell
curl.exe -F "images=@C:\path\to\desk.jpg" -F "images=@C:\path\to\receipt.jpg" -F "sourceId=expense-claim" http://127.0.0.1:5095/api/document-captures/process
```

The local capture UI can correct a source after its first result. Choose **Edit regions** to add, delete, reorder, move, or resize normalized rectangles, then choose **Reprocess corrected regions**. Corrections are kept only in the current page and are sent with the same source files; the API does not persist images, regions, or results.

Unsupported document types return a normal workflow response with `isSuccess: false` and a human-readable explanation. Capture requests that mix valid and invalid sources return HTTP 200 with `status: PartiallySucceeded`. Intake, configuration, provider, timeout, and model-response failures that prevent the request from starting use the documented API error contract.

## Test

Run the normal offline suite:

```powershell
dotnet test .\MafDocumentProcessor.sln
```

If the API executable is open, use an alternate output path to avoid a locked apphost:

```powershell
dotnet test .\MafDocumentProcessor.sln --no-restore -p:UseAppHost=false -p:OutDir=.build\test\
```

The repository also includes a real rotated Sujiko image and synthetic expense-report fixtures. Their provider-backed full-workflow checks are disabled by default. To run them with TogetherAI:

```powershell
$env:MAF_RUN_LIVE_ASSET_TESTS = "1"
dotnet test .\MafDocumentProcessor.sln --filter FullyQualifiedName~SujikoAssetRegressionTests
dotnet test .\MafDocumentProcessor.sln --filter FullyQualifiedName~ExpenseReportAssetTests
```

To collect offline test coverage:

```powershell
dotnet test .\MafDocumentProcessor.sln --collect:"XPlat Code Coverage"
```

The composite-capture detector also has an opt-in provider check against the non-confidential three-document desk sample:

```powershell
$env:MAF_RUN_LIVE_CAPTURE_DETECTION_TESTS = "1"
dotnet test .\MafDocumentProcessor.sln --filter FullyQualifiedName~CaptureRegionDetectionLiveTests
```

Personal capture photos can be dropped in `tests/MafDocumentProcessor.Tests/assets/local/` for local detection and crop checks. That folder is gitignored. The opt-in test skips when the folder is empty, does not copy those images into the repository, and writes accepted crops under `assets/local/crops/` so they can be inspected locally:

```powershell
$env:MAF_RUN_LOCAL_CAPTURE_SAMPLES = "1"
dotnet test .\MafDocumentProcessor.sln --filter FullyQualifiedName~CaptureLocalSampleTests
```

The normal suite includes a small, non-confidential [golden set](docs/golden-set.md) for the receipt, shopping-list, Sujiko, and unsupported routes, plus the [composite capture and expense-report corpus](docs/next-scenario-sample-set.md). The provider-free [Release baseline workflow](.github/workflows/release-baseline.yml) repeats the Release build, .NET and UI suites, and dependency vulnerability audit for pull requests and `main`. Run the sample-focused tests locally with:

```powershell
dotnet test .\MafDocumentProcessor.sln --filter FullyQualifiedName~GoldenSetTests
dotnet test .\MafDocumentProcessor.sln --filter FullyQualifiedName~CaptureGoldenSetTests
```

The bounded-parallel capture harness compares one source/member lane with two of each, using simulated model delays:

```powershell
dotnet test .\MafDocumentProcessor.sln --filter FullyQualifiedName~CaptureParallelismMeasurementTests
```

The annotated-capture UI keeps its geometry, selection, status, and accessibility model dependency-free. Run those focused checks with Node's built-in test runner:

```powershell
node --test .\tests\ui\capture-ui.test.cjs
```

## Configuration

Runtime settings live in [appsettings.json](src/MafDocumentProcessor.Api/appsettings.json):

- `AiModels:DocumentClassification`: Qwen vision model used before workflow routing.
- `AiModels:DocumentExtraction`: Qwen vision model used by all supported document extractors.
- `AiModels:DocumentRegionDetection`: Qwen vision model used to locate physical documents in a composite capture before classification.
- `AiModels:TextTesting`: reserved model role used only when explicitly constructing experimental text/quality workflows.
- `ModelImagePreprocessing`: region-detection, classification, and extraction resize limits plus JPEG quality.
- `DocumentIntake`: upload field name, size limit, content types, and extensions.
- `CompositeCapture`: source count, byte and pixel limits, useful-region thresholds, duplicate/overlap policy, crop padding, and source/member lane counts.
- `ReceiptPolicy`: review threshold and default currency.
- `ExpensePolicy`: high-value review threshold for expense reports.

Each model role includes its provider, endpoint, model ID, API-key environment variable, timeout, retry policy, and token pricing. Pricing is used only for local estimated-cost reporting. Legacy `AiModels:ImageRecognition` configuration is still accepted as a fallback for classification and extraction, but new configuration should use the named roles. Region detection is deliberately separate because it acts on a whole capture image and has different prompts, image sizing, usage, and future model-selection needs.

See [TogetherAI local setup](docs/together-ai-local-setup.md) for the current model defaults.

## Processing Design

Each request runs through one top-level MAF graph. Its classification executor prepares the classification image and calls the classification model once. Labelled conditional edges then send the typed result to exactly one destination: a bound receipt, shopping-list, Sujiko, or expense-report child workflow, or the unsupported-document executor. Supported documents are prepared separately for extraction before entering their child workflow.

The child workflows use deterministic executors around model extraction, validation, one repair pass, optional policy, and result construction. Classification, route selection, and the selected child completion are visible in the same workflow event stream. The graph is built per request and runs locally in-process; it does not add persistence or background processing.

The provider boundary is a local `IModelChatClient` abstraction. It is retained because TogetherAI-specific protocol options are required to disable Qwen thinking mode. OpenAI-compatible clients are cached by model settings, and transient provider failures use bounded retries.

The E3 composite-capture workflow is exposed as `POST /api/document-captures/process`. It detects and crops regions from each source, or accepts caller-corrected rectangles for selected sources, then processes accepted members through the same reusable document workflow used by individual uploads, using a fixed number of source and member lanes. The demo UI offers this as an additive **Capture set** mode. It retains the selected local images, draws the response's normalized bounds or outlines over the correctly ordered source previews, exposes accepted, review, rejected, and failed outcomes through both symbols and text, and supports ephemeral rectangle correction and reprocessing. Selecting an overlay or member row reveals that document's extracted data and findings. See the [technical process flow](docs/technical-process-flow.md#composite-capture-orchestration-e3) and [composite capture contract](docs/composite-capture-contract.md).

The demo is local-only. It has no authentication, persistence, workflow history, reviewer UI, or external hosting. Durable pause/resume is deliberately deferred while processing remains bounded foreground HTTP work; failed or canceled requests are safe to resubmit.

## Repository Layout

```text
src/MafDocumentProcessor/       Domain models, model services, and MAF workflows
src/MafDocumentProcessor.Api/   Minimal API and static demo UI
tests/MafDocumentProcessor.Tests/ Unit, workflow, parser, image, and API tests
docs/                           Architecture, contracts, policy, and backlog
```

## Contributing

Proposed changes enter through GitHub Issues and the [MAF Document Processor GitHub Project](https://github.com/users/nikcholer/projects/1). The Project is the live backlog and the source of truth for task readiness, priority, status, and dependencies.

Read [CONTRIBUTING.md](CONTRIBUTING.md) before selecting or delivering work. Development agents must also follow [AGENTS.md](AGENTS.md). The detailed tracking lifecycle and Jira compatibility guidance are in the [delivery workflow](docs/delivery-workflow.md).

## Outstanding Work

The core local demo has no incomplete required milestone. The remaining work is maintenance, evaluation, or optional product scope:

Forward architectural work is organized in the [MAF workflow evolution backlog](docs/maf-workflow-evolution-backlog.md) and tracked in the [MAF Document Processor GitHub Project](https://github.com/users/nikcholer/projects/1).

- Composite capture is implemented through the API and Capture set UI, including region correction. Expense reports are implemented as the next distinct document type for both individual and capture-member processing.
- Measure whether the opt-in Analyst/Critic workflow improves output enough to justify two additional model calls.
- Durable pause/resume, case storage, and claim submission are out of scope here. They are sketched only as [forward planning](docs/forward-planning-workflow-system.md) for a later workflow-management system that might call this converter.
- Maintain the current .NET 10, MAF 1.19, OpenAI 2.13, and test-tooling baseline. ImageSharp 4 and xUnit v3 are explicitly deferred as separate migrations.
- Optional icebox work includes a deterministic Sujiko solver, export/copy affordances, a rate-limited hosted demo, and later comparison of other vision models for document region detection.

## Further Documentation

- [Contribution guide](CONTRIBUTING.md)
- [Development-agent instructions](AGENTS.md)
- [Semantic Kernel to MAF migration strategy](docs/sk-to-maf-migration.md)
- [MAF workflow evolution backlog](docs/maf-workflow-evolution-backlog.md)
- [Batch capture and expense report sequencing decision](docs/batch-capture-expense-report-decision.md)
- [Composite capture contract](docs/composite-capture-contract.md)
- [Capture and expense report model boundaries](docs/capture-expense-model-boundaries.md)
- [Capture and expense report MAF capability selection](docs/capture-expense-maf-capabilities.md)
- [Dependency baseline decision](docs/dependency-baseline-decision.md)
- [Current workflow baseline measurements](docs/baseline-measurements.md)
- [Composite capture measurements](docs/composite-capture-measurements.md)
- [Extended workflow release baseline](docs/extended-workflow-release-baseline.md)
- [Observability and operational safeguards](docs/operational-safeguards.md)
- [Current document golden set](docs/golden-set.md)
- [Composite capture and expense report sample set](docs/next-scenario-sample-set.md)
- [Top-level document routing design](docs/top-level-routing-design.md)
- [Delivery workflow](docs/delivery-workflow.md)
- [Beginner's guide to a completed document slice](docs/slice-guide.md)
- [Technical process flow](docs/technical-process-flow.md)
- [Initial Microsoft Agent Framework migration backlog](docs/maf-migration-backlog.md)
- [Adding a document type](docs/adding-document-types.md)
- [API error contract](docs/api-error-contract.md)
- [Document result semantics](docs/document-result-semantics.md)
- [Human review policy](docs/human-review-policy.md)
- [Durability decision](docs/durability-decision.md)
- [Forward planning: structured data in a larger workflow system](docs/forward-planning-workflow-system.md)
- [Multi-agent quality prototype](docs/multi-agent-quality-prototype.md)
- [V1 Semantic Kernel inventory](docs/v1-semantic-kernel-inventory.md)
