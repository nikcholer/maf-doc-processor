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
    ISujikoPuzzleExtractor? sujikoPuzzleExtractor = null,
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
            DocumentCategory.SujikoPuzzle => await RunSujikoPuzzleWorkflowAsync(
                await CreateClassifiedDocumentForExtractionAsync(request, metadata, classification, cancellationToken),
                cancellationToken),
            _ => UnsupportedDocumentResultExecutor.CreateResult(new ClassifiedDocument(
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
        var workflow = DocumentWorkflowFactory.BuildReceiptWorkflow(
            receiptExtractor,
            policyOptions,
            cancellationToken);

        return await RunWorkflowAsync(
            workflow,
            DocumentWorkflowFactory.ReceiptWorkflowName,
            classifiedDocument,
            _logger,
            cancellationToken)
            ?? throw new InvalidOperationException(
                "Receipt workflow completed without a document processing result.");
    }

    private async Task<DocumentProcessingResult> RunShoppingListWorkflowAsync(
        ClassifiedDocument classifiedDocument,
        CancellationToken cancellationToken)
    {
        var workflow = DocumentWorkflowFactory.BuildShoppingListWorkflow(
            shoppingListExtractor,
            cancellationToken);

        return await RunWorkflowAsync(
            workflow,
            DocumentWorkflowFactory.ShoppingListWorkflowName,
            classifiedDocument,
            _logger,
            cancellationToken)
            ?? throw new InvalidOperationException(
                "Shopping list workflow completed without a document processing result.");
    }

    private async Task<DocumentProcessingResult> RunSujikoPuzzleWorkflowAsync(
        ClassifiedDocument classifiedDocument,
        CancellationToken cancellationToken)
    {
        if (sujikoPuzzleExtractor is null)
        {
            throw new InvalidOperationException("Sujiko puzzle extraction is not configured.");
        }

        var workflow = DocumentWorkflowFactory.BuildSujikoPuzzleWorkflow(
            sujikoPuzzleExtractor,
            cancellationToken);

        return await RunWorkflowAsync(
            workflow,
            DocumentWorkflowFactory.SujikoPuzzleWorkflowName,
            classifiedDocument,
            _logger,
            cancellationToken)
            ?? throw new InvalidOperationException(
                "Sujiko puzzle workflow completed without a document processing result.");
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

        if (result is null)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

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

}
