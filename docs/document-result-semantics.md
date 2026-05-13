# Document Result Semantics

The workflow returns `DocumentProcessingResult` for every successfully handled document-processing request, including unsupported document types. API or provider failures use the API error contract instead.

## Shared Rules

- `Category` is the classifier result.
- `Classification` records the classifier confidence and reasoning.
- `ModelUsage` includes every model call used by the workflow, including repair attempts.
- `Validation` describes whether the parsed document is structurally usable.
- `Errors` are blocking issues for the returned document type.
- `Warnings` are review or policy reasons that do not prevent returning parsed data.

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

## Unsupported Document Types

Unsupported documents return a normal workflow response with `IsSuccess=false`, no parsed document payload, and a human-readable error such as:

```text
This appears to be a car registration document. This demo can process receipts and shopping lists right now.
```

This is not an API failure. It means the system recognized the request and deliberately declined to process that document type.
