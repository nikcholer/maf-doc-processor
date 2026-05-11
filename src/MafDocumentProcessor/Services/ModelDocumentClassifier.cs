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
                    Return only compact JSON with this shape:
                    {"category":"Receipt|Invoice|Unknown","confidence":0.0,"confidenceReasoning":"short reason"}
                    Use Unknown when the image is not clearly a receipt or invoice.
                    """),
                    ModelChatMessage.CreateUser(
                        new ModelTextContent(
                            $"Classify this uploaded document image. File: {request.FileName}; content type: {request.ContentType}."),
                        new ModelImageContent(request.Content, request.ContentType))
                ],
                MaxOutputTokens: 400),
            cancellationToken);

        return new ModelResult<DocumentClassification>(
            ModelResponseParsers.ParseClassification(response.Content),
            response.Usage);
    }
}
