namespace MafDocumentProcessor.Services;

public sealed class CaptureSourceValidationException : Exception
{
    public CaptureSourceValidationException(string message)
        : base(message)
    {
    }

    public CaptureSourceValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
