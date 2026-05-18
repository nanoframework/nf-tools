//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using System.Xml.Linq;
using CliWrap;
using CliWrap.Buffered;
using Microsoft.Extensions.Configuration;
using NuGet.Packaging;
using NuGet.Versioning;
using Octokit;

namespace nanoFramework.Tools.DependencyUpdater
{
    internal class Program
    {
        private const string AMQPLiteLibraryName = "amqpnetlite";
        private static string _nuGetConfigFile;

        static readonly GitHubClient _octokitClient = new(new ProductHeaderValue("nanodu"));
        private static RunningEnvironment _runningEnvironment;
        private static string _gitHubAuth;
        private static string[] _solutionsExclusionList;
        private static string _gitHubUser;
        private static string _workingRepoOwner;
        private static string _baseBranch;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="workingDirectory">Working directory. Required when updating a single repository.</param>
        /// <param name="stablePackages">Use stable NuGet package versions. This is the default configuration.</param>
        /// <param name="previewPackages">Use preview NuGet package versions.</param>
        /// <param name="solutionsToCheck">List of Solution(s) to update in the <paramref name="workingDirectory"/> directory.</param>
        /// <param name="reposToUpdate">List of repository(es) to update.</param>
        /// <param name="exclusionList">List of solution names to exclude from the update. Comma separated, name only.</param>
        /// <param name="branchToPr">Name of the branch to submit PR with updates. Default is 'main'.</param>
        /// <param name="gitHubUser">Name of the git hub users. Used for creating PR and authentication. Default is 'nfbot'</param>
        /// <param name="gitHubEmail">Email of the git hub users. Used for creating commits. Default is 'nanoframework@outlook.com'</param>
        /// <param name="nugetConfig">Path to nuget.config file to use for update operations. Leave <see langword="null"/> to use 'nuget.org'</param>
        /// <param name="repoOwner">Github repository owner (https://github.com/[repoOwner]/repositoryName). If not provided, created based on github repository url where the tool was invoked.</param>
        /// <param name="gitHubAuth">GitHub authentication token. If not provided, the tool will try to use the GITHUB_TOKEN environment variable.</param>
        /// <param name="useGitTokenForClone">Set to true if you trying to update private GitHub repository. Default is false.</param>
        /// <param name="localUpdate">Perform the update locally without creating a PR or pushing to GitHub. When set, GitHub token and PR-related parameters are not required.</param>
        /// <param name="args">List of Solutions files to check or repositories to update. According to option specified with <paramref name="solutionsToCheck"/> or <paramref name="reposToUpdate"/>.</param>
        static void Main(
            string workingDirectory = null,
            bool stablePackages = false,
            bool previewPackages = false,
            bool solutionsToCheck = false,
            bool reposToUpdate = false,
            string exclusionList = null,
            string branchToPr = "main",
            string gitHubUser = "nfbot",
            string gitHubEmail = "nanoframework@outlook.com",
            string nugetConfig = null,
            string repoOwner = null,
            string gitHubAuth = null,
            bool useGitTokenForClone = false,
            bool localUpdate = false,
            string[] args = null)
        {
            // sanity check 
            if (!solutionsToCheck && !reposToUpdate)
            {
                Console.WriteLine($"::error::❌ Missing required option: specify either '--solutions-to-check' or '--repos-to-update'");
                Environment.Exit(1);
            }

            // check if this is running on a git repo
            var gitRepo = CheckIfDirectoryIsGitRepo(workingDirectory);
            if (!gitRepo.Item1)
            {
                Console.WriteLine($"::error::❌ Working directory is not a git repository");
                Environment.Exit(1);
            }

            if (repoOwner is null)
            {
                repoOwner = GitHubHelper.GetRepoOwnerFromUrl(gitRepo.Item2);
            }

            // find repo owner for runner
            _workingRepoOwner = GitHubHelper.GetRepoOwnerFromUrl(gitRepo.Item2);

            if (workingDirectory is null)
            {
                // default to current directory
                workingDirectory = Environment.CurrentDirectory;
            }

            if (!Directory.Exists(workingDirectory))
            {
                Console.WriteLine($"::error::❌ Directory not found: {workingDirectory}");
                Environment.Exit(1);
            }

            if (previewPackages)
            {
                stablePackages = false;
            }


            // if no preview or stable packages were requested, default to stable
            if (!stablePackages && !previewPackages)
            {
                stablePackages = true;
            }

            if (stablePackages && previewPackages)
            {
                Console.WriteLine($"::error::❌ Cannot specify both stable and preview packages simultaneously");
                Environment.Exit(1);
            }

            // remove empty arguments
            args = args.Where(a => !string.IsNullOrEmpty(a)).ToArray();

            _gitHubUser = gitHubUser;
            _baseBranch = branchToPr;

            _runningEnvironment = GetRunningEnvironment();
            Console.WriteLine($"\n🚀 Environment: {_runningEnvironment}");

            if (localUpdate)
            {
                Console.WriteLine("ℹ️  Mode: Local update (no PR will be created)");
            }
            else
            {
                Console.WriteLine($"🔀 Branch target: {branchToPr}");
            }

            // setup git stuff, only if we are not in debug mode
#if DEBUG
            // doing something with this parameter so it doesn't get caught by static analysers
            _ = gitHubEmail.Contains(_gitHubUser);
#else
            RunGitCli("config --global gc.auto 0", "");
            RunGitCli($"config --global user.name {gitHubUser}", "");
            RunGitCli($"config --global user.email {gitHubEmail}", "");
            RunGitCli("config --global core.autocrlf true", "");
#endif

            if (!localUpdate)
            {
                if (string.IsNullOrEmpty(gitHubAuth))
                {
                    // no auth provided, try to use GITHUB_TOKEN environment variable
                    var gitHubToken = GetGitHubToken();
                    _octokitClient.Credentials = new Octokit.Credentials(gitHubToken);
                    // compute authorization header in format "AUTHORIZATION: basic 'encoded token'"
                    _gitHubAuth = $"basic {Convert.ToBase64String(Encoding.UTF8.GetBytes($"{gitHubUser}:{gitHubToken}"))}";
                }
                else
                {
                    // use provided auth token
                    _octokitClient.Credentials = new Octokit.Credentials(gitHubAuth);
                    // compute authorization header in format "AUTHORIZATION: basic x-access-token:<accessToken>"
                    _gitHubAuth = $"basic {Convert.ToBase64String(Encoding.UTF8.GetBytes($"x-access-token:{gitHubAuth}"))}";
                }
            }

            // store exclusion list
            try
            {
                _solutionsExclusionList = exclusionList is not null ? exclusionList.Split(",") : Array.Empty<string>();
            }
            catch
            {
                Console.WriteLine($"::error::❌ Failed to parse exclusion list. Use comma-separated solution names.");
                Environment.Exit(1);
            }

            args = RebuildArgsListWithNewLinesIfNeeded(args);

            // choose work-flow
            if (solutionsToCheck)
            {
                // this is updating solution(s) on a repository

                // go for the library update
                UpdateLibrary(
                    workingDirectory,
                    stablePackages,
                    previewPackages,
                    branchToPr,
                    nugetConfig,
                    repoOwner,
                    gitRepo.Item2,
                    localUpdate,
                    args);
            }
            else
            {
                if (_runningEnvironment == RunningEnvironment.AzurePipelines)
                {
                    // need this to remove definition of redirect stdErr (only on Azure Pipelines image fo VS2019)
                    Environment.SetEnvironmentVariable("GIT_REDIRECT_STDERR", "2>&1");
                }

                var resultCollection = new List<Tuple<bool, string>>();
                foreach (var repoName in args)
                {
                    // remove quotes, if any
                    var library = repoName.Replace("'", "");

                    _baseBranch = branchToPr;

                    Console.WriteLine();
                    Console.WriteLine();
                    Console.WriteLine("═══════════════════════════════════════");
                    Console.WriteLine($"📦 Processing repository: {library}");
                    Console.WriteLine("═══════════════════════════════════════");

                    // clone depth
                    string cloneDepth = "--depth 1";

                    // if we are updating IoT bindings repo, can't use a shallow clone
                    // because we need to be able to compute a version
                    if (Environment.GetEnvironmentVariable("GITHUB_REPOSITORY") is not null
                        && Environment.GetEnvironmentVariable("GITHUB_REPOSITORY") == "nanoframework/nanoFramework.IoT.Device")
                    {
                        cloneDepth = "";
                    }

                    Console.WriteLine();
                    Console.WriteLine($"📥 Cloning repository: {library}");

                    var cloneCommand = CreateCloneCommand(cloneDepth, repoOwner, library, useGitTokenForClone, gitHubAuth);
                    if (!RunGitCli(cloneCommand, workingDirectory))
                    {
                        resultCollection.Add(Tuple.Create(false, $"❌ {repoName}: Clone failed"));
                        continue;
                    }

                    workingDirectory = Path.Combine(workingDirectory, library);

                    string[] sln;
                    // check for special repos that have sources on different location
                    if (library == AMQPLiteLibraryName)
                    {
                        sln = Directory.GetFiles(
                            workingDirectory,
                            "amqp-nanoFramework.sln",
                            SearchOption.TopDirectoryOnly).Select(n => Path.GetFileName(n)).ToArray();

                        // CD-CI branch is not 'develop'
                        _baseBranch = "nanoframework-dev";
                    }
                    else
                    {
                        sln = Directory.GetFiles(
                            workingDirectory,
                            "*.sln",
                            SearchOption.TopDirectoryOnly).Select(n => Path.GetFileName(n)).ToArray();
                    }

                    if (!RunGitCli($"checkout --quiet {_baseBranch}", workingDirectory))
                    {
                        resultCollection.Add(Tuple.Create(false, $"❌ {repoName}: Checkout to {_baseBranch} failed"));
                        continue;
                    }

                    // get git repo name for each library and repo owner
                    var gitRepoInternal = CheckIfDirectoryIsGitRepo(workingDirectory);
                    if (!gitRepoInternal.Item1)
                    {
                        resultCollection.Add(Tuple.Create(false, $"❌ {repoName}: Not a valid git repository"));
                        continue;
                    }

                    _workingRepoOwner = GitHubHelper.GetRepoOwnerFromUrl(gitRepoInternal.Item2);

                    // go for the library update
                    UpdateLibrary(
                        workingDirectory,
                        stablePackages,
                        previewPackages,
                        branchToPr,
                        nugetConfig,
                        repoOwner,
                        gitRepoInternal.Item2,
                        localUpdate,
                        sln);

                    resultCollection.Add(Tuple.Create(true, $"✅ {repoName}: Update completed"));
                }

                // Print result
                Console.WriteLine();
                Console.WriteLine("═══════════════════════════════════════");
                Console.WriteLine("📊 Update Summary");
                Console.WriteLine("═══════════════════════════════════════");
                foreach (var result in resultCollection)
                {
                    Console.WriteLine($"  {result.Item2}");
                }

                // If any error, exit application with error code
                if (resultCollection.Any(x => x.Item1))
                {
                    Environment.Exit(1);
                }
            }

            // exit OK
            Environment.Exit(0);
        }

