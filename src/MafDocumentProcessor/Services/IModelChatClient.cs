namespace MafDocumentProcessor.Services;

public interface IModelChatClient
{
    ValueTask<ModelChatResponse> CompleteAsync(
        ModelChatRequest request,
        CancellationToken cancellationToken);
}
