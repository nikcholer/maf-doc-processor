namespace MafDocumentProcessor.Services;

public sealed class DocumentModelResponseException : Exception
{
    public DocumentModelResponseException(string message)
        : base(message)
    {
    }

    public DocumentModelResponseException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
