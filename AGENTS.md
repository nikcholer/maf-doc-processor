# Agent Instructions

These instructions apply to the entire repository. They supplement [CONTRIBUTING.md](CONTRIBUTING.md), which is the canonical contribution contract for humans and agents.

## Required Reading

Before changing the repository:

1. Read `README.md` and `CONTRIBUTING.md`.
2. Read `docs/delivery-workflow.md` and the active GitHub issue.
3. Inspect open issues and pull requests for status. If a GitHub Project is available to you, use it for board fields; do not treat a private board as public documentation.
4. Read the documents relevant to the change. Start with `docs/slice-guide.md` for an existing document path or `docs/adding-document-types.md` for a new document type.

User instructions and the accepted issue define the requested outcome. Repository backlog documents supply strategy and context, but are not a substitute for a public readiness statement or explicit maintainer assignment.

## Taking Work

- When choosing work, select an issue whose public text, labels, or maintainer assignment explicitly marks it ready and unblocked; do not infer priority from Markdown ordering.
- When authenticated and authorized, assign the issue before implementation. If a GitHub Project is available, also move it to **In Progress**.
- Do not begin E5 checkpointing or pause/resume work. It is out of scope for the current image-to-structured-data effort; see `docs/durability-decision.md` and `docs/forward-planning-workflow-system.md`.
- Do not begin E6 agent-collaboration work before November 2026, and then only if issue #7 records that its quality-evaluation gate passed. The revisit is to catch a model step change in quality, speed, or price, not to fill a current product gap.
- If GitHub Project access is unavailable, do not guess the next task or silently alter tracking. Continue only with work the user explicitly assigned and report the tracking limitation.
- Do not broaden scope without updating the issue. Capture independently deliverable discoveries as separate proposed work.

## Working Safely

- Inspect the worktree before editing. Preserve user changes and unrelated work; never discard or rewrite them to simplify the task.
- Use a branch based on the issue number. Respect any branch prefix required by the execution environment.
- Make the smallest coherent change that satisfies the issue and its acceptance criteria.
- Do not commit secrets, source documents containing sensitive data, build output, or provider responses containing confidential content.
- Do not enable live model tests, spend provider credits, publish, deploy, change access, or mutate external systems unless the task authorizes that action.

## Repository Boundaries

- `src/MafDocumentProcessor/Domain`: domain records and processing results.
- `src/MafDocumentProcessor/Services`: classifier, extractors, parsers, preprocessing, configuration-facing model services, and provider boundaries.
- `src/MafDocumentProcessor/Workflow`: project-owned MAF executors, typed hand-offs, graph construction, policy, repair, and result composition.
- `src/MafDocumentProcessor.Api`: dependency registration, HTTP validation/contracts/endpoints, response mapping, configuration loading, and static UI.
- `tests/MafDocumentProcessor.Tests`: offline unit, workflow, parser, image, and API integration tests; provider-backed asset tests remain opt-in.
- `docs`: implemented architecture, contracts, policy, decisions, strategic phases, and delivery guidance.

`WorkflowBuilder`, `AddEdge`, executor base classes, workflow contexts, execution, and workflow events come from `Microsoft.Agents.AI.Workflows`. Types under `MafDocumentProcessor.*`, including `IModelChatClient`, `DocumentProcessingWorkflow`, and the document executors, are project-owned.

## Implementation Rules

- Keep deterministic parsing, validation, policy, routing decisions, and result construction deterministic unless the accepted design explicitly requires model behaviour.
- Treat all model output as untrusted. Validate it before it affects policy or public results.
- Keep model calls behind project interfaces and use configured model roles.
- Preserve typed workflow hand-offs, cancellation propagation, error semantics, correlation, and model usage accounting.
- Keep retries and repair attempts bounded. Added model calls must have tests and observable latency, token, and cost reporting.
- Keep HTTP concerns in the API project and provider-specific protocol behaviour in the provider adapter.
- Preserve existing API and document-result contracts unless the issue explicitly approves a change; update contract documentation with the implementation.
- Follow existing C# conventions: .NET 10, nullable enabled, file-scoped namespaces, asynchronous suffixes, and focused immutable records where appropriate.

## Validation

For normal code changes, run from the repository root:

```powershell
dotnet restore .\MafDocumentProcessor.sln
dotnet test .\MafDocumentProcessor.sln
```

Use the alternate output command in `README.md` if a running API locks the apphost. Add targeted tests for every changed route, failure path, parser contract, repair path, cancellation path, or API response.

Do not run the opt-in live Sujiko test unless the work item requires live verification and `TOGETHER_API_KEY` is deliberately available. Documentation-only work requires at least link/content review and `git diff --check`.

## Agent Provenance

- Follow the shared development-assistance convention in `CONTRIBUTING.md`.
- Every commit containing substantive work produced by this agent must include an `Assisted-by` trailer naming the agent product and, when known reliably, its model.
- Keep the human repository owner as the Git author. Do not invent an agent email address or use `Co-authored-by` without a genuine GitHub identity.
- Preserve the trailer through amendments, rebases, and squash merges, and verify it with `git log` before handover.

## Finishing Work

- Update relevant documentation in the same change.
- Ensure the worktree contains no unrelated edits and validation evidence is available.
- Use a focused commit containing the required provenance trailer and a pull request containing `Closes #<issue-number>`.
- When authorized, move or confirm the Project item in **In Review**. Project automation handles linked pull requests, issue closure, and reopening.
- Hand over with the issue/PR link, a concise outcome summary, validation performed, and any explicit exclusions or follow-up issues.

Do not mark work complete merely because code was written. Completion requires the accepted outcome, validation, documentation, and delivery state to agree.