        /// <summary>
        /// Executes git command to check if directory is valid git repository.
        /// </summary>
        /// <param name="directory">Path to check.</param>
        /// <returns>Tuple with status and remote address of git repository.</returns>
        private static Tuple<bool, string> CheckIfDirectoryIsGitRepo(string directory)
        {
            var gitRepo = string.Empty;
            if (!RunGitCli("remote -v", ref gitRepo, directory))
            {
                return Tuple.Create(false, string.Empty);
            }

            if (gitRepo.Contains("fatal: not a git repository"))
            {
                return Tuple.Create(false, string.Empty);
            }

            return Tuple.Create(true, gitRepo);
        }

        internal static string CreateCloneCommand(string cloneDepth, string repoOwner, string library, bool usePatForClone, string gitHubAuth)
        {
            if (usePatForClone)
            {
                var token = string.IsNullOrEmpty(gitHubAuth) ? GetGitHubToken() : gitHubAuth;
                return $"clone {cloneDepth} https://{token}@github.com/{repoOwner}/{library} {library}";
            }

            return $"clone {cloneDepth} https://github.com/{repoOwner}/{library} {library}";
        }

        private static string[] RebuildArgsListWithNewLinesIfNeeded(string[] args)
        {
            // parse args in case these are in a single line
            if (args.Count() == 1 && args[0].Contains("\r\n"))
            {
                Console.WriteLine($"INFO: rebuilding args list with new lines.");
                return args[0].Split("\r\n");
            }

            if (args.Count() == 1 && args[0].Contains("\n"))
            {
                Console.WriteLine($"INFO: rebuilding args list with new lines.");
                return args[0].Split("\n");
            }

            return args;
        }

