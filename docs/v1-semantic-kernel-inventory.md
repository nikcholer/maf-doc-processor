# V1 Semantic Kernel Inventory

## Source

Old repository: `C:\data\repo\csharp-semantic-document-processor`

This inventory captures the current Semantic Kernel implementation that V2 should preserve for the first Microsoft Agent Framework vertical slice.

## Baseline

- Solution: `CSharpSemanticDocumentProcessor.sln`
- API project: `src/SemanticDocumentProcessor.Api/SemanticDocumentProcessor.Api.csproj`
- Test project: `tests/SemanticDocumentProcessor.Tests/SemanticDocumentProcessor.Tests.csproj`
- Target framework: `net8.0`
- Main SK package: `Microsoft.SemanticKernel.Connectors.OpenAI` `1.75.0`
- Test framework: xUnit

Baseline check:

- `dotnet test CSharpSemanticDocumentProcessor.sln` restored/built the API project only.
- The solution does not include the test project.
- `dotnet test tests\SemanticDocumentProcessor.Tests\SemanticDocumentProcessor.Tests.csproj` passed: 19 passed, 0 failed.

## Current Workflow

V1 is a local ASP.NET Core minimal API with a static web frontend.

The processing endpoint is:

- `POST /api/documents/process`
- multipart image field: `image`
- optional form field: `sourceId`
- allowed image content types: `image/png`, `image/jpeg`
- allowed extensions: `.png`, `.jpg`, `.jpeg`
- max upload size: 5 MB by default

The orchestrator flow is:

1. Validate and buffer uploaded image.
2. Create `DocumentMetadata`.
3. Classify image as `Invoice`, `Receipt`, or `Unknown`.
4. If `Invoice`, extract invoice fields and evaluate invoice policy.
5. If `Receipt`, extract receipt fields and evaluate receipt policy.
6. If `Unknown`, skip extraction and policy.
7. Return `DocumentProcessingResponse` with category, metadata, classification, model usage, processed document, errors, and warnings.

## Receipt MVP Contract

V2 receipt extraction should initially match this record:

```csharp
public sealed record ReceiptData(
    string StoreName,
    decimal TotalAmount,
    DateOnly? PurchaseDate,
    string? PaymentMethod,
    string? CurrencyCode);
```

Current receipt prompt asks for this JSON shape:

```json
{
  "storeName": "string",
  "totalAmount": 0.0,
  "purchaseDate": "yyyy-MM-dd or null",
  "paymentMethod": "string or null",
  "currencyCode": "ISO-4217 code or null"
}
```

Receipt extraction rules:

- `storeName` is the merchant, shop, or seller name.
- `totalAmount` is the final paid amount.
- `purchaseDate` is the transaction date.
- `paymentMethod` is visible card/cash/payment method text; use null if unavailable.
- Do not infer values that are not visible.
- Dates use `yyyy-MM-dd`.
- Currency codes are normalized to uppercase.

Receipt policy result:

```csharp
public sealed record ReceiptPolicyResult(
    bool IsWithinReviewThreshold,
    bool HasPaymentMethod,
    PolicyDecision Decision,
    IReadOnlyList<string> Reasons);
```

Current receipt policy:

- approve when `TotalAmount <= Policy:ReceiptReviewThreshold` and `PaymentMethod` is present.
- default threshold is `50.00`.
- otherwise return `NeedsReview`.
- reasons explain threshold/payment-method failures or approval.

## Classification Contract

Current categories:

```csharp
public enum DocumentCategory
{
    Invoice,
    Receipt,
    Unknown
}
```

Current classification response:

```csharp
public sealed record ClassificationResult(
    DocumentCategory Category,
    decimal? Confidence,
    string ConfidenceReasoning);
```

Current classification JSON shape:

```json
{
  "category": "Invoice | Receipt | Unknown",
  "confidence": 0.0,
  "confidenceReasoning": "Brief user-facing explanation without hidden chain-of-thought."
}
```

Confidence is accepted only when it is between `0` and `1`.

## Metadata And Response Contract

Current metadata:

```csharp
public sealed record DocumentMetadata(
    string FileName,
    string ContentType,
    long FileSizeBytes,
    DateTimeOffset ReceivedAt,
    string? SourceId,
    string? ModelId,
    decimal? ClassificationConfidence);
```

Current response:

```csharp
public sealed record DocumentProcessingResponse(
    DocumentCategory Category,
    DocumentMetadata Metadata,
    ClassificationResult? Classification,
    DocumentModelUsage ModelUsage,
    ProcessedDocument? Document,
    bool IsSuccess,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);
```

