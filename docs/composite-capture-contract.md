# Composite Capture Contract

## Status

This document defines the proposed E0 contract for composite document capture. It is not yet implemented and does not change the existing single-document API.

Model and deterministic responsibilities are defined in [Capture and expense report model boundaries](capture-expense-model-boundaries.md).

## Purpose

Composite capture accepts one or more image files, each of which may contain zero, one, or several physical documents. It detects document regions before categorization, crops them from each high-resolution source, and processes every valid crop independently through the same classification and document workflow used for an individual upload.

The feature adds an intake and aggregation envelope. It does not add a `CompositeCapture` business document category and does not duplicate receipt, shopping-list, Sujiko, invoice, or unsupported-document logic.

## HTTP Boundary

The initial feature is additive:

- `POST /api/documents/process` retains its existing one-upload/one-document contract.
- `POST /api/document-captures/process` accepts one or more PNG or JPEG files in a repeated `images` multipart field and an optional request-level `sourceId`.
- The capture endpoint always returns the capture response shape, whether the request contains one or several source images and whether detection finds zero, one, or several regions in each source.

Using a separate endpoint prevents region detection from adding latency and cost to existing individual uploads. A future UI may choose the endpoint explicitly or offer automatic mode selection, but that is not part of this contract.

Request-level intake failures continue to use the existing API error contract. No files, too many files, or an excessive aggregate request size fail the request before processing. Once the multipart request is accepted, a source-specific validation or detection failure is isolated to that source so valid sibling images can still be processed.

## Processing Invariant

For every valid detected region:

```text
crop from oriented high-resolution source
  -> existing document classification
  -> existing category route
  -> existing document-specific workflow
  -> existing DocumentProcessingResponse mapping
```

The same crop must produce the same document-processing semantics whether it enters through composite capture or is submitted directly to the individual document endpoint. The batch layer may add correlation metadata, but it must not reinterpret the child's classification, validation, policy, human-review, success, error, warning, or model-usage values.

## Coordinate Contract

Detection coordinates refer to the source image after EXIF orientation has been normalized and before classification or extraction resizing.

Each region contains:

| Field | Required | Meaning |
| --- | --- | --- |
| `sourceItemId` | Yes | Request-scoped source-image identifier, such as `source-001` |
| `memberId` | Yes | Request-scoped identifier assigned after deterministic source and region ordering, such as `source-001-document-001` |
| `index` | Yes | One-based position in the capture-wide returned member collection |
| `bounds` | Yes | Normalized axis-aligned rectangle containing `x`, `y`, `width`, and `height` |
| `outline` | No | Four normalized points describing a detected quadrilateral when available |
| `confidence` | No | Detector confidence from `0` to `1`; advisory rather than proof of validity |
| `warnings` | Yes | Region-specific detection or crop warnings, empty when none apply |

Normalized coordinates use a top-left origin. `x` and `width` are fractions of the oriented source width; `y` and `height` are fractions of its height. Required bounds allow deterministic cropping even when an outline is absent. The initial slice may retain an outline for display and diagnostics without implementing perspective correction.

Source images retain multipart order and receive deterministic identifiers. Regions within each source are returned in top-to-bottom, then left-to-right order. Source and member identifiers are stable only within that response and are not persistent document identifiers.

## Proposed Response Shape

The API response is conceptually:

```text
CompositeCaptureProcessingResponse
  CaptureId
  Metadata
  Sources[]
  ModelUsage
  Status
  Members[]
  Errors[]
  Warnings[]

CompositeCaptureSourceResponse
  SourceItemId
  Index
  Metadata
  Detection
  Status
  Errors[]
  Warnings[]

CompositeCaptureMemberResponse
  SourceItemId
  MemberId
  Index
  Region
  Status
  Disposition
  DispositionReasons[]
  Result?
  Error?
```

### Capture fields

| Field | Meaning |
| --- | --- |
| `captureId` | Generated operation identifier used in the response and logs; it cannot be used to retrieve data later |
| `metadata` | Request receipt time, optional caller `sourceId`, source count, and aggregate uploaded byte size |
| `sources` | Original file metadata, oriented pixel dimensions, detection details, and source-level outcomes in multipart order |
| `modelUsage` | Every source-detection call plus all known member model calls, aggregated exactly once |
| `status` | `Succeeded`, `PartiallySucceeded`, or `Failed` |
| `members` | Capture-wide collection with one entry for every accepted or rejected detected region across all sources |
| `errors` | Capture-level failures, such as no usable regions |
| `warnings` | Capture-level concerns that do not prevent processing, such as overlapping documents |

