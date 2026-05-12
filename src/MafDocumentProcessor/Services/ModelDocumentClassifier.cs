using MafDocumentProcessor.Configuration;
using MafDocumentProcessor.Domain;

namespace MafDocumentProcessor.Services;

public sealed class ModelDocumentClassifier(
    IModelChatClient chatClient,
    ModelRoleSettings settings) : IDocumentClassifier
{
    private const string Operation = "classification";

    public async ValueTask<ModelResult<DocumentClassification>> ClassifyAsync(
        FileRequest request,
        CancellationToken cancellationToken)
    {
        var response = await chatClient.CompleteAsync(
            new ModelChatRequest(
                Operation,
                settings,
                [
                    ModelChatMessage.CreateSystem("""
                    You classify document images for a local document processor.
                    Do not explain, reason aloud, use markdown, or include any text outside the JSON object.
                    Return only compact JSON with this shape:
                    {"category":"Receipt|Invoice|ShoppingList|Unknown","confidence":0.0,"documentTypeDescription":"short human document type","confidenceReasoning":"short reason"}
                    Use ShoppingList for handwritten or printed shopping lists, grocery lists, packing lists, or to-buy lists.
                    If the common description is "grocery list" or "shopping list", return category "ShoppingList".
                    Use Unknown when the image is not clearly a receipt, invoice, or shopping list. If it is another recognizable type, name it in documentTypeDescription, for example "car registration document".
                    """),
                    ModelChatMessage.CreateUser(
                        new ModelTextContent(
                            $"Classify this uploaded document image. File: {request.FileName}; content type: {request.ContentType}."),
                        new ModelImageContent(request.Content, request.ContentType))
                ],
                MaxOutputTokens: 80),
            cancellationToken);

        return new ModelResult<DocumentClassification>(
            ModelResponseParsers.ParseClassification(response.Content),
            response.Usage);
    }
}
