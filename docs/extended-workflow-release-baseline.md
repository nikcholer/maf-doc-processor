# Extended Workflow Release Baseline

## Status and Release Gate

This note records the E7 baseline tagged after issue #8. The annotated tag `extended-workflow-baseline` points at `f1b198c` on `main`, after [Release baseline run 33053217495](https://github.com/nikcholer/maf-doc-processor/actions/runs/33053217495) succeeded and the same commit passed the clean local commands below.

The repository has no deployment or package-publishing step. The tag identifies a reproducible local application baseline and follows the descriptive annotated-tag convention established by `initial-demo`.

## Architecture Change Since `initial-demo`

| Area | `initial-demo` | Extended workflow baseline |
| --- | --- | --- |
| Supported results | Receipt, shopping list, and Sujiko | Adds expense reports with arithmetic validation, policy, and attestation |
| Document orchestration | Separate document paths | One typed top-level MAF router with bound document-specific child workflows |
| Intake | One image containing one document | Preserves individual upload and adds bounded multi-source, multi-region capture |
| Parallel work | Foreground single-document processing | Fixed source and member lanes with deterministic fan-in and cancellation |
| Capture correction | None | Request-scoped add, move, resize, delete, reorder, and resubmit of normalized regions |
| API description | Stable response envelope with opaque `document.data` | Four typed `oneOf` payload schemas without changing JSON |
| Operations | Per-path results and model usage | Cross-workflow correlation, exact aggregate usage, bounded retries/timeouts, and documented resource limits |
| Runtime baseline | .NET 8 and early stable MAF | .NET SDK 10.0.400, `net10.0`, and MAF 1.19.0 |

The default route remains deterministic outside classification and extraction. It adds no quality-review model calls, persistence, background execution, external submission, or hosted-service assumptions.

## Verification Evidence

Local verification was captured on 27 August 2026 from a clean build on Windows using .NET SDK 10.0.400 and Node.js 20.19.5.

| Check | Result |
| --- | --- |
| Clean, restore, and full solution test | 219 passed; 0 failed; 0 skipped |
| Focused golden-set and cancellation selection | 10 passed; 0 failed; 0 skipped |
| Capture UI tests | 11 passed; 0 failed |
| Dependency vulnerability audit | No known vulnerable packages in any project |
| Dependency update review | ImageSharp 4.1.1 is the only newer direct package and remains an accepted separate licensing/migration decision |
| OpenAPI integration | All four typed payload alternatives, nullable unsupported result, and declared response status schemas pass offline integration coverage |

The GitHub Actions workflow repeats restore, a warning-free Release build, the full provider-free .NET and UI suites, and the vulnerability audit on pull requests and `main`. NuGet vulnerability warnings `NU1901` through `NU1904` fail its restore step. The later audit step parses JSON from `dotnet list package --vulnerable` and fails the job if any project reports a vulnerability, because the console command exits 0 in the clean case. It uses .NET SDK 10.0.400 and Node.js 24. Live tests are deliberately excluded from CI because they consume provider credits and are non-deterministic.

## Selected Live Observations

The three versioned provider-backed fixture checks were deliberately enabled once during the E7 audit. All passed:

- the natural desk fixture produced all three expected document-region proposals;
- the synthetic expense report classified and extracted successfully with two lines, a GBP 48.50 total, and required ownership attestation; its workflow used 3,463 tokens, took 10,334 ms, and estimated $0.00036270; and
- the then-current Sujiko fixture matched every known total and given cell after one bounded repair extraction.

That E7 observation used the retired predecessor photograph. Before public visibility, the repository replaced it with an AI-generated fixture containing the same puzzle facts and no EXIF metadata. The figures below remain historical predecessor measurements; the replacement requires a new deliberately enabled live run before it can be compared.

The Sujiko observation is comparable with the earlier smoke result in [current workflow baseline measurements](baseline-measurements.md), but it is not evidence of a statistically reliable model-quality improvement:

| Observation | Calls | Tokens | Model duration | Workflow duration | Estimated cost | Known answer |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Earlier baseline | 2 | 3,945 | 4,565 ms | 5,382 ms | $0.00040285 | Did not match |
| E7 release audit | 3 | 6,958 | 9,320 ms | 9,764 ms | $0.00071110 | Matched after repair |

The later result cost more and took longer because it used the bounded repair path. Model and provider variability also changed between observations, so neither row should be used as a service-level objective or causal before/after benchmark.

The provider-free capture harness remained bounded and preserved its 12-call total. During this audit, four synthetic sources took 2,429 ms with one source/member lane and 1,055 ms with two lanes, approximately 2.3 times faster. See [composite capture measurements](composite-capture-measurements.md) for the protocol and original observation.

## Cleanup and Deferred Scope

The audit found no superseded production route to remove. The Analyst/Critic quality workflow remains an opt-in E6 experiment harness with separate tests; its decision gate has not passed, and it is not registered in the API or UI.

The remaining open work is deliberately outside this baseline:

- E5 checkpointing and in-app pause/resume are out of scope for this converter; they belong to a surrounding workflow system;
- E6 agent collaboration is deferred until November 2026, then gated on a measured step change in quality, speed, or price;
- alternative capture-detection models (#53) and per-region crop/rotation refinement (#58) remain in the icebox; and
- ImageSharp 4 and xUnit v3 remain separately scoped dependency migrations.

## Reproduction and Tagging

From a clean checkout of the merged `main` commit:

```powershell
dotnet clean .\MafDocumentProcessor.sln
dotnet restore .\MafDocumentProcessor.sln
dotnet build .\MafDocumentProcessor.sln --configuration Release --no-restore
dotnet test .\MafDocumentProcessor.sln --configuration Release --no-build --no-restore
node --test .\tests\ui\capture-ui.test.cjs
dotnet list .\MafDocumentProcessor.sln package --vulnerable --include-transitive --no-restore
git diff --check
```

Those commands, plus the `main` workflow, were the gate for creating the tag:

```powershell
git tag -a extended-workflow-baseline -m "Extended workflow baseline"
git push origin extended-workflow-baseline
```

To check out this baseline later:

```powershell
git checkout extended-workflow-baseline
```
