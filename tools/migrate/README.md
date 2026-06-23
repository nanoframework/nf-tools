# NanoMigrate — `nano-migrate`

Convert legacy nanoFramework `.nfproj` projects to the SDK-style MSBuild project system —
one project, a whole directory/solution, or an entire cloned fleet of repositories.

> **Scope: project-system migration only.** This tool moves a repo from the legacy flavored
> `.nfproj` format onto an SDK-style project that composes over `nanoFramework.NET.Sdk`, folds
> `packages.config` into `PackageReference`, and folds `.nuspec` metadata into MSBuild `Pack`
> properties. It does **not** touch OTA updates, modular firmware packaging,
> `runtimes/{rid}/native` layouts, or ABI manifests.

The conversion is **idempotent and reentrant**: already-SDK-style projects are skipped and
re-running over a tree is a safe no-op, so a partial or repeated migration is never destructive.

## Layout

```
tools/migrate/
  src/
    NanoMigrate.Core/          # conversion engine — console-free, unit-testable, NuGet-ready library
    NanoMigrate.Cli.Commands/  # shared Spectre commands (migrate, clean, rollback) used by both front ends
    NanoMigrate.Cli/           # the standalone `nano-migrate` CLI (adds clone + fleet)
  tests/
    NanoMigrate.Tests/         # unit tests over NanoMigrate.Core
```

The engine (`NanoMigrate.Core`) has no `Console`/`AnsiConsole` dependency, so it is testable and
packable on its own. The same `migrate`/`clean`/`rollback` commands are shared with the
[`dotnet nano`](../nano/README.md) umbrella tool, so `nano-migrate migrate …` and
`dotnet nano migrate …` behave identically.

### Packages

| Package id | Command | What it is |
|---|---|---|
| `nanoFramework.Migrate.Core` | — | the console-free conversion engine (library) |
| `nanoFramework.Migrate` | `nano-migrate` | the standalone CLI (this tool) |
| `nanoFramework.Tool` | `dotnet nano` | the umbrella that also hosts `migrate` (see [../nano](../nano/README.md)) |

## Install / run

As a .NET global (or local) tool:

```bash
dotnet tool install -g nanoFramework.Migrate     # then: nano-migrate <command> …
# or via the umbrella:
dotnet tool install -g nanoFramework.Tool        # then: dotnet nano migrate …
```

From source in this repo (no separate build step needed):

```bash
dotnet run --project tools/migrate/src/NanoMigrate.Cli -- <command> [options]
```

For repeated fleet runs, build once and call the dll directly (faster):

```bash
dotnet build -c Release tools/migrate/src/NanoMigrate.Cli
dotnet tools/migrate/src/NanoMigrate.Cli/bin/Release/net8.0/nano-migrate.dll <command> [options]
```

Requires the **.NET 8 SDK**. The tool is BCL-only apart from Spectre.Console and the official
`Microsoft.VisualStudio.SolutionPersistence` solution reader/writer.

## Discovering commands and options

Everything below is also discoverable from the tool itself — prefer `--help` as the source of
truth, since it always matches the installed version:

```bash
nano-migrate --help               # list every command
nano-migrate migrate --help       # options for one command
dotnet nano migrate --help        # same surface via the umbrella
```

## Commands

| Command | Summary |
|---|---|
| `migrate <path>` | Convert a `.nfproj`, a solution, or every `.nfproj` under a directory. |
| `clean [path]` | Remove migration leftovers: `*.nfproj.bak` files and `.nanomigrate/` rollback folders. |
| `rollback [path]` | Revert the last recorded migration under a path (restore originals, delete created projects). |
| `clone [out-dir]` | Clone all matching repos from a GitHub org (fleet prep). |
| `fleet <repos-dir>` | Migrate every `.nfproj` across cloned repos; write a report; optionally branch + commit. |

`clone` and `fleet` are only in the standalone `nano-migrate` CLI; `migrate`, `clean`, and
`rollback` are shared with `dotnet nano`.

### `migrate <path>`

`<path>` is a `.nfproj` file, a solution (`.sln`/`.slnx`), or a directory.

- **Solution** → only its referenced `.nfproj` are converted and that solution is retargeted.
- **Directory with solutions** → you choose which solution(s) to migrate (interactive multi-select;
  non-interactive / `--yes` selects all affected).
- **Directory with no solution** → every `.nfproj` under it is converted (loose mode); any solutions
  found higher in the tree are retargeted automatically.

| Option | Default | Meaning |
|---|---|---|
| `--solution <path>` | — | Migrate only this solution; overrides directory discovery. |
| `--glob <pattern>` | — | Only convert `.nfproj` whose path (relative to `<path>`) matches; `*`, `**`, `?` supported (e.g. `"Beginner/**"`). Solutions referencing a matched project are updated. |
| `--tfm <moniker>` | `netnano1.0` | Target framework moniker. |
| `--ext <ext>` | `.csproj` | Output extension: `.csproj` (retire `.nfproj`) or `.nfproj` (rewrite in place — lower-risk during a phased rollout). |
| `--no-backup` | off | Don't write a `.nfproj.bak`. Fully suppresses loose backups; the rollback journal stays self-contained in `.nanomigrate/`. |
| `--dry-run` (`--no-write`) | off | Analyse and preview only; write nothing. |
| `--verify` / `--no-verify` | on for real runs, off for `--dry-run` | After a real migration, build the affected solution(s)/project(s); a failed build offers a rollback. |
| `--report <path>` | — | Write a migration report. Format by extension: `.md`/`.markdown` → Markdown, `.html`/`.htm` → HTML (else Markdown). Works for `--dry-run` too. |
| `--sdk <version>` | (ignored) | Accepted for back-compat only; the SDK reference is versionless (pinned via `global.json` `msbuild-sdks`). |
| `-y\|--yes` | off | Skip interactive prompts and proceed with the default action. (Non-interactive runs never prompt regardless.) |

