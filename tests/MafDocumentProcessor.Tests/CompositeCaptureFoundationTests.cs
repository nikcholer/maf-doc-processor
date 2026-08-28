using System.Text.Json;
using MafDocumentProcessor.Api.Configuration;
using MafDocumentProcessor.Configuration;
using MafDocumentProcessor.Domain;
using MafDocumentProcessor.Workflow;
using Microsoft.Extensions.Configuration;

namespace MafDocumentProcessor.Tests;

public sealed class CompositeCaptureFoundationTests
{
    [Fact]
    public void Request_AssignsStableSourceOrderAndRequestScopedIdentifiers()
    {
        var receivedAt = new DateTimeOffset(2026, 8, 26, 12, 0, 0, TimeSpan.Zero);
        var sourceRequests = new List<FileRequest>
        {
            CreateFileRequest("desk.jpg", receivedAt),
            CreateFileRequest("ticket.png", receivedAt)
        };

        var request = CompositeCaptureRequest.Create(
            sourceRequests,
            receivedAt,
            sourceId: "expense-claim-17",
            captureId: "capture-test-001",
            traceId: "api-trace-001");
        sourceRequests.Add(CreateFileRequest("added-too-late.png", receivedAt));

        Assert.Equal("capture-test-001", request.CaptureId);
        Assert.Equal("expense-claim-17", request.SourceId);
        Assert.Equal("api-trace-001", request.TraceId);
        Assert.Equal(receivedAt, request.ReceivedAt);
        Assert.Collection(
            request.Sources,
            source =>
            {
                Assert.Equal("source-001", source.SourceItemId);
                Assert.Equal(1, source.Index);
                Assert.Equal("desk.jpg", source.Request.FileName);
                Assert.Equal("expense-claim-17", source.Request.SourceId);
            },
            source =>
            {
                Assert.Equal("source-002", source.SourceItemId);
                Assert.Equal(2, source.Index);
                Assert.Equal("ticket.png", source.Request.FileName);
                Assert.Equal("expense-claim-17", source.Request.SourceId);
            });
        Assert.Equal("source-002-document-003", CaptureIdentifiers.MemberId("source-002", 3));
    }

