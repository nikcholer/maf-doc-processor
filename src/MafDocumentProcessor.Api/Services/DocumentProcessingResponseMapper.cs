using MafDocumentProcessor.Api.Contracts;
using MafDocumentProcessor.Domain;

namespace MafDocumentProcessor.Api.Services;

public static class DocumentProcessingResponseMapper
{
    public static DocumentProcessingResponse Map(DocumentProcessingResult result)
    {
        var documentData = GetDocumentData(result);
        var document = documentData is null
            ? null
            : new ProcessedDocumentResponse(
                result.Category,
                result.Metadata,
                documentData,
                result.PolicyResult,
                result.Validation);

        return new DocumentProcessingResponse(
            result.Category,
            result.Metadata,
            result.Classification,
            result.ModelUsage,
            result.HumanReview,
            document,
            result.IsSuccess,
            result.Errors,
            result.Warnings);
    }

    private static object? GetDocumentData(DocumentProcessingResult result)
    {
        return result.Category switch
        {
            DocumentCategory.Receipt => result.Receipt,
            DocumentCategory.ShoppingList => result.ShoppingList,
            DocumentCategory.SujikoPuzzle => result.SujikoPuzzle,
            _ => null
        };
    }
}
