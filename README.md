# MAF Document Processor

Local Microsoft Agent Framework document-processing demo for receipt and shopping-list images.

The current slice is deliberately small: upload one PNG/JPEG through the local web UI, classify the document, route it to the matching workflow, extract structured data, and show model usage, latency, estimated cost, validation, and raw JSON.

## Prerequisites

- .NET SDK `8.0.419` or compatible `8.0.x` SDK. The repo pins this in `global.json`.
- A TogetherAI API key in `TOGETHER_API_KEY`.

Set the API key for your Windows user:

```powershell
[Environment]::SetEnvironmentVariable("TOGETHER_API_KEY", "<your-key>", "User")
```

Or for the current terminal only:

```powershell
$env:TOGETHER_API_KEY = "<your-key>"
```

## Run

From the repo root:

```powershell
dotnet build .\src\MafDocumentProcessor.Api\MafDocumentProcessor.Api.csproj
dotnet run --project .\src\MafDocumentProcessor.Api\MafDocumentProcessor.Api.csproj --urls http://127.0.0.1:5095
```

Then open:

```text
http://127.0.0.1:5095/
```

If the API is already running, stop it before rebuilding so Windows does not keep the output executable locked.

## Test

```powershell
dotnet test .\MafDocumentProcessor.sln --no-restore
```

For local runs while the API executable is open, this alternate output path avoids the locked apphost:

```powershell
dotnet test .\MafDocumentProcessor.sln --no-restore -p:UseAppHost=false -p:OutDir=.build\test\
```

## Configuration

Model roles live under `AiModels` in [appsettings.json](src/MafDocumentProcessor.Api/appsettings.json):

- `DocumentClassification`: Qwen vision model for categorization.
- `DocumentExtraction`: Qwen vision model for receipt/shopping-list extraction.
- `TextTesting`: reserved for future text-only experiments.

Each role includes provider, endpoint, model id, API key environment variable, timeout, retry policy, and token pricing. Pricing is used only for local estimated-cost display.

Image preprocessing lives under `ModelImagePreprocessing`. The server keeps the uploaded image intact for intake, but sends downscaled JPEGs to the model when configured.

## Current Scope

Supported document types:

- Receipts
- Shopping lists

Unsupported but recognized document types return a human-readable message, for example: "This appears to be a car registration document. This demo can process receipts and shopping lists right now."

The demo is local-only. It does not include authentication, persistence, user workflow history, human-review screens, or external hosting.

## Notes

- Classification intentionally happens before the MAF workflow graph so the app can route into a document-specific workflow.
- The model boundary is a local `IModelChatClient` abstraction rather than `Microsoft.Extensions.AI` for now, because TogetherAI-specific protocol options are required to disable Qwen thinking mode.
- Transient model/provider failures are retried with a short bounded backoff. Structural validation failures get one bounded repair extraction attempt.
- Durable pause/resume is deliberately deferred for the local demo. Failed or canceled requests are safe to resubmit.

## Further Docs

- [Adding a document type](docs/adding-document-types.md)
- [API error contract](docs/api-error-contract.md)
- [Document result semantics](docs/document-result-semantics.md)
- [Human review policy](docs/human-review-policy.md)
- [Durability decision](docs/durability-decision.md)
