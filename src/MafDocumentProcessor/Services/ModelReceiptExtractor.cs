using MafDocumentProcessor.Configuration;
using MafDocumentProcessor.Domain;

namespace MafDocumentProcessor.Services;

public sealed class ModelReceiptExtractor(
    IModelChatClient chatClient,
    ModelRoleSettings settings) : IReceiptExtractor
{
    private const string Operation = "receipt_extraction";

    public async ValueTask<ModelResult<ReceiptData>> ExtractReceiptAsync(
        FileRequest request,
        CancellationToken cancellationToken)
    {
        var response = await chatClient.CompleteAsync(
            new ModelChatRequest(
                Operation,
                settings,
                [
                    ModelChatMessage.CreateSystem("""
                    You extract receipt fields from document images.
                    Return only compact JSON with this exact shape:
                    {"storeName":"string","totalAmount":0.0,"purchaseDate":"yyyy-MM-dd|null","paymentMethod":"string|null","currencyCode":"GBP|null"}
                    Use null for optional fields that are not visible. Do not invent values.
                    """),
                    ModelChatMessage.CreateUser(
                        new ModelTextContent(
                            $"Extract receipt fields from this uploaded image. File: {request.FileName}; content type: {request.ContentType}."),
                        new ModelImageContent(request.Content, request.ContentType))
                ],
                MaxOutputTokens: 700),
            cancellationToken);

        return new ModelResult<ReceiptData>(
            ModelResponseParsers.ParseReceipt(response.Content),
            response.Usage);
    }
}
