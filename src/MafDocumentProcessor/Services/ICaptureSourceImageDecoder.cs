using MafDocumentProcessor.Domain;

namespace MafDocumentProcessor.Services;

public interface ICaptureSourceImageDecoder
{
    OrientedCaptureSourceImage Decode(CompositeCaptureSource source);
}
