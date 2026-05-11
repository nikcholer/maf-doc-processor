using MafDocumentProcessor.Api.Contracts;
using MafDocumentProcessor.Domain;

namespace MafDocumentProcessor.Api.Services;

public static class DocumentProcessingResponseMapper
{
    public static DocumentProcessingResponse Map(ReceiptProcessingResult result)
    {
        var document = result.Receipt is null
            ? null
            : new ReceiptDocumentResponse(
                result.Category,
                result.Metadata,
                result.Receipt,
                result.PolicyResult,
                result.Validation);

        return new DocumentProcessingResponse(
            result.Category,
            result.Metadata,
            result.Classification,
            result.ModelUsage,
            document,
            result.IsSuccess,
            result.Errors,
            result.Warnings);
    }
}
