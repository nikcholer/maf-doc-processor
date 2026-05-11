using MafDocumentProcessor.Domain;

namespace MafDocumentProcessor.Workflow;

public sealed record ClassifiedDocument(
    FileRequest Request,
    DocumentMetadata Metadata,
    DocumentClassification Classification,
    ModelTokenUsage ClassificationUsage);
