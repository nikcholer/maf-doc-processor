namespace MafDocumentProcessor.Api.Configuration;

public sealed class DocumentIntakeSettings
{
    public string ImageFormFieldName { get; init; } = "image";

    public long MaxUploadBytes { get; init; } = 5 * 1024 * 1024;

    public string[] AllowedContentTypes { get; init; } = ["image/png", "image/jpeg"];

    public string[] AllowedExtensions { get; init; } = [".png", ".jpg", ".jpeg"];
}
