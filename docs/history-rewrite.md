# Repository History Rewrite

## Status

Before the repository is made public, its Git history was rewritten on 27 August 2026 to remove a superseded binary test fixture. The current tree uses a deliberately synthetic, redistribution-safe fixture instead.

This was repository hygiene, not a security incident. No credentials were exposed and there is no current operational risk.

The rewrite used `git-filter-repo` 2.47.0 against the exact legacy path. All locally writable branches and tags were then force-updated.

## Verification

- All six published branches and both annotated tags were rewritten successfully.
- A fresh scan of the rewritten branches and tags cannot resolve the superseded object or path.
- Rewritten `main` has the same source tree and synthetic fixture blob as the merge that introduced the public-safe asset.
- The full provider-free .NET suite passes with 219 tests, and [rewritten-main release run 33086197762](https://github.com/nikcholer/maf-doc-processor/actions/runs/33086197762) completed successfully.
- The repository had no forks and no open pull requests at the rewrite boundary.

GitHub-managed historical pull-request refs are read-only and were not rewritten. Their retention is accepted.

## Historical Consequences

Every commit identifier at or after the first affected change was replaced. Old commit identifiers must not be used in documentation, automation, bookmarks, or comparison scripts.

The annotated milestone tags remain useful architectural markers, but the path-wide removal means that historical snapshots which referenced the legacy fixture no longer contain it. In particular, an asset regression test in those snapshots may not run from the tag alone. Use current `main` for a fully reproducible checkout and treat the tags as records of the architecture at those milestones.

## Clone Safety

Any clone made before the rewrite contains the superseded object graph. Discard it and clone the repository again rather than merging or pushing old history into the rewritten repository.
