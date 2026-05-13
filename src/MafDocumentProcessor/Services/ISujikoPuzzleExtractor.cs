using MafDocumentProcessor.Domain;

namespace MafDocumentProcessor.Services;

public interface ISujikoPuzzleExtractor
{
    ValueTask<ModelResult<SujikoPuzzleData>> ExtractSujikoPuzzleAsync(
        FileRequest request,
        CancellationToken cancellationToken,
        IReadOnlyList<string>? repairInstructions = null);
}