### Source fields

| Field | Meaning |
| --- | --- |
| `sourceItemId`, `index` | Request-scoped identity and multipart ordering |
| `metadata` | Original filename, content type, byte size, oriented pixel dimensions, and receipt time |
| `detection` | Detector model identifier, region count before and after validation, and detection warnings |
| `status` | `Succeeded`, `PartiallySucceeded`, or `Failed`, calculated from that source's members |
| `errors`, `warnings` | Source-level intake, detection, crop, or overlap outcomes |

### Member fields

| Field | Meaning |
| --- | --- |
| `sourceItemId`, `memberId`, `index`, `region` | Request-scoped source, identity, ordering, and location |
| `status` | `Processed` or `Failed` |
| `disposition` | Deterministic presentation outcome: `Accepted`, `Review`, or `Rejected` |
| `dispositionReasons` | Human-readable reasons supporting the disposition, empty for an unqualified acceptance |
| `result` | Existing `DocumentProcessingResponse` when the child workflow produced a normal result |
| `error` | Existing machine-readable error code, message, and optional target when the member could not produce a normal result |

`result` and `error` are mutually exclusive. A recognized but unsupported category is a normal processed result with `result.isSuccess = false`; it is not a member infrastructure error.

Member errors use the existing `ApiErrorResponse` shape and request `traceId`. They reuse `model_response_invalid`, `model_timeout`, `model_provider_failed`, `document_processing_failed`, and `document_processing_unhandled` when the equivalent failure happens inside a child workflow. Composite capture adds `invalid_detected_region` for bounds, duplication, crop, or region-limit failures; its `target` identifies the member or invalid region field.

The child's `DocumentMetadata.SourceId` remains the caller-supplied request-level value. `sourceItemId` and `memberId` provide request-scoped uniqueness without changing the existing document result contract. Derived crop filenames may use the original source stem plus the member identifier for diagnostics.

## Status Semantics

| Condition | Capture status |
| --- | --- |
| At least one member exists, every source completed, and every member result has `isSuccess = true` | `Succeeded` |
| At least one member result has `isSuccess = true`, and at least one source or member failed or returned `isSuccess = false` | `PartiallySucceeded` |
| No member result has `isSuccess = true`, including no usable source or region | `Failed` |

Each source applies the same status rules using only its own members. A source that fails intake validation or region detection is `Failed` even though valid sibling sources may continue.

Partial success is a normal HTTP `200` capture response. Request-level model configuration or failures that prevent the capture envelope from being created use the existing non-`200` API error contract. Once source processing begins, source-specific detection parsing, provider, and timeout failures are isolated to that source so trustworthy sibling results remain available.

After region detection succeeds, a non-cancellation failure in one member is isolated into that member's `error` so independently processable siblings can complete. Request cancellation cancels detection and all active or pending members, then propagates without returning a normal partial response, matching the current cancellation contract.

## Member Disposition and Annotated Preview

`disposition` is computed by the batch result stage, not inferred independently by the browser. It combines region validity, classification confidence, child success, validation, warnings, and human-review state:

| Disposition | Required conditions | Overlay treatment |
| --- | --- | --- |
| `Accepted` | Member processed successfully, classification meets the normal-confidence threshold, no review is recommended or required, and no region/result warning remains | Green bounds and a tick |
| `Review` | Member produced a usable result, but classification or detection confidence is advisory, regions overlap, or validation, policy, warnings, or `HumanReview` require attention | Amber bounds and a question mark |
| `Rejected` | Region is invalid, member processing failed, category is unsupported, or the child result has `isSuccess = false` | Red bounds and a cross |

The response UI displays one annotated preview per source image in multipart order, using a gallery or selectable source list. Each vector overlay uses the same oriented coordinate space as its region contract, preferring `outline` and falling back to `bounds`. Each overlay includes the symbol, member identifier, category when known, and classification confidence when available. Selecting a region reveals that member's extracted data, warnings, errors, and disposition reasons.

Colour is not the only signal: tick, question-mark, and cross symbols have accessible labels, and the textual member list exposes the same disposition. Overlapping regions remain individually selectable and receive a deterministic drawing order.

