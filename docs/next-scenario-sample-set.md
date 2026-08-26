# Composite Capture and Expense Report Sample Set

## Purpose

This sample set gives the next two application phases agreed examples to build and test against:

- composite capture: one or more uploaded images containing zero, one, or several physical documents; and
- expense reports: the next supported business document type.

Each example says which source image to use and what should happen. For a composite image, that includes the expected document bounds, classification, child-workflow result, and accepted/review/rejected disposition. For an expense report, it includes the saved extraction values, arithmetic result, bounded-repair expectation, and review state.

The set contains only fictional content. It is versioned with the repository so a changed image, coordinate, or expected outcome is visible in review.

## Where It Lives

The files are under `tests/MafDocumentProcessor.Tests/next-scenario-samples`:

```text
manifest.json          Asset list, expected regions, and expected outcomes
sources/               PNG fixtures plus one deliberately invalid JPEG
source-definitions/    Project-authored SVG sources for deterministic fixtures
render-fixtures.ps1    Windows renderer for the SVG sources
generation-notes.md    Provenance and prompt for the generated natural desk photo
```

`manifest.json` records the SHA-256 hash and origin of every asset. The normal offline test suite checks those hashes, confirms the PNG dimensions, checks every normalized rectangle, and proves that the required scenario types are present.

## Composite Capture Cases

| Case | What it protects | Expected result |
| --- | --- | --- |
| Single source, single receipt | Existing one-document meaning inside the capture envelope | One accepted receipt; capture succeeds |
| Natural desk with three documents | A realistic multi-document photograph | Two receipts and one shopping list are accepted |
| Multiple source files | Multipart ordering and independent source handling | Three members across two sources; unsupported ticket makes the capture partial |
| Overlapping receipts | Overlap is visible but does not automatically discard usable documents | Both receipts process and require review |
| Duplicate detector regions | Near-identical bounds are not charged and processed twice | One accepted receipt and one rejected duplicate |
| Receipt plus event ticket | Unsupported members use the normal document result | Receipt accepted, ticket rejected, capture partially succeeds |
| Invalid source plus valid receipt | One bad file does not discard a trustworthy sibling | Failed source and successful receipt; capture partially succeeds |
| Empty desk | A successful detector call can still find nothing useful | No members; capture fails clearly |
| Low detection confidence | Detector confidence remains advisory | Usable receipt result with review disposition |

The rectangles use the coordinate system from the [composite capture contract](composite-capture-contract.md): top-left origin, normalized against the oriented source image. Bounds around the generated desk photograph were recorded after visual inspection; bounds for project-authored SVG fixtures follow their known geometry.

## Expense Report Cases

| Case | Visible facts | Expected result |
| --- | --- | --- |
| Valid report | GBP 18.50 + GBP 30.00 = GBP 48.50 | Structurally valid and successful; review still required for ownership attestation |
| Invalid total | Lines total GBP 48.50 but the report claims GBP 60.00 | Deterministic arithmetic failure; rejected without asking the model to judge its own sum |
| Repairable extraction | The document contains both lines, but the saved first extraction omits one | One bounded repair restores both lines; final structure is valid |
| Review-required report | GBP 480.00 hotel line with no visible receipt reference | Structurally valid; review required for attestation and policy concerns |

These expectations follow the accepted [capture and expense report model boundaries](capture-expense-model-boundaries.md). They do not imply that an expense report is approved, reimbursable, linked to stored receipts, or submitted anywhere.

## What The Set Does Not Prove

The manifest records agreed answers before the features are implemented. It is not yet an end-to-end composite-capture or expense-report test, and it does not claim that a live model will always return the saved classifications or extraction values.

As E3 and E4 are built, their offline tests can supply the manifest's saved detector and model outputs at project interfaces, then compare real application results with the expected semantics. Separate opt-in checks may later measure the configured provider against the same image assets.

## Validation and Regeneration

Run the corpus check alone with:

```powershell
dotnet test .\MafDocumentProcessor.sln --filter FullyQualifiedName~NextScenarioSampleSetTests
```

On Windows, regenerate the project-authored PNG fixtures with:

```powershell
.\tests\MafDocumentProcessor.Tests\next-scenario-samples\render-fixtures.ps1
```

The renderer uses headless Microsoft Edge. It does not replace the generated natural desk photograph or the deliberately invalid source. If a committed asset is intentionally regenerated, inspect it visually, update its expected bounds where necessary, and update the hash in `manifest.json` in the same reviewed change.