        private static string GetGitHubToken()
        {
#if DEBUG
            var config = new ConfigurationBuilder()
                .AddUserSecrets<Program>()
                .Build();
            return config.GetSection("GITHUB_TOKEN").Value;
#else
            var tokenFromEnvironmentVariable = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
            if (tokenFromEnvironmentVariable is null)
            {
                Console.WriteLine($"::error::❌ GITHUB_TOKEN environment variable not found");

                // exit with error
                Environment.Exit(1);
            }

            return tokenFromEnvironmentVariable;
#endif
        }

        private static RunningEnvironment GetRunningEnvironment()
        {
            // these variables are only available on Azure Pipelines build
            if (Environment.GetEnvironmentVariable("Agent_HomeDirectory") is not null
                && Environment.GetEnvironmentVariable("Build_BuildNumber") is not null)
            {
                return RunningEnvironment.AzurePipelines;
            }

            // this variable it's only set when running on a GitHub action
            if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") is not null)
            {
                return RunningEnvironment.GitHubAction;
            }

            return RunningEnvironment.Other;
        }

        static void UpdateLibrary(string workingDirectory,
                        bool stablePackages,
                        bool previewPackages,
                        string branchToPr,
                        string nugetConfig,
                        string repoOwner,
                        string gitRepo,
                        bool localUpdate,
                        string[] solutionsToCheck)
        {
            string releaseType = stablePackages ? "stable" : previewPackages ? "preview" : "?????";

            Console.WriteLine($"\n📂 Working directory: {workingDirectory}");

            Console.WriteLine($"📦 Using {releaseType} NuGet packages");

            if (solutionsToCheck is not null)
            {
                Console.WriteLine($"🎯 Target solution(s): {string.Join(", ", solutionsToCheck.Select(Path.GetFileName))}");
            }
            else
            {
                Console.WriteLine("🎯 Target: All solutions in repository");
            }

            var repoName = GitHubHelper.GetRepoNameFromInputString(gitRepo);
            if (!repoName.Success)
            {
                Console.WriteLine($"::error::❌ Unable to determine repository name from git remote");
                Environment.Exit(1);
            }

            // deal with NuGet.Config
            if (nugetConfig is not null)
            {
                // check if file exists
                if (File.Exists(nugetConfig))
                {
                    Console.WriteLine();
                    Console.WriteLine($"INFO: working with '{nugetConfig}'");

                    // compose option for nuget CLI 
                    _nuGetConfigFile = $" -ConfigFile {nugetConfig}";
                }
                else if (File.Exists(Path.Combine(workingDirectory, nugetConfig)))
                {
                    Console.WriteLine();
                    Console.WriteLine($"INFO: working with '{Path.Combine(workingDirectory, nugetConfig)}'");

                    // compose option for nuget CLI 
                    _nuGetConfigFile = $" -ConfigFile {Path.Combine(workingDirectory, nugetConfig)}";
                }
                else
                {
                    Console.WriteLine($"::error::❌ NuGet config file not found: {nugetConfig}");
                    Environment.Exit(1);
                }
            }

            var libraryName = GitHubHelper.GetLibNameFromRegexMatch(repoName);

            Console.WriteLine($"\n📦 Repository: {libraryName}");

            // adjust location and 
            if (_runningEnvironment == RunningEnvironment.AzurePipelines)
            {
                // adjust location for the SLN file
                workingDirectory = Path.Combine(Environment.GetEnvironmentVariable("Build_SourcesDirectory"), Environment.GetEnvironmentVariable("NF_Library"));
            }
            else if (_runningEnvironment == RunningEnvironment.GitHubAction)
            {
                // store library name
                libraryName = Directory.GetParent(workingDirectory).Name;
            }

            // init/reset these
            int updateCount = 0;
            int nuspecUpdatedCounter = 0;
            StringBuilder commitMessage = new();
            string newBranchName = $"{_gitHubUser}/update-dependencies/{Guid.NewGuid().ToString()}";

            // collect solution(s)
            List<string> solutionFiles = CollectSolutions(workingDirectory, solutionsToCheck);

            if (!solutionFiles.Any())
            {
                Console.WriteLine();
                Console.WriteLine("INFO: No solutions found...");
                return;
            }

            // list solutions to check
            Console.WriteLine();
            Console.WriteLine($"📝 Solutions to process:");

            foreach (var sln in solutionFiles)
            {
                Console.Write($"  • {Path.GetRelativePath(workingDirectory, sln)}");

                // check if this on is in the exclusion list
                if (_solutionsExclusionList.Contains(Path.GetFileNameWithoutExtension(sln)))
                {
                    Console.WriteLine(" \u274c EXCLUDED");
                }
                else
                {
                    Console.WriteLine("");
                }
            }

            // Initialize a HashSet to collect unique update messages
            HashSet<string> updateMessages = new();

            // go through each solution (filter out the ones in the exclusion list)
            foreach (var solutionFile in solutionFiles.Where(s => !_solutionsExclusionList.Contains(Path.GetFileNameWithoutExtension(s))))
            {
                Console.WriteLine();
                Console.WriteLine();
                Console.WriteLine("::group::🔧 Processing solution");
                Console.WriteLine($"  📄 {Path.GetFileName(solutionFile)}");

                // look for nfproj
                var slnFileContent = File.ReadAllText(solutionFile);

                if (!slnFileContent.Contains(".nfproj"))
                {
                    Console.WriteLine($"  ⚠️  No .nfproj files found - skipping");
                    Console.WriteLine("::endgroup::");
                    continue;
                }

                // find part for SLN
                var solutionPath = Directory.GetParent(solutionFile).FullName;

                // find ALL packages.config files inside the solution projects
                var packageConfigs = Directory.GetFiles(solutionPath, "packages.config", SearchOption.AllDirectories);

                // perform NuGet restore
                Console.WriteLine();
                Console.WriteLine("  📥 Restoring NuGet packages...");

                if (!RunNugetCLI("restore", $"\"{solutionFile}\""))
                {
                    Environment.Exit(1);
                }

                Console.WriteLine($"  ✅ Found {packageConfigs.Length} packages.config file(s)");

                // Build package update cache once for the entire solution
                var packageUpdateCache = BuildPackageUpdateCache(packageConfigs, stablePackages);

                // Pre-check: skip solution if no updates available (only for stable packages)
                if (stablePackages && !packageUpdateCache.Any(kv => kv.Value != null))
                {
                    // no packages to update
                    Console.WriteLine();
                    Console.WriteLine($"⏭️  No updates available - skipping solution");
                    Console.WriteLine();

                    continue;
                }

                // specify repository path, just in case
                string repositoryPath = $"-RepositoryPath {Directory.GetParent(solutionFile).FullName}\\packages";

                foreach (var packageConfigFile in packageConfigs)
                {
                    Console.WriteLine();
                    Console.WriteLine($"::group::📝 Processing project: {Path.GetFileName(Path.GetDirectoryName(packageConfigFile))}");

                    // check if the project the packages.config belongs to it's in the solution 
                    var projectPathInSln = Path.GetRelativePath(solutionPath, Directory.GetParent(packageConfigFile).FullName);

                    // check for project in the same folder
                    if (projectPathInSln == ".")
                    {
                        projectPathInSln = "";
                    }
                    else
                    {
                        // need these extra replacements to adjust for regex expression
                        projectPathInSln = projectPathInSln.Replace("\\", "\\\\");
                        projectPathInSln = projectPathInSln.Replace(".", "\\.");

                        // add trailing \ to match whatever will be in the solution file
                        projectPathInSln += "\\\\";
                    }

                    var match = Regex.Match(
                        slnFileContent,
                        $"(?> = \\\")(?'projectname'[a-zA-Z0-9_.-]+)(?>\\\", \\\"{projectPathInSln})(?'projectpath'[a-zA-Z0-9_.-]+.nfproj)(\\\")",
                        RegexOptions.IgnoreCase);

                    if (!match.Success)
                    {
                        Console.WriteLine($"  ⚠️  No matching project found - skipping");
                        Console.WriteLine("::endgroup::");
                        continue;
                    }

                    var projectToUpdate = Directory.GetFiles(solutionPath, match.Groups["projectpath"].Value, SearchOption.AllDirectories).FirstOrDefault();
                    var projectName = match.Groups["projectname"].Value;



                    // load packages.config 
                    var packageReader = new NuGet.Packaging.PackagesConfigReader(XDocument.Load(packageConfigFile));
                    var packageList = packageReader.GetPackages();

                    if (!packageList.Any())
                    {
                        // no packages to update
                        Console.WriteLine($"  ℹ️  No packages found - skipping");
                        Console.WriteLine("::endgroup::");

                        continue;
                    }

                    if (libraryName == "Samples")
                    {
                        UpdateProject(projectToUpdate, repositoryPath, ref updateCount, ref commitMessage);
                    }
                    else
                    {
                        // reset warning var
                        string nuspecNotFoundMessage = "";

                        // try to find nuspec to update it
                        string nuspecFileName = null;

                        var candidateNuspecFiles = Directory.GetFiles(solutionPath, $"*{Path.GetFileNameWithoutExtension(projectToUpdate)}.nuspec", SearchOption.AllDirectories);
                        if (candidateNuspecFiles.Any())
                        {
                            // take 1st
                            nuspecFileName = candidateNuspecFiles.FirstOrDefault();
                        }

                        // sanity check for nuspec file
                        if (nuspecFileName is null)
                        {
                            // try again with project name
                            candidateNuspecFiles = Directory.GetFiles(solutionPath, $"*{projectName}.nuspec", SearchOption.AllDirectories);
                            if (candidateNuspecFiles.Any())
                            {
                                // take 1st
                                nuspecFileName = candidateNuspecFiles.FirstOrDefault();
                            }

                            // if this is AMQPLite, there is a different pattern for the nuspec names
                            if (libraryName == AMQPLiteLibraryName)
                            {
                                candidateNuspecFiles = Directory.GetFiles(solutionPath, $"*{projectName.Replace("Amqp.Micro", "AMQPNetMicro").Replace("Amqp.", "AMQPNetLite.")}.nuspec", SearchOption.AllDirectories);

                                if (candidateNuspecFiles.Any())
                                {
                                    // take 1st
                                    nuspecFileName = candidateNuspecFiles.FirstOrDefault();
                                }
                            }

                            if (nuspecFileName is null)
                            {
                                if (!nuspecNotFoundMessage.Contains(projectToUpdate))
                                {
                                    Console.WriteLine();
                                    Console.WriteLine();
                                    Console.WriteLine("**********************************************");
                                    Console.WriteLine($"INFO: Can't find nuspec file matching project '{Path.GetFileNameWithoutExtension(projectToUpdate)}'");
                                    Console.WriteLine("**********************************************");
                                    Console.WriteLine();

                                    // store project name, so the warning shows only once
                                    nuspecNotFoundMessage += projectToUpdate;
                                }
                            }

                            if (nuspecFileName is not null)
                            {
                                Console.WriteLine($"  📋 Nuspec: {Path.GetFileName(nuspecFileName)}");
                            }
                        }

                        // list packages to check
                        Console.WriteLine();
                        Console.WriteLine("  📦 Packages:");
                        foreach (var package in packageList)
                        {
                            Console.WriteLine($"    • {package.PackageIdentity.Id} {package.PackageIdentity.Version}");
                        }
                        Console.WriteLine();

                        // check all packages

                        foreach (PackageReference package in packageList)
                        {
                            // get package name and target version
                            string packageName = package.PackageIdentity.Id;
                            string packageOriginVersion = package.PackageIdentity.Version.ToNormalizedString();



                            // Check cache first to see if update is available
                            // Skip packages not in cache or with no available updates (unless they need special handling)
                            // UnitsNet packages need special handling (search nuget.org)
                            // TestFramework needs special handling (updates .nfproj file)
                            if (!packageName.StartsWith("UnitsNet.")
                                && !packageName.StartsWith("nanoFramework.TestFramework"))
                            {
                                var cacheKey = (packageName, packageOriginVersion);
                                if (packageUpdateCache.TryGetValue(cacheKey, out var cachedVersion))
                                {
                                    if (cachedVersion == null)
                                    {
                                        Console.WriteLine($"  ✓ {packageName} {packageOriginVersion}");
                                        continue;
                                    }
                                }
                            }

                            string updateResult = "";
                            string updateParameters;

                            if ((stablePackages && !packageName.StartsWith("UnitsNet."))
                                || (package.IsDevelopmentDependency && !packageName.StartsWith("nanoFramework.TestFramework")))
                            {
                                // don't allow prerelease for:
                                // - release
                                // - main branches
                                // - UnitsNet packages
                                // - development dependencies (except nanoFramework.TestFramework)
                                updateParameters = $"{projectToUpdate} -Id {packageName} {repositoryPath} -FileConflictAction Overwrite";
                            }
                            else if (packageName.StartsWith("UnitsNet."))
                            {
                                // grab latest version from NuGet
                                string unitsNetPackageInfo = "";
                                if (!RunNugetCLI(
                                    "search",
                                    $" {packageName} -Verbosity quiet ",
                                    false,
                                    ref unitsNetPackageInfo))
                                {
                                    Environment.Exit(1);
                                }

                                string unitsNetVersion = "N/A";

                                Regex regex = new Regex(@"\| ([\d\.]+)");
                                Match unitsNetVersionFound = regex.Match(unitsNetPackageInfo);

                                if (unitsNetVersionFound.Success)
                                {
                                    unitsNetVersion = unitsNetVersionFound.Groups[1].Value;
                                }

                                // we have to use nuget.org with UnitsNet
                                updateParameters = $"\"{projectToUpdate}\" -Id {packageName} -Version {unitsNetVersion} {repositoryPath} -FileConflictAction Overwrite  -Source \"https://api.nuget.org/v3/index.json\"";
                            }
                            else
                            {
                                // all the rest, use prerelase packages
                                // go with our Azure feed
                                updateParameters = $"\"{projectToUpdate}\" -Id {packageName} -PreRelease {repositoryPath} -FileConflictAction Overwrite";
                            }

                            bool okToRetry = true;
                            string packageTargetVersion = "";

                        performUpdate:
                            // perform NuGet update
                            if (!RunNugetCLI(
                                "update",
                                updateParameters,
                                true,
                                ref updateResult))
                            {
                                Environment.Exit(1);
                            }

                            // check update outcome
                            string updateRegex = $"(?:Successfully\\sinstalled\\s\\'{packageName.Replace(".", "\\.")}\\s)(?'newVersion'\\S+)(?:\\'\\sto\\s)";

                            var updateOutcome = Regex.Match(updateResult, updateRegex);

                            if (!updateOutcome.Success)
                            {
                                // check for no updates available message
                                if (!updateResult.Contains("There are no new updates available"))
                                {
                                    // something wrong happened!
                                    // output update message for the log
                                    Console.WriteLine($"::warning::⚠️  Unexpected update outcome:");
                                    Console.WriteLine($"{updateResult}");
                                    Console.WriteLine();

                                    if (okToRetry)
                                    {
                                        // reset flag
                                        okToRetry = false;

                                        goto performUpdate;
                                    }

                                    Environment.Exit(1);
                                }

                                // this could have been updated as a nested dependency
                                // refresh package list
                                // load packages.config 
                                packageReader = new PackagesConfigReader(XDocument.Load(packageConfigFile));

                                // filter out Nerdbank.GitVersioning package and development dependencies (except our Test Framework)
                                var packageToRefresh = packageReader.GetPackages()
                                    .FirstOrDefault(p => p.PackageIdentity.Id == packageName);

                                if (packageToRefresh is null)
                                {
                                    Console.WriteLine($"    ℹ️  Package not found in any NuGet source - skipping");
                                    Console.WriteLine();
                                    continue;
                                }

                                // grab target version
                                packageTargetVersion =
                                    packageToRefresh.PackageIdentity.Version.ToNormalizedString();

                                if (packageOriginVersion == packageTargetVersion)
                                {
                                    Console.WriteLine($"  ✓ {packageName} {packageOriginVersion}");
                                }
                            }
                            else
                            {
                                // grab target version
                                packageTargetVersion = updateOutcome.Groups["newVersion"].Value;
                            }

                            // sanity check
                            if (packageTargetVersion.Contains("alpha"))
                            {
                                Console.WriteLine($"::error::🛑 Attempting to use alpha version of {packageName} - aborting");

                                // exit with error 
                                Environment.Exit(1);
                            }
                            else
                            {
                                // build update message
                                if (packageOriginVersion != packageTargetVersion)
                                {
                                    string updateMessage = $"Bumps {packageName} from {packageOriginVersion} to {packageTargetVersion}</br>";

                                    // add to the HashSet to ensure uniqueness
                                    updateMessages.Add(updateMessage);

                                    Console.WriteLine($"  ⬆️  {packageName} {packageOriginVersion} → {packageTargetVersion}");
                                }

                                // if this is the Test Framework, need to update the nfproj file too
                                if (packageName == "nanoFramework.TestFramework")
                                {
                                    // read nfproj file
                                    var nfprojFileContent = File.ReadAllText(projectToUpdate);

                                    var updatedProjContent = Regex.Replace(nfprojFileContent, $"(?<=packages\\\\nanoFramework\\.TestFramework\\.)(?'version'\\d+\\.\\d+\\.\\d+)(?=\\\\build\\\\)", packageTargetVersion, RegexOptions.ExplicitCapture);

                                    File.WriteAllText(projectToUpdate, updatedProjContent);
                                }

                                // Remove the <Private> elements NuGet adds to the project file
                                // This could be async be the tool isn't using that so I'll skip it for now
                                var projectDocument = XDocument.Load(projectToUpdate);
                                var referenceElements = projectDocument.Root?.Descendants().Where(x => x.Name.LocalName == "Reference").ToList();

                                foreach (var referenceElement in referenceElements)
                                {
                                    var privateElements = referenceElement.Descendants().Where(x => x.Name.LocalName == "Private").ToList();
                                    foreach (var privateElement in privateElements)
                                    {
                                        privateElement.Remove();
                                    }
                                }

                                // Save project file and ensure stream is fully closed before proceeding
                                using (var projectFile = File.Create(projectToUpdate))
                                {
                                    projectDocument.Save(projectFile, SaveOptions.None);
                                }

                                // load nuspec file content, if there is a nuspec file to update
                                if (nuspecFileName is not null)
                                {
                                    var nuspecFile = new XmlDocument();
                                    nuspecFile.Load(nuspecFileName);

                                    // Define a regular expression pattern to match the namespace declaration
                                    string pattern = @"xmlns\s*=\s*""(?<namespace>.*?)""";

                                    // Use Regex.Match to find the first match of the pattern in the nuspec content
                                    Match namespaceMatch = Regex.Match(nuspecFile.InnerXml, pattern, RegexOptions.Singleline);

                                    // default to empty namespace
                                    string nuspecNamespace = string.Empty;

                                    // if a match is found, extract the namespace value
                                    if (namespaceMatch.Success)
                                    {
                                        nuspecNamespace = namespaceMatch.Groups["namespace"].Value;
                                    }

                                    XmlNamespaceManager nsmgr = new XmlNamespaceManager(nuspecFile.NameTable);
                                    nsmgr.AddNamespace("package", nuspecNamespace);

                                    // update version, if this dependency is listed
                                    var dependency = nuspecFile.SelectSingleNode($"descendant::package:dependency[@id='{packageName}']", nsmgr);
                                    if (dependency is not null)
                                    {
                                        if (dependency.Attributes["version"].Value != packageTargetVersion)
                                        {
                                            dependency.Attributes["version"].Value = packageTargetVersion;

                                            Console.WriteLine($"Updating nuspec: '{dependency.Attributes["id"].Value}' ---> {packageTargetVersion}");

                                            // save back changes
                                            // developer note: using stream writer instead of Save(to file name) because of random issues with updated content
                                            // not being saved thus causing bogus updates on the nuspec content
                                            using (StreamWriter nuspecStreamWriter = new StreamWriter(nuspecFileName, false))
                                            {
                                                nuspecFile.Save(nuspecStreamWriter);
                                            }

                                            // bump counter
                                            nuspecUpdatedCounter++;
                                        }
                                    }
                                    else
                                    {
                                        // Silently skip - not listed in nuspec (no need to log)
                                    }

                                }
                            }
                        }
                    }
                }
            }

            // Convert the HashSet to a list and build the commit message
            if (updateMessages.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine("INFO: generating commit message");

                List<string> uniqueUpdateMessages = updateMessages.ToList();
                commitMessage.Append(string.Join("", uniqueUpdateMessages));

                // Update the update count
                updateCount = uniqueUpdateMessages.Count;
            }

            Console.WriteLine("::endgroup::");

            // check if any packages where updated
            if (updateCount == 0)
            {
                Console.WriteLine();
                Console.WriteLine("ℹ️  No package updates were applied");
            }

            // sanity check for no nuspecs found
            if (nuspecUpdatedCounter == 0)
            {
                Console.WriteLine();

                if (updateCount != 0)
                {
                    Console.WriteLine($"::warning::⚠️  No nuspec files were updated - this may need review");
                }
            }

            if (updateCount == 0
                && nuspecUpdatedCounter == 0)
            {
                return;
            }

            Console.WriteLine();
            Console.WriteLine($"::notice::✅ Successfully updated {updateCount} package(s)");

            // need this line so nfbot flags the PR appropriately
            commitMessage.Append("\n[version update]\n\n");

            // better add this warning line               
            commitMessage.Append("### :warning: This is an automated update. :warning:\n");

            if (localUpdate)
            {
                Console.WriteLine($"INFO: local update mode, skipping branch creation and PR submission.");
                return;
            }



            Console.WriteLine($"  🔀 Branch: {newBranchName}");

            // create branch to perform updates
            if (!RunGitCli($"branch {newBranchName}", workingDirectory))
            {
                Environment.Exit(1);
            }

            // checkout branch
            if (!RunGitCli($"checkout {newBranchName}", workingDirectory))
            {
                Environment.Exit(1);
            }

            Console.WriteLine($"  ➕ Staging changes");

            // add changes without any .runsettings file which may override user config and break unit test execution
            if (!RunGitCli("add --all -- :!*.runsettings", workingDirectory))
            {
                Environment.Exit(1);
            }

            string prTitle = $"Update {updateCount} NuGet dependencies";

            // commit message with a different title if one or more dependencies are updated
            if (updateCount > 1)
            {
                if (!RunGitCli($"commit -m \"Update {updateCount} NuGet dependencies\" -m\"{commitMessage}\"", workingDirectory))
                {
                    Environment.Exit(1);
                }
            }
            else
            {
                if (!RunGitCli($"commit -m \"{prTitle}\" -m\"{commitMessage}\"", workingDirectory))
                {
                    Environment.Exit(1);
                }
            }

            // push changes to github
            if (!RunGitCli($"-c http.extraheader=\"AUTHORIZATION: {_gitHubAuth}\" push --set-upstream origin {newBranchName}", workingDirectory))
            {
                Environment.Exit(1);
            }

            // go for PR submission with the update
            if (CretePRWithUpdate(
                branchToPr,
                libraryName,
                commitMessage.ToString(),
                newBranchName,
                prTitle,
                repoOwner) == PrCreationOutcome.QuitSubmission)
            {
                // PR wasn't submitted, better delete new branch so they don't pile up

                // checkout to "base" branch
                Console.WriteLine($"\n♻️  Cleaning up: checking out '{_baseBranch}'");
                if (!RunGitCli($"checkout {_baseBranch}", workingDirectory))
                {
                    Console.WriteLine($"::warning::⚠️  Failed to checkout '{_baseBranch}' - manual cleanup may be needed");
                }

                // delete local and remote branches
                Console.WriteLine($"  🗑️  Deleting branch: {newBranchName}");

                // local (don't care about outcome
                _ = RunGitCli($"branch -D {newBranchName}", workingDirectory);

                // remote
                if (!RunGitCli($"push origin --delete {newBranchName}", workingDirectory))
                {
                    Console.WriteLine($"::warning::⚠️  Failed to delete remote branch '{newBranchName}' - manual cleanup may be needed");
                }
            }
        }

