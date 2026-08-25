# Dependency Baseline Decision

## Status

Accepted for implementation on 25 August 2026. Issue #16 records the review; issue #17 applies the selected updates in bounded groups.

## Context

The next workflow phases depend on conditional routing, reusable sub-workflows, bounded fan-out/fan-in, custom events, and inspectable topology. The dependency baseline should support those capabilities without combining unrelated framework migrations into the same change.

The review compared the repository's direct package references with stable releases available on 25 August 2026, inspected official release notes, and ran restore, outdated-package, and vulnerability checks. The current dependency graph reports no known vulnerable packages.

## Selected Baseline

| Area | Current | Selected for issue #17 | Decision |
| --- | --- | --- | --- |
| .NET target framework | `net8.0` | `net10.0` | Move to the current LTS release before .NET 8 support ends on 10 November 2026 |
| .NET SDK | `8.0.419` | `10.0.400`, retaining `latestFeature` roll-forward | Pin the current .NET 10 feature band |
| `Microsoft.Agents.AI` | `1.4.0` | `1.19.0` | Keep all MAF packages on one version |
| `Microsoft.Agents.AI.OpenAI` | `1.4.0` | `1.19.0` | Keep all MAF packages on one version |
| `Microsoft.Agents.AI.Workflows` | `1.4.0` | `1.19.0` | Provides and corrects the selected workflow capabilities |
| `OpenAI` | `2.10.0` | `2.13.0` | Compatible with MAF 1.19 and includes a relevant large image/file memory improvement |
| `SixLabors.ImageSharp` | `3.1.12` | `3.1.12` | Defer the 4.x major-version and licensing migration |
| `Microsoft.AspNetCore.Mvc.Testing` | `8.0.27` | `10.0.11` | Align the integration-test host with `net10.0` |
| `Microsoft.NET.Test.Sdk` | `17.8.0` | `18.9.0` | Adopt the current stable test platform |
| `coverlet.collector` | `6.0.0` | `10.0.1` | Adopt the current stable coverage collector |
| `xunit` | `2.9.3` | `2.9.3` | Retain xUnit v2; a move to the `xunit.v3` package is a separate test-framework migration |
| `xunit.runner.visualstudio` | `4.0.0` | `4.0.0` | Already current and supports the retained tests |

Issue #17 should recheck stable patch versions immediately before applying the changes. A newer compatible patch may replace a selected patch version, but a new major version requires another explicit decision.

## MAF Compatibility Evidence

A temporary executable probe compiled and ran against .NET 10 and MAF 1.19.0. It demonstrated:

- typed conditional `AddEdge<T>` routing;
- a built workflow bound and reused as an executor;
- fixed-lane `AddFanOutEdge<T>` and `AddFanInBarrierEdge` processing;
- deterministic aggregation after every lane reports;
- custom workflow events and standard workflow output;
- Mermaid and DOT topology generation; and
- request cancellation when executor work links the request token.

Two details are part of the implementation contract:

1. Normal `InProcessExecution.RunAsync` executes distinct ready executors concurrently within a workflow superstep. `InProcessExecution.Concurrent` instead enables concurrent *runs of the same workflow instance* and requires share-capable or factory-created executors. The capture design needs the former, not the latter.
2. Executors that begin cancellable classification, detection, or member work must continue to capture and link the HTTP request token with the handler token. The runner's wait token alone must not be treated as the cancellation boundary for already-running executor work.

MAF 1.19 depends on OpenAI 2.10.0 or later, so the selected direct OpenAI 2.13.0 reference is compatible. Relevant MAF releases since 1.4 also include workflow output, fan-in checkpoint, message-ordering, diagnostics, and telemetry-failure corrections. The MCP long-running-task change noted in MAF 1.19 does not affect this application because it does not use MCP.

## Update Groups for Issue #17

Apply and validate the baseline in separable commits or clearly reported groups:

1. Move the target frameworks, pinned SDK, and ASP.NET Core integration-test host to .NET 10. Restore, build, and run the offline suite.
2. Align all MAF packages on 1.19.0 and update OpenAI to 2.13.0. Add permanent compatibility coverage for routing, sub-workflows, fan-out/fan-in, events, topology, deterministic aggregation, and linked cancellation before running the full suite and API tests.
3. Update the test SDK and coverage collector, then rerun the full offline suite and coverage collection.

Package updates must not change the public API contract or add model calls to existing document paths. Any behavioural incompatibility that cannot be corrected within the relevant group should stop that group and be recorded rather than hidden by a broader rewrite.

## Deferred Updates

### ImageSharp 4.1.1

ImageSharp 4 is not a routine package refresh. Direct consumers must provide a build-time licence key, and the project must decide how that secret is supplied to local, CI, and public-repository builds without committing it. The major version also deserves focused decode, orientation, resize, crop, encoder, memory, and image-regression tests. Version 3.1.12 remains selected until that licensing and migration work is separately scoped; the current audit reports no known vulnerability requiring an immediate move.

### xUnit v3

The current `xunit` 2.9.3 package is retained. Moving to the `xunit.v3` package changes the test framework and may require source, runner, and CI adjustments. It provides no prerequisite capability for the selected application work and should not be coupled to the runtime and workflow updates.

## Sources

- [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy)
- [Microsoft Agent Framework 1.19.0 release](https://github.com/microsoft/agent-framework/releases/tag/dotnet-1.19.0)
- [OpenAI .NET 2.13.0 release](https://github.com/openai/openai-dotnet/releases/tag/OpenAI_2.13.0)
- [ImageSharp documentation and licence-key guidance](https://docs.sixlabors.com/articles/imagesharp/index.html)
- [ImageSharp 4 announcement](https://sixlabors.com/posts/announcing-imagesharp-400/)
- [coverlet 10.0.1 release](https://github.com/coverlet-coverage/coverlet/releases/tag/v10.0.1)
- [Microsoft Test Platform 18.9.0 release](https://github.com/microsoft/vstest/releases/tag/v18.9.0)
- [xUnit Visual Studio runner 4.0.0 release](https://github.com/xunit/visualstudio.xunit/releases/tag/4.0.0)

