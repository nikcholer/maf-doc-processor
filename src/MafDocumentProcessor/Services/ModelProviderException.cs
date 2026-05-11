namespace MafDocumentProcessor.Services;

public sealed class ModelProviderException : Exception
{
    public ModelProviderException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
