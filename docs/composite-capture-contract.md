# Composite Capture Contract

## Status

This document defines the accepted contract for composite document capture. Shared types, configuration, source decoding/orientation, one-call automatic region detection, caller-corrected region overrides, deterministic region validation, high-resolution cropping, bounded source/member orchestration, `POST /api/document-captures/process`, annotated capture previews, and ephemeral rectangle editing in the local UI are implemented. The existing single-document API and UI mode remain available.

Model and deterministic responsibilities are defined in [Capture and expense report model boundaries](capture-expense-model-boundaries.md).

Bounded workflow orchestration is defined in [Capture and expense report MAF capability selection](capture-expense-maf-capabilities.md).

## Purpose

Composite capture accepts one or more image files, each of which may contain zero, one, or several physical documents. It detects document regions before categorization, crops them from each high-resolution source, and processes every valid crop independently through the same classification and document workflow used for an individual upload.

The feature adds an intake and aggregation envelope. It does not add a `CompositeCapture` business document category and does not duplicate receipt, shopping-list, Sujiko, invoice, or unsupported-document logic.

## HTTP Boundary

The initial feature is additive:

- `POST /api/documents/process` retains its existing one-upload/one-document contract.
- `POST /api/document-captures/process` accepts one or more PNG or JPEG files in a repeated `images` multipart field, an optional request-level `sourceId`, and an optional `regionOverrides` JSON field.
- The capture endpoint always returns the capture response shape, whether the request contains one or several source images and whether detection finds zero, one, or several regions in each source.

Using a separate endpoint prevents region detection from adding latency and cost to existing individual uploads. A future UI may choose the endpoint explicitly or offer automatic mode selection, but that is not part of this contract.

Request-level intake failures continue to use the existing API error contract. No files, too many files, or an excessive aggregate request size fail the request before processing. Once the multipart request is accepted, a source-specific validation or detection failure is isolated to that source so valid sibling images can still be processed.

`regionOverrides` uses one-based multipart source indexes and contains only the sources the caller intends to correct:

```json
{
  "sources": [
    {
      "sourceIndex": 1,
      "regions": [
        {
          "bounds": { "x": 0.1, "y": 0.2, "width": 0.6, "height": 0.5 }
        }
      ]
    },
    { "sourceIndex": 2, "regions": [] }
  ]
}
```

A listed source bypasses the region detector, including when its `regions` array is explicitly empty. An omitted source follows automatic detection. Optional four-point `outline` values use the same normalized oriented coordinate space as response outlines. Structural JSON errors, duplicate/out-of-range source indexes, missing bounds, a non-quadrilateral outline, or too many supplied regions fail the request with `invalid_document_upload` targeted at `regionOverrides`. Numeric geometry remains untrusted and flows through the same deterministic validation used for detector output.

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
| `detection` | Detector model identifier, region count before and after validation, detection warnings, and `usedRegionOverrides` indicating that the detector was bypassed |
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

The browser renders overlays in a `0`–`100` vector coordinate space directly over each locally retained image, so the API's normalized coordinates scale with the preview at every responsive size. Modern browser image decoding applies EXIF orientation before display; the UI also compares the preview aspect ratio with the API's oriented source dimensions and surfaces a warning when they disagree. Pure UI-model tests cover bounds and outline mapping at desktop, mobile, and portrait/rotated dimensions, while API/image tests retain server-side EXIF coverage.

After a result, the browser can put an individual source into rectangle-edit mode. The user may add, delete, reorder, drag, resize with four corner handles, use arrow keys to move, use `Alt` plus arrow keys to resize, or enter normalized coordinates. **Reprocess corrected regions** resubmits the same selected files and serializes only edited sources into `regionOverrides`; unedited sources still use automatic detection. These edits live only in browser memory for the current file selection and are not a persisted reviewer state.

## Deterministic Region Validation

Uploaded sources, model-produced regions, and caller-supplied overrides are untrusted. Before any crop is classified:

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

The implemented source hand-off retains the decoded, oriented high-resolution image in memory for the crop stage. The detector receives a JPEG derivative whose longest edge is limited by `RegionDetectionMaxLongEdgePixels`; producing that derivative clones the in-memory image and does not decode the upload again. Accepted regions are cropped from that oriented source using mapped pixel bounds, then encoded as PNG `FileRequest` values for the existing classification and extraction preprocessors. The source image is disposable request-scoped state and is never persisted.