        /// <summary>
        /// Builds a cache of available package updates for all unique packages in the solution.
        /// Returns a dictionary mapping (packageId, currentVersion) to availableVersion (or null if no update available).
        /// </summary>
        private static Dictionary<(string Id, string Version), string> BuildPackageUpdateCache(string[] packageConfigs, bool stablePackages)
        {
            var updateCache = new Dictionary<(string Id, string Version), string>();
            var uniquePackages = new Dictionary<(string Id, string Version), bool>(); // Track if development dependency

            // Collect all unique package identities across all config files
            foreach (var packageConfig in packageConfigs)
            {
                var packageReader = new PackagesConfigReader(XDocument.Load(packageConfig));
                var packageList = packageReader.GetPackages();

                foreach (var package in packageList)
                {
                    var key = (package.PackageIdentity.Id, package.PackageIdentity.Version.ToString());

                    // Track if this package is a development dependency anywhere
                    if (!uniquePackages.ContainsKey(key))
                    {
                        uniquePackages[key] = package.IsDevelopmentDependency;
                    }
                    else if (package.IsDevelopmentDependency)
                    {
                        // If we find it's a dev dependency in any project, mark it as such
                        uniquePackages[key] = true;
                    }
                }
            }

            Console.WriteLine();
            Console.WriteLine($"::group::📦 Building update cache for {uniquePackages.Count} unique package(s)");

            // Check each unique package and cache the result
            foreach (var packageEntry in uniquePackages)
            {
                var (packageId, packageVersion) = packageEntry.Key;
                bool isDevelopmentDependency = packageEntry.Value;

                string nugetApiUrl = $"https://api.nuget.org/v3-flatcontainer/{packageId.ToLower()}/index.json";

                using (var httpClient = new HttpClient())
                {
                    try
                    {
                        var json = httpClient.GetStringAsync(nugetApiUrl).Result;
                        dynamic packageInfo = Newtonsoft.Json.JsonConvert.DeserializeObject(json);

                        // Determine if we should only use stable versions for this package
                        bool useOnlyStable = stablePackages
                            || (isDevelopmentDependency && !packageId.StartsWith("nanoFramework.TestFramework"));

                        // Filter versions based on package type and mode
                        var versions = new List<string>();
                        foreach (var version in packageInfo.versions)
                        {
                            string versionStr = version.ToString();

                            if (useOnlyStable)
                            {
                                // Only include stable versions for:
                                // - stable mode
                                // - development dependencies (except nanoFramework.TestFramework)
                                if (!versionStr.Contains("-"))
                                {
                                    versions.Add(versionStr);
                                }
                            }
                            else
                            {
                                // Include all versions (stable + preview)
                                versions.Add(versionStr);
                            }
                        }

                        if (versions.Count == 0)
                        {
                            // No versions available, mark as no update
                            updateCache[(packageId, packageVersion)] = null;
                            continue;
                        }

                        // Grab latest version
                        var latestVersion = versions[versions.Count - 1];

                        // Use NuGet version comparison to properly handle semantic versioning
                        if (NuGetVersion.TryParse(packageVersion, out var currentVersion) &&
                            NuGetVersion.TryParse(latestVersion.ToString(), out var newestVersion))
                        {
                            if (currentVersion < newestVersion)
                            {
                                // Update available
                                updateCache[(packageId, packageVersion)] = newestVersion.ToString();
                                Console.WriteLine($"  ⬆️  {packageId}: {packageVersion} → {newestVersion}");
                            }
                            else
                            {
                                // Already up-to-date
                                updateCache[(packageId, packageVersion)] = null;
                            }
                        }
                        else
                        {
                            // Version parsing failed, mark as no update to be safe
                            updateCache[(packageId, packageVersion)] = null;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"WARNING: Failed to fetch version info for {packageId}. Exception: {ex.Message}");
                        // Mark as no update available in cache to avoid retrying
                        updateCache[(packageId, packageVersion)] = null;
                    }
                }
            }

            var updatesFound = updateCache.Count(kv => kv.Value != null);
            if (updatesFound > 0)
            {
                Console.WriteLine($"✅ Update cache built: {updatesFound} package(s) have updates available");
            }
            else
            {
                Console.WriteLine($"✅ Update cache built: All packages are up-to-date");
            }
            Console.WriteLine("::endgroup::");

            return updateCache;
        }

