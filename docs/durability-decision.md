# Durability Decision

## Status

Accepted. Still in force after composite capture, expense reports, and the extended-workflow baseline. Phase E5 stays gated until this decision is reopened.

## What This Decision Is About

This repository runs a **local, foreground** document processor. A person uploads one image, or a small set of images, to a local API. One MAF workflow classifies each document, extracts fields, validates them, optionally repairs once, and returns JSON while the browser (or `curl`) is still waiting.

**Durable pause/resume** and **checkpointing** would change that contract. They mean: the workflow is allowed to stop *before it has a result*, write enough state to disk (or another store) that a later process can continue the *same* run, and wait—possibly for hours, a process restart, or a human answer—before finishing.

That is a different product from “process this upload and tell me what you got.” The rest of this note explains the difference with a concrete situation, then records the decision not to add that machinery yet.

## A Relevant User Situation

Alex has just come back from a two-day client visit. On the desk are:

- a filled-in expense report (train fare and lunch, claimed total GBP 48.50);
- the two supporting till receipts;
- an unrelated event ticket that happened to be in the same pile.

Alex opens the local demo, chooses **Capture set**, photographs the desk, and optionally drops the expense-report scan in as a second file. The browser waits. The API detects document regions, classifies each crop, extracts fields, checks that 18.50 + 30.00 equals 48.50, and returns one capture aggregate: two receipts, one expense report, one unsupported ticket.

What Alex can do **today**, still in that same sitting:

- read the structured fields, review reasons, and model usage;
- see that the expense report succeeded but is marked **ownership attestation required**;
- correct a bad detection rectangle and **reprocess** as a new request;
- close the tab and forget the whole thing.

Nothing is stored. Nothing is waiting on the server. If the API process dies during the wait, Alex uploads again. That is safe because the failed run had no side effects beyond log lines and spent model tokens.

The expense-report attestation flag is easy to misread as “the workflow is blocked on Alex.” It is not. Extraction already finished. `IsSuccess` is true. The flag is a reminder that *if* this parse were ever submitted as a claim, Alex—not the model—would have to own that later action. There is no claim-submission step in the demo, and no second request that resumes a paused graph.

## What Durable Pause/Resume Would Look Like

Suppose, instead, the product insisted that Alex **answer something before the original run could finish**. Two different designs get confused here.

### Design A — the same run waits (this decision)

The capture request would not return a completed aggregate. After extraction the workflow would **checkpoint**: persist enough MAF state to resume, assign a durable id, and reply “waiting for input.” Alex might go to a meeting and come back the next day. A new HTTP call would load that checkpoint, apply Alex’s answer, and continue the *same* workflow instance—even if the API had been restarted overnight.

A plausible prompt in that design: overlapping receipts, and the system refuses to return until Alex chooses “keep both” or “drop the duplicate.” The capture result does not exist until that answer arrives. That is **human guidance on extraction**. The in-flight graph cannot produce its output without it.

Checkpointing is also how you would survive a crash *mid-run*: save after classification, restart the process, resume extraction without charging a second classification call. That is restart-survival of an unfinished job, not a saved document.

Implications of Design A:

- There is a second execution model (paused jobs) beside the current request-scoped graph.
- Checkpoints are a trust boundary: they must not hold provider API keys, and they should not keep source images longer than needed.
- Someone must define retention, expiry, cleanup, duplicate answers, invalid answers, and cancellation of a wait that never completes.
- Tests must cover process restart, resume, and expired or conflicting input—not only happy-path extraction.
- The UI needs a way to find and answer pending runs, not only “upload and see a result.”
- A failed or abandoned run is no longer “just retry the upload”; it may have leftover state.

MAF can do request/response human input and Durable Task-style checkpoints. This decision is that **the current demo does not need that capability**, not that the framework lacks it.

### Design B — extraction finishes; a later process waits (not this decision)

Alex’s capture returns exactly as it does today. A future product might **save** the expense-report result, then later ask Alex to attest or submit a claim. That wait belongs to a **new** command on a stored document (“attest result X”), not to the original MAF graph sitting in memory or on disk.

That design needs persistence, identities, access control, and an audit log of reviewer decisions. Those are already listed as deferred storage and review-surface work. They do **not** require pause/resume of the extraction workflow. The records `ReviewerInput` and `ReviewDecisionLogEntry` sketch that later surface; they are unused by the live path.

