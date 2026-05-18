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
using Newtonsoft.Json.Linq;

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
        private static bool _isGitHubActions;
        private static bool _isAzureDevOps;

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
            // Detect running environment
            DetectEnvironment();

            // sanity check 
            if (!solutionsToCheck && !reposToUpdate)
            {
                WriteError($"❌ Missing required option: specify either '--solutions-to-check' or '--repos-to-update'");
                Environment.Exit(1);
            }

            // check if this is running on a git repo
            var gitRepo = CheckIfDirectoryIsGitRepo(workingDirectory);
            if (!gitRepo.Item1)
            {
                WriteError($"❌ Working directory is not a git repository");
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
                WriteError($"❌ Directory not found: {workingDirectory}");
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
                WriteError($"❌ Cannot specify both stable and preview packages simultaneously");
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
                    string gitHubToken = GetGitHubToken();
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
                WriteError($"❌ Failed to parse exclusion list. Use comma-separated solution names.");
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
                foreach (string repoName in args)
                {
                    // remove quotes, if any
                    string library = repoName.Replace("'", "");

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

                    string cloneCommand = CreateCloneCommand(cloneDepth, repoOwner, library, useGitTokenForClone, gitHubAuth);
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
            string gitRepo = string.Empty;
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
                string token = string.IsNullOrEmpty(gitHubAuth) ? GetGitHubToken() : gitHubAuth;
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
                WriteError($"❌ GITHUB_TOKEN environment variable not found");

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
                _isAzureDevOps = true;
                return RunningEnvironment.AzurePipelines;
            }

            // this variable it's only set when running on a GitHub action
            if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") is not null)
            {
                _isGitHubActions = true;
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
                WriteError($"❌ Unable to determine repository name from git remote");
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
                    WriteError($"❌ NuGet config file not found: {nugetConfig}");
                    Environment.Exit(1);
                }
            }

            string libraryName = GitHubHelper.GetLibNameFromRegexMatch(repoName);

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

            foreach (string sln in solutionFiles)
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
            foreach (string solutionFile in solutionFiles.Where(s => !_solutionsExclusionList.Contains(Path.GetFileNameWithoutExtension(s))))
            {
                Console.WriteLine();
                Console.WriteLine();
                WriteGroupStart("🔧 Processing solution");
                Console.WriteLine($"  📄 {Path.GetFileName(solutionFile)}");

                // look for nfproj
                string slnFileContent = File.ReadAllText(solutionFile);

                if (!slnFileContent.Contains(".nfproj"))
                {
                    Console.WriteLine($"  ⚠️  No .nfproj files found - skipping");
                    WriteGroupEnd();
                    continue;
                }

                // find part for SLN
                string solutionPath = Directory.GetParent(solutionFile).FullName;

                // find ALL packages.config files inside the solution projects
                string[] packageConfigs = Directory.GetFiles(solutionPath, "packages.config", SearchOption.AllDirectories);

                // perform NuGet restore
                Console.WriteLine();
                Console.WriteLine("  📥 Restoring NuGet packages...");

                if (!RunNugetCLI("restore", $"\"{solutionFile}\""))
                {
                    Environment.Exit(1);
                }

                Console.WriteLine($"  ✅ Found {packageConfigs.Length} packages.config file(s)");

                // Build package update cache once for the entire solution
                Dictionary<(string Id, string Version), string> packageUpdateCache = BuildPackageUpdateCache(packageConfigs, stablePackages);

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

                foreach (string packageConfigFile in packageConfigs)
                {
                    Console.WriteLine();
                    WriteGroupStart($"📝 Processing project: {Path.GetFileName(Path.GetDirectoryName(packageConfigFile))}");

                    // check if the project the packages.config belongs to it's in the solution 
                    string projectPathInSln = Path.GetRelativePath(solutionPath, Directory.GetParent(packageConfigFile).FullName);

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

                    Match match = Regex.Match(
                        slnFileContent,
                        $"(?> = \\\")(?'projectname'[a-zA-Z0-9_.-]+)(?>\\\", \\\"{projectPathInSln})(?'projectpath'[a-zA-Z0-9_.-]+.nfproj)(\\\")",
                        RegexOptions.IgnoreCase);

                    if (!match.Success)
                    {
                        Console.WriteLine($"  ⚠️  No matching project found - skipping");
                        WriteGroupEnd();
                        continue;
                    }

                    string projectToUpdate = Directory.GetFiles(solutionPath, match.Groups["projectpath"].Value, SearchOption.AllDirectories).FirstOrDefault();
                    string projectName = match.Groups["projectname"].Value;



                    // load packages.config 
                    PackagesConfigReader packageReader = new NuGet.Packaging.PackagesConfigReader(XDocument.Load(packageConfigFile));
                    IEnumerable<PackageReference> packageList = packageReader.GetPackages();

                    if (!packageList.Any())
                    {
                        // no packages to update
                        Console.WriteLine($"  ℹ️  No packages found - skipping");
                        WriteGroupEnd();

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

                        nuspecFileName = FindNuspecFile(solutionPath, projectToUpdate, projectName, libraryName);

                        if (nuspecFileName is null)
                        {
                            if (!nuspecNotFoundMessage.Contains(projectToUpdate))
                            {
                                WriteWarning($"⚠️  Can't find nuspec file matching project '{Path.GetFileNameWithoutExtension(projectToUpdate)}'");

                                // store project name, so the warning shows only once
                                nuspecNotFoundMessage += projectToUpdate;
                            }
                        }

                        // list packages to check
                        Console.WriteLine();
                        Console.WriteLine("  📦 Packages:");
                        foreach (PackageReference package in packageList)
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
                                (string packageName, string packageOriginVersion) cacheKey = (packageName, packageOriginVersion);

                                if (packageUpdateCache.TryGetValue(cacheKey, out string cachedVersion))
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

                            Match updateOutcome = Regex.Match(updateResult, updateRegex);

                            if (!updateOutcome.Success)
                            {
                                // check for no updates available message
                                if (!updateResult.Contains("There are no new updates available"))
                                {
                                    // something wrong happened!
                                    // output update message for the log
                                    WriteWarning($"⚠️  Unexpected update outcome:");
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
                                PackageReference packageToRefresh = packageReader.GetPackages()
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
                                WriteError($"🛑 Attempting to use alpha version of {packageName} - aborting");

                                // exit with error 
                                Environment.Exit(1);
                            }
                            else
                            {
                                // build update message
                                if (packageOriginVersion != packageTargetVersion)
                                {
                                    string updateMessage = $"Bumps {packageName} from {packageOriginVersion} to {packageTargetVersion}</br>";

                                    // add to the HashSet to ensure uniqueness; only count and append if not already seen
                                    if (updateMessages.Add(updateMessage))
                                    {
                                        commitMessage.Append(updateMessage);
                                        updateCount++;
                                    }

                                    Console.WriteLine($"  ⬆️  {packageName} {packageOriginVersion} → {packageTargetVersion}");
                                    Console.WriteLine($"    📋 Project file: {Path.GetFileName(projectToUpdate)}");
                                }

                                // if this is the Test Framework, need to update the nfproj file too
                                if (packageName == "nanoFramework.TestFramework")
                                {
                                    // read nfproj file
                                    string nfprojFileContent = File.ReadAllText(projectToUpdate);

                                    string updatedProjContent = Regex.Replace(nfprojFileContent, $"(?<=packages\\\\nanoFramework\\.TestFramework\\.)(?'version'\\d+\\.\\d+\\.\\d+)(?=\\\\build\\\\)", packageTargetVersion, RegexOptions.ExplicitCapture);

                                    File.WriteAllText(projectToUpdate, updatedProjContent);
                                }

                                // Remove the <Private> elements NuGet adds to the project file
                                // This could be async be the tool isn't using that so I'll skip it for now
                                var projectDocument = XDocument.Load(projectToUpdate);
                                var referenceElements = projectDocument.Root?.Descendants().Where(x => x.Name.LocalName == "Reference").ToList();

                                foreach (XElement referenceElement in referenceElements)
                                {
                                    var privateElements = referenceElement.Descendants().Where(x => x.Name.LocalName == "Private").ToList();
                                    foreach (var privateElement in privateElements)
                                    {
                                        privateElement.Remove();
                                    }
                                }

                                // Save project file and ensure stream is fully closed before proceeding
                                using (FileStream projectFile = File.Create(projectToUpdate))
                                {
                                    projectDocument.Save(projectFile, SaveOptions.None);
                                }

                                // load nuspec file content, if there is a nuspec file to update
                                if (nuspecFileName is not null)
                                {
                                    Console.WriteLine($"      📋 Nuspec: {Path.GetFileName(nuspecFileName)}");

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
                                        string currentVersion = dependency.Attributes["version"]?.Value;

                                        if (currentVersion != packageTargetVersion)
                                        {
                                            dependency.Attributes["version"].Value = packageTargetVersion;

                                            Console.WriteLine($"        ✏️  Updating nuspec: '{dependency.Attributes["id"].Value}' ---> {packageTargetVersion}");

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
                                        Console.WriteLine($"      ⚠️  Dependency '{packageName}' not found in nuspec (may not be required)");
                                    }

                                }
                            }
                        }
                    }
                }
            }

            WriteGroupEnd();

            // check if any packages where updated
            if (updateCount == 0)
            {
                Console.WriteLine();
                Console.WriteLine("ℹ️  No package updates were applied");
                return;
            }

            // sanity check for no nuspecs found
            if (nuspecUpdatedCounter == 0)
            {
                Console.WriteLine();
                WriteWarning($"⚠️  No nuspec files were updated - this may need review");
            }

            Console.WriteLine();
            WriteNotice($"✅ Successfully updated {updateCount} package(s)");

            // need this line so nfbot flags the PR appropriately
            commitMessage.Append("\n[version update]\n\n");

            // better add this warning line               
            commitMessage.Append("### :warning: This is an automated update. :warning:\n");

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
                    WriteWarning($"⚠️  Failed to checkout '{_baseBranch}' - manual cleanup may be needed");
                }

                // delete local and remote branches
                Console.WriteLine($"  🗑️  Deleting branch: {newBranchName}");

                // local (don't care about outcome
                _ = RunGitCli($"branch -D {newBranchName}", workingDirectory);

                // remote
                if (!RunGitCli($"push origin --delete {newBranchName}", workingDirectory))
                {
                    WriteWarning($"⚠️  Failed to delete remote branch '{newBranchName}' - manual cleanup may be needed");
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
            foreach (string packageConfig in packageConfigs)
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
            Console.WriteLine();
            WriteGroupStart($"📦 Building update cache for {uniquePackages.Count} unique package(s)");

            // Check each unique package and cache the result
            foreach (KeyValuePair<(string Id, string Version), bool> packageEntry in uniquePackages)
            {
                (string packageId, string packageVersion) = packageEntry.Key;
                bool isDevelopmentDependency = packageEntry.Value;

                try
                {
                    // Determine if we should include prerelease versions for this package
                    bool useOnlyStable = stablePackages
                        || (isDevelopmentDependency && !packageId.StartsWith("nanoFramework.TestFramework"));

                    var latestVersion = GetLatestListedVersion(packageId, !useOnlyStable);

                    if (latestVersion is not null
                        && NuGetVersion.TryParse(packageVersion, out NuGetVersion currentVersion)
                        && currentVersion < latestVersion)
                    {
                        updateCache[(packageId, packageVersion)] = latestVersion.ToString();
                        Console.WriteLine($"  ⬆️  {packageId}: {packageVersion} → {latestVersion}");
                    }
                    else
                    {
                        updateCache[(packageId, packageVersion)] = null;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"WARNING: Failed to fetch version info for {packageId}. Exception: {ex.Message}");
                    updateCache[(packageId, packageVersion)] = null;
                }
            }

            int updatesFound = updateCache.Count(kv => kv.Value != null);
            if (updatesFound > 0)
            {
                Console.WriteLine($"✅ Update cache built: {updatesFound} package(s) have updates available");
            }
            else
            {
                Console.WriteLine($"✅ Update cache built: All packages are up-to-date");
            }
            WriteGroupEnd();

            return updateCache;
        }

        private static NuGetVersion GetLatestListedVersion(string packageId, bool includePrerelease)
        {
            using var httpClient = new HttpClient();
            string url = $"https://api.nuget.org/v3/search?q=packageid:{packageId}&prerelease={includePrerelease.ToString().ToLowerInvariant()}&semVerLevel=2.0.0";
            string json = httpClient.GetStringAsync(url).Result;
            var data = JObject.Parse(json)["data"] as JArray;

            if (data is null || data.Count == 0)
            {
                return null;
            }

            string versionStr = data[0]["version"]?.ToString();
            return NuGetVersion.TryParse(versionStr, out var version) ? version : null;
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

            foreach (string sln in solutionsToCheck.Where(s => s.EndsWith(".sln")))
            {
                string[] solutions = Directory.GetFiles(workingDirectory, $"{sln}", SearchOption.AllDirectories);

                if (solutions.Any())
                {
                    listToReturn.AddRange(solutions);
                }
                else
                {
                    WriteError($"❌ Solution not found: {sln}");
                    Environment.Exit(1);
                }
            }

            return listToReturn;
        }

        private static string FindNuspecFile(string solutionPath, string projectToUpdate, string projectName, string libraryName)
        {
            string projectBaseName = Path.GetFileNameWithoutExtension(projectToUpdate);

            // Strategy 1: exact project file name match
            // Strategy 2: with nanoFramework. prefix
            // Strategy 3: project name match
            // Strategy 4: project name with nanoFramework. prefix
            // Strategy 5: AMQPLite special case
            string[] namesToTry =
            [
                $"{projectBaseName}.nuspec",
                $"nanoFramework.{projectBaseName}.nuspec",
                $"{projectName}.nuspec",
                $"nanoFramework.{projectName}.nuspec",
            ];

            foreach (string pattern in namesToTry)
            {
                string[] candidates = Directory.GetFiles(solutionPath, pattern, SearchOption.AllDirectories);
                if (candidates.Length > 0)
                {
                    return candidates[0];
                }
            }

            // AMQPLite special case
            if (libraryName == AMQPLiteLibraryName)
            {
                string amqpPattern = $"{projectName.Replace("Amqp.Micro", "AMQPNetMicro").Replace("Amqp.", "AMQPNetLite.")}.nuspec";
                string[] amqpCandidates = Directory.GetFiles(solutionPath, amqpPattern, SearchOption.AllDirectories);
                if (amqpCandidates.Length > 0)
                {
                    return amqpCandidates[0];
                }
            }

            // Strategy 6: content-based scan - look at every nuspec file in the solution and its parent
            // and match <id> element against project base name or project name
            var searchRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                solutionPath,
                Directory.GetParent(solutionPath)?.FullName
            };
            searchRoots.RemoveWhere(r => r is null);

            var allNuspecs = searchRoots
                .SelectMany(root => Directory.GetFiles(root, "*.nuspec", SearchOption.AllDirectories))
                .Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (string nuspecFile in allNuspecs)
            {
                try
                {
                    var doc = XDocument.Load(nuspecFile);
                    XNamespace ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;
                    string id = doc.Root
                        ?.Element(ns + "metadata")
                        ?.Element(ns + "id")
                        ?.Value;

                    if (id is null)
                    {
                        continue;
                    }

                    // match against project base name or project name (with or without nanoFramework. prefix)
                    if (string.Equals(id, projectBaseName, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(id, projectName, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(id, $"nanoFramework.{projectBaseName}", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(id, $"nanoFramework.{projectName}", StringComparison.OrdinalIgnoreCase))
                    {
                        return nuspecFile;
                    }
                }
                catch
                {
                    // skip malformed nuspec files
                }
            }

            return null;
        }

        private static void UpdateProject(string projectToUpdate, string repositoryPath, ref int updateCount, ref StringBuilder commitMessage)
        {
            string updateResult = "";

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

                    if (!commitMessage.ToString().Contains(updateMessage))
                    {
                        commitMessage.Append(updateMessage);

                        updateCount++;
                    }
                }
            }
        }

        /// <summary>
        /// Creates a Pull Request with the update.
        /// </summary>
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

                WriteNotice($"✅ Pull request created: #{updatePr.Number} in {repoOwner}/{libraryName}");
            }
            catch (Exception ex)
            {
                WriteError($"❌ Failed to create PR: {ex.Message}:{ex.InnerException.Message}");
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

        private static void DetectEnvironment()
        {
            // Check for GitHub Actions environment
            _isGitHubActions = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"));

            // Check for Azure Pipelines environment
            _isAzureDevOps = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SYSTEM_TEAMFOUNDATIONCOLLECTIONURI"));
        }

        private static void WriteGroupStart(string title)
        {
            if (_isGitHubActions)
            {
                Console.WriteLine($"::group::{title}");
            }
            else if (_isAzureDevOps)
            {
                Console.WriteLine($"##[group]{title}");
            }
            else
            {
                Console.WriteLine($"\n{title}");
            }
        }

        private static void WriteGroupEnd()
        {
            if (_isGitHubActions)
            {
                Console.WriteLine("::endgroup::");
            }
            else if (_isAzureDevOps)
            {
                Console.WriteLine("##[endgroup]");
            }
        }

        private static void WriteError(string message)
        {
            if (_isGitHubActions)
            {
                Console.WriteLine($"::error::{message}");
            }
            else if (_isAzureDevOps)
            {
                Console.WriteLine($"##vso[task.logissue type=error]{message}");
            }
            else
            {
                Console.WriteLine($"ERROR: {message}");
            }
        }

        private static void WriteWarning(string message)
        {
            if (_isGitHubActions)
            {
                Console.WriteLine($"::warning::{message}");
            }
            else if (_isAzureDevOps)
            {
                Console.WriteLine($"##vso[task.logissue type=warning]{message}");
            }
            else
            {
                Console.WriteLine($"WARNING: {message}");
            }
        }

        private static void WriteNotice(string message)
        {
            if (_isGitHubActions)
            {
                Console.WriteLine($"::notice::{message}");
            }
            else if (_isAzureDevOps)
            {
                Console.WriteLine($"##vso[task.logissue type=warning]{message}");
            }
            else
            {
                Console.WriteLine($"NOTICE: {message}");
            }
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