        private static List<string> CollectSolutions(string workingDirectory, string[] solutionsToCheck)
        {
            if (solutionsToCheck is null)
            {
                return Directory.GetFiles(workingDirectory, "*.sln", SearchOption.AllDirectories).ToList();
            }

            if (solutionsToCheck.Length == 1
                && solutionsToCheck[0] == "*.sln")
            {
                return Directory.GetFiles(workingDirectory, "*.sln", SearchOption.AllDirectories).ToList();
            }

            var listToReturn = new List<string>();

            foreach (var sln in solutionsToCheck.Where(s => s.EndsWith(".sln")))
            {
                var solutions = Directory.GetFiles(workingDirectory, $"{sln}", SearchOption.AllDirectories);

                if (solutions.Any())
                {
                    listToReturn.AddRange(solutions);
                }
                else
                {
                    Console.WriteLine($"::error::❌ Solution not found: {sln}");
                    Environment.Exit(1);
                }
            }

            return listToReturn;
        }

        private static void UpdateProject(string projectToUpdate, string repositoryPath, ref int updateCount, ref StringBuilder commitMessage)
        {
            string updateResult = "";

            // perform NuGet update
            if (!RunNugetCLI(
                "update",
                $"\"{projectToUpdate}\" {repositoryPath} -FileConflictAction Overwrite",
                true,
                ref updateResult))
            {
                Environment.Exit(1);
            }

            string updateRegex = "(?:Successfully installed \\')(?'package'.+)(?:' to )";

            var updateOutcome = Regex.Matches(updateResult, updateRegex);

            if (updateOutcome.Count > 0)
            {
                foreach (Match packageUpdatedInfo in updateOutcome)
                {
                    string updateMessage = $"Bumps {packageUpdatedInfo.Groups["package"].Value.Replace(" ", " to ")}</br>";

                    // append to commit message, if not already reported
                    if (!commitMessage.ToString().Contains(updateMessage))
                    {
                        commitMessage.Append(updateMessage);

                        updateCount++;
                    }
                }
            }
        }

