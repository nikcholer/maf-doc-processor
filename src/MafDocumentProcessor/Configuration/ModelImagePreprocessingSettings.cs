namespace MafDocumentProcessor.Configuration;

public sealed record ModelImagePreprocessingSettings(
    bool Enabled = true,
    int ClassificationMaxLongEdgePixels = 1280,
    int ExtractionMaxLongEdgePixels = 2048,
    int JpegQuality = 85);
