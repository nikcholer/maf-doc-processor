using MafDocumentProcessor.Domain;
using Microsoft.Agents.AI.Workflows;

namespace MafDocumentProcessor.Workflow;

public sealed class UnsupportedDocumentResultExecutor()
    : Executor<ClassifiedDocument, DocumentProcessingResult>(ExecutorId)
{
    public const string ExecutorId = "UnsupportedDocumentResult";

    public override ValueTask<DocumentProcessingResult> HandleAsync(
        ClassifiedDocument message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(CreateResult(message));
    }

    public static DocumentProcessingResult CreateResult(ClassifiedDocument document)
    {
        var message = BuildUnsupportedDocumentMessage(document.Classification);

        return new DocumentProcessingResult(
            document.Classification.Category,
            document.Metadata,
            document.Classification,
            DocumentModelUsage.FromCalls([document.ClassificationUsage]),
            Receipt: null,
            ShoppingList: null,
            SujikoPuzzle: null,
            ExpenseReport: null,
            PolicyResult: null,
            ExpensePolicy: null,
            ValidationResult.Invalid(message),
            HumanReviewEvaluator.Evaluate(
                document.Classification,
                policyResult: null,
                errors: [message],
                warnings: []),
            IsSuccess: false,
            Errors: [message],
            Warnings: []);
    }

    private static string BuildUnsupportedDocumentMessage(DocumentClassification classification)
    {
        var description = NormalizeDocumentTypeDescription(classification);
        var article = GetIndefiniteArticle(description);

        return $"This appears to be {article} {description}. This demo can process receipts, shopping lists, Sujiko puzzles, and expense reports right now.";
    }

    private static string NormalizeDocumentTypeDescription(DocumentClassification classification)
    {
        var description = classification.DocumentTypeDescription;
        if (string.IsNullOrWhiteSpace(description))
        {
            description = classification.Category switch
            {
                DocumentCategory.Invoice => "invoice",
                DocumentCategory.ShoppingList => "shopping list",
                DocumentCategory.SujikoPuzzle => "Sujiko puzzle",
                DocumentCategory.Unknown => "unsupported document",
                _ => classification.Category.ToString()
            };
        }

        description = description.Trim().TrimEnd('.');
        foreach (var prefix in new[] { "a ", "an ", "the " })
        {
            if (description.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                description = description[prefix.Length..];
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            description = "unsupported document";
        }

        return char.ToLowerInvariant(description[0]) + description[1..];
    }

    private static string GetIndefiniteArticle(string description)
    {
        return description.Length > 0
            && "aeiou".Contains(char.ToLowerInvariant(description[0]))
            ? "an"
            : "a";
    }
}
