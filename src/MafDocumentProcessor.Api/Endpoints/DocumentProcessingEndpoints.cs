using System.Diagnostics;
using MafDocumentProcessor.Api.Configuration;
using MafDocumentProcessor.Api.Contracts;
using MafDocumentProcessor.Api.Services;
using MafDocumentProcessor.Configuration;
using MafDocumentProcessor.Domain;
using MafDocumentProcessor.Services;
using MafDocumentProcessor.Workflow;

namespace MafDocumentProcessor.Api.Endpoints;

public static class DocumentProcessingEndpoints
{
    public static IEndpointRouteBuilder MapDocumentProcessingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/documents/process", ProcessDocumentAsync);
        return endpoints;
    }

    private static async Task<IResult> ProcessDocumentAsync(
        HttpRequest request,
        DocumentIntakeSettings intakeSettings,
        AiModelSettings aiModelSettings,
        DocumentImageValidator imageValidator,
        ReceiptProcessingWorkflow workflow,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("DocumentProcessing");
        var elapsed = Stopwatch.StartNew();
        logger.LogInformation(
            "Received document processing request {TraceId}. ContentLength={ContentLength}.",
            request.HttpContext.TraceIdentifier,
            request.ContentLength);

        if (!request.HasFormContentType)
        {
            return BadRequest(
                request,
                new DocumentIntakeErrorResponse("form", "Expected a multipart form request."));
        }

        var form = await request.ReadFormAsync(cancellationToken);
        logger.LogInformation(
            "Read multipart form for request {TraceId} after {ElapsedMilliseconds} ms. FileCount={FileCount}.",
            request.HttpContext.TraceIdentifier,
            elapsed.ElapsedMilliseconds,
            form.Files.Count);

        var image = form.Files.GetFile(intakeSettings.ImageFormFieldName);
        if (image is null || image.Length == 0)
        {
            return BadRequest(
                request,
                new DocumentIntakeErrorResponse(
                    intakeSettings.ImageFormFieldName,
                    $"An uploaded image file is required in the '{intakeSettings.ImageFormFieldName}' form field."));
        }

        var validationError = imageValidator.Validate(
            new DocumentImageValidationRequest(
                image.FileName,
                image.ContentType,
                image.Length),
            intakeSettings);
        if (validationError is not null)
        {
            return BadRequest(request, validationError);
        }

        if (!ApiKeyEnvironment.HasApiKey(aiModelSettings.ImageRecognition.ApiKeyEnvironmentVariable))
        {
            return ProcessingError(
                request,
                StatusCodes.Status500InternalServerError,
                "model_configuration_invalid",
                $"Environment variable '{aiModelSettings.ImageRecognition.ApiKeyEnvironmentVariable}' is required for model role '{aiModelSettings.ImageRecognition.ServiceId}'.");
        }

        await using var imageStream = image.OpenReadStream();
        using var imageBuffer = new MemoryStream(capacity: checked((int)image.Length));
        await imageStream.CopyToAsync(imageBuffer, cancellationToken);
        logger.LogInformation(
            "Buffered document image {FileName} ({ContentType}, {FileSizeBytes} bytes) for request {TraceId} after {ElapsedMilliseconds} ms.",
            Path.GetFileName(image.FileName),
            image.ContentType,
            image.Length,
            request.HttpContext.TraceIdentifier,
            elapsed.ElapsedMilliseconds);

        var sourceId = form.TryGetValue("sourceId", out var sourceValues)
            ? NormalizeOptionalValue(sourceValues.ToString())
            : null;
        var fileName = Path.GetFileName(image.FileName);

        logger.LogInformation(
            "Starting receipt workflow for {FileName} ({ContentType}, {FileSizeBytes} bytes).",
            fileName,
            image.ContentType,
            image.Length);

        try
        {
            var result = await workflow.RunAsync(
                new FileRequest(
                    imageBuffer.ToArray(),
                    fileName,
                    image.ContentType,
                    image.Length,
                    DateTimeOffset.UtcNow,
                    sourceId),
                cancellationToken);

            logger.LogInformation(
                "Completed receipt workflow for {FileName} after {ElapsedMilliseconds} ms. Success={IsSuccess}, Category={Category}.",
                fileName,
                elapsed.ElapsedMilliseconds,
                result.IsSuccess,
                result.Category);

            return Results.Ok(DocumentProcessingResponseMapper.Map(result));
        }
        catch (ModelConfigurationException ex)
        {
            logger.LogWarning(ex, "Model configuration failed for uploaded image {FileName}.", fileName);
            return ProcessingError(
                request,
                StatusCodes.Status500InternalServerError,
                "model_configuration_invalid",
                ex.Message);
        }
        catch (DocumentModelResponseException ex)
        {
            logger.LogWarning(ex, "Model response parsing failed for uploaded image {FileName}.", fileName);
            return ProcessingError(
                request,
                StatusCodes.Status502BadGateway,
                "model_response_invalid",
                ex.Message);
        }
        catch (TimeoutException ex)
        {
            logger.LogWarning(ex, "Document workflow timed out for uploaded image {FileName}.", fileName);
            return ProcessingError(
                request,
                StatusCodes.Status504GatewayTimeout,
                "model_timeout",
                "The model provider did not return a response before the configured timeout. Try a smaller or clearer image, or retry in a moment.");
        }
        catch (ModelProviderException ex)
        {
            logger.LogWarning(ex, "Model provider failed for uploaded image {FileName}.", fileName);
            return ProcessingError(
                request,
                StatusCodes.Status502BadGateway,
                "model_provider_failed",
                ex.Message);
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Document workflow failed for uploaded image {FileName}.", fileName);
            return ProcessingError(
                request,
                StatusCodes.Status502BadGateway,
                "document_processing_failed",
                ex.Message);
        }
    }

    private static IResult BadRequest(
        HttpRequest request,
        DocumentIntakeErrorResponse validationError)
    {
        return Results.BadRequest(new ApiErrorResponse(
            "invalid_document_upload",
            validationError.Message,
            validationError.Field,
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
}
