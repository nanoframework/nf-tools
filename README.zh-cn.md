[![License](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE) [![#yourfirstpr](https://img.shields.io/badge/first--timers--only-friendly-blue.svg)](https://github.com/nanoframework/Home/blob/main/CONTRIBUTING.md) [![Discord](https://img.shields.io/discord/478725473862549535.svg)](https://discord.gg/gCyBu8T)

![nanoFramework logo](https://github.com/nanoframework/Home/blob/main/resources/logo/nanoFramework-repo-logo.png)

---

文档语言: [English](README.md) | [简体中文](README.zh-cn.md)

### 欢迎来到 **nanoFramework** Tools 仓库！

此仓库包含 **nanoFramework** 开发、使用和仓库管理所需的各种工具。

- [Azure DevOps extensions](AzureDevOps) 在仓库构建流水线中使用的扩展。
- [Azure Pipelines Templates](azure-pipelines-templates) 仓库使用的 Azure Pipelines 模板。
- [GitHub-bot](AzureFunction-github-bot) Azure 函数项目，用于 **nanoFramework** GitHub 机器人，有助于管理通信和拉取请求工作流的各个方面。
- [Tools](tools) 用于 **nanoFramework** 开发工作流的 CLI 和桌面工具。

## 工具

以下工具位于 [tools](tools) 文件夹下：

| 工具 | 文件夹 | 描述 |
|------|--------|------|
| `nano-migrate` (NanoMigrate) | [tools/migrate](tools/migrate) | 将旧式 `.nfproj` 项目转换为 SDK 风格，支持单仓库或整个仓库集群。由配套的[迁移技能](skills/nanoframework-sdk-migration)驱动。 |
| `nanodu` (Dependency Updater) | [tools/DependencyUpdater](tools/DependencyUpdater) | 更新 .NET nanoFramework 解决方案中的 NuGet 依赖项及相关元数据。 |
| `nanovc` (VersionCop) | [tools/VersionCop](tools/VersionCop) | 校验包版本一致性，并报告 nanoFramework 构建中的版本不匹配。 |
| `NanoProfiler` | [tools/Profiler](tools/Profiler) | 用于分析 nanoFramework 目标设备运行时和内存行为的 Windows 分析工具。 |

## 反馈和文档

有关文档、反馈、问题和参与贡献的信息，请参阅 [Home 仓库](https://github.com/nanoframework/Home)。

点击[这里](https://discord.gg/gCyBu8T)加入我们的 Discord 社区。

## 致谢

本项目贡献者名单请参见 [CONTRIBUTORS](https://github.com/nanoframework/Home/blob/main/CONTRIBUTORS.md)。

## 许可证

**nanoFramework** tools 基于 [MIT license](LICENSE.md) 许可。

## 行为准则

该项目采用了贡献者公约定义的行为准则，以澄清我们社区的预期行为。
有关详细信息，请参阅 [.NET Foundation Code of Conduct](https://dotnetfoundation.org/code-of-conduct)。

### .NET Foundation

该项目由 [.NET Foundation](https://dotnetfoundation.org) 支持。
