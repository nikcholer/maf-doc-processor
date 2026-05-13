# Adding A Document Type

This guide describes the current path for adding a new supported document type to the local MAF document processor.

Use receipts and shopping lists as the working examples. A new type should follow the same shape unless there is a clear reason to do otherwise.

## Before Coding

Decide these first:

- The classifier category name, e.g. `WarrantyCard`.
- The output data contract, including required vs optional fields.
- The validation rules that make the parsed document structurally usable.
- Whether policy/review rules are needed beyond validation.
- The result semantics: should validation failures be blocking `Errors`, non-blocking `Warnings`, or policy review reasons?
- Whether the document requires user ownership or attestation. See [human-review-policy.md](human-review-policy.md).

## Code Checklist

1. Add the category to `DocumentCategory`.

   File: `src/MafDocumentProcessor/Domain/DocumentCategory.cs`

2. Add domain records for the extracted document data.

   Put these in `src/MafDocumentProcessor/Domain/`. Keep records small and serializable. Use nullable fields for optional visible-but-not-always-present values.

3. Update classification prompting and parsing.

   Files:

   - `src/MafDocumentProcessor/Services/ModelDocumentClassifier.cs`
   - `src/MafDocumentProcessor/Services/ModelResponseParsers.cs`

   Include the new category in the classifier JSON contract and add plain-text fallback parsing when useful.

4. Add parser support for the new extraction JSON.

   File: `src/MafDocumentProcessor/Services/ModelResponseParsers.cs`

   Follow `ParseReceipt` or `ParseShoppingList`: fail clearly with `DocumentModelResponseException` when required fields are missing or invalid.

5. Add an extractor interface and model extractor.

   Files:

   - `src/MafDocumentProcessor/Services/I{Type}Extractor.cs`
   - `src/MafDocumentProcessor/Services/Model{Type}Extractor.cs`

   Include optional `repairInstructions` so validation repair can call back into the model with precise failures.

6. Add workflow records and executors.

   Put these in `src/MafDocumentProcessor/Workflow/`:

   - `{Type}Extraction`
   - `Validated{Type}Extraction`
   - `{Type}ExtractionExecutor`
   - `{Type}ValidationExecutor`
   - `{Type}ValidationRepairExecutor`
   - `{Type}ResultExecutor`
   - Optional: `{Type}PolicyExecutor` and `{Type}PolicyEvaluation`

   Model-call executors should link the workflow cancellation token with the executor token, matching the receipt and shopping-list executors.

7. Route the category in `DocumentProcessingWorkflow`.

   File: `src/MafDocumentProcessor/Workflow/DocumentProcessingWorkflow.cs`

   Add a `Run{Type}WorkflowAsync` method and route the new `DocumentCategory` in the classification switch.

8. Register services in the API host.

   File: `src/MafDocumentProcessor.Api/Program.cs`

   Register the new extractor interface and implementation using the configured `DocumentExtraction` model role unless the new type needs a dedicated role.

9. Extend `DocumentProcessingResult` and response mapping.

   Files:

   - `src/MafDocumentProcessor/Domain/DocumentProcessingResult.cs`
   - `src/MafDocumentProcessor.Api/Services/DocumentProcessingResponseMapper.cs`

   Add a nullable property for the new data and map it in `GetDocumentData`.

10. Update UI rendering if the new type needs a custom field table.

    File: `src/MafDocumentProcessor.Api/wwwroot/app.js`

    The raw JSON view should work automatically if the API response includes the new document data. Add a tailored summary only when it helps the demo.

11. Add tests.

    Minimum useful set:

    - Parser tests for valid and invalid extraction JSON.
    - Workflow routing test from classification to the new extractor.
    - Validation repair test when the first extraction fails validation and the second succeeds.
    - API integration test if response shape or error behavior changes.

12. Update docs.

    Files:

    - `README.md`
    - `docs/document-result-semantics.md`
    - `docs/human-review-policy.md`, if ownership/review rules differ
    - `docs/maf-migration-backlog.md`, if this completes or changes a backlog item

## Implementation Pattern

The current happy path is:

```text
upload
  -> classify image
  -> route by DocumentCategory
  -> extract typed data
  -> validate typed data
  -> one repair extraction if validation fails
  -> optional policy/review executor
  -> DocumentProcessingResult
  -> API response mapper
```

Unsupported categories should stay explicit. A recognized but unsupported type should return a normal workflow response with `IsSuccess=false` and a human-readable message, not a provider/API error.

## When To Add A Dedicated Model Role

Start with `DocumentExtraction`. Add a dedicated config role only when the new type needs materially different:

- Model id.
- Timeout or retry policy.
- Token pricing.
- Prompt protocol behavior.
- Image preprocessing requirements.

If a dedicated role is added, update `AiModelSettings`, `AiModelSettingsDefaults`, `ApiConfigurationLoader`, `appsettings.json`, README configuration notes, and tests.

## Done Criteria

A new document type is ready when:

- It can be classified from at least one real sample image.
- It returns structured typed data in the API response.
- Invalid model output fails with a clear `model_response_invalid` path.
- Structural validation failures either repair once or return documented errors/warnings.
- Token usage, model latency, and estimated cost still appear in the UI.
- Tests cover parser behavior, workflow routing, and the API surface touched by the change.
