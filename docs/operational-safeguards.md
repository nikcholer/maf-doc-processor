# Observability and Operational Safeguards

This note records the bounded local operating model verified during the E7 release audit. The application is still a local demo: it has no authentication, persistence, background jobs, hosted quotas, or durable workflow state.

## Request and Image Bounds

The API applies two layers of limits:

| Boundary | Default | Enforcement and result |
| --- | ---: | --- |
| Individual upload | 5 MiB | Multipart intake validates the `image` field, declared PNG/JPEG content type, extension, and byte size. Invalid input returns HTTP 400 with `invalid_document_upload`. |
| Capture source count | 5 | Capture intake rejects a larger repeated `images` set with HTTP 400. |
| Capture aggregate upload | 25 MiB | Capture intake sums declared source lengths and rejects a larger request with HTTP 400. Kestrel and multipart parsing use the larger configured upload limit plus 256 KiB of multipart overhead. |
| One capture source | 10 MiB | Source processing validates each buffered source independently. An invalid source becomes `invalid_capture_source` inside the HTTP 200 aggregate so valid siblings can continue. |
| Decoded source dimensions | 12,000 x 12,000 and 50 million pixels | Identification and post-EXIF-orientation dimensions are checked before region detection. Oversized or mismatched images fail only their source. |
| Detector proposals | 20 per source | Deterministic validation bounds the proposals retained from one source. |
| Processed capture members | 30 per capture | Overflow regions are rejected before classification, so model fan-out remains bounded. |

The host buffers capture upload bytes before starting the workflow, but the aggregate byte limit bounds that buffer. Source lanes decode at most one source at a time and dispose the oriented high-resolution image after accepted crops have been produced. With the defaults, at most two source lanes decode concurrently. A 50-million-pixel RGBA source can occupy roughly 200 MB before derivatives and encoded crops, so the theoretical local worst case is intentionally high. Any hosted deployment must choose smaller pixel, source, and concurrency limits from measured memory capacity rather than copying these local defaults.

Accepted crops remain as encoded request-scoped PNG values until member processing completes. Their count is bounded by `MaxMembersPerCapture`; images, crops, region overrides, and results are never persisted.

## Concurrency, Timeouts, Retries, and Cancellation

`MaxConcurrentSources` and `MaxConcurrentMembers` create fixed MAF lane counts, currently two source lanes and four member lanes. Each lane handles its assigned work sequentially. The graph does not create a node or unbounded task for every upload or detected rectangle.

Each configured model role currently has a 60-second operation timeout, two retry attempts after the initial attempt, and a 500 ms base delay. The OpenAI SDK retry policy is disabled; the project-owned retry loop handles transient transport failures and status codes 408, 429, 500, 502, 503, and 504. The single operation timeout covers the initial call, retry delays, and retry attempts together. Backoff delays are 500 ms and 1,500 ms with the default policy.

There is no separate server-wide deadline for a whole capture. The browser aborts the local single-document request after 65 seconds and capture request after 180 seconds. Client disconnect or explicit cancellation propagates through active and pending lanes and model calls. Cancellation is not converted into a normal partial result.

Failure boundaries are deliberate:

- missing model configuration prevents the request from starting and uses the API error contract;
- source decode, region detection, and region-validation failures remain isolated to that source;
- ordinary document member failures remain isolated to that member;
- cancellation aborts the request; and
- independently trustworthy sibling sources and members continue after non-cancellation failures.

## Correlation and Workflow Events

Both API endpoints create a logging scope with the ASP.NET `TraceIdentifier` and the optional caller-provided `X-Correlation-ID`. The capture endpoint also passes the HTTP trace identifier into `CompositeCaptureRequest`, so capture workflow events use the same trace value instead of an unrelated generated identifier.

Capture events carry the applicable `traceId`, `captureId`, caller `sourceId`, `sourceItemId`, and `memberId`. They cover capture start, source completion and aggregation, member start and completion, and capture completion. `CompositeCaptureWorkflow` writes these fields as structured debug logs. Document routing emits category, filename, source ID, and selected destination events inside the same HTTP logging scope; MAF executor events keep each selected child workflow inspectable.

Capture IDs and member IDs are request-scoped diagnostics. They are not retrieval keys and do not imply persistence.

## Model Usage Accounting

A document result contains one classification usage record, one extraction usage record, and at most one repair-extraction usage record. Unsupported documents contain classification usage only. A capture result adds every source-detection call and every member result call exactly once; source overrides add no detector usage.

`DocumentModelUsage.FromCalls` sums only known values. Total model duration is the sum of semantic model-operation durations, including retry time inside a successful operation. It is not capture wall-clock latency when lanes run concurrently. Provider attempts that fail without returning usage metadata cannot be estimated safely and therefore do not create invented token or cost records.

## API and OpenAPI Contract

Both processing endpoints declare their 200, 400, 500, 502, and 504 response schemas in generated OpenAPI. Capture source/member failures that occur after request acceptance remain inside an HTTP 200 aggregate as documented in the [API error contract](api-error-contract.md).

Runtime response mapping covers receipt, shopping-list, Sujiko, and expense-report data. The stable response exposes that value through category-discriminated `document.data`. A .NET OpenAPI schema transformer now generates and registers all four data schemas and applies them to `data` with `oneOf`; the property description records the mapping from the enclosing `document.category` value to each shape.

This transformer was chosen over a typed response-envelope hierarchy. A hierarchy would require new public response types and polymorphic serialization rules, risking changes to the existing category and JSON envelope. An OpenAPI discriminator was also rejected at the `data` level because its discriminator property would have to live inside the data object, while the established discriminator is the sibling `document.category`. The transformer documents the runtime contract without changing it, and the nullable `document` schema preserves the normal `document: null` response for unsupported categories.

### OpenAPI Scalability Boundary

The transformer deliberately contains an explicit list of the four supported payload types. Their structures remain in their document-specific domain files; the transformer only identifies which existing types belong in the public union. This is simple and reviewable for the current bounded catalog, but it is not intended to grow into a central file containing hundreds of registrations or document definitions.

Before the catalog grows enough that adding a slice requires repeated central edits, or the combined specification becomes unwieldy for client generators and documentation tools, the next design should be evaluated in this order:

1. Introduce a document-type descriptor registry assembled from slice-owned service registrations. Each slice would contribute its category, payload type, and schema identity/version; the OpenAPI transformer would enumerate the registry instead of naming every type. Routing, extraction, and validation would remain document-specific, with any broader use of the registry accepted as a separate architecture change.
2. Split generated OpenAPI documents by stable domain or API family if a single first-party catalog becomes too large, while retaining a discoverable index and consistent shared envelope.
3. For dynamically installed or third-party document types, move to an explicitly versioned extensibility contract that returns a schema identifier and exposes schemas through a catalog. The base response could then keep `data` extensible without rebuilding one static union for every plug-in.

If future clients require a generated discriminated union across a large stable catalog, a versioned contract may instead place `oneOf` and the discriminator at the whole `document` object, where `category` actually resides. That would be a public contract design rather than a transparent schema fix and must not be introduced by extending the current transformer silently.

## Verification

Offline coverage verifies route topology, child workflow completion, capture correlation fields, exact detection/classification/extraction/repair accounting, failure isolation, cancellation, configured bounds, API error responses, and OpenAPI status schemas. The release workflow remains:

```powershell
dotnet restore .\MafDocumentProcessor.sln
dotnet test .\MafDocumentProcessor.sln
node --test .\tests\ui\capture-ui.test.cjs
dotnet list .\MafDocumentProcessor.sln package --vulnerable --include-transitive
git diff --check
```
