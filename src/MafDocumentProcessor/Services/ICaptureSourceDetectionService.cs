using MafDocumentProcessor.Workflow;

namespace MafDocumentProcessor.Services;

public interface ICaptureSourceDetectionService
{
    ValueTask<CaptureSourceDetectionOutput> DetectAsync(
        CaptureSourceDetectionInput input,
        CancellationToken cancellationToken);
}
