# nanovc (VersionCop)

`nanovc` is a .NET global tool that checks version consistency in .NET nanoFramework builds.
It validates package versions used by solution projects and helps detect mismatches early in CI/CD.

## Purpose

- Validate dependency version alignment across a solution.
- Detect package version mismatches before release.
- Support automated quality checks in build pipelines.

## Install

Install from NuGet as a .NET global tool:

```bash
dotnet tool install -g nanovc
```

Update to the latest version:

```bash
dotnet tool update -g nanovc
```

## Companion GitHub Action

For GitHub Actions usage, use the companion action repository:

- https://github.com/nanoframework/nanovc
