# `dotnet nano` — the nanoFramework umbrella CLI

One entry point — `dotnet nano <command>` — for nanoFramework workflows. It hosts built-in managed
commands in-process over engine libraries (e.g. `migrate` over `NanoMigrate.Core`) and wraps
already-built external tools (e.g. `nanoff`) under the same namespace with one uniform
Spectre.Console UX, so the toolchain is **one install and one CLI**.

## Layout

```
tools/nano/
  nanoFramework.Tool/          # the dotnet-nano umbrella (PackAsTool, ToolCommandName=nano)
    Program.cs                 # Spectre CommandApp host (wires the command surface)
    Commands/                  # flash (nanoff) + deploy/monitor/devices placeholders
    ExternalTools/             # IExternalTool + resolver/providers (NanoffTool, …) + nano-tools.json
  nanoFramework.Tool.Tests/    # tests for the umbrella (external-tool resolution, arg mapping)
```

The `migrate`, `clean`, and `rollback` commands are **not** reimplemented here — the umbrella
references the shared `NanoMigrate.Cli.Commands` library (over the console-free `NanoMigrate.Core`
engine), the same one the standalone [`nano-migrate`](../migrate/README.md) CLI uses. There is one
implementation behind both front ends.

### Package

| Package id | Command | What it is |
|---|---|---|
| `nanoFramework.Tool` | `dotnet nano` | this umbrella tool |

## Install / run

```bash
dotnet tool install -g nanoFramework.Tool     # then: dotnet nano <command> …
```

Or install it as a **local** tool (pin the version in `.config/dotnet-tools.json` alongside the
rest of the toolchain so CI is reproducible). From source in this repo:

```bash
dotnet run --project tools/nano/nanoFramework.Tool -- <command> [options]
```

Requires the **.NET 8 SDK**.

## Discovering commands and options

Prefer `--help` as the source of truth — it always matches the installed version:

```bash
dotnet nano --help                # list every command
dotnet nano migrate --help        # options for one command
```

## Commands

| Command | Kind | Backed by |
|---|---|---|
| `migrate <path>` | built-in (in-proc) | `NanoMigrate.Core` — convert legacy `.nfproj` to SDK-style |
| `clean [path]` | built-in (in-proc) | remove `*.nfproj.bak` + `.nanomigrate/` leftovers |
| `rollback [path]` | built-in (in-proc) | revert the last recorded migration |
| `flash` | external | `nanoff` (prebuilt release, version-pinned) |
| `deploy` | built-in | *placeholder — not yet implemented in the CLI* |
| `monitor` | built-in | *placeholder — not yet implemented in the CLI* |
| `devices` | built-in | *placeholder — not yet implemented in the CLI* |

### `migrate` / `clean` / `rollback`

Identical to the standalone tool — see [`../migrate/README.md`](../migrate/README.md) for the full
option reference. `dotnet nano migrate <path> --dry-run`, `dotnet nano clean`, and
`dotnet nano rollback` all behave exactly as their `nano-migrate` equivalents. (`clone` and `fleet`
live only in the standalone `nano-migrate` CLI.)

### `flash`

Resolves and runs the external `nanoff` firmware flasher with mapped arguments.

| Option | Maps to | Meaning |
|---|---|---|
| `-t\|--target <TARGET>` | `nanoff --target` | Target board/firmware name (required). |
| `-p\|--port <PORT>` | `nanoff --serialport` | Serial port. |
| `--update` | `nanoff --update` | Update the device firmware rather than a clean flash. |

Anything after a literal `--` is passed straight through to `nanoff`. Example:

```bash
dotnet nano flash --target ESP32_REV0 --port COM5
dotnet nano flash --target ESP32_REV0 -- --deploy --image app.bin   # passthrough
```

### `deploy` / `monitor` / `devices`

On the design's command surface (so they appear in `--help`) but not yet implemented in the CLI —
they print a "use Visual Studio or VS Code for now" notice and return non-zero. Build/deploy/debug
from the IDE in the meantime.

## External tools

External, prebuilt tools are wrapped, not rebuilt: the umbrella deploys/pins already-released
binaries from their own repos and exposes them under `dotnet nano *`. Resolution order for an
external tool (e.g. `nanoff`):

1. a binary **bundled** in the tool package (`tools/<name>/`, prebuilt and version-pinned),
2. a globally/locally **installed** tool or one on `PATH`,
3. **download** a pinned release to a user cache (verified by version/hash).

> The downloader (step 3) is not wired yet: when nothing resolves, `flash` fails with a clear
> "install nanoff with `dotnet tool install -g nanoff`" message rather than attempting an
> unimplemented network fetch.

The set of external tools, each pinned version, and its download source are declared in the embedded
**`nano-tools.json`** manifest ([`ExternalTools/nano-tools.json`](nanoFramework.Tool/ExternalTools/nano-tools.json)),
so resolution/fetch is deterministic and CI-reproducible:

```json
{
  "tools": [
    { "name": "nanoff", "version": "2.5.78", "packageId": "nanoff",
      "source": "https://www.nuget.org/api/v2/package/nanoff/2.5.78" }
  ]
}
```

## Tests

```bash
dotnet test tools/nano/nanoFramework.Tool.Tests
```

## See also

- [`../migrate/README.md`](../migrate/README.md) — the migrate engine, the standalone `nano-migrate`
  CLI, and the full `migrate`/`clean`/`rollback` option reference.
- [`skills/nanoframework-sdk-migration`](../../skills/nanoframework-sdk-migration) — the installable
  migration skill.
