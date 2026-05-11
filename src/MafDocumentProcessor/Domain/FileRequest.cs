namespace MafDocumentProcessor.Domain;

public sealed record FileRequest(
    byte[] Content,
    string FileName,
    string ContentType,
    long FileSizeBytes,
    DateTimeOffset ReceivedAt,
    string? SourceId);
