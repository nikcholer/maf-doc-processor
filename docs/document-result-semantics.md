# Document Result Semantics

The workflow returns `DocumentProcessingResult` for every successfully handled document-processing request, including unsupported document types. API or provider failures use the API error contract instead.

## Shared Rules

- `Category` is the classifier result.
- `Classification` records the classifier confidence and reasoning.
- `ModelUsage` includes every model call used by the workflow, including repair attempts.
- `Validation` describes whether the parsed document is structurally usable.
- `HumanReview` describes whether a person should inspect, approve, or attest to the result.
- `Errors` are blocking issues for the returned document type.
- `Warnings` are review or policy reasons that do not prevent returning parsed data.

## API Response Shape

The individual API response preserves one stable JSON envelope. For a supported category, `document.data` contains exactly one of `ReceiptData`, `ShoppingListData`, `SujikoPuzzleData`, or `ExpenseReportData`; the enclosing `document.category` selects the shape. Generated OpenAPI describes those four alternatives with `oneOf` and names the category-to-shape mapping in the property description.

The schema intentionally does not put an OpenAPI discriminator on `data`: the discriminator value is the sibling `document.category`, not a property inside the data object. Moving or duplicating that value would change the established JSON contract. Unsupported categories therefore continue to return `document: null`, while request and provider failures use the separate API error response.

The explicit four-type schema registration is a bounded choice for the current application, not the intended discovery mechanism for a large document catalog. The [OpenAPI scalability boundary](operational-safeguards.md#openapi-scalability-boundary) records when and how this design should evolve.

## Receipt

Receipts are successful when the workflow can return parsed receipt data, even if policy review is required.

| Condition | `IsSuccess` | `Errors` | `Warnings` |
| --- | --- | --- | --- |
| Valid receipt, policy approved | `true` | Empty | Empty |
| Receipt parsed, but policy review is needed | `true` | Empty | Policy or validation review reasons |
| Receipt parsed, but structural validation still fails after repair | `true` | Empty | Validation reasons |

Receipt validation currently checks for a store name, non-negative total, and three-letter currency code when currency is present. Receipt policy currently reviews missing payment method or totals above the configured review threshold.

## Shopping List

Shopping lists are successful only when the parsed list is structurally usable.

| Condition | `IsSuccess` | `Errors` | `Warnings` |
| --- | --- | --- | --- |
| Valid shopping list | `true` | Empty | Empty |
| Shopping list still invalid after repair | `false` | Validation reasons | Empty |

Shopping-list validation currently requires at least one readable item and no blank item names.

## Sujiko Puzzle

Sujiko puzzles are successful only when the starting state is structurally usable.

| Condition | `IsSuccess` | `Errors` | `Warnings` |
| --- | --- | --- | --- |
| Four quadrant totals and valid given cells | `true` | Empty | Empty |
| Puzzle still invalid after repair | `false` | Validation reasons | Empty |

The extracted starting state contains the four required quadrant totals and zero or more given cells. Cell coordinates are 1-based: row `1`, column `1` is the top-left cell. Sujiko validation currently requires positive quadrant totals, given cell rows and columns in the 1-3 grid, given values in the 1-9 range, and no duplicate given-cell locations.

## Expense Report

Expense reports are successful only when the parsed report is structurally usable. A valid report still requires user attestation, so `HumanReview` is `Required` and a capture member receives the `Review` disposition until attestation exists.

| Condition | `IsSuccess` | `Errors` | `Warnings` |
| --- | --- | --- | --- |
| Valid report, policy approved | `true` | Empty | Empty |
| Valid report, high-value or missing receipt-reference policy | `true` | Empty | Empty |
| Report still invalid after repair | `false` | Validation reasons | Empty |

Expense-report validation currently requires a report number or title, at least one readable line, non-negative amounts, a three-letter currency code, consistently ordered dates, and claimed-total arithmetic within 0.01 of the line sum. Policy review currently flags totals or lines above the configured high-value threshold and lines without a visible receipt reference. Policy and attestation never imply that the claim was approved, matched to stored receipts, or submitted.

## Unsupported Document Types

Unsupported documents return a normal workflow response with `IsSuccess=false`, no parsed document payload, and a human-readable error such as:

```text
This appears to be a car registration document. This demo can process receipts, shopping lists, Sujiko puzzles, and expense reports right now.
```

This is not an API failure. It means the system recognized the request and deliberately declined to process that document type.

## Human Review

`HumanReview.Status` is one of:

- `NotRequired`: no human review reasons were identified.
- `Recommended`: processing succeeded, but confidence or non-blocking validation reasons deserve human inspection.
- `Required`: the result has blocking errors, policy review reasons, very low/missing confidence, or user attestation requirements.

The local demo does not pause for review. It returns the review state and reasons immediately so the UI and future review endpoints can display or persist them.