        private static PrCreationOutcome CretePRWithUpdate(
            string branchToPr,
            string libraryName,
            string commitMessage,
            string newBranchName,
            string prTitle,
            string repoOwner)
        {
            try
            {
                // check if there is already a PR with these updates
                var openPRs = _octokitClient.PullRequest.GetAllForRepository(repoOwner, libraryName, new PullRequestRequest() { State = ItemStateFilter.Open }).Result;

                var updatePRs = openPRs.Where(pr => pr.Title == prTitle && pr.Body == commitMessage);

                if (updatePRs.Any())
                {
                    Console.WriteLine($"\nℹ️  PR already exists: #{updatePRs.First().Number} - skipping creation");

                    return PrCreationOutcome.QuitSubmission;
                }

                // go ahead and create PR
                Console.WriteLine($"  📤 Creating PR: {repoOwner}/{libraryName}");
                Console.WriteLine($"      Head: {repoOwner}:{newBranchName}");
                Console.WriteLine($"      Base: {branchToPr}");

                // developer note: head must be in the format 'user:branch'
                var updatePr = _octokitClient.PullRequest.Create(
                    repoOwner,
                    libraryName,
                    new NewPullRequest(
                        prTitle,
                        $"{_workingRepoOwner}:{newBranchName}",
                        branchToPr)).Result;

                // update PR body
                var updatePrBody = new PullRequestUpdate() { Body = commitMessage };

                _ = _octokitClient.PullRequest.Update(
                    repoOwner,
                    libraryName,
                    updatePr.Number,
                    updatePrBody).Result;

                Console.WriteLine($"::notice::✅ Pull request created: #{updatePr.Number} in {repoOwner}/{libraryName}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"::error::❌ Failed to create PR: {ex.Message}:{ex.InnerException.Message}");
                Environment.Exit(1);
            }

            return PrCreationOutcome.Success;
        }

        private static bool ShouldAppendDefaultSourcesToNuGetCommand(string arguments)
        {
            // If we are using nugetconfig file and the file was found, we do not want to override it
            if (!string.IsNullOrEmpty(_nuGetConfigFile))
            {
                return false;
            }

            // If arguments already contains Source, do not override it
            if (arguments.Contains("-Source"))
            {
                return false;
            }

            return true;
        }

        private static bool RunNugetCLI(string command, string arguments)
        {
            string dummy = null;

            return RunNugetCLI(
                command,
                arguments,
                true,
                ref dummy);
        }

        private static bool RunNugetCLI(
            string command,
            string arguments,
            bool useNugetConfig,
            ref string output)
        {
            bool retry = true;

            if (ShouldAppendDefaultSourcesToNuGetCommand(arguments))
            {
                arguments += " -Source \"https://api.nuget.org/v3/index.json\"";
            }

            var cmd = Cli.Wrap(Path.Combine(AppContext.BaseDirectory, "NuGet.exe"))
                .WithArguments($" {command} {arguments} {(useNugetConfig && _nuGetConfigFile is not null ? _nuGetConfigFile : null)}")
                .WithValidation(CommandResultValidation.None);

            // setup cancellation token with a timeout of 2 minutes
            using var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromMinutes(2));

        execute:

            try
            {
                var cliResult = cmd.ExecuteBufferedAsync(cts.Token).GetAwaiter().GetResult();

                if (cliResult.ExitCode == 0)
                {
                    // grab output, if required
                    if (output is not null)
                    {
                        output = cliResult.StandardOutput;
                    }

                    return true;
                }

                Console.WriteLine($"ERROR: nuget CLI exited with code {cliResult.ExitCode}");
                Console.WriteLine($"{cliResult.StandardError}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: exception on 1st attempt to run nuget CLI {ex}");

                if (retry)
                {
                    // lower flag
                    retry = false;

                    // wait a sec here
                    Thread.Sleep(1000);

                    // try again
                    goto execute;
                }
            }

            return false;
        }

