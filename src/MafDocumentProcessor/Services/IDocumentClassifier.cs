using MafDocumentProcessor.Domain;

namespace MafDocumentProcessor.Services;

public interface IDocumentClassifier
{
    ValueTask<ModelResult<DocumentClassification>> ClassifyAsync(
        FileRequest request,
        CancellationToken cancellationToken);
}
