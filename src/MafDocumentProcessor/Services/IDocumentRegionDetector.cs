using MafDocumentProcessor.Domain;

namespace MafDocumentProcessor.Services;

public interface IDocumentRegionDetector
{
    ValueTask<ModelResult<IReadOnlyList<DocumentRegionProposal>>> DetectAsync(
        OrientedCaptureSourceImage source,
        CancellationToken cancellationToken);
}
