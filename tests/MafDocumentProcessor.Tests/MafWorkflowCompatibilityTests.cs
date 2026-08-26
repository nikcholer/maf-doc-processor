using System.Reflection;
using MafDocumentProcessor.Domain;
using Microsoft.Agents.AI.Workflows;

namespace MafDocumentProcessor.Tests;

public sealed class MafWorkflowCompatibilityTests
{
    [Theory]
    [InlineData(DocumentCategory.Receipt, "receipt-workflow")]
    [InlineData(DocumentCategory.ShoppingList, "shopping-list-workflow")]
    [InlineData(DocumentCategory.SujikoPuzzle, "sujiko-workflow")]
    [InlineData(DocumentCategory.ExpenseReport, "expense-report-workflow")]
    [InlineData(DocumentCategory.Invoice, "unsupported-document")]
    [InlineData(DocumentCategory.Unknown, "unsupported-document")]
    public async Task DocumentCategoryRoute_UsesExactlyOneWorkflowDestination(
        DocumentCategory category,
        string expectedDestination)
    {
        var classification = new ClassificationRouteExecutor();
        var receipt = BuildDocumentRouteWorkflow(DocumentCategory.Receipt, "receipt")
            .BindAsExecutor("receipt-workflow");
        var shoppingList = BuildDocumentRouteWorkflow(DocumentCategory.ShoppingList, "shopping-list")
            .BindAsExecutor("shopping-list-workflow");
        var sujiko = BuildDocumentRouteWorkflow(DocumentCategory.SujikoPuzzle, "sujiko")
            .BindAsExecutor("sujiko-workflow");
        var expenseReport = BuildDocumentRouteWorkflow(DocumentCategory.ExpenseReport, "expense-report")
            .BindAsExecutor("expense-report-workflow");
        var unsupported = new UnsupportedDocumentRouteExecutor();

        var workflow = new WorkflowBuilder(classification)
            .AddEdge<ClassifiedRoute>(
                classification,
                receipt,
                document => document is { Category: DocumentCategory.Receipt },
                "receipt")
            .AddEdge<ClassifiedRoute>(
                classification,
                shoppingList,
                document => document is { Category: DocumentCategory.ShoppingList },
                "shopping-list")
            .AddEdge<ClassifiedRoute>(
                classification,
                sujiko,
                document => document is { Category: DocumentCategory.SujikoPuzzle },
                "sujiko")
            .AddEdge<ClassifiedRoute>(
                classification,
                expenseReport,
                document => document is { Category: DocumentCategory.ExpenseReport },
                "expense-report")
            .AddEdge<ClassifiedRoute>(
                classification,
                unsupported,
                document => document is
                    { Category: DocumentCategory.Invoice or DocumentCategory.Unknown },
                "unsupported")
            .WithOutputFrom(receipt, shoppingList, sujiko, expenseReport, unsupported)
            .WithName("Document Routing Compatibility")
            .Build();

        var run = await InProcessExecution.RunAsync(workflow, category);
        var events = run.NewEvents.ToArray();
        var output = Assert.Single(events.OfType<WorkflowOutputEvent>());
        var outcome = Assert.IsType<DocumentRouteOutcome>(output.Data);

        Assert.Equal(category, outcome.Category);
        Assert.Equal(expectedDestination, outcome.Destination);
        var completedExecutorIds = events
            .OfType<ExecutorCompletedEvent>()
            .Select(completed => completed.ExecutorId)
            .ToHashSet(StringComparer.Ordinal);
        Assert.Equal(
            [expectedDestination],
            AllDocumentDestinations.Where(completedExecutorIds.Contains).ToArray());
        Assert.Contains(
            events,
            evt => evt.Data is CompatibilityEvent { Stage: var stage }
                && stage == expectedDestination);

        var mermaid = WorkflowVisualizer.ToMermaidString(workflow);
        var dot = WorkflowVisualizer.ToDotString(workflow);
        foreach (var destination in AllDocumentDestinations)
        {
            Assert.Contains(destination, mermaid, StringComparison.Ordinal);
            Assert.Contains(destination, dot, StringComparison.Ordinal);
        }
    }

    private static Microsoft.Agents.AI.Workflows.Workflow BuildDocumentRouteWorkflow(
        DocumentCategory category,
        string routeName)
    {
        var route = new DocumentRouteExecutor(category, routeName);
        return new WorkflowBuilder(route)
            .WithOutputFrom(route)
            .WithName($"{routeName} document route")
            .Build();
    }

