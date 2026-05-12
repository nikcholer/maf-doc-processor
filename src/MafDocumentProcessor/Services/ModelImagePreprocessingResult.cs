using MafDocumentProcessor.Domain;

namespace MafDocumentProcessor.Services;

public sealed record ModelImagePreprocessingResult(
    FileRequest Request,
    ModelImagePreprocessingPurpose Purpose,
    bool WasResized,
    int OriginalWidth,
    int OriginalHeight,
    int Width,
    int Height,
    long OriginalFileSizeBytes,
    long FileSizeBytes);
