using MafDocumentProcessor.Domain;

namespace MafDocumentProcessor.Services;

public interface ICaptureDetectionImagePreparer
{
    ValueTask<FileRequest> PrepareAsync(
        OrientedCaptureSourceImage source,
        CancellationToken cancellationToken);
}
