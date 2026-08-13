# MAF Document Processor — Code Review

**Build**: ✅ clean, 0 warnings  
**Tests**: ✅ 28 passed, 0 failed  
**Backlog**: P0 ✅ complete, P1 ✅ complete, P1.5 ✅ complete, P2 partially started

---

## Overall Assessment

This is a well-structured, disciplined migration. The codebase is clean, the naming is consistent, the domain records are lean, and the MAF `Executor<TIn, TOut>` / `WorkflowBuilder` integration looks correct. The test suite is meaningful — it covers the routing, policy, validation, and unsupported-document paths rather than just happy-path wiring. The separation between the workflow library and the API host is good, and the API layer has sensible intake validation and error handling. The backlog itself is unusually well-written for a solo effort — clear priorities, honest "Open" statuses, and no phantom items marked done that aren't.

What follows is honest feedback: things I'd flag in a code review, architectural concerns for the next phases, and suggested new backlog items.

---

## Concerns

### 1. `DocumentClassificationExecutor` is dead code

[DocumentClassificationExecutor.cs](file:///c:/data/repo/maf-doc-processor/src/MafDocumentProcessor/Workflow/DocumentClassificationExecutor.cs) inherits from `Executor<FileRequest, ClassifiedDocument>` and is fully implemented — but it is never used. The `DocumentProcessingWorkflow` calls `classifier.ClassifyAsync` directly and builds the `ClassifiedDocument` inline, then feeds the per-type sub-workflows starting from the extraction executor.

This means classification is the *one* step that bypasses MAF's executor/workflow model. It's understandable — classification is the routing decision, and you need its result to choose which sub-workflow to build — but it leaves a dangling class in the project and creates an inconsistency in how the pipeline is described.

**Suggestion**: Either delete `DocumentClassificationExecutor` (it's unused and misleading), or add a backlog item to explore a single top-level workflow that starts with classification and uses conditional edges to fan out, which would make the full pipeline a single MAF graph. If you keep it for future use, add a comment explaining that.

---

### 2. `ChatClient` is re-created per call — no connection reuse

In [OpenAICompatibleModelChatClient.cs:112–130](file:///c:/data/repo/maf-doc-processor/src/MafDocumentProcessor/Services/OpenAICompatibleModelChatClient.cs#L112-L130), `CreateClient` builds a fresh `ChatClient` (and its underlying `OpenAIClient`) on every invocation. The `OpenAI` SDK's `OpenAIClient` wraps an `HttpClient`; creating and disposing these per call means you lose HTTP connection pooling and may hit socket exhaustion under load.

For a local demo this is fine. If you ever process batches or add concurrency, this will bite.

**Suggestion**: Consider caching the `ChatClient` per-settings-key (or making it a singleton when settings are static), or at minimum share an `HttpClient` instance.

---

### 3. Retry policy is explicitly disabled — fine now, risky later

```csharp
RetryPolicy = new ClientRetryPolicy(maxRetries: 0)
```

The backlog has "Add retry policy for transient model/provider failures" in P2, so this is intentional. Just worth noting that the `TimeoutException` wrapping in the catch blocks is solid and will compose well with a retry layer when you add one.

---

### 4. `IModelChatClient` is your own abstraction, not `Microsoft.Extensions.AI.IChatClient`

The backlog says "Use `Microsoft.Extensions.AI` as the shared AI abstraction layer," and the package is referenced. But nothing in the codebase actually *uses* `Microsoft.Extensions.AI`. Your `IModelChatClient` / `ModelChatMessage` / `ModelChatContent` hierarchy is a hand-rolled equivalent of `IChatClient` / `ChatMessage` / `ChatContent` from that package.

This isn't necessarily wrong — the hand-rolled types give you tight control over the content-part discriminated union and the cost-estimation side-channel — but it means the `Microsoft.Extensions.AI` dependency is currently phantom. If you intend to adopt `IChatClient` middleware (logging, caching, rate-limiting) later, the current abstraction would need to be replaced or wrapped.

**Suggestion**: Either (a) note in the backlog that MEAI integration is deferred and the package is referenced for future use, or (b) add a P2/P3 item to evaluate migrating `IModelChatClient` → `IChatClient` so you get middleware composition for free.

---

### 5. Image data flows through `byte[]` on domain records — large allocation concern

`FileRequest.Content` is a `byte[]` that carries the full image payload. Since `FileRequest` (and by extension `ClassifiedDocument`, `ReceiptExtraction`, etc.) are records, they're passed by reference, so the array isn't *copied* — but the image is preprocessed twice (once for classification, once for extraction), producing two separate byte arrays that live in memory simultaneously. For a 5 MB upload resized to two JPEG variants, this is ~10–15 MB of pinned buffers per request.

For a single-user local demo this is trivially fine. For batch or concurrent processing it would be worth streaming or pinning to pooled buffers.

---

### 6. No `IDisposable` / `IAsyncDisposable` on the image preprocessor's `Image` load

In [ModelImagePreprocessor.cs:28](file:///c:/data/repo/maf-doc-processor/src/MafDocumentProcessor/Services/ModelImagePreprocessor.cs#L28), `Image.Load(request.Content)` is wrapped in a `using` — good. But the two `MemoryStream` allocations (the JPEG output stream) are `await using` — also good. No concern here, just confirming this is clean.

---

### 7. `ProcessedDocumentResponse.Data` is typed as `object`

In [DocumentProcessingResponse.cs:18](file:///c:/data/repo/maf-doc-processor/src/MafDocumentProcessor.Api/Contracts/DocumentProcessingResponse.cs#L18):

```csharp
public sealed record ProcessedDocumentResponse(
    DocumentCategory Category,
    DocumentMetadata Metadata,
    object Data,          // <-- untyped
    ReceiptPolicyResult? PolicyResult,
    ValidationResult Validation);
```

The `object Data` works because System.Text.Json serializes the runtime type. But it means the API contract is untyped — consumers have to infer the schema from `Category`. For a demo UI this is fine, but it makes the API harder to document or generate OpenAPI schemas for.

**Suggestion**: When you formalize the API (post-demo), consider a discriminated response shape or separate response types per category.

---

### 8. Shopping list `IsSuccess` can be `false` with only validation warnings

In [ShoppingListResultExecutor.cs:31–32](file:///c:/data/repo/maf-doc-processor/src/MafDocumentProcessor/Workflow/ShoppingListResultExecutor.cs#L31-L32):

```csharp
IsSuccess: message.Validation.IsValid,
Errors: message.Validation.IsValid ? [] : message.Validation.Reasons,
```

For shopping lists, a validation failure (e.g. no readable items) sets `IsSuccess = false` and moves the reasons to `Errors`. For receipts, the equivalent scenario sets `IsSuccess = true` and moves reasons to `Warnings`. This behavioral difference is likely intentional (a receipt with missing fields is still "processed," but a shopping list with no items is useless), but it's not documented and could confuse consumers.

**Suggestion**: Add a brief comment or backlog note explaining the intentional difference in success semantics between document types.

---

## Nitpicks (minor, non-blocking)

### `NormalizeDocumentTypeDescription` strips articles but doesn't handle "A " in the middle of a description

If the model returns `"a technical document with a diagram"`, the stripping logic would remove `"a "` from the front and produce `"technical document with a diagram"` — which is correct. But the prefix-stripping only fires once (`break`), so it's fine. Just noting the logic is sound.

### The `Logging` section is missing from `appsettings.json`

The API project has no `Logging` configuration in `appsettings.json`. ASP.NET Core defaults will apply (Information level), which is fine for a demo, but you'll want explicit log levels when you add structured telemetry.

### `RequestTimeoutSeconds` is 600 (10 minutes)

This seems very generous. For Gemma 4 on TogetherAI, classification typically completes in 5–15 seconds and extraction in 10–30 seconds. A 600-second timeout means a hung request will block for 10 minutes before failing. Consider whether a shorter default (e.g. 120s) with per-role override would give faster failure feedback.

### The `TextTesting` role is configured but not yet wired

`AiModelSettings.TextTesting` exists in config and is loaded, but nothing in the codebase uses it. Both classification and extraction use `ImageRecognition`. This is presumably reserved for future text-only model calls or test harness work, but currently it's dead config.

---

## Suggested New Backlog Items

Based on the review, here are items I'd consider adding:

### P2 additions

| Item | Rationale |
|---|---|
| **Remove or document `DocumentClassificationExecutor`** | It's implemented but unused. Either delete it or add a note that it's reserved for a future single-graph workflow. |
| **Evaluate `IModelChatClient` → `IChatClient` migration** | The `Microsoft.Extensions.AI` package is referenced but unused. Decide whether to adopt `IChatClient` middleware or remove the dependency. |
| **Add `CancellationToken` timeout shorter than 600s by default** | 600s is very generous for a vision model call. A 90–120s default with per-role override would improve failure UX. |
| **Add request-scoped correlation/operation ID to workflow logging** | The API endpoint logs `TraceIdentifier` but the workflow and model client don't receive or log it. Threading a correlation ID through would make end-to-end log tracing possible. |
| **Cache or reuse `ChatClient` instances** | Re-creating the OpenAI `ChatClient` per call loses HTTP connection pooling. |
| **Add a `README.md`** | The repo has no README. For a portfolio piece this is the first thing a reviewer sees. |

### P2.5 — Hardening (between workflow maturity and long-running)

| Item | Rationale |
|---|---|
| **Add integration test with `WebApplicationFactory`** | The test project references the API project but `ApiDemoTests` only tests config loading and response mapping — not the HTTP pipeline. A single `WebApplicationFactory` test hitting `/api/documents/process` with a fake classifier would verify DI wiring end-to-end. |
| **Add CancellationToken propagation test** | Verify that cancelling the request's token propagates through the workflow and model client correctly. |
| **Formalize API error contract documentation** | The `ApiErrorResponse` and `DocumentIntakeErrorResponse` records exist but there's no documentation of the error codes (`invalid_document_upload`, `model_timeout`, etc.) that the API returns. |

### P3 additions

| Item | Rationale |
|---|---|
| **Define success/failure semantics per document type** | Receipt processing always returns `IsSuccess = true` (even with validation failures as warnings), while shopping list processing returns `IsSuccess = false` when validation fails. Codify this as a deliberate policy. |

---

## Things Done Well

Worth calling out explicitly:

- **Disciplined V1 inventory**: The [v1-semantic-kernel-inventory.md](file:///c:/data/repo/maf-doc-processor/docs/v1-semantic-kernel-inventory.md) is thorough and honest. It captured contracts, tests, config, and SK abstractions — which made the migration straightforward rather than a guessing game.
- **Executor design**: The `Executor<TIn, TOut>` usage is clean. Each executor has a single responsibility, and the intermediate records (`ReceiptExtraction`, `ValidatedReceiptExtraction`, `ReceiptPolicyEvaluation`) make the pipeline stages type-safe.
- **Image preprocessing**: Separating classification vs extraction preprocessing resolutions is a smart optimization for token cost. Classifying at 1280px and extracting at 2048px is a reasonable split.
- **Cost estimation as first-class output**: Capturing per-call token pricing and estimated USD cost in the domain model is forward-thinking for a portfolio piece.
- **Error handling in the API layer**: The catch chain in `DocumentProcessingEndpoints` is well-structured — distinct HTTP status codes for configuration errors (500), model response errors (502), timeouts (504), and provider failures (502).
- **Model response parsing is resilient**: `ModelResponseParsers` handles markdown fences, leading text before JSON, string-encoded numbers, and alternative field names. This is exactly the kind of defensive parsing you need when working with smaller open models like Gemma.
- **Test fakes are minimal and purposeful**: The in-test fakes (`FakeDocumentClassifier`, `FakeReceiptExtractor`, `PassThroughImagePreprocessor`) are lightweight and don't pull in a mocking framework, which keeps the test project lean and the test assertions readable.
