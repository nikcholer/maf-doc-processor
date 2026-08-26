using MafDocumentProcessor.Domain;
using Microsoft.Agents.AI.Workflows;

namespace MafDocumentProcessor.Workflow;

public sealed class ReceiptResultExecutor()
    : Executor<ReceiptPolicyEvaluation, DocumentProcessingResult>("ReceiptResult")
{
    public override ValueTask<DocumentProcessingResult> HandleAsync(
        ReceiptPolicyEvaluation message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        var extraction = message.ValidatedExtraction.Extraction;
        var classifiedDocument = extraction.ClassifiedDocument;
        var modelUsage = DocumentModelUsage.FromCalls(
            [classifiedDocument.ClassificationUsage, .. extraction.ExtractionUsages]);

        var warnings = message.Validation.IsValid ? [] : message.Validation.Reasons;
        var humanReview = HumanReviewEvaluator.Evaluate(
            classifiedDocument.Classification,
            message.PolicyResult,
            errors: [],
            warnings);

        return ValueTask.FromResult(new DocumentProcessingResult(
            classifiedDocument.Classification.Category,
            classifiedDocument.Metadata,
            classifiedDocument.Classification,
            modelUsage,
            extraction.Receipt,
            ShoppingList: null,
            SujikoPuzzle: null,
            ExpenseReport: null,
            message.PolicyResult,
            ExpensePolicy: null,
            message.Validation,
            humanReview,
            IsSuccess: true,
            Errors: [],
            Warnings: warnings));
    }
}
