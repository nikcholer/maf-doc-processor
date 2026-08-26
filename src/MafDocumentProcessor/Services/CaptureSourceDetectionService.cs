using MafDocumentProcessor.Domain;
using MafDocumentProcessor.Workflow;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MafDocumentProcessor.Services;

public sealed class CaptureSourceDetectionService(
    ICaptureSourceImageDecoder imageDecoder,
    IDocumentRegionDetector regionDetector,
    ILogger<CaptureSourceDetectionService>? logger = null) : ICaptureSourceDetectionService
{
    private readonly ILogger<CaptureSourceDetectionService> _logger =
        logger ?? NullLogger<CaptureSourceDetectionService>.Instance;

    public async ValueTask<CaptureSourceDetectionOutput> DetectAsync(
        CaptureSourceDetectionInput input,
        CancellationToken cancellationToken)
    {
        OrientedCaptureSourceImage? image = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            image = imageDecoder.Decode(input.Source);
            if (input.Source.RegionOverrides is { } regionOverrides)
            {
                _logger.LogInformation(
                    "Using {RegionCount} user-supplied region overrides for {CaptureId}/{SourceItemId}; detector call skipped.",
                    regionOverrides.Count,
                    input.Context.CaptureId,
                    input.Source.SourceItemId);
                return OverrideSuccess(input, image, regionOverrides);
            }

            var detection = await regionDetector.DetectAsync(image, cancellationToken);
            _logger.LogInformation(
                "Detected {RegionCount} document region proposals for {CaptureId}/{SourceItemId} using {ModelId}.",
                detection.Value.Count,
                input.Context.CaptureId,
                input.Source.SourceItemId,
                detection.Usage.ModelId);

            return Success(input, image, detection);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            image?.Dispose();
            throw;
        }
        catch (ModelConfigurationException)
        {
            image?.Dispose();
            throw;
        }
        catch (CaptureSourceValidationException ex)
        {
            image?.Dispose();
            return Failure(input, "invalid_capture_source", ex.Message);
        }
        catch (DocumentRegionModelResponseException ex)
        {
            var imageMetadata = GetMetadata(image);
            image?.Dispose();
            return Failure(input, "model_response_invalid", ex.Message, ex.Usage, imageMetadata);
        }
        catch (TimeoutException ex)
        {
            var imageMetadata = GetMetadata(image);
            image?.Dispose();
            return Failure(
                input,
                "model_timeout",
                $"Document region detection timed out for {input.Source.SourceItemId}: {ex.Message}",
                imageMetadata: imageMetadata);
        }
        catch (ModelProviderException ex)
        {
            var imageMetadata = GetMetadata(image);
            image?.Dispose();
            return Failure(
                input,
                "model_provider_failed",
                $"Document region detection failed for {input.Source.SourceItemId}: {ex.Message}",
                imageMetadata: imageMetadata);
        }
    }

    private static CaptureSourceDetectionOutput Success(
        CaptureSourceDetectionInput input,
        OrientedCaptureSourceImage image,
        ModelResult<IReadOnlyList<DocumentRegionProposal>> detection)
    {
        return new CaptureSourceDetectionOutput(
            input.Context.ForSource(input.Source.SourceItemId),
            input.Source,
            CaptureSourceImageMetadata.From(image),
            image,
            detection.Value,
            DocumentModelUsage.FromCalls([detection.Usage]),
            [],
            []);
    }

    private static CaptureSourceDetectionOutput OverrideSuccess(
        CaptureSourceDetectionInput input,
        OrientedCaptureSourceImage image,
        IReadOnlyList<DocumentRegionProposal> regionOverrides)
    {
        return new CaptureSourceDetectionOutput(
            input.Context.ForSource(input.Source.SourceItemId),
            input.Source,
            CaptureSourceImageMetadata.From(image),
            image,
            regionOverrides,
            DocumentModelUsage.FromCalls([]),
            [],
            []);
    }

    private CaptureSourceDetectionOutput Failure(
        CaptureSourceDetectionInput input,
        string code,
        string message,
        ModelTokenUsage? usage = null,
        CaptureSourceImageMetadata? imageMetadata = null)
    {
        _logger.LogWarning(
            "Capture source detection failed for {CaptureId}/{SourceItemId}. Code={ErrorCode}. Message={ErrorMessage}",
            input.Context.CaptureId,
            input.Source.SourceItemId,
            code,
            message);

        return new CaptureSourceDetectionOutput(
            input.Context.ForSource(input.Source.SourceItemId),
            input.Source,
            imageMetadata,
            OrientedSource: null,
            Proposals: [],
            DocumentModelUsage.FromCalls(usage is null ? [] : [usage]),
            [new CaptureProcessingError(code, message, input.Source.SourceItemId)],
            []);
    }

    private static CaptureSourceImageMetadata? GetMetadata(OrientedCaptureSourceImage? image)
    {
        return image is null ? null : CaptureSourceImageMetadata.From(image);
    }
}