The canonical API response remains geometry and structured status rather than additional encoded copies of the source images. The browser retains local previews of the selected files, so it can render overlays without another image payload. An optional **Download annotated image** action may rasterize one selected source preview and its vector overlay client-side; the downloaded image is a presentation artifact, not a persisted processing result.

The implementation must test overlay alignment for normal and EXIF-rotated fixtures and at responsive display sizes. The preview, source pixel dimensions, and normalized coordinates must all use the same orientation convention.

## Deterministic Region Validation

Uploaded sources and model-produced regions are untrusted. Before any crop is classified:

- each source must independently pass configured filename, content-type, byte-size, decode, and dimension checks;
- coordinates must be finite and contained within the normalized `0`–`1` image space;
- width, height, and area must exceed configured useful-region thresholds;
- the mapped pixel crop must be non-empty and decodable;
- duplicate or near-duplicate regions must be resolved deterministically;
- containment and overlap must be measured and surfaced rather than silently ignored; and
- the number of processed regions must not exceed a configured maximum.

The exact source-count, per-source size, aggregate size, area, overlap, duplicate, member-count, and concurrency thresholds are configuration decisions, not hard-coded contract values. Documents may physically overlap, so overlap alone is not always invalid. A near-duplicate region may be rejected, while distinct overlapping regions may proceed with warnings or require review.

Every detector-proposed region receives either a processed result or a region/member error. The response records counts before and after validation so rejected detections are observable.

## Image Handling

Each valid source is decoded and orientation-normalized once. Region detection may use a lower-resolution layout derivative, but normalized coordinates are mapped back to that oriented high-resolution source. Each crop then enters the existing classification and extraction preprocessing paths independently.

This ordering prevents small text from being lost by shrinking the whole desk image to extraction dimensions before cropping. It also keeps classification and extraction image settings reusable at member level.

The implementation must bound upload dimensions, decoded memory, region count, and member concurrency. It must not start an unbounded task for every model-proposed region.

## Model Usage and Correlation

- Detection uses a distinct operation name such as `document_region_detection` for each valid source image.
- A member's result contains only that member's classification, extraction, and bounded repair calls.
- Capture-level model usage includes every known source-detection call and each member call once; it must not add a member's already-aggregated total as another call.
- Summed model-call duration retains the existing usage meaning and is not presented as wall-clock capture latency when member calls run concurrently.
- HTTP `traceId`, generated `captureId`, caller `sourceId`, `sourceItemId`, and `memberId` are included in log scopes where available.
- Member failures retain any model usage known before the failure when the provider boundary can report it safely.

## Representative Cases

| Input | Expected outcome |
| --- | --- |
| One clear receipt on a desk | One member; receipt result matches individual processing; capture `Succeeded` |
| Two clear receipts and one shopping list | Three independently classified and processed members; capture `Succeeded` |
| Three separate files, each containing one receipt | Three source entries and three independently processed receipt members; capture `Succeeded` |
| One desk photo with two receipts plus a separate shopping-list image | Two source entries and three members using the same per-document paths; capture `Succeeded` |
| One valid receipt and one unsupported train ticket | Receipt succeeds, ticket returns the normal unsupported result; capture `PartiallySucceeded` |
| One invalid source file beside one valid receipt image | Invalid source is reported, receipt succeeds; capture `PartiallySucceeded` |
| One valid receipt and one member-level provider failure | Receipt succeeds, failed member carries a provider error; capture `PartiallySucceeded` |
| Empty desk or no useful region | No members, readable capture error; capture `Failed` |
| Duplicate detections for the same receipt | One accepted member and an observable rejected duplicate; no duplicate processing charge |
| Two partially overlapping receipts | Both retained when independently usable, with overlap warnings or review state |
| Detector returns out-of-range coordinates | Region rejected deterministically and represented by a member error |
| More regions than the configured maximum | Bounded member set plus a capture error or warning defined by policy; never unbounded processing |
| Client cancels during fan-out | Active work is cancelled and cancellation propagates; no normal partial response |

## Non-Goals

- Persisting source images, crops, document results, capture IDs, or member IDs.
- Retrieving or linking previously processed receipts.
- Implementing the expense-report document type or claim submission.
- Changing the existing individual document endpoint or result semantics.
- Returning a base64 or server-rasterized annotated image inside the JSON response.
- PDF/page ingestion, perspective correction, OCR-specific processing, or arbitrary image stitching.
- Durable checkpointing, reviewer queues, or additional analyst/critic agents.

These may be introduced through separate decisions and work items once their requirements and trust boundaries are explicit.
