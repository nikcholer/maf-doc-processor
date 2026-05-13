using MafDocumentProcessor.Domain;
using Microsoft.Agents.AI.Workflows;

namespace MafDocumentProcessor.Workflow;

public sealed class SujikoPuzzleResultExecutor()
    : Executor<ValidatedSujikoPuzzleExtraction, DocumentProcessingResult>("SujikoPuzzleResult")
{
    public override ValueTask<DocumentProcessingResult> HandleAsync(
        ValidatedSujikoPuzzleExtraction message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        var extraction = message.Extraction;
        var classifiedDocument = extraction.ClassifiedDocument;
        var modelUsage = DocumentModelUsage.FromCalls(
            [classifiedDocument.ClassificationUsage, .. extraction.ExtractionUsages]);
        var errors = message.Validation.IsValid ? [] : message.Validation.Reasons;
        var humanReview = HumanReviewEvaluator.Evaluate(
            classifiedDocument.Classification,
            policyResult: null,
            errors,
            warnings: []);

        return ValueTask.FromResult(new DocumentProcessingResult(
            classifiedDocument.Classification.Category,
            classifiedDocument.Metadata,
            classifiedDocument.Classification,
            modelUsage,
            Receipt: null,
            ShoppingList: null,
            extraction.SujikoPuzzle,
            PolicyResult: null,
            message.Validation,
            humanReview,
            IsSuccess: message.Validation.IsValid,
            Errors: errors,
            Warnings: []));
    }
}
