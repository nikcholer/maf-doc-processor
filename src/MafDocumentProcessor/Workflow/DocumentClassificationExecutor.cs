using MafDocumentProcessor.Domain;
using MafDocumentProcessor.Services;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MafDocumentProcessor.Workflow;

public sealed class DocumentClassificationExecutor(
    IDocumentClassifier classifier,
    IModelImagePreprocessor imagePreprocessor,
    CancellationToken workflowCancellationToken = default,
    ILogger<DocumentClassificationExecutor>? logger = null)
    : Executor<FileRequest, ClassifiedDocument>(ExecutorId)
{
    public const string ExecutorId = "DocumentClassification";

    private readonly ILogger<DocumentClassificationExecutor> _logger =
        logger ?? NullLogger<DocumentClassificationExecutor>.Instance;

    public override async ValueTask<ClassifiedDocument> HandleAsync(
        FileRequest message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            workflowCancellationToken);
        var effectiveCancellationToken = linkedCancellation.Token;
        var classificationImage = await imagePreprocessor.PreprocessAsync(
            message,
            ModelImagePreprocessingPurpose.Classification,
            effectiveCancellationToken);
        var classification = await classifier.ClassifyAsync(
            classificationImage.Request,
            effectiveCancellationToken);

        await context.AddEventAsync(
            new WorkflowEvent(new DocumentClassifiedEvent(
                classification.Value.Category,
                classification.Value.Confidence,
                classification.Usage.ModelId,
                message.FileName,
                message.SourceId)),
            effectiveCancellationToken);

        _logger.LogInformation(
            "Document classified as {Category} with confidence {Confidence} using {ModelId}.",
            classification.Value.Category,
            classification.Value.Confidence,
            classification.Usage.ModelId);

        var metadata = DocumentMetadata.FromRequest(
            message,
            classification.Usage.ModelId,
            classification.Value.Confidence);
        var workflowRequest = classificationImage.Request;
        if (IsSupportedCategory(classification.Value.Category))
        {
            var extractionImage = await imagePreprocessor.PreprocessAsync(
                message,
                ModelImagePreprocessingPurpose.Extraction,
                effectiveCancellationToken);
            workflowRequest = extractionImage.Request;
        }

        var destinationExecutorId = DocumentWorkflowFactory.GetDestinationExecutorId(
            classification.Value.Category);
        await context.AddEventAsync(
            new WorkflowEvent(new DocumentRouteSelectedEvent(
                classification.Value.Category,
                destinationExecutorId,
                message.FileName,
                message.SourceId)),
            effectiveCancellationToken);
        _logger.LogInformation(
            "Routing document {FileName} to {Category} workflow.",
            message.FileName,
            classification.Value.Category);

        return new ClassifiedDocument(
            workflowRequest,
            metadata,
            classification.Value,
            classification.Usage,
            message);
    }

    private static bool IsSupportedCategory(DocumentCategory category)
    {
        return category is
            DocumentCategory.Receipt or
            DocumentCategory.ShoppingList or
            DocumentCategory.SujikoPuzzle;
    }
}
