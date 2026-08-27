# Delivery Workflow

## System of Record

GitHub Issues and pull requests are the public systems for planning and delivery. Repository backlog documents describe scope and sequencing; issues hold actionable work, decisions, acceptance criteria, and implementation links. Maintainers may also use a GitHub Project for board fields, but readiness, dependencies, and decision gates must be understandable from the public issue. [CONTRIBUTING.md](../CONTRIBUTING.md) is the shared delivery contract for human developers and development agents.

The project uses:

- Parent issues for phases E0-E7.
- Child issues for work that is ready to refine or deliver.
- Pull requests for reviewable code and documentation changes.
- Pull-request validation notes for repeatable build and test evidence; CI results provide additional evidence when a workflow is configured.
- The [MAF workflow evolution backlog](maf-workflow-evolution-backlog.md) for phase objectives, decision gates, and exit criteria.

## Optional Maintainer Project Fields

These fields may be mirrored on a maintainer Project for board views. They do not replace the issue: contributors who cannot see the Project must still be able to tell whether work is ready, gated, or blocked.

| Field | Purpose |
| --- | --- |
| Status | Backlog, Ready, In Progress, In Review, Blocked, or Done |
| Phase | E0 through E7 |
| Work item type | Epic, Feature, Task, Spike, Decision, or Bug |
| Priority | P0 through P3 |
| Estimate | Relative size: 1, 2, 3, 5, or 8 |
| MAF capability | The primary framework or application concern |
| Target | Current baseline, next baseline, later, or icebox |

## Status Definitions

- **Backlog:** Captured but not yet sufficiently refined or selected.
- **Ready:** Acceptance criteria and dependencies are clear enough to begin.
- **In Progress:** Active implementation or investigation is underway.
- **In Review:** A pull request, decision, or result is awaiting review.
- **Blocked:** Progress requires a decision, dependency, permission, or external change.
- **Done:** Acceptance criteria are satisfied and required changes are merged.

## Work Item Lifecycle

1. Create or refine an issue using the delivery work-item form.
2. Confirm its phase, type, priority, intended outcome, acceptance criteria, and validation approach.
3. Record **Ready** publicly in the issue only when dependencies and the expected result are clear. Maintainers may mirror that state to a Project.
4. Create a branch named `<issue-number>-<short-description>`.
5. Keep commits focused and reference the issue where useful.
6. Open a pull request containing `Closes #<issue-number>`.
7. Treat the open pull request as **In Review** while required checks and review are outstanding. Maintainers may mirror that state to a Project.
8. Merge after validation; closing the issue is the public **Done** signal.

## Phase and Gate Management

Phase parent issues summarize the objective and exit criteria from the active backlog. Detailed child issues are created when work is near enough to be estimated and delivered.

E5 checkpointing and in-app pause/resume are **out of scope** for the current image-to-structured-data effort; see the [durability decision](durability-decision.md) and [forward planning](forward-planning-workflow-system.md). Do not move E5 child issues to **Ready**. E6 agent collaboration is deferred until November 2026 and then remains gated until its quality-evaluation gate is satisfied; do not move it to **Ready** to chase a current product gap.

Architecture decisions should use `Work item type = Decision` and record:

- Context and constraints.
- Options considered.
- Decision and rationale.
- Consequences and follow-up work.

## Jira Compatibility

GitHub remains the source of truth for this repository. If delivery is incorporated into a Jira-based organization, the supported GitHub for Atlassian integration can associate Jira work-item keys with branches, commits, pull requests, builds, and deployments.

In a Jira-managed environment, use the Jira key consistently:

```text
MAF-42-top-level-routing
MAF-42 Add conditional document routing
MAF-42: Route supported categories through sub-workflows
```

The integration provides development visibility in Jira without requiring the application architecture or GitHub review process to change. Avoid maintaining duplicate authoritative work items in both systems.