If we treated Design B as E5, we would freeze an extraction graph that has already done its job, only to wait for a click that belongs to a claim workflow the demo does not have.

## Three Different Human Roles

| Role | Example in Alex’s sitting | Does the extraction workflow need to pause? |
| --- | --- | --- |
| Guidance **on extraction** | “Are these two overlapping boxes two receipts or one?” before any capture result exists | Only if we refuse to return and require an answer on this run. Today we return both with a review warning, or Alex corrects rectangles and starts a **new** request. |
| **Storage** of a finished result | Save the expense report and receipts so they can be opened next week | No. That is persisting output after the graph has completed. |
| A **later process** blocked on a person | Attest or submit the already-parsed expense claim | No. The parse is complete. The wait is ownership of a downstream action. |

E5 is only the first row, and only the variant that cannot finish the current run without the answer. Region correction, re-upload, and “here is the parse, you own any later submission” are not E5.

## What Is At Stake

Choosing **not** to add durability keeps the demo’s contract simple:

- One upload produces one response, or an error; there is no “pending workflow” list.
- Cancellation and failure mean “throw it away and send the files again.”
- Operational surface stays logs, `traceId`, configured timeouts, and bounded retries.
- Tests stay about routing, validation, repair, capture aggregation, and HTTP—not checkpoint restore.

The cost of that choice:

- A crash during a long capture wastes the work already done in that request (Alex retries).
- There is no server-side inbox of “Alex still owes an answer.”
- Attestation cannot be collected as a continuation of the original run; it would have to be a new, later API if we ever want it.

Choosing **to** add durability would be justified only if we had a user problem that retries and new requests cannot solve. The extra machinery is then mandatory, not optional sugar: storage, trust, expiry, and resume tests come with the feature.

## Decision

Do not add durable pause/resume or checkpointing to the current local demo.

The current processing model is a bounded HTTP request:

- Images are uploaded to the local API.
- Classification and extraction use configured per-call timeouts.
- Transient provider failures are retried with bounded backoff.
- Request cancellation propagates to model-call executors.
- Successful runs return in seconds with the current Qwen configuration, including composite captures and expense reports.

Adding MAF Durable Task or a local job store now would add operational and testing complexity without solving a current user problem. Expense-report attestation and future “save this result” behaviour are not that problem.

## Options Considered

| Option | Fit now | Notes |
| --- | --- | --- |
| MAF Durable Task locally | Low | Useful once a run genuinely cannot finish without pause/resume, external workers, durable timers, or an extraction-time human answer. More infrastructure than the local demo needs. |
| Lightweight local job store or checkpoint files | Low | Simpler than Durable Task, but it still creates a second execution model, persistence rules, cleanup, and duplicated retry/resume behaviour. |
| Defer durability until a run cannot finish in one foreground request | Best | Keeps the current product simple. Storage of completed results and later attestation remain separate decisions. |

## Reopen Criteria

Revisit this decision when at least one of these becomes true:

- A single workflow instance **cannot produce its result** without an answer, and we refuse to end the request and let the user start a new one (true extraction-time human input).
- Processing moves out of a single foreground HTTP request into queued or background jobs **before** a result exists.
- Model or document workloads **routinely** exceed interactive request timeouts, so crash-resume of an unfinished run is the user-visible need.
- The app is hosted for external users and must survive process restarts **mid-job**, not merely save completed documents.
- Users need history or retry **from the last incomplete step of an in-flight run** (as opposed to re-uploading, or loading a stored completed result).

These do **not** reopen the decision on their own:

- Saving categorized, extracted documents after the graph has completed.
- Collecting attestation, approval, or claim submission on a stored result.
- A reviewer queue over documents that already have a `DocumentProcessingResult`.
- Request-scoped region correction followed by a new process call.

## Current Operational Guidance

- Treat a failed or canceled local request as safe to resubmit.
- Keep model timeout and retry settings in configuration.
- Use API logs and `traceId` for failure diagnosis.
- Prefer improving model latency, image preprocessing, and provider error handling before adding durable orchestration.
- Keep completed-result persistence and human attestation of stored documents off the E5 path; design them as new commands if they are ever needed.