Region validation is deterministic. It converts untrusted `ProposedNormalizedBounds` into `NormalizedBounds` only when coordinates are finite, contained in the `0`–`1` image space, and above the configured useful-region thresholds. It then maps those bounds onto the oriented source by rounding opposite edges independently, rejects empty pixel crops, orders remaining regions top-to-bottom then left-to-right, drops near-duplicates at `DuplicateIntersectionOverUnionThreshold`, caps accepted members at the smaller of `MaxDetectedRegionsPerSource` and `MaxMembersPerCapture`, expands each accepted box by `RegionEdgePadding` on every side (clamped to the image), and records `detected regions overlap` when distinct retained regions exceed `OverlapReviewIntersectionOverUnionThreshold`. A little neighbouring paper in a crop is acceptable: classification and extraction are instructed to use the main document occupying most of the image, including its centre. Invalid, duplicate, empty, and overflow regions become `invalid_detected_region` results. A successful detection that yields no accepted crop becomes `no_usable_document_region`.

`DocumentRegionDetection` is a separate configured model role. It makes one semantic call for each source without an override that passes declared-type, extension, byte-size, decoded-format, and dimension checks. A source with an override is still decoded and orientation-normalized, but its caller-supplied proposals replace detector output and contribute no detection model usage. Detection and override proposals both pass through the same padding, duplicate/overlap, crop, useful-area, and member-limit policy. The detector returns only bounds, an optional four-point outline, and advisory confidence. The parser preserves numeric out-of-range proposals for the deterministic validator rather than treating model coordinates as trusted geometry.

An invalid source returns `invalid_capture_source` without a model call. Invalid detector JSON, timeouts, and provider failures become source-specific `model_response_invalid`, `model_timeout`, or `model_provider_failed` results, allowing sibling sources to continue. Missing model configuration remains a request-level failure. Request cancellation is propagated rather than converted into a partial result.

This ordering prevents small text from being lost by shrinking the whole desk image to extraction dimensions before cropping. It also keeps classification and extraction image settings reusable at member level.

The implementation must bound upload dimensions, decoded memory, region count, and member concurrency. It must not start an unbounded task for every model-proposed region.

## Initial Configuration

The `CompositeCapture` section in `appsettings.json` holds the limits used by the capture endpoint and workflow. The application validates these values at startup, so an impossible or unbounded value fails clearly instead of being discovered after an upload begins.

| Setting | Initial value | What it limits |
| --- | ---: | --- |
| `MaxSourceCount` | 5 | Image files in one capture request |
| `MaxSourceBytes` | 10 MiB | Uploaded bytes for one source image |
| `MaxAggregateBytes` | 25 MiB | Uploaded bytes across the whole capture |
| `MaxSourceWidthPixels`, `MaxSourceHeightPixels` | 12,000 each | Oriented source dimensions |
| `MaxSourcePixelCount` | 50,000,000 | Decoded pixels in one source, including unusual aspect ratios |
| `MaxDetectedRegionsPerSource` | 20 | Detector proposals retained for validation from one source |
| `MaxMembersPerCapture` | 30 | Documents that may continue across all sources |
| `MinRegionWidth`, `MinRegionHeight`, `MinRegionArea` | 0.02, 0.02, 0.0025 | Smallest useful normalized document region |
| `DuplicateIntersectionOverUnionThreshold` | 0.90 | Similarity at which two proposals are treated as the same region |
| `OverlapReviewIntersectionOverUnionThreshold` | 0.10 | Distinct overlap that should be surfaced for review |
| `RegionEdgePadding` | 0.03 | Extra normalized margin added on every side of an accepted crop |
| `MaxConcurrentSources`, `MaxConcurrentMembers` | 2, 4 | Fixed source and document processing lanes |

These are safe local starting values, not business constants. Request intake, source decoding, region validation, and the fixed capture workflow lanes enforce the byte, image, geometry, duplicate, overlap, member, and concurrency limits. The measured concurrency trade-off is recorded in [composite capture measurements](composite-capture-measurements.md); decoded-memory implications and failure boundaries are recorded in [observability and operational safeguards](operational-safeguards.md).

## Model Usage and Correlation

- Detection uses a distinct operation name such as `document_region_detection` for each valid source image.
- A member's result contains only that member's classification, extraction, and bounded repair calls.
- Capture-level model usage includes every known source-detection call and each member call once; it must not add a member's already-aggregated total as another call.
- Summed model-call duration retains the existing usage meaning and is not presented as wall-clock capture latency when member calls run concurrently.
- The capture endpoint passes its HTTP `traceId` into the workflow; capture events and structured debug logs retain it with the generated `captureId`, caller `sourceId`, `sourceItemId`, and `memberId` where applicable.
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
