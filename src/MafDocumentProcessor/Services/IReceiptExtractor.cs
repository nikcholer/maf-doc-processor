using MafDocumentProcessor.Domain;

namespace MafDocumentProcessor.Services;

public interface IReceiptExtractor
{
    ValueTask<ModelResult<ReceiptData>> ExtractReceiptAsync(
        FileRequest request,
        CancellationToken cancellationToken,
        IReadOnlyList<string>? repairInstructions = null);
}
