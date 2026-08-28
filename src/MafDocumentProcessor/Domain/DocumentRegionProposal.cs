namespace MafDocumentProcessor.Domain;

public sealed record ProposedNormalizedPoint(double X, double Y);

public sealed record ProposedNormalizedBounds(
    double X,
    double Y,
    double Width,
    double Height);

public sealed record CaptureRegionOverride
{
    public CaptureRegionOverride(
        ProposedNormalizedBounds bounds,
        IReadOnlyList<ProposedNormalizedPoint>? outline = null,
        string? sourceId = null)
    {
        ArgumentNullException.ThrowIfNull(bounds);
        Bounds = bounds;
        Outline = outline is null ? null : Array.AsReadOnly(outline.ToArray());
        SourceId = sourceId;
    }

    public ProposedNormalizedBounds Bounds { get; }

    public IReadOnlyList<ProposedNormalizedPoint>? Outline { get; }

    public string? SourceId { get; }
}

public sealed record DocumentRegionProposal
{
    public DocumentRegionProposal(
        string sourceItemId,
        int detectionIndex,
        ProposedNormalizedBounds bounds,
        IReadOnlyList<ProposedNormalizedPoint>? outline,
        decimal? confidence,
        string? sourceId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceItemId);
        ArgumentNullException.ThrowIfNull(bounds);
        if (detectionIndex <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(detectionIndex),
                detectionIndex,
                "The detection index must be one or greater.");
        }

        SourceItemId = sourceItemId;
        DetectionIndex = detectionIndex;
        Bounds = bounds;
        Outline = outline is null ? null : Array.AsReadOnly(outline.ToArray());
        Confidence = confidence;
        SourceId = sourceId;
    }

    public string SourceItemId { get; }

    public int DetectionIndex { get; }

    public ProposedNormalizedBounds Bounds { get; }

    public IReadOnlyList<ProposedNormalizedPoint>? Outline { get; }

    public decimal? Confidence { get; }

    public string? SourceId { get; }
}
