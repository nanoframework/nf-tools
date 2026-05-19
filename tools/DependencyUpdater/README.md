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

## Companion GitHub Action

For GitHub Actions usage, use the companion action repository:

- https://github.com/nanoframework/nanodu
