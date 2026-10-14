---
name: nanoframework-sdk-migration
description: >-
  Migrate .NET nanoFramework projects from the legacy custom .nfproj project system to the
  SDK-style MSBuild project system (<Project Sdk="nanoFramework.NET.Sdk">, netnano1.0,
  PackageReference). Use whenever converting a nanoFramework library or app to SDK-style,
  folding packages.config into PackageReference or .nuspec into MSBuild Pack properties, or
  bulk-migrating many repos at once (the nanoframework/lib-* fleet). Trigger for any mention of
  ".nfproj", "SDK-style nanoFramework", "convert nfproj to csproj", "migrate the nano
  libraries", or a fleet/bulk migration — even if the word "skill" is never used.
  SCOPE: project-system migration only; this does NOT cover OTA updates or modular firmware
  packaging.
---

# nanoFramework SDK-style project migration

Convert a nanoFramework repo from the legacy-flavored `.nfproj` project system onto SDK-style
projects that compose over `nanoFramework.NET.Sdk`. The mechanical conversion is done by the
**NanoMigrate** tool (in this repo at `tools/migrate`, surfaced as `dotnet nano migrate`). The
tool is **idempotent + reentrant**: it skips already-SDK-style projects and re-running over a tree
is a safe no-op, so a partial or repeated migration is never destructive.

The full, as-built command/option reference is [`tools/migrate/README.md`](../../tools/migrate/README.md)
(and [`tools/nano/README.md`](../../tools/nano/README.md) for the umbrella). It is the source of
truth; this skill is the workflow guide. **Always confirm the live surface with `--help`** — it
matches the installed version exactly:

```bash
dotnet nano --help               # every command
dotnet nano migrate --help       # options for one command
```

## Scope — read this first

Project-system migration **only**. Do not add, and do not ask the tool to touch: OTA update
artifacts, modular/firmware packaging, `runtimes/{rid}/native` layouts, or ABI manifests. The
exact per-project transformation rules are in
[references/migration-rules.md](references/migration-rules.md); contribution + PR conventions are
in [references/contributing-compliance.md](references/contributing-compliance.md).

## Commands at a glance

Run any of these as `dotnet nano <command>` (umbrella) or `nano-migrate <command>` (standalone) —
or from source with `dotnet run --project tools/migrate/src/NanoMigrate.Cli -- <command>`.

| Command | What it does |
|---|---|
| `migrate <path>` | Convert a `.nfproj`, a solution (`.sln`/`.slnx`), or every `.nfproj` under a directory. |
| `clean [path]` | Remove migration leftovers: `*.nfproj.bak` files and `.nanomigrate/` rollback folders. |
| `rollback [path]` | Revert the last recorded migration (restore originals, delete created projects). |
| `clone [out-dir]` | Clone all matching repos from a GitHub org (fleet prep). *Standalone CLI only.* |
| `fleet <repos-dir>` | Migrate every `.nfproj` across cloned repos; write a report; optionally branch + commit. *Standalone CLI only.* |

Key `migrate` options (see `migrate --help` / the README for the rest): `--dry-run`, `--glob <p>`,
`--solution <p>`, `--ext .csproj|.nfproj`, `--no-backup`, `--report <file.md|.html>`,
`--verify`/`--no-verify`, `-y|--yes`.

## Always test a directory first, then migrate for real

1. **Dry-run** to preview every change (writes nothing):
   ```
   dotnet nano migrate <path> --dry-run
   # or, from this repo: dotnet run --project tools/migrate/src/NanoMigrate.Cli -- migrate <path> --dry-run
   ```
   Review the preview table: the target `.csproj`, the resolved `PackageReference`s, the files
   that would be deleted, the `.sln` edits, and anything in the yellow **manual review** panel.
2. **Scope** with a glob if you only want part of the tree (`*`, `**`, `?`; relative to `<path>`):
   ```
   dotnet nano migrate <path> --glob "Beginner/**" --dry-run
   ```
3. **Run for real** once the preview is right:
   ```
   dotnet nano migrate <path>          # interactive: confirms once. Add --yes for CI/unattended.
   ```
