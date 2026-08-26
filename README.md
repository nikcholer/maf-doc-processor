# MAF Document Processor

Local Microsoft Agent Framework (MAF) document-processing demo for receipt, shopping-list, and Sujiko puzzle images.

Upload one PNG or JPEG through the web UI or HTTP API. One top-level MAF workflow classifies the image and routes it to a document-specific child workflow, which extracts structured data, validates it, makes one bounded repair attempt when needed, and returns model usage, latency, estimated cost, human-review state, and raw JSON.

## Project Status

The local vertical slice is complete and covered by unit and API integration tests. It supports:

- Receipts, including policy checks for payment method and review threshold.
- Shopping lists, including item validation.
- Sujiko puzzles, including quadrant-total and given-cell validation. The current scope extracts the starting state; it does not solve the puzzle.
- Human-review recommendations returned with the response. There is no reviewer queue or pause/resume flow yet.
- An opt-in Analyst/Critic quality-review prototype. It is not part of the default API path because its quality benefit has not yet been measured against a representative sample set.

The [initial migration backlog](docs/maf-migration-backlog.md) records the completed milestones. Forward architectural work is tracked in the [MAF workflow evolution backlog](docs/maf-workflow-evolution-backlog.md).

## Prerequisites

- .NET SDK `10.0.400` or a compatible later .NET 10 feature band. The repository pins this in `global.json`.
- A TogetherAI API key in `TOGETHER_API_KEY` for live processing.

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

Then open <http://127.0.0.1:5095/>. The launch profile binds to that address by default.

If the API is already running, stop it before rebuilding so Windows does not keep the output executable locked.

## API

- `GET /health` reports API-key readiness and configured model information.
- `POST /api/documents/process` accepts `multipart/form-data` with an image in the `image` field and an optional `sourceId` value.

The default upload limit is 5 MiB. Accepted types are PNG and JPEG with `.png`, `.jpg`, or `.jpeg` extensions.

Example:

```powershell
curl.exe -F "image=@C:\path\to\receipt.jpg" -F "sourceId=manual-test" http://127.0.0.1:5095/api/documents/process
```

Unsupported document types return a normal workflow response with `isSuccess: false` and a human-readable explanation. Intake, configuration, provider, timeout, and model-response failures use the documented API error contract.

## Test

Run the normal offline suite:

```powershell
dotnet test .\MafDocumentProcessor.sln
```

If the API executable is open, use an alternate output path to avoid a locked apphost:

```powershell
dotnet test .\MafDocumentProcessor.sln --no-restore -p:UseAppHost=false -p:OutDir=.build\test\
```

The repository also includes a real rotated Sujiko image. Its provider-backed full-workflow regression and measurement assertion is disabled by default. To run it with TogetherAI:

```powershell
$env:MAF_RUN_LIVE_ASSET_TESTS = "1"
dotnet test .\MafDocumentProcessor.sln --filter FullyQualifiedName~SujikoAssetRegressionTests
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

The normal suite includes a small, non-confidential [golden set](docs/golden-set.md) for the receipt, shopping-list, Sujiko, and unsupported routes. Run it alone with:

```powershell
dotnet test .\MafDocumentProcessor.sln --filter FullyQualifiedName~GoldenSetTests
```

## Configuration

Runtime settings live in [appsettings.json](src/MafDocumentProcessor.Api/appsettings.json):

- `AiModels:DocumentClassification`: Qwen vision model used before workflow routing.
- `AiModels:DocumentExtraction`: Qwen vision model used by all supported document extractors.
- `AiModels:DocumentRegionDetection`: Qwen vision model used to locate physical documents in a composite capture before classification.
- `AiModels:TextTesting`: reserved model role used only when explicitly constructing experimental text/quality workflows.
- `ModelImagePreprocessing`: region-detection, classification, and extraction resize limits plus JPEG quality.
- `DocumentIntake`: upload field name, size limit, content types, and extensions.
- `ReceiptPolicy`: review threshold and default currency.

Each model role includes its provider, endpoint, model ID, API-key environment variable, timeout, retry policy, and token pricing. Pricing is used only for local estimated-cost reporting. Legacy `AiModels:ImageRecognition` configuration is still accepted as a fallback for classification and extraction, but new configuration should use the named roles. Region detection is deliberately separate because it acts on a whole capture image and has different prompts, image sizing, usage, and future model-selection needs.

See [TogetherAI local setup](docs/together-ai-local-setup.md) for the current model defaults.

## Processing Design

Each request runs through one top-level MAF graph. Its classification executor prepares the classification image and calls the classification model once. Labelled conditional edges then send the typed result to exactly one destination: a bound receipt, shopping-list, or Sujiko child workflow, or the unsupported-document executor. Supported documents are prepared separately for extraction before entering their child workflow.

The child workflows use deterministic executors around model extraction, validation, one repair pass, optional policy, and result construction. Classification, route selection, and the selected child completion are visible in the same workflow event stream. The graph is built per request and runs locally in-process; it does not add persistence or background processing.

The provider boundary is a local `IModelChatClient` abstraction. It is retained because TogetherAI-specific protocol options are required to disable Qwen thinking mode. OpenAI-compatible clients are cached by model settings, and transient provider failures use bounded retries.

The E3 composite-capture source boundary is implemented but is not yet exposed through the API or UI. It validates and orientation-normalizes each source once, makes one region-detection call, then deterministically validates, orders, and crops accepted regions from the oriented high-resolution image. See the [technical process flow](docs/technical-process-flow.md#composite-capture-source-detection-e3) and [composite capture contract](docs/composite-capture-contract.md).

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

- The selected next application path adds multi-source composite capture, processing every detected document through the existing category workflow, followed by expense report as the next distinct document type.
- Measure whether the opt-in Analyst/Critic workflow improves output enough to justify two additional model calls.
- Maintain the current .NET 10, MAF 1.19, OpenAI 2.13, and test-tooling baseline. ImageSharp 4 and xUnit v3 are explicitly deferred as separate migrations.
- Optional icebox work includes a deterministic Sujiko solver, export/copy affordances, and a rate-limited hosted demo.

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
- [Multi-agent quality prototype](docs/multi-agent-quality-prototype.md)
- [V1 Semantic Kernel inventory](docs/v1-semantic-kernel-inventory.md)
