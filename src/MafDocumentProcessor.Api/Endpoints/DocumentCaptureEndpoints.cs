using System.Diagnostics;
using MafDocumentProcessor.Api.Configuration;
using MafDocumentProcessor.Api.Contracts;
using MafDocumentProcessor.Api.Services;
using MafDocumentProcessor.Configuration;
using MafDocumentProcessor.Domain;
using MafDocumentProcessor.Services;
using MafDocumentProcessor.Workflow;
namespace MafDocumentProcessor.Api.Endpoints;

public static class DocumentCaptureEndpoints
{
    public const string ImagesFormFieldName = "images";

    public static IEndpointRouteBuilder MapDocumentCaptureEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/document-captures/process", ProcessCaptureAsync)
            .WithName("ProcessDocumentCapture")
            .WithTags("DocumentCaptures")
            .WithSummary("Process one or more capture images.")
            .WithDescription(
                "Accepts one or more PNG or JPEG files in a repeated 'images' multipart field and an optional request-level sourceId. " +
                "Each source is searched for document regions; every valid crop is processed through the same document workflow as an individual upload. " +
                "The response is always the capture aggregate, including partial success. Content type: multipart/form-data.")
            .Produces<CompositeCaptureProcessingResponse>(StatusCodes.Status200OK)
            .Produces<ApiErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ApiErrorResponse>(StatusCodes.Status500InternalServerError)
            .Produces<ApiErrorResponse>(StatusCodes.Status502BadGateway)
            .Produces<ApiErrorResponse>(StatusCodes.Status504GatewayTimeout);
        return endpoints;
    }

    public static async Task<IResult> ProcessCaptureAsync(
        HttpRequest request,
        CompositeCaptureOptions captureOptions,
        AiModelSettings aiModelSettings,
        CompositeCaptureWorkflow workflow,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("DocumentCapture");
        var elapsed = Stopwatch.StartNew();
        using var logScope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["TraceId"] = request.HttpContext.TraceIdentifier,
            ["CorrelationId"] = request.Headers.TryGetValue("X-Correlation-ID", out var correlationId)
                ? correlationId.ToString()
                : null
        });
        logger.LogInformation(
            "Received document capture request {TraceId}. ContentLength={ContentLength}.",
            request.HttpContext.TraceIdentifier,
            request.ContentLength);

        if (!request.HasFormContentType)
        {
            return BadRequest(request, "form", "Expected a multipart form request.");
        }

        var form = await request.ReadFormAsync(cancellationToken);
        var images = form.Files
            .Where(file => string.Equals(file.Name, ImagesFormFieldName, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        logger.LogInformation(
            "Read multipart form for capture {TraceId} after {ElapsedMilliseconds} ms. ImageCount={ImageCount}.",
            request.HttpContext.TraceIdentifier,
            elapsed.ElapsedMilliseconds,
            images.Length);

        if (images.Length == 0)
        {
            return BadRequest(
                request,
                ImagesFormFieldName,
                $"At least one image file is required in the '{ImagesFormFieldName}' form field.");
        }

        if (images.Length > captureOptions.MaxSourceCount)
        {
            return BadRequest(
                request,
                ImagesFormFieldName,
                $"A capture request may include at most {captureOptions.MaxSourceCount} images.");
        }

        var aggregateBytes = images.Sum(image => image.Length);
        if (aggregateBytes > captureOptions.MaxAggregateBytes)
        {
            return BadRequest(
                request,
                ImagesFormFieldName,
                $"The combined image payload must be {captureOptions.MaxAggregateBytes} bytes or smaller.");
        }

        var missingModelRole = GetMissingModelRole(aiModelSettings);
        if (missingModelRole is not null)
        {
            return ProcessingError(
                request,
                StatusCodes.Status500InternalServerError,
                "model_configuration_invalid",
                $"Environment variable '{missingModelRole.ApiKeyEnvironmentVariable}' is required for model role '{missingModelRole.ServiceId}'.");
        }

        var sourceId = form.TryGetValue("sourceId", out var sourceValues)
            ? NormalizeOptionalValue(sourceValues.ToString())
            : null;
        var receivedAt = DateTimeOffset.UtcNow;
        var sourceRequests = new List<FileRequest>(images.Length);
        foreach (var image in images)
        {
            await using var imageStream = image.OpenReadStream();
            using var imageBuffer = new MemoryStream(capacity: checked((int)Math.Max(image.Length, 0)));
            await imageStream.CopyToAsync(imageBuffer, cancellationToken);
            sourceRequests.Add(new FileRequest(
                imageBuffer.ToArray(),
                Path.GetFileName(image.FileName),
                image.ContentType,
                image.Length,
                receivedAt,
                sourceId));
        }

        logger.LogInformation(
            "Starting capture workflow for {SourceCount} images ({AggregateBytes} bytes).",
            sourceRequests.Count,
            aggregateBytes);

        try
        {
            var result = await workflow.RunAsync(
                CompositeCaptureRequest.Create(sourceRequests, receivedAt, sourceId),
                cancellationToken);
            logger.LogInformation(
                "Completed capture workflow {CaptureId} after {ElapsedMilliseconds} ms. Status={Status}, MemberCount={MemberCount}.",
                result.CaptureId,
                elapsed.ElapsedMilliseconds,
                result.Status,
                result.Members.Count);
            return Results.Ok(CompositeCaptureResponseMapper.Map(
                result,
                request.HttpContext.TraceIdentifier));
        }
        catch (ModelConfigurationException ex)
        {
            logger.LogWarning(ex, "Model configuration failed for capture request {TraceId}.", request.HttpContext.TraceIdentifier);
            return ProcessingError(
                request,
                StatusCodes.Status500InternalServerError,
                "model_configuration_invalid",
                ex.Message);
        }
        catch (DocumentModelResponseException ex)
        {
            logger.LogWarning(ex, "Model response parsing failed for capture request {TraceId}.", request.HttpContext.TraceIdentifier);
            return ProcessingError(
                request,
                StatusCodes.Status502BadGateway,
                "model_response_invalid",
                ex.Message);
        }
        catch (TimeoutException ex)
        {
            logger.LogWarning(ex, "Capture workflow timed out for request {TraceId}.", request.HttpContext.TraceIdentifier);
            return ProcessingError(
                request,
                StatusCodes.Status504GatewayTimeout,
                "model_timeout",
                "The model provider did not return a response before the configured timeout. Try fewer or clearer images, or retry in a moment.");
        }
        catch (ModelProviderException ex)
        {
            logger.LogWarning(ex, "Model provider failed for capture request {TraceId}.", request.HttpContext.TraceIdentifier);
            return ProcessingError(
                request,
                StatusCodes.Status502BadGateway,
                "model_provider_failed",
                ex.Message);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("Capture workflow was canceled for request {TraceId}.", request.HttpContext.TraceIdentifier);
            throw;
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Capture workflow failed for request {TraceId}.", request.HttpContext.TraceIdentifier);
            return ProcessingError(
                request,
                StatusCodes.Status502BadGateway,
                "document_processing_failed",
                ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected capture workflow failure for request {TraceId}.", request.HttpContext.TraceIdentifier);
            return ProcessingError(
                request,
                StatusCodes.Status500InternalServerError,
                "document_processing_unhandled",
                $"Document capture failed unexpectedly. Check the API logs for details. Error type: {ex.GetType().Name}.");
        }
    }

    private static IResult BadRequest(HttpRequest request, string target, string message)
    {
        return Results.BadRequest(new ApiErrorResponse(
            "invalid_document_upload",
            message,
            target,
            request.HttpContext.TraceIdentifier));
    }

    private static IResult ProcessingError(
        HttpRequest request,
        int statusCode,
        string code,
        string message)
    {
        return Results.Json(
            new ApiErrorResponse(
                code,
                message,
                Target: null,
                request.HttpContext.TraceIdentifier),
            statusCode: statusCode);
    }

    private static string? NormalizeOptionalValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static ModelRoleSettings? GetMissingModelRole(AiModelSettings settings)
    {
        foreach (var role in new[]
        {
            settings.DocumentRegionDetection,
            settings.DocumentClassification,
            settings.DocumentExtraction
        })
        {
            if (!ApiKeyEnvironment.HasApiKey(role.ApiKeyEnvironmentVariable))
            {
                return role;
            }
        }

        return null;
    }
}
