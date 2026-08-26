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
        CancellationToken cancellationToken,
        IReadOnlyList<string>? repairInstructions = null)
    {
        var response = await chatClient.CompleteAsync(
            new ModelChatRequest(
                Operation,
                settings,
                [
                    ModelChatMessage.CreateSystem("""
                    You extract receipt fields from document images.
                    Identify the main document occupying most of the image, including its centre. Ignore fragments of neighbouring documents at the edges.
                    Return only compact JSON with this exact shape:
                    {"storeName":"string","totalAmount":0.0,"purchaseDate":"yyyy-MM-dd|null","paymentMethod":"string|null","currencyCode":"GBP|null"}
                    Use null for optional fields that are not visible. Do not invent values.
                    """),
                    ModelChatMessage.CreateUser(
                        new ModelTextContent(
                            BuildUserInstruction(request, repairInstructions)),
                        new ModelImageContent(request.Content, request.ContentType))
                ],
                MaxOutputTokens: 700),
            cancellationToken);

        return new ModelResult<ReceiptData>(
            ModelResponseParsers.ParseReceipt(response.Content),
            response.Usage);
    }

    private static string BuildUserInstruction(
        FileRequest request,
        IReadOnlyList<string>? repairInstructions)
    {
        var instruction =
            $"{DocumentImagePrompts.MainDocumentFocus} Extract receipt fields from this uploaded image. File: {request.FileName}; content type: {request.ContentType}.";
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