4. **Re-run is safe** — reentrant. A partially-converted tree converts only the remaining
   `.nfproj` and leaves existing `.csproj` untouched.

A real run **verifies by default**: after converting, it builds the affected solution(s)/project(s)
and, on a failed build, offers to roll the whole run back (interactive) or tells you to run
`rollback` later (non-interactive). Use `--no-verify` to skip it. Add `--report <file>` to emit a
record of what changed — `.md`/`.markdown` → Markdown, `.html`/`.htm` → HTML — which also works
under `--dry-run` (it reports what *would* change).

## Solutions (.sln / .slnx)

Migration is solution-aware and keeps solutions in sync:

- **Pass a solution** (`migrate <path>.sln` or `.slnx`) to convert only the projects it references
  and retarget the solution itself (classic `.sln`: project-type GUID + `.nfproj`→`.csproj`;
  `.slnx`: the `Path`).
- **Pass a directory**: if it contains solutions you are asked to pick which one(s) to migrate
  (all / none / several); if it contains none, every `.nfproj` under it is converted (loose mode).
- **With `--glob`**: the tool finds the matching projects, discovers the solutions that reference
  them, and (after you confirm / select) updates only those solutions.
- `--solution <path>` forces a single target; `--yes` (or non-interactive) selects all affected.

## What the tool does per project

(Full detail in [references/migration-rules.md](references/migration-rules.md).)

- Emits `<Project Sdk="nanoFramework.NET.Sdk">` with `netnano1.0`.
- Maps `packages.config` / HintPath versions to `PackageReference` — the package id comes from the
  `packages\<Id>.<Version>\` folder, so nano package ids resolve correctly (not the bare assembly
  name).
- Folds `.nuspec` metadata into MSBuild `Pack` properties.
- Deletes `packages.config` and a hand-written `Properties/AssemblyInfo.cs` (the SDK generates it).
- Rewrites the `.sln` entry: project-type GUID → the SDK C#/CPS GUID, path `.nfproj` → `.csproj`.
- Writes a `.nfproj.bak` backup (unless `--no-backup`).

## Manual review

Entries in the yellow "manual review" panel did not resolve automatically — typically a
`<Reference>` with no HintPath and no matching `packages.config` entry. Add the right
`PackageReference` by hand, then re-run (a no-op for everything already converted).

## Back up, roll back, clean up

- Each real conversion writes a `.nfproj.bak` next to the project (suppress with `--no-backup`).
- Independently, a real run records a **self-contained rollback journal** under `.nanomigrate/`.
  `rollback [path]` reverts the last recorded migration (restore originals, delete created files);
  it's idempotent — no journal means "nothing to roll back", exit 0.
- When you're satisfied with a migration, `clean [path]` removes the leftovers — every
  `*.nfproj.bak` and every `.nanomigrate/` folder — so you don't commit a pile of backups. Both
  preview before deleting; add `-y|--yes` to skip the prompt.

## Fleet migration (many repos)

Two steps — `clone` to fetch the repos, `fleet` to migrate them (`clone`/`fleet` are in the
standalone `nano-migrate` CLI). From source:

```
# 1) clone a GitHub org's matching repos (default org nanoframework, filter lib-)
dotnet run --project tools/migrate/src/NanoMigrate.Cli -- clone ./nano-repos --token $GITHUB_TOKEN

# 2) dry-run the whole fleet and read the report before changing anything
dotnet run --project tools/migrate/src/NanoMigrate.Cli -- fleet ./nano-repos --dry-run --report fleet-report.md

# 3) apply on a branch and commit, so each repo ends up ready to PR
dotnet run --project tools/migrate/src/NanoMigrate.Cli -- fleet ./nano-repos \
  --branch sdk-migration --commit --issue <home-issue> --report fleet-report.md
```

`fleet` walks each repo, converts every matching `.nfproj`, writes the report, and (with
`--branch`/`--commit`) commits a contribution-compliant, signed-off message; `--commit` implies
`--no-backup`. It stops at the commit — it never pushes or opens PRs. Open PRs from the
nanoFramework org template — see
[references/contributing-compliance.md](references/contributing-compliance.md). Convert leaf-first
(dependencies before dependents); branch names must not start with `develop`; never target `main`.