Current processed document types:

- `InvoiceDocument`
- `ReceiptDocument`
- `UnknownDocument`

## Model Usage Contract

The old repo records model token usage per operation:

```csharp
public sealed record ModelTokenUsage(
    string Operation,
    string ModelId,
    int? InputTokens,
    int? OutputTokens,
    int? TotalTokens);
```

Aggregated model usage:

```csharp
public sealed record DocumentModelUsage(
    IReadOnlyList<ModelTokenUsage> Calls,
    int? TotalInputTokens,
    int? TotalOutputTokens,
    int? TotalTokens);
```

Current operation names:

- `classification`
- `invoice_extraction`
- `receipt_extraction`

## Semantic Kernel Usage

Package:

- `Microsoft.SemanticKernel.Connectors.OpenAI` `1.75.0`

Kernel setup:

- `Kernel.CreateBuilder()`
- `AddOpenAIChatCompletion(...)`
- provider configured as OpenAI-compatible Together AI endpoint
- API key read from environment variable

SK services/classes:

- `SemanticKernelDocumentClassificationService`
- `SemanticKernelDocumentExtractionService`
- `SemanticKernelPolicyEvaluationService`
- `ModelTokenUsageExtractor`
- `ApprovalPolicyPlugin`
- `VendorPolicyPlugin`

SK abstractions in use:

- `Kernel`
- `IChatCompletionService`
- `ChatHistory`
- `ChatMessageContentItemCollection`
- `TextContent`
- `ImageContent`
- `OpenAIPromptExecutionSettings`
- `ResponseFormat = "json_object"`
- `Temperature = 0`
- `KernelArguments`
- `Kernel.InvokeAsync<T>()`
- `[KernelFunction]`
- `KernelContent.Metadata`
- `UsageDetails`

The V2 migration should replace SK plugin invocation with deterministic policy executors. The policy code itself is deterministic and does not need an agent.

## Configuration To Preserve Or Evolve

Current AI config:

```json
{
  "Provider": "TogetherAI",
  "Endpoint": "https://api.together.xyz/v1",
  "ModelId": "google/gemma-4-31B-it",
  "ApiKeyEnvironmentVariable": "TOGETHER_API_KEY",
  "ServiceId": "together-vision",
  "RequestTimeoutSeconds": 180
}
```

V2 should split or generalize this into configured model roles:

- image recognition model: initially Gemma 4
- text/test model: initially a GPT mini model
- provider endpoints and API keys outside source control

Current document intake config:

- image form field: `image`
- max upload bytes: `5242880`
- content types: `image/png`, `image/jpeg`
- extensions: `.png`, `.jpg`, `.jpeg`

Current policy config:

- receipt review threshold: `50.00`
- default currency: `GBP`

## Tests To Preserve

Important V1 tests to carry forward or re-express:

- classification JSON parsing accepts valid JSON and rejects invalid JSON.
- invoice parsing normalizes currency/date and rejects missing required total.
- receipt parsing rejects invalid JSON.
- orchestrator routes invoice through invoice extraction and invoice policy.
- orchestrator routes receipt through receipt extraction and receipt policy.
- orchestrator routes unknown without extraction or policy.
- receipt policy applies threshold and payment method rules.
- image validation accepts configured content type/extension and rejects oversize/unsupported uploads.

## V2 Implications

- Start V2 with receipts only, but keep `DocumentCategory` and processed-document shape extensible.
- Use a receipt extraction schema that matches V1 exactly before adding fields.
- Make classification and extraction separate workflow steps.
- Make policy evaluation deterministic executors, not model calls.
- Keep token/model usage as first-class output if the new provider APIs expose it.
- Keep local-only processing for the MVP.
- Defer durable execution until local interruption/batch requirements justify it.

## Initial V2 Package Pins

Use a coherent Agent Framework 1.4.x set for the first implementation pass:

- `Microsoft.Agents.AI` `1.4.0`
- `Microsoft.Agents.AI.Workflows` `1.4.0`
- `Microsoft.Agents.AI.OpenAI` `1.4.0`
- `Microsoft.Extensions.AI` `10.5.2`
- target framework: `net8.0`

Rationale: Agent Framework 1.5.0 packages have started to appear, but the OpenAI provider package is currently clear at 1.4.0. Keeping the Agent Framework packages aligned reduces early migration noise.
