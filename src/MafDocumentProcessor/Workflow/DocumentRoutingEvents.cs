using MafDocumentProcessor.Domain;

namespace MafDocumentProcessor.Workflow;

public sealed record DocumentClassifiedEvent(
    DocumentCategory Category,
    decimal? Confidence,
    string ModelId,
    string FileName,
    string? SourceId);

public sealed record DocumentRouteSelectedEvent(
    DocumentCategory Category,
    string DestinationExecutorId,
    string FileName,
    string? SourceId);
