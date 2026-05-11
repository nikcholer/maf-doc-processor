namespace MafDocumentProcessor.Api.Contracts;

public sealed record ApiErrorResponse(
    string Code,
    string Message,
    string? Target,
    string TraceId);
