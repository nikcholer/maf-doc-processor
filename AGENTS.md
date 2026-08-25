# Agent Instructions

These instructions apply to the entire repository. They supplement [CONTRIBUTING.md](CONTRIBUTING.md), which is the canonical contribution contract for humans and agents.

## Required Reading

Before changing the repository:

1. Read `README.md` and `CONTRIBUTING.md`.
2. Read `docs/delivery-workflow.md` and the active GitHub issue.
3. Inspect the [GitHub Project](https://github.com/users/nikcholer/projects/1) for status, priority, dependencies, and phase gates.
4. Read the documents relevant to the change. Start with `docs/slice-guide.md` for an existing document path or `docs/adding-document-types.md` for a new document type.

User instructions and the accepted issue define the requested outcome. The GitHub Project defines live delivery state. Repository backlog documents supply strategy and context, but are not a substitute for a Ready issue.

## Taking Work

- When choosing work, select an unblocked item with `Status = Ready`; do not infer priority from Markdown ordering.
- When authenticated and authorized, assign the issue and move it to **In Progress** before implementation.
- Do not begin E5 checkpointing/human-input work or E6 agent-collaboration work until the relevant issue records that its decision gate passed.
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
- Follow existing C# conventions: .NET 8, nullable enabled, file-scoped namespaces, asynchronous suffixes, and focused immutable records where appropriate.

## Validation

For normal code changes, run from the repository root:

```powershell
dotnet restore .\MafDocumentProcessor.sln
dotnet test .\MafDocumentProcessor.sln
```

Use the alternate output command in `README.md` if a running API locks the apphost. Add targeted tests for every changed route, failure path, parser contract, repair path, cancellation path, or API response.

Do not run the opt-in live Sujiko test unless the work item requires live verification and `TOGETHER_API_KEY` is deliberately available. Documentation-only work requires at least link/content review and `git diff --check`.

## Finishing Work

- Update relevant documentation in the same change.
- Ensure the worktree contains no unrelated edits and validation evidence is available.
- Use a focused commit and a pull request containing `Closes #<issue-number>`.
- When authorized, move or confirm the Project item in **In Review**. Project automation handles linked pull requests, issue closure, and reopening.
- Hand over with the issue/PR link, a concise outcome summary, validation performed, and any explicit exclusions or follow-up issues.

Do not mark work complete merely because code was written. Completion requires the accepted outcome, validation, documentation, and delivery state to agree.
