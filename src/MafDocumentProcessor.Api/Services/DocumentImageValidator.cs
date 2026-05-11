using MafDocumentProcessor.Api.Configuration;

namespace MafDocumentProcessor.Api.Services;

public sealed class DocumentImageValidator
{
    public DocumentIntakeErrorResponse? Validate(
        DocumentImageValidationRequest image,
        DocumentIntakeSettings settings)
    {
        if (image.Length > settings.MaxUploadBytes)
        {
            return new DocumentIntakeErrorResponse(
                settings.ImageFormFieldName,
                $"Image file must be {settings.MaxUploadBytes} bytes or smaller.");
        }

        if (!settings.AllowedContentTypes.Contains(image.ContentType, StringComparer.OrdinalIgnoreCase))
        {
            return new DocumentIntakeErrorResponse(
                settings.ImageFormFieldName,
                $"Unsupported content type '{image.ContentType}'.");
        }

        var extension = Path.GetExtension(image.FileName);
        if (!settings.AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            return new DocumentIntakeErrorResponse(
                settings.ImageFormFieldName,
                $"Unsupported file extension '{extension}'.");
        }

        return null;
    }
}

public sealed record DocumentImageValidationRequest(
    string FileName,
    string ContentType,
    long Length);

public sealed record DocumentIntakeErrorResponse(
    string Field,
    string Message);
