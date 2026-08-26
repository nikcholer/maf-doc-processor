using MafDocumentProcessor.Domain;

namespace MafDocumentProcessor.Services;

public sealed class DocumentRegionModelResponseException(
    string message,
    ModelTokenUsage usage,
    Exception innerException) : Exception(message, innerException)
{
    public ModelTokenUsage Usage { get; } = usage;
}