    [Fact]
    public async Task FanOutAndFanIn_RunFixedLanesConcurrentlyAndAggregateDeterministically()
    {
        var concurrency = new LaneConcurrencyProbe(laneCount: 2);
        var start = new CaptureStartExecutor();
        var firstLane = new CaptureLaneExecutor("lane-one", order: 1, concurrency);
        var secondLane = new CaptureLaneExecutor("lane-two", order: 2, concurrency);
        var aggregate = new CaptureAggregationExecutor();

        var workflow = new WorkflowBuilder(start)
            .AddFanOutEdge(start, [firstLane, secondLane])
            .AddFanInBarrierEdge([firstLane, secondLane], aggregate)
            .WithOutputFrom(aggregate)
            .WithName("Fan Out Compatibility")
            .Build();

        var run = await InProcessExecution.RunAsync(workflow, new CaptureWork("capture-1"));
        var events = run.NewEvents.ToArray();
        var output = Assert.Single(events.OfType<WorkflowOutputEvent>());
        var result = Assert.IsType<CaptureAggregate>(output.Data);

        Assert.Equal(2, concurrency.MaximumConcurrentLanes);
        Assert.Equal(["lane-one", "lane-two"], result.LaneIds);
        Assert.Equal(
            ["lane-one", "lane-two"],
            events
                .Where(evt => evt.Data is LaneCompletedEvent)
                .Select(evt => ((LaneCompletedEvent)evt.Data!).LaneId)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray());

        var mermaid = WorkflowVisualizer.ToMermaidString(workflow);
        Assert.Contains("lane-one", mermaid, StringComparison.Ordinal);
        Assert.Contains("lane-two", mermaid, StringComparison.Ordinal);
        Assert.Contains("capture-aggregation", mermaid, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExecutorLinkedToRequestToken_CancelsAlreadyRunningWork()
    {
        using var requestCancellation = new CancellationTokenSource();
        var executor = new CancellableExecutor(requestCancellation.Token);
        var workflow = new WorkflowBuilder(executor)
            .WithOutputFrom(executor)
            .Build();

        var runTask = InProcessExecution.RunAsync(workflow, "start").AsTask();
        await executor.Started.WaitAsync(TimeSpan.FromSeconds(2));
        requestCancellation.Cancel();

        var completedTask = await Task.WhenAny(runTask, Task.Delay(TimeSpan.FromSeconds(2)));
        Assert.Same(runTask, completedTask);

        var run = await runTask;
        var error = Assert.Single(run.NewEvents.OfType<WorkflowErrorEvent>());
        Assert.True(ContainsCancellation(error.Exception));
    }

    private static bool ContainsCancellation(Exception? exception)
    {
        return exception switch
        {
            null => false,
            OperationCanceledException => true,
            TargetInvocationException { InnerException: { } inner } => ContainsCancellation(inner),
            AggregateException aggregate => aggregate.InnerExceptions.Any(ContainsCancellation),
            _ => ContainsCancellation(exception.InnerException)
        };
    }

    private static readonly string[] AllDocumentDestinations =
    [
        "receipt-workflow",
        "shopping-list-workflow",
        "sujiko-workflow",
        "expense-report-workflow",
        "unsupported-document"
    ];

    private sealed class ClassificationRouteExecutor()
        : Executor<DocumentCategory, ClassifiedRoute>("classification")
    {
        public override ValueTask<ClassifiedRoute> HandleAsync(
            DocumentCategory message,
            IWorkflowContext context,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(new ClassifiedRoute(message));
        }
    }

    private sealed class DocumentRouteExecutor(
        DocumentCategory acceptedCategory,
        string routeName)
        : Executor<ClassifiedRoute, DocumentRouteOutcome>($"{routeName}-handler")
    {
        public override async ValueTask<DocumentRouteOutcome> HandleAsync(
            ClassifiedRoute message,
            IWorkflowContext context,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(acceptedCategory, message.Category);
            var destination = $"{routeName}-workflow";
            await context.AddEventAsync(
                new WorkflowEvent(new CompatibilityEvent(destination)),
                cancellationToken);
            return new DocumentRouteOutcome(message.Category, destination);
        }
    }

    private sealed class UnsupportedDocumentRouteExecutor()
        : Executor<ClassifiedRoute, DocumentRouteOutcome>("unsupported-document")
    {
        public override async ValueTask<DocumentRouteOutcome> HandleAsync(
            ClassifiedRoute message,
            IWorkflowContext context,
            CancellationToken cancellationToken = default)
        {
            Assert.True(message.Category is DocumentCategory.Invoice or DocumentCategory.Unknown);
            await context.AddEventAsync(
                new WorkflowEvent(new CompatibilityEvent("unsupported-document")),
                cancellationToken);
            return new DocumentRouteOutcome(message.Category, "unsupported-document");
        }
    }

    private sealed class CaptureStartExecutor() : Executor<CaptureWork, CaptureWork>("capture-start")
    {
        public override ValueTask<CaptureWork> HandleAsync(
            CaptureWork message,
            IWorkflowContext context,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(message);
        }
    }

    private sealed class CaptureLaneExecutor : Executor<CaptureWork, LaneResult>
    {
        private readonly string _laneId;
        private readonly int _order;
        private readonly LaneConcurrencyProbe _concurrency;

        public CaptureLaneExecutor(
            string laneId,
            int order,
            LaneConcurrencyProbe concurrency)
            : base(laneId)
        {
            _laneId = laneId;
            _order = order;
            _concurrency = concurrency;
        }

        public override async ValueTask<LaneResult> HandleAsync(
            CaptureWork message,
            IWorkflowContext context,
            CancellationToken cancellationToken = default)
        {
            await _concurrency.EnterAsync(cancellationToken);
            try
            {
                await context.AddEventAsync(
                    new WorkflowEvent(new LaneCompletedEvent(_laneId)),
                    cancellationToken);
                return new LaneResult(_order, _laneId, message.CaptureId);
            }
            finally
            {
                _concurrency.Exit();
            }
        }
    }

    [YieldsOutput(typeof(CaptureAggregate))]
    private sealed class CaptureAggregationExecutor()
        : Executor<LaneResult>("capture-aggregation")
    {
        private readonly List<LaneResult> _results = [];

        public override ValueTask HandleAsync(
            LaneResult message,
            IWorkflowContext context,
            CancellationToken cancellationToken = default)
        {
            _results.Add(message);
            return ValueTask.CompletedTask;
        }

        protected override ValueTask OnMessageDeliveryFinishedAsync(
            IWorkflowContext context,
            CancellationToken cancellationToken = default)
        {
            var aggregate = new CaptureAggregate(
                _results
                    .OrderBy(result => result.Order)
                    .Select(result => result.LaneId)
                    .ToArray());
            _results.Clear();
            return context.YieldOutputAsync(aggregate, cancellationToken);
        }
    }

    private sealed class CancellableExecutor(CancellationToken requestCancellationToken)
        : Executor<string, string>("cancellable")
    {
        private readonly TaskCompletionSource _started = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => _started.Task;

        public override async ValueTask<string> HandleAsync(
            string message,
            IWorkflowContext context,
            CancellationToken cancellationToken = default)
        {
            _started.TrySetResult();
            using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                requestCancellationToken,
                cancellationToken);
            await Task.Delay(Timeout.InfiniteTimeSpan, linkedCancellation.Token);
            return message;
        }
    }

    private sealed class LaneConcurrencyProbe(int laneCount)
    {
        private readonly TaskCompletionSource _allStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int _activeLanes;
        private int _maximumConcurrentLanes;
        private int _startedLanes;

        public int MaximumConcurrentLanes => Volatile.Read(ref _maximumConcurrentLanes);

        public async ValueTask EnterAsync(CancellationToken cancellationToken)
        {
            var active = Interlocked.Increment(ref _activeLanes);
            UpdateMaximum(active);
            if (Interlocked.Increment(ref _startedLanes) == laneCount)
            {
                _allStarted.TrySetResult();
            }

            await _allStarted.Task.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
        }

        public void Exit()
        {
            Interlocked.Decrement(ref _activeLanes);
        }

        private void UpdateMaximum(int candidate)
        {
            var current = Volatile.Read(ref _maximumConcurrentLanes);
            while (candidate > current)
            {
                var observed = Interlocked.CompareExchange(
                    ref _maximumConcurrentLanes,
                    candidate,
                    current);
                if (observed == current)
                {
                    return;
                }

                current = observed;
            }
        }
    }

    private sealed record CompatibilityEvent(string Stage);

    private sealed record ClassifiedRoute(DocumentCategory Category);

    private sealed record DocumentRouteOutcome(
        DocumentCategory Category,
        string Destination);

    private sealed record CaptureWork(string CaptureId);

    private sealed record LaneResult(int Order, string LaneId, string CaptureId);

    private sealed record CaptureAggregate(IReadOnlyList<string> LaneIds);

    private sealed record LaneCompletedEvent(string LaneId);
}
