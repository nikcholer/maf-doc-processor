# Contributing

This repository uses GitHub Issues and the [MAF Document Processor project](https://github.com/users/nikcholer/projects/1) to plan and deliver changes. The Project is the live source of truth for what is proposed, ready, active, blocked, or complete.

This document is the shared contribution contract for human developers and development agents. Automated contributors must also follow [AGENTS.md](AGENTS.md).

## Start With the Work Item

All proposed changes start with a GitHub issue and a Project item. Use the [delivery work-item or bug form](https://github.com/nikcholer/maf-doc-processor/issues/new/choose) and describe the intended outcome, acceptance criteria, dependencies, and validation approach.

- Backlog documents describe strategic scope and sequencing; they are not the live task list.
- Select the next task from `Status = Ready`, considering priority and dependencies.
- Do not start gated E5 or E6 work until its issue records that the documented decision gate has been satisfied.
- Move an item to **In Progress** and assign it when taking ownership.
- If progress stops, move it to **Blocked** and record the specific dependency, decision, or external change required.
- Update the issue before materially expanding its scope. Create a separate issue when the additional work can be delivered independently.

New open issues are added to the Project automatically. The full field definitions and status lifecycle are in the [delivery workflow](docs/delivery-workflow.md).

## Branches, Commits, and Pull Requests

Normally use one issue, one focused branch, and one pull request.

- Name branches `<issue-number>-<short-description>`, for example `42-top-level-routing`. Tool-required prefixes are permitted, for example `codex/42-top-level-routing`.
- Keep commits cohesive and describe the change in imperative language.
- Avoid mixing opportunistic cleanup with the issue's accepted scope.
- Open a pull request using the repository template and include `Closes #<issue-number>`.
- Link the pull request to its issue. Project automation moves linked work to **In Review**.
- Merge only when acceptance criteria and required validation are satisfied. Closing the issue moves the item to **Done**; reopening it returns the item to **Ready**.

If a Jira-managed organization adopts the repository, include the Jira key in the branch, commits, and pull request as described in the [delivery workflow](docs/delivery-workflow.md). GitHub remains authoritative unless the team explicitly changes that policy.

### Development-Assistance Provenance

The commit author remains accountable for the change. When an LLM or development agent materially generates, revises, or advises the committed work, record that assistance with an `Assisted-by` trailer:

```text
Add document routing tests

Closes #42

Assisted-by: Google Antigravity (Gemini 3.1 Pro)
```

This convention applies equally to autonomous agent work and a human working interactively with an LLM.

- Use the product name and include the model when it is known reliably, for example `Assisted-by: OpenAI Codex`.
- Add one `Assisted-by` trailer for each assistant that contributed materially.
- Do not use `Co-authored-by` for an assistant unless it has a genuine GitHub identity and associated email address; never invent an identity or email address.
- Preserve the trailers in the final commit message when commits are amended, rebased, or squash-merged.
- Incidental completion, spelling, or lookup assistance does not need a trailer unless the contributor considers it material.
- Verify the recorded provenance before handover:

  ```powershell
  git log -1 --format="%(trailers:key=Assisted-by,valueonly)"
  ```

## Implementation Principles

Preserve the current contracts unless the work item explicitly changes them:

- Keep domain and workflow behaviour in `src/MafDocumentProcessor`; keep HTTP and UI concerns in `src/MafDocumentProcessor.Api`.
- Treat model output as untrusted input. Parsing, deterministic validation, business policy, and response construction remain explicit C# stages.
- Use MAF typed executors and hand-off records for workflow stages. Make topology and route changes inspectable and testable.
- Keep model-backed work behind project-owned service interfaces such as `IModelChatClient` and the document extractor interfaces.
- Use the configured model roles. Add a role only when the task requires materially different model, timeout, retry, pricing, protocol, or preprocessing behaviour.
- Keep repair and provider retries bounded. Do not add unbounded cycles or hidden model calls.
- Propagate cancellation and preserve correlation, latency, token, and estimated-cost reporting for every model call.
- Treat structural validity, policy decisions, and human-review recommendations as separate concerns.
- Do not introduce durable pause/resume or agent collaboration merely because the framework supports it; E5 and E6 require their documented evidence gates.
- Preserve the API error contract and document-result semantics unless the issue explicitly approves a contract change.

For an existing vertical slice, begin with the [slice guide](docs/slice-guide.md). For a new document type, use [adding a document type](docs/adding-document-types.md).

## Code Conventions

- Target the SDK pinned in `global.json` and retain nullable reference types.
- Follow the existing file-scoped namespace, naming, formatting, and asynchronous method patterns.
- Prefer small immutable records for domain values and workflow hand-offs where the existing design does so.
- Pass `CancellationToken` through asynchronous I/O and model boundaries.
- Keep provider-specific behaviour inside the provider adapter rather than leaking it into domain or workflow code.
- Add or update tests with the production change. Offline tests use fakes at model boundaries and must not require credentials or network access.
- Do not commit API keys, model responses containing sensitive source data, local build output, or ad hoc customer documents. Personal capture photos for local testing belong in `tests/MafDocumentProcessor.Tests/assets/local/`, which is gitignored.

## Validation

Run validation in proportion to the change and record the commands and outcome in the pull request.

For normal code or configuration changes:

```powershell
dotnet restore .\MafDocumentProcessor.sln
dotnet test .\MafDocumentProcessor.sln
```

If a running API has locked the Windows apphost:

```powershell
dotnet test .\MafDocumentProcessor.sln --no-restore -p:UseAppHost=false -p:OutDir=.build\test\
```

Additional expectations:

- Dependency changes require a clean build, the full offline suite, and `dotnet list .\MafDocumentProcessor.sln package --vulnerable --include-transitive`.
- Workflow changes require coverage for every added route, failure path, cancellation path, and bounded repair behaviour.
- API changes require integration coverage and corresponding contract documentation.
- Model prompt, parser, or preprocessing changes require representative fixtures; run live-provider checks only when explicitly required and credentials are available.
- Documentation-only changes require link/content review and `git diff --check`; run code tests when the documentation reflects or accompanies a code change.

The opt-in live Sujiko regression test is documented in the [README](README.md). Never make provider-backed tests part of the default offline suite.

## Documentation and Decisions

Update documentation in the same pull request when behaviour, configuration, architecture, contracts, or operational trade-offs change.

- `README.md` describes the current application and how to run it.
- `docs/technical-process-flow.md` describes the implemented processing design.
- `docs/maf-workflow-evolution-backlog.md` records strategic phases and gates.
- `docs/delivery-workflow.md` defines the tracking lifecycle.
- Contract, policy, and architecture decision documents explain stable rules and rationale.

Record significant architectural choices as a `Work item type = Decision` issue and a concise repository document. Include context, options, rationale, consequences, and follow-up work.

## Before Requesting Review

Confirm that:

- The issue still describes the delivered scope and acceptance criteria.
- Relevant tests and checks pass, with evidence in the pull request.
- New model calls, routes, failure behaviour, and public contracts are covered.
- Documentation and configuration examples match the implementation.
- No secrets, sensitive documents, generated output, or unrelated changes are included.
- Deliberate exclusions and follow-up work have their own issues rather than hidden TODOs.
