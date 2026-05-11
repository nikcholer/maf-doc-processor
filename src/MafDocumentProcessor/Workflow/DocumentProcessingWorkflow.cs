using MafDocumentProcessor.Configuration;
using MafDocumentProcessor.Domain;
using MafDocumentProcessor.Services;
using Microsoft.Agents.AI.Workflows;

namespace MafDocumentProcessor.Workflow;

public sealed class DocumentProcessingWorkflow(
    IDocumentClassifier classifier,
    IReceiptExtractor receiptExtractor,
    IShoppingListExtractor shoppingListExtractor,
    ReceiptPolicyOptions policyOptions)
{
    public async Task<DocumentProcessingResult> RunAsync(
        FileRequest request,
        CancellationToken cancellationToken = default)
    {
        var classification = await classifier.ClassifyAsync(request, cancellationToken);
        var classifiedDocument = new ClassifiedDocument(
            request,
            DocumentMetadata.FromRequest(
                request,
                classification.Usage.ModelId,
                classification.Value.Confidence),
            classification.Value,
            classification.Usage);

        return classifiedDocument.Classification.Category switch
        {
            DocumentCategory.Receipt => await RunReceiptWorkflowAsync(classifiedDocument, cancellationToken),
            DocumentCategory.ShoppingList => await RunShoppingListWorkflowAsync(classifiedDocument, cancellationToken),
            _ => CreateUnsupportedDocumentResult(classifiedDocument)
        };
    }

    private async Task<DocumentProcessingResult> RunReceiptWorkflowAsync(
        ClassifiedDocument classifiedDocument,
        CancellationToken cancellationToken)
    {
        var extractionExecutor = new ReceiptExtractionExecutor(receiptExtractor);
        var validationExecutor = new ReceiptValidationExecutor();
        var policyExecutor = new ReceiptPolicyExecutor(policyOptions);
        var resultExecutor = new ReceiptResultExecutor();

        var workflow = new WorkflowBuilder(extractionExecutor)
            .AddEdge(extractionExecutor, validationExecutor)
            .AddEdge(validationExecutor, policyExecutor)
            .AddEdge(policyExecutor, resultExecutor)
            .WithOutputFrom(resultExecutor)
            .WithName("Receipt Processing")
            .WithDescription("Extracts, validates, and evaluates a receipt image.")
            .Build();

        return await RunWorkflowAsync(workflow, classifiedDocument, cancellationToken)
            ?? throw new InvalidOperationException(
                "Receipt workflow completed without a document processing result.");
    }

    private async Task<DocumentProcessingResult> RunShoppingListWorkflowAsync(
        ClassifiedDocument classifiedDocument,
        CancellationToken cancellationToken)
    {
        var extractionExecutor = new ShoppingListExtractionExecutor(shoppingListExtractor);
        var validationExecutor = new ShoppingListValidationExecutor();
        var resultExecutor = new ShoppingListResultExecutor();

        var workflow = new WorkflowBuilder(extractionExecutor)
            .AddEdge(extractionExecutor, validationExecutor)
            .AddEdge(validationExecutor, resultExecutor)
            .WithOutputFrom(resultExecutor)
            .WithName("Shopping List Processing")
            .WithDescription("Extracts and validates shopping list items from an image.")
            .Build();

        return await RunWorkflowAsync(workflow, classifiedDocument, cancellationToken)
            ?? throw new InvalidOperationException(
                "Shopping list workflow completed without a document processing result.");
    }

    private static async Task<DocumentProcessingResult?> RunWorkflowAsync(
        Microsoft.Agents.AI.Workflows.Workflow workflow,
        ClassifiedDocument classifiedDocument,
        CancellationToken cancellationToken)
    {
        var run = await InProcessExecution.RunAsync(
            workflow,
            classifiedDocument,
            cancellationToken: cancellationToken);

        return run.NewEvents
            .OfType<WorkflowOutputEvent>()
            .Select(evt => evt.Data)
            .OfType<DocumentProcessingResult>()
            .LastOrDefault();
    }

    private static DocumentProcessingResult CreateUnsupportedDocumentResult(ClassifiedDocument document)
    {
        var message = BuildUnsupportedDocumentMessage(document.Classification);

        return new DocumentProcessingResult(
            document.Classification.Category,
            document.Metadata,
            document.Classification,
            DocumentModelUsage.FromCalls([document.ClassificationUsage]),
            Receipt: null,
            ShoppingList: null,
            PolicyResult: null,
            ValidationResult.Invalid(message),
            IsSuccess: false,
            Errors: [message],
            Warnings: []);
    }

    private static string BuildUnsupportedDocumentMessage(DocumentClassification classification)
    {
        var description = NormalizeDocumentTypeDescription(classification);
        var article = GetIndefiniteArticle(description);

        return $"This appears to be {article} {description}. This demo can process receipts and shopping lists right now.";
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
