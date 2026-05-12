using System.Reflection;
using System.Runtime.ExceptionServices;
using MafDocumentProcessor.Configuration;
using MafDocumentProcessor.Domain;
using MafDocumentProcessor.Services;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MafDocumentProcessor.Workflow;

public sealed class DocumentProcessingWorkflow(
    IDocumentClassifier classifier,
    IReceiptExtractor receiptExtractor,
    IShoppingListExtractor shoppingListExtractor,
    ReceiptPolicyOptions policyOptions,
    IModelImagePreprocessor? imagePreprocessor = null,
    ILogger<DocumentProcessingWorkflow>? logger = null)
{
    private readonly IModelImagePreprocessor _imagePreprocessor =
        imagePreprocessor ?? ModelImagePreprocessor.CreateDefault();
    private readonly ILogger<DocumentProcessingWorkflow> _logger =
        logger ?? NullLogger<DocumentProcessingWorkflow>.Instance;

    public async Task<DocumentProcessingResult> RunAsync(
        FileRequest request,
        CancellationToken cancellationToken = default)
    {
        var classificationImage = await _imagePreprocessor.PreprocessAsync(
            request,
            ModelImagePreprocessingPurpose.Classification,
            cancellationToken);
        var classification = await classifier.ClassifyAsync(
            classificationImage.Request,
            cancellationToken);
        _logger.LogInformation(
            "Document classified as {Category} with confidence {Confidence} using {ModelId}.",
            classification.Value.Category,
            classification.Value.Confidence,
            classification.Usage.ModelId);
        var metadata = DocumentMetadata.FromRequest(
            request,
            classification.Usage.ModelId,
            classification.Value.Confidence);

        _logger.LogInformation(
            "Routing document {FileName} to {Category} workflow.",
            request.FileName,
            classification.Value.Category);

        return classification.Value.Category switch
        {
            DocumentCategory.Receipt => await RunReceiptWorkflowAsync(
                await CreateClassifiedDocumentForExtractionAsync(request, metadata, classification, cancellationToken),
                cancellationToken),
            DocumentCategory.ShoppingList => await RunShoppingListWorkflowAsync(
                await CreateClassifiedDocumentForExtractionAsync(request, metadata, classification, cancellationToken),
                cancellationToken),
            _ => CreateUnsupportedDocumentResult(new ClassifiedDocument(
                classificationImage.Request,
                metadata,
                classification.Value,
                classification.Usage,
                request))
        };
    }

    private async ValueTask<ClassifiedDocument> CreateClassifiedDocumentForExtractionAsync(
        FileRequest originalRequest,
        DocumentMetadata metadata,
        ModelResult<DocumentClassification> classification,
        CancellationToken cancellationToken)
    {
        var extractionImage = await _imagePreprocessor.PreprocessAsync(
            originalRequest,
            ModelImagePreprocessingPurpose.Extraction,
            cancellationToken);

        return new ClassifiedDocument(
            extractionImage.Request,
            metadata,
            classification.Value,
            classification.Usage,
            originalRequest);
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

        return await RunWorkflowAsync(workflow, "Receipt Processing", classifiedDocument, _logger, cancellationToken)
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

        return await RunWorkflowAsync(workflow, "Shopping List Processing", classifiedDocument, _logger, cancellationToken)
            ?? throw new InvalidOperationException(
                "Shopping list workflow completed without a document processing result.");
    }

    private static async Task<DocumentProcessingResult?> RunWorkflowAsync(
        Microsoft.Agents.AI.Workflows.Workflow workflow,
        string workflowName,
        ClassifiedDocument classifiedDocument,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Starting MAF workflow {WorkflowName} for {FileName}.",
            workflowName,
            classifiedDocument.OriginalRequest?.FileName ?? classifiedDocument.Request.FileName);

        var run = await InProcessExecution.RunAsync(
            workflow,
            classifiedDocument,
            cancellationToken: cancellationToken);

        var events = run.NewEvents.ToArray();
        logger.LogInformation(
            "MAF workflow {WorkflowName} emitted {EventCount} events.",
            workflowName,
            events.Length);
        foreach (var evt in events)
        {
            logger.LogDebug(
                "MAF workflow event {WorkflowName}: {EventType}.",
                workflowName,
                evt.GetType().Name);
        }

        var error = events
            .OfType<WorkflowErrorEvent>()
            .LastOrDefault();
        if (error is not null)
        {
            var exception = UnwrapWorkflowException(error.Exception)
                ?? new InvalidOperationException("Workflow failed without reporting an exception.");
            logger.LogWarning(
                exception,
                "MAF workflow {WorkflowName} reported an error event.",
                workflowName);
            ExceptionDispatchInfo.Capture(exception).Throw();
        }

        var result = events
            .OfType<WorkflowOutputEvent>()
            .Select(evt => evt.Data)
            .OfType<DocumentProcessingResult>()
            .LastOrDefault();
        logger.LogInformation(
            "Completed MAF workflow {WorkflowName}. HasResult={HasResult}.",
            workflowName,
            result is not null);

        return result;
    }

    private static Exception? UnwrapWorkflowException(Exception? exception)
    {
        while (exception is TargetInvocationException { InnerException: not null } invocationException)
        {
            exception = invocationException.InnerException;
        }

        return exception;
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