        private static bool RunGitCli(
            string arguments,
            string workingDirectory)
        {
            string dummy = null;

            return RunGitCli(
                arguments,
                ref dummy,
                workingDirectory);
        }

        private static bool RunGitCli(
            string arguments,
            ref string output,
            string workingDirectory)
        {
            bool retry = true;

            var cmd = Cli.Wrap("git")
                .WithArguments($"{arguments}")
                .WithWorkingDirectory(workingDirectory)
                .WithValidation(CommandResultValidation.None);

            // setup cancellation token with a timeout of 2 minutes
            using var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromMinutes(2));

        execute:

            try
            {
                var cliResult = cmd.ExecuteBufferedAsync(cts.Token).GetAwaiter().GetResult();

                if (cliResult.ExitCode == 0)
                {
                    // grab output, if required
                    if (output is not null)
                    {
                        output = cliResult.StandardOutput;
                    }

                    return true;
                }

                Console.WriteLine($"ERROR: git CLI exited with code {cliResult.ExitCode}");
                Console.WriteLine($"{cliResult.StandardError}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: exception on 1st attempt to run nuget CLI {ex}");

                if (retry)
                {
                    // lower flag
                    retry = false;

                    // wait a sec here
                    Thread.Sleep(1000);

                    // try again
                    goto execute;
                }
            }

            return false;
        }

        private enum PrCreationOutcome
        {
            /// <summary>
            /// Failed to create PR because of conditions or other transient conditions.
            /// </summary>
            Failed,

            /// <summary>
            /// Successfully created PR.
            /// </summary>
            Success,

            /// <summary>
            /// Quit PR submission because the same update was already there.
            /// </summary>
            QuitSubmission
        }
    }
}
