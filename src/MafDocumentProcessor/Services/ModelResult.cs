using MafDocumentProcessor.Domain;

namespace MafDocumentProcessor.Services;

public sealed record ModelResult<T>(
    T Value,
    ModelTokenUsage Usage);
