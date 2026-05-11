using MafDocumentProcessor.Domain;
using Microsoft.Agents.AI.Workflows;

namespace MafDocumentProcessor.Workflow;

public sealed class ShoppingListResultExecutor()
    : Executor<ValidatedShoppingListExtraction, DocumentProcessingResult>("ShoppingListResult")
{
    public override ValueTask<DocumentProcessingResult> HandleAsync(
        ValidatedShoppingListExtraction message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        var extraction = message.Extraction;
        var classifiedDocument = extraction.ClassifiedDocument;
        var modelUsage = DocumentModelUsage.FromCalls(
            [
                classifiedDocument.ClassificationUsage,
                extraction.ExtractionUsage
            ]);

        return ValueTask.FromResult(new DocumentProcessingResult(
            classifiedDocument.Classification.Category,
            classifiedDocument.Metadata,
            classifiedDocument.Classification,
            modelUsage,
            Receipt: null,
            extraction.ShoppingList,
            PolicyResult: null,
            message.Validation,
            IsSuccess: message.Validation.IsValid,
            Errors: message.Validation.IsValid ? [] : message.Validation.Reasons,
            Warnings: []));
    }
}
