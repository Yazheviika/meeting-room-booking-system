---
name: pr-workflow
description: This repo's git workflow for feature work — branch naming, commit style, gates, and how PRs get merged. Use whenever creating a branch, committing, or opening/merging a PR in this repository.
---

- **Branch naming**: `feat/<short-kebab-description>` for features, `fix/`, `docs/`, `refactor/`, `chore/`, `test/`, `ci/` for the rest — mirrors the commit-type prefix. Cut from an up-to-date `main`.
- **Commits**: small and atomic — one logical change each, never a mixed "and also" commit. [Conventional Commits](https://www.conventionalcommits.org/) style (`feat:`, `fix:`, `docs:`, `refactor:`, `test:`, `chore:`). The body explains **why**, not what the diff already shows — the reasoning, the alternative rejected, the bug that prompted it.
- **Gates**: build and run the relevant test suite before *every* commit, not just before the PR — `dotnet build && dotnet test` for backend changes, `npm run build && npm test` for frontend changes. A commit that doesn't build is never pushed.
- **PRs**: one coherent feature per PR, opened ready-for-review (never draft) with a summary and a test plan. Plan non-trivial work before writing code.
- **Merging**: **Rebase and merge** only — never squash, never a merge commit. History stays linear and each atomic commit's message survives into `main` intact.
