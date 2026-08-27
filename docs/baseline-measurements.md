# Current Workflow Baseline

## Purpose

This baseline provides a reproducible comparison point for later workflow changes. It records the offline regression suite and one representative provider-backed run through the complete existing document path. It is not a service-level objective or a statistically meaningful performance benchmark.

The comparison base is commit `a1563c8`, after the API contracts were protected in issue #20. Measurements were captured on 25 August 2026 from a Windows development workstation using .NET SDK 10.0.400 and the `net10.0` target.

## Protocol

The offline baseline builds and runs the entire solution without enabling any live-provider test:

```powershell
dotnet test .\MafDocumentProcessor.sln --no-restore -p:UseAppHost=false -p:OutDir=.build\issue18-offline\
```

The live observation protocol uses the versioned synthetic Sujiko asset and exercises the same application-level path as an individual upload: classification image preparation, classification, routing, extraction image preparation, the Sujiko MAF workflow, validation, an optional bounded repair, and result aggregation.

```powershell
$env:MAF_RUN_LIVE_ASSET_TESTS = "1"
dotnet test .\MafDocumentProcessor.sln `
  --filter FullyQualifiedName~RunAsync_CanBeLiveCheckedAndMeasuredAgainstSyntheticSujikoAsset `
  --logger "console;verbosity=detailed"
```

`TOGETHER_API_KEY` must also be available. The live test emits structured measurements to the test output; it does not emit the source image or raw provider response.

## Offline Baseline

| Measure | Result |
| --- | ---: |
| Passed | 74 |
| Failed | 0 |
| Skipped | 0 |
| Test-runner duration | 1 s |
| Complete command duration | 10,773 ms |

The complete command duration includes build and test-process startup, so the test-runner duration is the better indicator for later suite comparisons. Both values remain sensitive to machine load and build-cache state.

## Representative Live Observation

The recorded 25 August live run used the retired predecessor photograph with `Qwen/Qwen3.5-9B` for both configured model roles. Before public visibility, that photograph was replaced by `synthetic-sujiko-newspaper.jpg`, which preserves the same visible puzzle facts without personal metadata or unclear source provenance. The measurements below remain a historical observation of the predecessor and must not be presented as measurements of the replacement until the opt-in live check is deliberately rerun. The historical run made two calls and did not invoke the repair path.

| Operation | Input tokens | Output tokens | Total tokens | Model duration | Estimated cost (USD) |
| --- | ---: | ---: | ---: | ---: | ---: |
| Classification | 1,137 | 56 | 1,193 | 2,583 ms | $0.00012210 |
| Sujiko extraction | 2,641 | 111 | 2,752 | 1,982 ms | $0.00028075 |
| **Total** | **3,778** | **167** | **3,945** | **4,565 ms** | **$0.00040285** |

End-to-end workflow time was 5,382 ms. This includes local image preparation and orchestration around the model calls. The complete test command took 16,724 ms, including build and test-host startup.

Classification selected `SujikoPuzzle` correctly. The extraction returned the correct four totals and the given `7`, but placed the printed `3` in row 2 rather than its known row 1 position. The application reported the result as structurally valid and successful, with no human review required; the provider-backed golden assertion therefore failed. This is part of the baseline rather than a prompt change hidden inside the measurement task. The [versioned golden set](golden-set.md) records the correct known answer alongside the other current document paths.

## Interpretation

- Token counts and durations come from the provider response captured in `DocumentModelUsage`.
- Estimated cost uses the configured prices of $0.10 per million input tokens and $0.15 per million output tokens. It excludes local compute and is not a billing statement.
- Model duration is the sum of individual call durations. Workflow duration also includes local preprocessing and orchestration. Command duration additionally includes build and test-host overhead.
- This single run is a smoke baseline. Provider load, network conditions, image content, repair routing, and model behaviour can all change later observations. Use repeated runs and report their distribution before making performance claims.
- Compare later designs on call count, tokens, estimated cost, model duration, end-to-end duration, and known-answer quality together; improving one measure does not imply an overall improvement.
