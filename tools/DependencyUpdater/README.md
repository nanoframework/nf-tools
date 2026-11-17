# nanodu (Dependency Updater)

`nanodu` is a .NET global tool used to update NuGet dependencies in .NET nanoFramework solutions.
It can update one or more solutions, and can also operate across multiple repositories in CI/CD scenarios.

## Purpose

- Update package references in `packages.config` files.
- Keep nuspec and package declarations aligned with updated dependencies.
- Support automation in GitHub Actions and Azure Pipelines.

## Install

Install from NuGet as a .NET global tool:

```bash
dotnet tool install -g nanodu
```

Update to the latest version:

```bash
dotnet tool update -g nanodu
```

## Update a local solution

Run from a folder containing a single `.sln` or `.slnx` file:

```bash
nanodu --preview-packages --local-update
```

When neither `--solutions-to-check` nor `--repos-to-update` is specified, nanodu
selects the only solution in the current folder. Use `--working-directory` to
select a different folder. Automatic discovery does not search subfolders.

If no solution is found, nanodu fails and requests an explicit solution or
repository selection. If multiple solutions are found (including a `.sln` and
`.slnx` for the same solution), select the one to update:

```bash
nanodu --preview-packages --local-update --solutions-to-check nanoFramework.LoRa.sln
```

`--local-update` is still required to update locally without creating a PR or
pushing changes. Omit `--preview-packages` to use stable packages.

## Companion GitHub Action

For GitHub Actions usage, use the companion action repository:

- https://github.com/nanoframework/nanodu
