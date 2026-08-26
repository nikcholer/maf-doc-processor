using MafDocumentProcessor.Workflow;

namespace MafDocumentProcessor.Services;

public interface ICaptureRegionValidationService
{
    ValueTask<CaptureRegionValidationOutput> ValidateAsync(
        CaptureRegionValidationInput input,
        CancellationToken cancellationToken);
}
