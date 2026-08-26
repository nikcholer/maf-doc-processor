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
        CancellationToken cancellationToken,
        IReadOnlyList<string>? repairInstructions = null)
    {
        var response = await chatClient.CompleteAsync(
            new ModelChatRequest(
                Operation,
                settings,
                [
                    ModelChatMessage.CreateSystem("""
                    You extract shopping list items from document images.
                    Identify the main document occupying most of the image, including its centre. Ignore fragments of neighbouring documents at the edges.
                    Do not explain, reason aloud, use markdown, or include any text outside the JSON object.
                    Return only compact JSON with this exact shape:
                    {"title":"string|null","items":[{"name":"string","quantity":0.0|null,"unit":"string|null","isChecked":true|null}],"notes":"string|null"}
                    Use null for optional fields that are not visible. Do not invent items.
                    """),
                    ModelChatMessage.CreateUser(
                        new ModelTextContent(
                            BuildUserInstruction(request, repairInstructions)),
                        new ModelImageContent(request.Content, request.ContentType))
                ],
                MaxOutputTokens: 700),
            cancellationToken);

        return new ModelResult<ShoppingListData>(
            ModelResponseParsers.ParseShoppingList(response.Content),
            response.Usage);
    }

    private static string BuildUserInstruction(
        FileRequest request,
        IReadOnlyList<string>? repairInstructions)
    {
        var instruction =
            $"{DocumentImagePrompts.MainDocumentFocus} Extract shopping list items from this uploaded image. File: {request.FileName}; content type: {request.ContentType}.";
        if (repairInstructions is not { Count: > 0 })
        {
            return instruction;
        }

        return string.Join(
            Environment.NewLine,
            instruction,
            "A previous extraction failed validation for:",
            string.Join(Environment.NewLine, repairInstructions.Select(reason => $"- {reason}")),
            "Re-extract from the image, correcting those validation failures only when the document visibly supports the correction. Return JSON only.");
    }
}
