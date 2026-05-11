namespace MafDocumentProcessor.Services;

public enum ModelChatRole
{
    System,
    User
}

public sealed record ModelChatMessage(
    ModelChatRole Role,
    IReadOnlyList<ModelChatContent> Content)
{
    public static ModelChatMessage CreateSystem(string text)
    {
        return new ModelChatMessage(
            ModelChatRole.System,
            [new ModelTextContent(text)]);
    }

    public static ModelChatMessage CreateUser(params ModelChatContent[] content)
    {
        return new ModelChatMessage(ModelChatRole.User, content);
    }
}
