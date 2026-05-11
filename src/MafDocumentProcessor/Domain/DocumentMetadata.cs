namespace MafDocumentProcessor.Domain;

public sealed record DocumentMetadata(
    string FileName,
    string ContentType,
    long FileSizeBytes,
    DateTimeOffset ReceivedAt,
    string? SourceId,
    string? ModelId,
    decimal? ClassificationConfidence)
{
    public static DocumentMetadata FromRequest(
        FileRequest request,
        string? modelId,
        decimal? classificationConfidence)
    {
        return new DocumentMetadata(
            request.FileName,
            request.ContentType,
            request.FileSizeBytes,
            request.ReceivedAt,
            request.SourceId,
            modelId,
            classificationConfidence);
    }
}
