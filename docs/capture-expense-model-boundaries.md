# Capture and Expense Report Model Boundaries

## Status

Accepted E0 boundary and result-semantics definition. Composite capture is implemented. The E4 expense-report slice implements the extraction, validation, repair, policy, and attestation contract below without persistent receipt linking or claim submission.

## Decision

Models may identify visual regions, categorize cropped documents, and extract visible fields. Project-owned deterministic code validates every model output and owns routing, arithmetic, policy, review, aggregation, error conversion, and presentation disposition.

No model decides whether a source, region, expense report, or batch is valid, accepted, rejected, approved, or submitted.

## Responsibility Matrix

| Stage | Model-backed responsibility | Deterministic project responsibility | Output |
| --- | --- | --- | --- |
| Request intake | None | Validate multipart structure, source count, filenames, media types, byte limits, and aggregate limits | Accepted request or API intake error |
| Source decoding | None | Decode once, apply EXIF orientation, record dimensions, and enforce decoded-memory limits | Oriented high-resolution source |
| Region detection | Propose document bounds, optional outlines, and advisory confidence from a layout-sized source image | Parse the response and reject untrusted or unusable geometry | Proposed regions plus model usage |
| Region validation | None | Validate normalized coordinates, mapped pixels, useful area, duplication, overlap, containment, member limits, and deterministic ordering | Accepted and rejected regions |
| Cropping | None | Crop accepted regions from the oriented high-resolution source and create derived member requests | Classification-ready member image |
| Classification | Return category, confidence, description, and reasoning for one crop | Parse the response, apply confidence guidance, and route by the parsed category | `DocumentClassification` plus model usage |
| Document routing | None | Use the reusable top-level MAF route to select exactly one supported or unsupported destination | Selected document workflow |
| Field extraction | Return only visible fields for the routed document contract | Parse JSON and reject malformed or contract-incompatible values | Typed candidate document plus model usage |
| Structural validation | None | Apply document-specific required-field, range, format, arithmetic, and consistency rules | `ValidationResult` |
| Repair | Re-extract once with deterministic validation reasons when the document remains visibly repairable | Decide whether repair is allowed, bound it to one attempt, revalidate, and account for the call | Final typed candidate and validation |
| Policy and attestation | None | Apply receipt or expense policy, confidence review, and ownership-attestation rules | Review/policy outcome |
| Batch aggregation | None | Isolate failures, aggregate sources and members, sum model usage exactly once, and calculate capture/source status | Capture result |
| Member disposition | None | Calculate `Accepted`, `Review`, or `Rejected` from trusted result state | Overlay and textual disposition |

MAF supplies the workflow execution, typed edges, fan-out/fan-in, events, and conditional routing selected in [Capture and expense report MAF capability selection](capture-expense-maf-capabilities.md). Interfaces, prompts, parsers, validators, policies, result records, and disposition rules remain project-owned.

## Region Detection Boundary

### Model contract

Region detection uses a project interface such as `IDocumentRegionDetector`. Its model-backed implementation receives one layout-sized, orientation-normalized source and returns compact structured data containing:

- normalized axis-aligned bounds for every proposed physical document;
- an optional four-point normalized outline;
- optional advisory detection confidence; and
- no document category or extracted business fields.

Detection deliberately does not categorize a whole desk image. Categorization occurs once per accepted crop through the existing `IDocumentClassifier` boundary.

### Configured role

Add an `AiModels:DocumentRegionDetection` role and an image-preprocessing purpose for region detection. The role may initially use the same provider and model ID as classification and extraction, but it remains independently configurable because layout resolution, timeout, retry, pricing, and future model choice may differ.

Expense-report extraction initially uses the existing `DocumentExtraction` role. A different prompt and parser are not sufficient reasons for another role; add one only if expense reports later require materially different operational settings.

### Validation and retries

Detector output is untrusted. JSON parsing and every geometry decision are deterministic. The initial slice makes one semantic detection call per valid source. Provider transport retries remain bounded by role configuration, but there is no hidden model repair loop for malformed or low-quality region sets.

A source with an invalid detection response or no usable regions returns a source failure. Valid sibling sources continue. A later evaluation may justify one explicit detector repair attempt, but that would require its own observable usage, latency, test, and bounded-route decision.

## Classification and Routing Boundary

Every accepted crop uses the same classifier interface, prompt contract, parser, confidence guidance, and top-level category route as an individually uploaded document.

The classifier will add `ExpenseReport` to its supported output values when the E4 slice is implemented. It does not know whether the crop came from an individual request or a batch. Batch metadata such as `captureId`, `sourceItemId`, and `memberId` travels alongside the crop for correlation rather than changing classification behaviour.

Routing is deterministic. A model cannot select an executor or MAF edge directly. The parsed `DocumentCategory` is the typed value used by conditional routing, and exactly one category destination must receive each member.

Low or missing classification confidence follows the existing human-review guidance. It does not trigger repeated classification calls.

## Expense Report Extraction Boundary

The model extracts only values visible on the expense report. The E4 implementation contract may refine field names and optionality, but the candidate data is expected to include:

