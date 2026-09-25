[![License](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE) [![#yourfirstpr](https://img.shields.io/badge/first--timers--only-friendly-blue.svg)](https://github.com/nanoframework/Home/blob/main/CONTRIBUTING.md) [![Discord](https://img.shields.io/discord/478725473862549535.svg)](https://discord.gg/gCyBu8T)

![nanoFramework logo](https://github.com/nanoframework/Home/blob/main/resources/logo/nanoFramework-repo-logo.png)

---

Document Language: [English](README.md) | [中文简体](README.zh-cn.md)

### Welcome to the **nanoFramework** Tools repository!

This repo contains various tools that are required in **nanoFramework** development, usage or repository management.

- [Azure DevOps extensions](AzureDevOps) Extensions used in build pipelines for our repositories.
- [Azure Pipelines Templates](azure-pipelines-templates) Azure Pipelines templates for our repositories.
- [GitHub-bot](AzureFunction-github-bot) Azure Function project for **nanoFramework** GitHub bot which help with managing various aspects of communication and the pull requests workflow.
- [Tools](tools) CLI and desktop tools used in **nanoFramework** development workflows.

## Tools

The following tools are available under the [tools](tools) folder:

| Tool | Folder | Description |
|------|--------|-------------|
| `nano-migrate` (NanoMigrate) | [tools/migrate](tools/migrate) | Converts legacy `.nfproj` projects to SDK-style, single-repo or across an entire fleet. Driven by the companion [migration skill](skills/nanoframework-sdk-migration). |
| `nanodu` (Dependency Updater) | [tools/DependencyUpdater](tools/DependencyUpdater) | Updates NuGet dependencies and related metadata for .NET nanoFramework solutions. |
| `nanovc` (VersionCop) | [tools/VersionCop](tools/VersionCop) | Validates package version consistency and reports version mismatches in nanoFramework builds. |
| `NanoProfiler` | [tools/Profiler](tools/Profiler) | Windows profiler application to inspect runtime and memory behavior of nanoFramework targets. |

## Feedback and documentation

For documentation, providing feedback, issues and finding out how to contribute please refer to the [Home repo](https://github.com/nanoframework/Home).

Join our Discord community [here](https://discord.gg/gCyBu8T).

## Credits

The list of contributors to this project can be found at [CONTRIBUTORS](https://github.com/nanoframework/Home/blob/main/CONTRIBUTORS.md).

## License

The **nanoFramework** tools are are licensed under the [MIT license](LICENSE.md).

## Code of Conduct

This project has adopted the code of conduct defined by the Contributor Covenant to clarify expected behavior in our community.
For more information see the [.NET Foundation Code of Conduct](https://dotnetfoundation.org/code-of-conduct).

### .NET Foundation

This project is supported by the [.NET Foundation](https://dotnetfoundation.org).
