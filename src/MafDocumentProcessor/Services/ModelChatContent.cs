namespace MafDocumentProcessor.Services;

public abstract record ModelChatContent;

public sealed record ModelTextContent(string Text) : ModelChatContent;

public sealed record ModelImageContent(
    byte[] Content,
    string ContentType) : ModelChatContent;
