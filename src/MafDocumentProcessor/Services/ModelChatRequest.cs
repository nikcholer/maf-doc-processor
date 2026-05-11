using MafDocumentProcessor.Configuration;

namespace MafDocumentProcessor.Services;

public sealed record ModelChatRequest(
    string Operation,
    ModelRoleSettings Settings,
    IReadOnlyList<ModelChatMessage> Messages,
    int MaxOutputTokens);
