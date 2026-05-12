using MafDocumentProcessor.Configuration;
using MafDocumentProcessor.Domain;

namespace MafDocumentProcessor.Services;

public sealed class ModelShoppingListExtractor(
    IModelChatClient chatClient,
    ModelRoleSettings settings) : IShoppingListExtractor
{
    private const string Operation = "shopping_list_extraction";

    public async ValueTask<ModelResult<ShoppingListData>> ExtractShoppingListAsync(
        FileRequest request,
        CancellationToken cancellationToken)
    {
        var response = await chatClient.CompleteAsync(
            new ModelChatRequest(
                Operation,
                settings,
                [
                    ModelChatMessage.CreateSystem("""
                    You extract shopping list items from document images.
                    Do not explain, reason aloud, use markdown, or include any text outside the JSON object.
                    Return only compact JSON with this exact shape:
                    {"title":"string|null","items":[{"name":"string","quantity":0.0|null,"unit":"string|null","isChecked":true|null}],"notes":"string|null"}
                    Use null for optional fields that are not visible. Do not invent items.
                    """),
                    ModelChatMessage.CreateUser(
                        new ModelTextContent(
                            $"Extract shopping list items from this uploaded image. File: {request.FileName}; content type: {request.ContentType}."),
                        new ModelImageContent(request.Content, request.ContentType))
                ],
                MaxOutputTokens: 700),
            cancellationToken);

        return new ModelResult<ShoppingListData>(
            ModelResponseParsers.ParseShoppingList(response.Content),
            response.Usage);
    }
}
