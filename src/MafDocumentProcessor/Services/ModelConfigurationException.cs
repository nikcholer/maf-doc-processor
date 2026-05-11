namespace MafDocumentProcessor.Services;

public sealed class ModelConfigurationException : Exception
{
    public ModelConfigurationException(string message)
        : base(message)
    {
    }

    public ModelConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
