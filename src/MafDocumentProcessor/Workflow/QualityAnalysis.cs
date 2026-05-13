using MafDocumentProcessor.Domain;

namespace MafDocumentProcessor.Workflow;

public sealed record QualityAnalysis(
    DocumentProcessingResult DocumentResult,
    string AnalystSummary,
    ModelTokenUsage AnalystUsage);
