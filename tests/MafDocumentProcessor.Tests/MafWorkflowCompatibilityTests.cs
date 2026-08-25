using System.Reflection;
using Microsoft.Agents.AI.Workflows;

namespace MafDocumentProcessor.Tests;

public sealed class MafWorkflowCompatibilityTests
{
    [Theory]
    [InlineData(4, 8, "positive-subworkflow")]
    [InlineData(-4, -5, "negative-route")]
    public async Task ConditionalRoute_UsesExactlyOneTypedDestination(
        int input,
        int expectedOutput,
        string expectedDestination)
    {
        var route = new RouteExecutor();
        var multiply = new MultiplyExecutor();
        var positiveWorkflow = new WorkflowBuilder(multiply)
            .WithOutputFrom(multiply)
            .WithName("Positive Route")
            .Build();
        var positive = positiveWorkflow.BindAsExecutor("positive-subworkflow");
        var negative = new NegativeRouteExecutor();

        var workflow = new WorkflowBuilder(route)
            .AddEdge<int>(route, positive, value => value >= 0, "non-negative")
            .AddEdge<int>(route, negative, value => value < 0, "negative")
            .WithOutputFrom(positive, negative)
            .WithName("Conditional Compatibility")
            .Build();

        var run = await InProcessExecution.RunAsync(workflow, input);
        var events = run.NewEvents.ToArray();
        var output = Assert.Single(events.OfType<WorkflowOutputEvent>());

        Assert.Equal(expectedOutput, Assert.IsType<int>(output.Data));
        Assert.Contains(
            events.OfType<ExecutorCompletedEvent>(),
            completed => completed.ExecutorId == expectedDestination);
        Assert.DoesNotContain(
            events.OfType<ExecutorCompletedEvent>(),
            completed => completed.ExecutorId == (input >= 0 ? "negative-route" : "positive-subworkflow"));

        if (input >= 0)
        {
            Assert.Contains(events, evt => evt.Data is CompatibilityEvent { Stage: "sub-workflow" });
        }
        else
        {
            Assert.DoesNotContain(events, evt => evt.Data is CompatibilityEvent { Stage: "sub-workflow" });
        }

        var mermaid = WorkflowVisualizer.ToMermaidString(workflow);
        var dot = WorkflowVisualizer.ToDotString(workflow);
        Assert.Contains("positive-subworkflow", mermaid, StringComparison.Ordinal);
        Assert.Contains("negative-route", mermaid, StringComparison.Ordinal);
        Assert.Contains("positive-subworkflow", dot, StringComparison.Ordinal);
        Assert.Contains("negative-route", dot, StringComparison.Ordinal);
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

    private sealed class RouteExecutor() : Executor<int, int>("typed-route")
    {
        public override ValueTask<int> HandleAsync(
            int message,
            IWorkflowContext context,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(message);
        }
    }

    private sealed class MultiplyExecutor() : Executor<int, int>("multiply")
    {
        public override async ValueTask<int> HandleAsync(
            int message,
            IWorkflowContext context,
            CancellationToken cancellationToken = default)
        {
            await context.AddEventAsync(
                new WorkflowEvent(new CompatibilityEvent("sub-workflow")),
                cancellationToken);
            return message * 2;
        }
    }

    private sealed class NegativeRouteExecutor() : Executor<int, int>("negative-route")
    {
        public override ValueTask<int> HandleAsync(
            int message,
            IWorkflowContext context,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(message - 1);
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

    private sealed record CaptureWork(string CaptureId);

    private sealed record LaneResult(int Order, string LaneId, string CaptureId);

    private sealed record CaptureAggregate(IReadOnlyList<string> LaneIds);

    private sealed record LaneCompletedEvent(string LaneId);
}
