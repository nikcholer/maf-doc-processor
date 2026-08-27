# Composite Capture Measurements

## Purpose

This records the E3 comparison between sequential capture processing and the configured bounded-parallel lane layout. It is a repeatable local harness, not a service-level objective.

The observation was captured on 26 August 2026 from a Windows development workstation using .NET SDK 10.0.400 and the `net10.0` target.

## Protocol

The harness is `CaptureParallelismMeasurementTests.BoundedParallelLanes_AreFasterThanASingleLaneForIndependentSources`. It does not call a live provider. Each of four independent source images pays a 250 ms simulated detection delay and a 250 ms simulated classification delay, then an immediate stubbed receipt extraction.

```powershell
dotnet test .\MafDocumentProcessor.sln --filter FullyQualifiedName~CaptureParallelismMeasurementTests --logger "console;verbosity=detailed"
```

| Layout | Source lanes | Member lanes |
| --- | ---: | ---: |
| Sequential baseline | 1 | 1 |
| Configured bounded parallel | 2 | 2 |

Both layouts process the same four synthetic PNG sources and must produce the same member count and model-call count.

## Observation

| Layout | Wall-clock | Members | Model calls |
| --- | ---: | ---: | ---: |
| Sequential 1/1 | 2,203 ms | 4 | 12 |
| Bounded 2/2 | 1,060 ms | 4 | 12 |

The 12 calls are four detections, four classifications, and four extractions. Bounded lanes did not change the call count. Wall-clock time fell from 2,203 ms to 1,060 ms, about 2.1×, which matches two source lanes overlapping independent 250 ms detections and two member lanes overlapping independent 250 ms classifications.

## Interpretation

- Superstep fan-in still waits for the slowest lane, so the win is bounded by lane count and by how even the work is.
- Real provider calls are slower and more variable than these sleeps. The harness only proves that the fixed-lane graph can overlap independent work without extra model calls.
- Production defaults remain `MaxConcurrentSources = 2` and `MaxConcurrentMembers = 4`.
- Do not treat this as a hosted capacity number. Re-run the harness on the target machine before changing lane counts.

## E7 Release Verification

The same harness was repeated on 27 August 2026 during the clean release audit. The sequential 1/1 layout took 2,429 ms and the bounded 2/2 layout took 1,055 ms. Both produced four members and 12 model calls. The approximately 2.3-times observation is consistent with the original result; timing variation between runs reinforces that this is topology evidence rather than a capacity target.
