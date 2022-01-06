//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

namespace nanoFramework.Tools.DependencyUpdater
{
    internal enum RunningEnvironment
    {
        Unknown = 0,
        AzurePipelines,
        GitHubAction,
        Other
    }
}