    [Fact]
    public void Request_RejectsAnEmptySourceCollection()
    {
        var exception = Assert.Throws<ArgumentException>(() => CompositeCaptureRequest.Create(
            [],
            DateTimeOffset.UtcNow));

        Assert.Contains("at least one source", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Request_AppliesOverridesOnlyToSelectedSourcesAndPreservesAnExplicitEmptySet()
    {
        var receivedAt = new DateTimeOffset(2026, 8, 26, 12, 0, 0, TimeSpan.Zero);
        var overrides = new Dictionary<int, IReadOnlyList<CaptureRegionOverride>>
        {
            [1] =
            [
                new CaptureRegionOverride(
                    new ProposedNormalizedBounds(0.1, 0.2, 0.3, 0.4),
                    [
                        new ProposedNormalizedPoint(0.1, 0.2),
                        new ProposedNormalizedPoint(0.4, 0.2),
                        new ProposedNormalizedPoint(0.4, 0.6),
                        new ProposedNormalizedPoint(0.1, 0.6)
                    ],
                    "claim:receipt-1")
            ],
            [2] = []
        };

        var request = CompositeCaptureRequest.Create(
            [
                CreateFileRequest("desk.jpg", receivedAt),
                CreateFileRequest("empty.jpg", receivedAt),
                CreateFileRequest("automatic.jpg", receivedAt)
            ],
            receivedAt,
            regionOverridesBySourceIndex: overrides);

        var first = Assert.Single(request.Sources[0].RegionOverrides!);
        Assert.Equal("source-001", first.SourceItemId);
        Assert.Equal(1, first.DetectionIndex);
        Assert.Null(first.Confidence);
        Assert.Equal(4, first.Outline?.Count);
        Assert.Equal("claim:receipt-1", first.SourceId);
        Assert.Empty(request.Sources[1].RegionOverrides!);
        Assert.Null(request.Sources[2].RegionOverrides);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void Request_RejectsOverrideSourceIndexesOutsideTheRequest(int sourceIndex)
    {
        var receivedAt = DateTimeOffset.UtcNow;
        var overrides = new Dictionary<int, IReadOnlyList<CaptureRegionOverride>>
        {
            [sourceIndex] = []
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => CompositeCaptureRequest.Create(
            [CreateFileRequest("one.jpg", receivedAt), CreateFileRequest("two.jpg", receivedAt)],
            receivedAt,
            regionOverridesBySourceIndex: overrides));
    }

    [Fact]
    public void Geometry_RejectsNonFiniteOrOutOfRangeValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new NormalizedPoint(double.NaN, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new NormalizedPoint(0, double.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => new NormalizedBounds(-0.1, 0, 0.2, 0.2));
        Assert.Throws<ArgumentOutOfRangeException>(() => new NormalizedBounds(0.9, 0, 0.2, 0.2));
        Assert.Throws<ArgumentOutOfRangeException>(() => new NormalizedBounds(0, 0, 0, 0.2));
    }

    [Fact]
    public void Region_RequiresAQuadrilateralAndCopiesCallerCollections()
    {
        var outline = new List<NormalizedPoint>
        {
            new(0.1, 0.1),
            new(0.4, 0.1),
            new(0.4, 0.5),
            new(0.1, 0.5)
        };
        var warnings = new List<string> { "slight overlap" };
        var region = new DetectedDocumentRegion(
            "source-001",
            1,
            new NormalizedBounds(0.1, 0.1, 0.3, 0.4),
            outline,
            0.92m,
            warnings);

        outline.Clear();
        warnings.Clear();

        Assert.Equal(4, region.Outline?.Count);
        Assert.Equal("slight overlap", Assert.Single(region.Warnings));
        Assert.Equal(0.12, region.Bounds.Area, 8);
        Assert.Throws<ArgumentException>(() => new DetectedDocumentRegion(
            "source-001",
            1,
            new NormalizedBounds(0.1, 0.1, 0.3, 0.4),
            [new NormalizedPoint(0.1, 0.1)]));
    }

    [Fact]
    public void StatusAndDisposition_SerializeAsStableNames()
    {
        var json = JsonSerializer.Serialize(new
        {
            Status = CaptureProcessingStatus.PartiallySucceeded,
            MemberStatus = CaptureMemberStatus.Processed,
            Disposition = CaptureMemberDisposition.Review
        });

        Assert.Contains("\"Status\":\"PartiallySucceeded\"", json, StringComparison.Ordinal);
        Assert.Contains("\"MemberStatus\":\"Processed\"", json, StringComparison.Ordinal);
        Assert.Contains("\"Disposition\":\"Review\"", json, StringComparison.Ordinal);

        var boundsJson = JsonSerializer.Serialize(new NormalizedBounds(0.1, 0.2, 0.3, 0.4));
        var roundTrip = JsonSerializer.Deserialize<NormalizedBounds>(boundsJson);
        Assert.NotNull(roundTrip);
        Assert.Equal(0.1, roundTrip.X);
        Assert.Equal(0.4, roundTrip.Height);
        Assert.DoesNotContain("Area", boundsJson, StringComparison.Ordinal);
    }

    [Fact]
    public void FailedMember_RequiresAnErrorAndRejectedDisposition()
    {
        var region = new DetectedDocumentRegion(
            "source-001",
            1,
            new NormalizedBounds(0.1, 0.1, 0.3, 0.4));
        var member = new CaptureMember(
            "source-001",
            CaptureIdentifiers.MemberId("source-001", 1),
            1,
            1,
            region);

        var failed = new CaptureMemberResult(
            member,
            CaptureMemberStatus.Failed,
            CaptureMemberDisposition.Rejected,
            ["The crop was invalid."],
            result: null,
            new CaptureProcessingError("invalid_detected_region", "The crop was invalid.", member.MemberId));

        Assert.Equal("invalid_detected_region", failed.Error?.Code);
        Assert.Throws<ArgumentException>(() => new CaptureMemberResult(
            member,
            CaptureMemberStatus.Failed,
            CaptureMemberDisposition.Review,
            [],
            result: null,
            new CaptureProcessingError("invalid_detected_region", "Invalid.")));
    }

    [Fact]
    public void WorkflowContext_CarriesCorrelationIntoSourceAndMemberHandoffs()
    {
        var capture = new CaptureWorkflowContext("trace-123", "capture-123", "claim-123");

        var source = capture.ForSource("source-002");
        var member = capture.ForMember("source-002", "source-002-document-004");

        Assert.Equal("trace-123", source.TraceId);
        Assert.Equal("capture-123", source.CaptureId);
        Assert.Equal("claim-123", source.SourceId);
        Assert.Equal("source-002", source.SourceItemId);
        Assert.Null(source.MemberId);
        Assert.Equal("source-002-document-004", member.MemberId);
    }

    [Fact]
    public void DefaultOptions_AreValidAndKeepConcurrencyBounded()
    {
        var options = new CompositeCaptureOptions().Validate();

        Assert.InRange(options.MaxConcurrentSources, 1, options.MaxSourceCount);
        Assert.InRange(options.MaxConcurrentMembers, 1, options.MaxMembersPerCapture);
        Assert.True(
            options.OverlapReviewIntersectionOverUnionThreshold
            < options.DuplicateIntersectionOverUnionThreshold);
        Assert.Equal(0.03, options.RegionEdgePadding);
    }

    [Fact]
    public void Options_RejectContradictoryOrUnboundedValues()
    {
        Assert.Throws<CompositeCaptureConfigurationException>(() =>
            (new CompositeCaptureOptions() with { MaxSourceCount = 0 }).Validate());
        Assert.Throws<CompositeCaptureConfigurationException>(() =>
            (new CompositeCaptureOptions() with { MaxConcurrentSources = 0 }).Validate());
        Assert.Throws<CompositeCaptureConfigurationException>(() =>
            (new CompositeCaptureOptions() with
            {
                OverlapReviewIntersectionOverUnionThreshold = 0.95,
                DuplicateIntersectionOverUnionThreshold = 0.90
            }).Validate());
        Assert.Throws<CompositeCaptureConfigurationException>(() =>
            (new CompositeCaptureOptions() with { MinRegionArea = double.NaN }).Validate());
        Assert.Throws<CompositeCaptureConfigurationException>(() =>
            (new CompositeCaptureOptions() with { RegionEdgePadding = 1.2 }).Validate());
    }

    [Fact]
    public void ConfigurationLoader_BindsOverridesAndValidatesThem()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CompositeCapture:MaxSourceCount"] = "3",
                ["CompositeCapture:MaxMembersPerCapture"] = "12",
                ["CompositeCapture:MaxConcurrentSources"] = "3",
                ["CompositeCapture:MaxConcurrentMembers"] = "6"
            })
            .Build();

        var options = ApiConfigurationLoader.LoadCompositeCaptureOptions(configuration);

        Assert.Equal(3, options.MaxSourceCount);
        Assert.Equal(12, options.MaxMembersPerCapture);
        Assert.Equal(3, options.MaxConcurrentSources);
        Assert.Equal(6, options.MaxConcurrentMembers);

        var invalidConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CompositeCapture:MaxConcurrentMembers"] = "0"
            })
            .Build();

        Assert.Throws<CompositeCaptureConfigurationException>(() =>
            ApiConfigurationLoader.LoadCompositeCaptureOptions(invalidConfiguration));
    }

    private static FileRequest CreateFileRequest(string fileName, DateTimeOffset receivedAt)
    {
        return new FileRequest(
            [1, 2, 3],
            fileName,
            fileName.EndsWith(".jpg", StringComparison.Ordinal) ? "image/jpeg" : "image/png",
            3,
            receivedAt,
            SourceId: null);
    }
}
