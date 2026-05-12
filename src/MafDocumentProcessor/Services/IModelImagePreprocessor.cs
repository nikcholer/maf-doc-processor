using MafDocumentProcessor.Domain;

namespace MafDocumentProcessor.Services;

public interface IModelImagePreprocessor
{
    ValueTask<ModelImagePreprocessingResult> PreprocessAsync(
        FileRequest request,
        ModelImagePreprocessingPurpose purpose,
        CancellationToken cancellationToken);
}