**Exit codes:** `0` clean · `2` completed but some projects need manual review · `1` an error
occurred or a verification build failed (and changes were kept or rolled back per the prompt).

### `clean [path]`

Removes migration leftovers under a path: every `*.nfproj.bak` and every `.nanomigrate/` rollback
folder. Previews what will go and confirms before deleting (`-y|--yes` to skip the prompt;
non-interactive proceeds). `[path]` defaults to the current directory. This is the tidy-up step
after a migration you're happy with, so you don't commit a pile of `.bak` files.

### `rollback [path]`

Reverts the last recorded migration under a path by reading its `.nanomigrate/` rollback journal:
restores backed-up originals and deletes files the migration created. Idempotent and safe — with no
journal it reports "nothing to roll back" and exits `0`. `[path]` defaults to the current directory.

### `clone [out-dir]`

Clones all matching repositories from a GitHub org (the first half of a fleet migration).
`[out-dir]` defaults to `./nano-repos`.

| Option | Default | Meaning |
|---|---|---|
| `--org <name>` | `nanoframework` | GitHub org to enumerate. |
| `--filter <prefix>` | `lib-` | Repo-name prefix to match. |
| `--token <pat>` | `$GITHUB_TOKEN` | GitHub token to lift the unauthenticated API rate limit. |
| `--include-archived` | off | Include archived repositories (skipped by default). |

### `fleet <repos-dir>`

Migrates every `.nfproj` across a directory of cloned repos and writes a report. With `--branch`
(and optionally `--commit`) each repo ends up on a ready-to-PR branch.

| Option | Default | Meaning |
|---|---|---|
| `--glob <pattern>` | — | Only convert matching `.nfproj` within each repo (`*`, `**`, `?`). |
| `--tfm <moniker>` | `netnano1.0` | Target framework moniker. |
| `--ext <ext>` | `.csproj` | Output extension (`.csproj` or `.nfproj`). |
| `--no-backup` | off | Don't write `.nfproj.bak` (implied by `--commit` — git history already preserves the original). |
| `--dry-run` (`--no-write`) | off | Analyse and preview only. |
| `--report <path>` | `migration-report.md` | Report path (Markdown). |
| `--branch <name>` | — | Create/reset this git branch in each repo. Must not start with `develop`. |
| `--commit` | off | Commit the changes (requires `--branch`); writes a contribution-compliant message and signs off. |
| `--message <msg>` | — | Commit summary line (kept ≤ 50 chars). |
| `--issue <n>` | — | Add a `Fix #<n>` trailer to the commit. |
| `--no-sign-off` | off | Don't add a `Signed-off-by` line. |

`fleet` stops at the commit — it never pushes or opens PRs. Migrate **leaf-first** (dependencies
before dependents) and open PRs from the org template (see the contribution guide). **Exit codes:**
`0` all clean · `2` one or more repos errored or need review.

## What the conversion does (per project)

- Emits `<Project Sdk="nanoFramework.NET.Sdk">` with `<TargetFramework>netnano1.0</TargetFramework>`.
- Maps `packages.config` / `<Reference>` HintPath versions to `<PackageReference>` — the package id
  comes from the `packages\<Id>.<Version>\` folder, so nano package ids resolve correctly rather
  than the bare assembly name. Aliases legacy `mscorlib`/`System` to `nanoFramework.CoreLibrary`.
- Folds `.nuspec` metadata (`id`, `description`, `authors`, `tags`, `projectUrl`) into MSBuild
  `Pack` properties.
- Deletes `packages.config` and a hand-written `Properties/AssemblyInfo.cs` (the SDK regenerates
  it), while preserving non-default items (linked files, files in subfolders, `EmbeddedResource`,
  `Content`).
- Rewrites the `.sln`/`.slnx` entry: project-type GUID → the SDK C#/CPS GUID, path `.nfproj` →
  `.csproj`. Solutions are read/written through the official
  `Microsoft.VisualStudio.SolutionPersistence` library.
- **Fails loud:** anything it cannot confidently resolve is written to a manual-review list instead
  of being silently guessed.

The exact rule set, including edge cases, lives in the skill at
[`skills/nanoframework-sdk-migration/references/migration-rules.md`](../../skills/nanoframework-sdk-migration/references/migration-rules.md).

## Backups, the rollback journal, and verification

- Each real conversion writes a `.nfproj.bak` next to the project (unless `--no-backup`).
- Independently, a real run records a **self-contained rollback journal** under `.nanomigrate/`
  (`rollback-<id>/` + `manifest.json`) that backs up every file the run modifies or deletes and
  records every file it creates — this is what `rollback` and the post-verify rollback prompt use.
- With `--verify` (default for real runs) the affected solution(s)/project(s) are built after the
  conversion; on failure you're offered an immediate rollback (interactive) or advised to run
  `rollback` later (non-interactive).
- When you're done, `clean` removes both the loose `*.nfproj.bak` files and the `.nanomigrate/`
  folders.

## Tests

```bash
dotnet test tools/migrate/tests/NanoMigrate.Tests
```

## See also

- [`../nano/README.md`](../nano/README.md) — the `dotnet nano` umbrella tool that also hosts these commands.
- [`skills/nanoframework-sdk-migration`](../../skills/nanoframework-sdk-migration) — the installable
  skill that drives this tool, with the full rule set and contribution/PR conventions.
