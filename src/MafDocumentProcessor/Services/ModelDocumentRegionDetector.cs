using MafDocumentProcessor.Configuration;
using MafDocumentProcessor.Domain;

namespace MafDocumentProcessor.Services;

public sealed class ModelDocumentRegionDetector(
    IModelChatClient chatClient,
    ICaptureDetectionImagePreparer imagePreparer,
    ModelRoleSettings settings,
    CompositeCaptureOptions captureOptions) : IDocumentRegionDetector
{
    public const string Operation = "document_region_detection";

    public async ValueTask<ModelResult<IReadOnlyList<DocumentRegionProposal>>> DetectAsync(
        OrientedCaptureSourceImage source,
        CancellationToken cancellationToken)
    {
        var modelImage = await imagePreparer.PrepareAsync(source, cancellationToken);
        var response = await chatClient.CompleteAsync(
            new ModelChatRequest(
                Operation,
                settings,
                [
                    ModelChatMessage.CreateSystem($$"""
                    Find separate physical documents visible in an image. Locate them only; do not classify them or extract their text.
                    Return no more than {{captureOptions.MaxDetectedRegionsPerSource}} regions.
                    Coordinates must be fractions of the correctly oriented image, with top-left origin and values from 0 to 1.
                    Return only compact JSON with this shape:
                    {"regions":[{"bounds":{"x":0.0,"y":0.0,"width":0.0,"height":0.0},"outline":[{"x":0.0,"y":0.0},{"x":0.0,"y":0.0},{"x":0.0,"y":0.0},{"x":0.0,"y":0.0}],"confidence":0.0}]}
                    Use an empty regions array when no physical document is visible.
                    Bounds are required. Outline is optional, but when present it must contain the four document corners in clockwise order.
                    Each box must contain the whole document: headers, logos, store names, addresses, barcodes, footers, and paper edges.
                    Prefer extra surrounding background over a tight crop. A little desk, carpet, or shadow around the document is better than clipping content.
                    Do not zoom in on the densest text, and do not crop through a header, footer, or side margin.
                    Do not merge separate documents into one region merely to add padding.
                    Confidence expresses visual certainty only. Do not omit a visible document merely because its type is unfamiliar.
                    """),
                    ModelChatMessage.CreateUser(
                        new ModelTextContent(
                            $"Locate the documents in this image. Include a margin of background around each one. Oriented source dimensions: {source.WidthPixels}x{source.HeightPixels}. File: {source.Source.Request.FileName}."),
                        new ModelImageContent(modelImage.Content, modelImage.ContentType))
                ],
                MaxOutputTokens: Math.Min(
                    4_000,
                    200 + (captureOptions.MaxDetectedRegionsPerSource * 160))),
            cancellationToken);

        try
        {
            return new ModelResult<IReadOnlyList<DocumentRegionProposal>>(
                DocumentRegionResponseParser.Parse(response.Content, source.Source.SourceItemId),
                response.Usage);
        }
        catch (DocumentModelResponseException ex)
        {
            throw new DocumentRegionModelResponseException(
                ex.Message,
                response.Usage,
                ex);
        }
    }
}
