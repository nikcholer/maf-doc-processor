using MafDocumentProcessor.Domain;
using MafDocumentProcessor.Services;
using Microsoft.Agents.AI.Workflows;

namespace MafDocumentProcessor.Workflow;

public sealed class DocumentClassificationExecutor(IDocumentClassifier classifier)
    : Executor<FileRequest, ClassifiedDocument>("DocumentClassification")
{
    public override async ValueTask<ClassifiedDocument> HandleAsync(
        FileRequest message,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        var classification = await classifier.ClassifyAsync(message, cancellationToken);
        var metadata = DocumentMetadata.FromRequest(
            message,
            classification.Usage.ModelId,
            classification.Value.Confidence);

        return new ClassifiedDocument(
            message,
            metadata,
            classification.Value,
            classification.Usage);
    }
}