- report number or title when present;
- claimant or employee name when visible;
- reporting-period dates;
- stated currency and claimed total;
- expense lines with visible date, description, category, amount, and receipt reference where present; and
- visible notes or approval fields without inferring approval.

The model must not:

- invent missing lines, receipt references, currencies, or approval;
- calculate acceptance or policy decisions;
- claim that supporting receipts were matched when persistence and linking are absent;
- decide ownership or attest on the user's behalf; or
- submit or recommend submission of a claim.

Parser failures remain model-response failures. Parsed values then pass through deterministic validation.

## Expense Report Validation, Repair, and Review

### Structural and arithmetic validation

Deterministic code owns at least:

- required report identity or title rules selected by the E4 contract;
- finite, non-negative monetary amounts;
- valid and consistently ordered dates;
- three-letter currency format and single-currency consistency unless mixed currency is explicitly designed;
- line-total arithmetic against the stated claimed total, using an explicit rounding tolerance;
- blank, malformed, or duplicate line handling; and
- separation of visible receipt references from any future verified receipt association.

The model is never asked to judge whether its own arithmetic is correct.

### Repair

If parsed expense data is structurally invalid but the visible document could support a correction, deterministic code may make one repair extraction call with the exact validation reasons. The repaired result is parsed and validated once more. There is no second repair, recursive retry, or model-selected retry.

Missing user evidence, ownership, or external policy information is not repairable by re-prompting the model.

### Review and attestation

A structurally valid expense report returns `IsSuccess = true`. Because an expense claim is user-owned, its `HumanReview` requires explicit user attestation that the parsed report is theirs, the visible fields are acceptable, and any later submission is intentional. This produces the batch member `Review` disposition rather than a green `Accepted` disposition until attestation exists.

Policy or confidence concerns add review reasons without converting trustworthy structured data into an API failure. A report still structurally invalid after repair returns `IsSuccess = false`, validation reasons in `Errors`, and a `Rejected` member disposition.

The initial result must describe extracted and validated report data, not an approved, reimbursable, matched, or submitted claim.

## Capture, Source, and Member Outcomes

| Condition | Boundary outcome | Continue siblings? |
| --- | --- | --- |
| Multipart form missing or capture-wide limits exceeded | Existing request-level API error | No |
| Required model role configuration missing | Existing request-level configuration error | No |
| One source fails filename, media, size, decode, or dimension checks after the request envelope is accepted | Failed source with nested error | Yes |
| One source detector times out, fails, or returns invalid JSON | Failed source with nested model error | Yes |
| Detector returns no usable region | Failed source with readable source error | Yes |
| Region is out of bounds, negligible, or a duplicate | Rejected member with `invalid_detected_region` | Yes |
| Distinct regions overlap but remain usable | Process both; attach review warnings | Yes |
| Crop classification is unsupported | Normal member result with `IsSuccess = false` and `Rejected` disposition | Yes |
| One member classifier or extractor fails | Failed member with nested model/processing error | Yes |
| Expense report remains structurally invalid after repair | Normal expense result with `IsSuccess = false` and validation errors | Yes |
| Expense report is valid but needs attestation or policy review | Normal expense result with `IsSuccess = true` and `Review` disposition | Yes |
| Client cancels the request | Propagate cancellation through detection and all child workflows; return no normal partial response | No |

Batch and source status are calculated only from these trusted outcomes. Exceptions are never passed to a model for interpretation.

## Disposition Rules

The deterministic batch result stage applies the contract's visual dispositions:

- `Accepted`: child result succeeds, classification meets the normal-confidence threshold, and no region, validation, policy, warning, or human-review concern remains.
- `Review`: usable child result succeeds, but confidence, overlap, warning, policy, or human review requires attention.
- `Rejected`: the region or member fails, the category is unsupported, or the child result has `IsSuccess = false`.

The browser renders the disposition; it does not recalculate it. This keeps API clients, logs, tests, and annotated previews consistent.

## Usage, Concurrency, and Cancellation

- Each source has at most one semantic detection call in the initial slice.
- Each member has one classification call, one extraction call for a supported category, and at most one repair extraction.
- Source and member fan-out is bounded by project configuration and the workflow design selected in #12.
- Capture usage contains every known call exactly once; member usage excludes source detection.
- Summed call duration is not presented as wall-clock batch latency when calls overlap.
- The request cancellation token propagates through preprocessing, detection, classification, extraction, repair, and MAF execution.
- Cancellation is not caught and converted into a source or member failure.

## Deferred Boundaries

The following require later explicit decisions:

- persistent source, crop, receipt, or expense-report storage;
- stable identities resolvable after the request;
- linking or matching receipts to expense lines;
- ownership and access control for stored documents;
- retention, deletion, audit, and reprocessing rules;
- external expense-policy systems or claim submission;
- durable pause/resume or reviewer queues; and
- analyst/critic or other agent collaboration.

Until those decisions are accepted, no model or workflow may imply that receipt evidence was verified against an expense report or that a claim was approved or submitted.
