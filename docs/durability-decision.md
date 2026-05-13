# Durability Decision

## Decision

Do not add durable pause/resume or checkpointing to the current local receipt/shopping-list demo.

The current processing model is a bounded HTTP request:

- Images are uploaded to the local API.
- Classification and extraction use configured per-call timeouts.
- Transient provider failures are retried with bounded backoff.
- Request cancellation now propagates to model-call executors.
- Successful runs are returning in seconds with the current Qwen configuration.

Adding MAF Durable Task or a local job store now would add operational and testing complexity without solving a current user problem.

## Options Considered

| Option | Fit Now | Notes |
| --- | --- | --- |
| MAF Durable Task locally | Low | Useful once workflows genuinely need pause/resume, external workers, durable timers, or human-review waits. It is more infrastructure than the local demo needs. |
| Lightweight local job store/checkpoint files | Low | Simpler than Durable Task, but it creates a second execution model, persistence rules, cleanup concerns, and partially duplicated retry/resume behavior. |
| Defer durability until hosted/background processing exists | Best | Keeps the current product simple while preserving the decision point for a later architecture phase. |

## Reopen Criteria

Revisit durability when at least one of these becomes true:

- Processing moves out of a single foreground HTTP request into queued/background jobs.
- Human review requires pausing and resuming a workflow across user sessions.
- Model or document workloads routinely exceed interactive request timeouts.
- Users need workflow history, retry from last completed step, or audit trails for interrupted processing.
- The app is hosted for external users and must survive process restarts mid-job.

## Current Operational Guidance

- Treat a failed or canceled local request as safe to resubmit.
- Keep model timeout and retry settings in configuration.
- Use API logs and `traceId` for failure diagnosis.
- Prefer improving model latency, image preprocessing, and provider error handling before adding durable orchestration.
