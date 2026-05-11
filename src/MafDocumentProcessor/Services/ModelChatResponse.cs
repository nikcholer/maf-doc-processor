using MafDocumentProcessor.Domain;

namespace MafDocumentProcessor.Services;

public sealed record ModelChatResponse(
    string? Content,
    ModelTokenUsage Usage);
