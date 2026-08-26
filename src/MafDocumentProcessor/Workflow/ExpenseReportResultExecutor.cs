using MafDocumentProcessor.Domain;
using Microsoft.Agents.AI.Workflows;

namespace MafDocumentProcessor.Workflow;

public sealed class ExpenseReportResultExecutor()
    : Executor<ExpenseReportPolicyEvaluation, DocumentProcessingResult>("ExpenseReportResult")
{
    public const string AttestationPrompt = "ownership attestation required";

    public override ValueTask<DocumentProcessingResult> HandleAsync(
        ExpenseReportPolicyEvaluation message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        var extraction = message.ValidatedExtraction.Extraction;
        var classifiedDocument = extraction.ClassifiedDocument;
        var validation = message.ValidatedExtraction.Validation;
        var modelUsage = DocumentModelUsage.FromCalls(
            [classifiedDocument.ClassificationUsage, .. extraction.ExtractionUsages]);
        var errors = validation.IsValid ? [] : validation.Reasons;
        var policyForReview = validation.IsValid
            ? message.PolicyResult
            : null;
        var humanReview = HumanReviewEvaluator.Evaluate(
            classifiedDocument.Classification,
            policyForReview?.Decision,
            policyForReview?.Reasons,
            errors,
            warnings: [],
            requiresUserAttestation: true,
            attestationPrompt: AttestationPrompt);

        return ValueTask.FromResult(new DocumentProcessingResult(
            classifiedDocument.Classification.Category,
            classifiedDocument.Metadata,
            classifiedDocument.Classification,
            modelUsage,
            Receipt: null,
            ShoppingList: null,
            SujikoPuzzle: null,
            extraction.ExpenseReport,
            PolicyResult: null,
            message.PolicyResult,
            validation,
            humanReview,
            IsSuccess: validation.IsValid,
            Errors: errors,
            Warnings: []));
    }
}