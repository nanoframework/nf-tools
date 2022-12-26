//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using CliWrap;
using CliWrap.Buffered;
using Microsoft.Extensions.Configuration;
using NuGet.Packaging;
using Octokit;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using System.Xml.Linq;

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
        /// <param name="args">List of Solutions files to check or repositories to update. According to option specified with <paramref name="solutionsToCheck"/> or <paramref name="reposToUpdate"/>.</param>
        static void Main(
            string workingDirectory = null,
            bool stablePackages = true,
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
            string[] args = null)
        {
            // sanity check 
            if (!solutionsToCheck && !reposToUpdate)
            {
                Console.WriteLine($"ERROR: need to specify update options. Either '--solutions-to-check' or '--repos-to-update'.");
                Environment.Exit(1);
            }

            // check if this is running on a git repo
            var gitRepo = CheckIfDirectoryIsGitRepo(workingDirectory);
            if (!gitRepo.Item1)
            {
                Console.WriteLine($"ERROR: working directory is not a git repository");
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
                Console.WriteLine($"ERROR: directory '{workingDirectory}' does not exist!");
                Environment.Exit(1);
            }

            if (!stablePackages && !previewPackages)
            {
                Console.WriteLine($"ERROR: can't specify stable and preview NuGet packages simultaneously!");
                Environment.Exit(1);
            }

            if (stablePackages && previewPackages)
            {
                Console.WriteLine($"ERROR: can't specify stable and preview NuGet packages simultaneously!");
                Environment.Exit(1);
            }

            _gitHubUser = gitHubUser;
            _runningEnvironment = GetRunningEnvironment();
            Console.WriteLine($"Running on {_runningEnvironment.ToString()} environment");
            Console.WriteLine($"Branch to submit PR: {branchToPr}");

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

            // store exclusion list
            try
            {
                _solutionsExclusionList = exclusionList is not null ? exclusionList.Split(",") : Array.Empty<string>();
            }
            catch
            {
                Console.WriteLine($"ERROR: exception parsing {nameof(exclusionList)}. Make sure to follow the instructions and pass a comma separated list of solution names.");
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
                    Console.WriteLine("*******************************");
                    Console.WriteLine($"Updating {library}");

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
                    Console.WriteLine($"INFO: cloning '{library}' repository");

                    var cloneCommand = CreateCloneCommand(cloneDepth, repoOwner, library, useGitTokenForClone, gitHubAuth);
                    if (!RunGitCli(cloneCommand, workingDirectory))
                    {
                        resultCollection.Add(Tuple.Create(false, $"{repoName} ERROR: unable to clone"));
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
                        resultCollection.Add(Tuple.Create(false, $"{repoName} ERROR: unable to checkout to {_baseBranch}"));
                        continue;
                    }

                    // get git repo name for each library and repo owner
                    var gitRepoInternal = CheckIfDirectoryIsGitRepo(workingDirectory);
                    if (!gitRepoInternal.Item1)
                    {
                        resultCollection.Add(Tuple.Create(false, $"{repoName} ERROR: working directory is not a git repository"));
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
                        sln);

                    resultCollection.Add(Tuple.Create(true, $"{repoName} SUCCESS: Update completed"));
                }

                // Print result
                Console.WriteLine($"Update result");
                foreach (var result in resultCollection)
                {
                    Console.WriteLine($"{result.Item2}");
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
                Console.WriteLine($"ERROR: environment variable with GitHub token not found");

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
                        string[] solutionsToCheck)
        {
            string releaseType = stablePackages ? "stable" : previewPackages ? "preview" : "?????";

            Console.WriteLine($"Working directory is: '{workingDirectory ?? "null"}'");

            Console.WriteLine($"Using {releaseType} NuGet packages.");

            if (solutionsToCheck is not null)
            {
                Console.WriteLine($"Target solution(s): '{string.Join(";", solutionsToCheck)}'");
            }
            else
            {
                Console.WriteLine("Targeting every solution in the repository.");
            }

            var repoName = GitHubHelper.GetRepoNameFromInputString(gitRepo);
            if (!repoName.Success)
            {
                Console.WriteLine($"ERROR: couldn't determine repository name.");
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
                else
                {
                    Console.WriteLine($"INFO: couldn't find :{nugetConfig}");
                    Environment.Exit(1);
                }
            }

            var libraryName = GitHubHelper.GetLibNameFromRegexMatch(repoName);

            Console.WriteLine($"Repository is: '{libraryName ?? "null"}'");

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
            int nuspecCounter = 0;
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
            Console.WriteLine("");
            Console.WriteLine($"Solutions to check are:");

            foreach (var sln in solutionFiles)
            {
                Console.Write($"{Path.GetRelativePath(workingDirectory, sln)}");

                // check if this on is in the exclusion list
                if (_solutionsExclusionList.Contains(Path.GetFileNameWithoutExtension(sln)))
                {
                    Console.WriteLine(" *** EXCLUDED ***");
                }
                else
                {
                    Console.WriteLine("");
                }
            }

            // go through each solution (filter out the ones in the exclusion list)
            foreach (var solutionFile in solutionFiles.Where(s => !_solutionsExclusionList.Contains(Path.GetFileNameWithoutExtension(s))))
            {
                Console.WriteLine();
                Console.WriteLine();
                Console.WriteLine("************");
                Console.WriteLine($"Processing solution '{solutionFile}'");

                // look for nfproj
                var slnFileContent = File.ReadAllText(solutionFile);

                if (!slnFileContent.Contains(".nfproj"))
                {
                    Console.WriteLine($"INFO: solution file '{solutionFile}' doesn't have any nfproj file. *** SKIPPING ***");
                    continue;
                }

                // find part for SLN
                var solutionPath = Directory.GetParent(solutionFile).FullName;

                // perform NuGet restore
                Console.WriteLine();
                Console.WriteLine($"INFO: restoring solution...");

                if (!RunNugetCLI("restore", $"\"{solutionFile}\""))
                {
                    Environment.Exit(1);
                }

                // find ALL packages.config files inside the solution projects
                var packageConfigs = Directory.GetFiles(solutionPath, "packages.config", SearchOption.AllDirectories);

                Console.WriteLine($"Found {packageConfigs.Length} packages.config files...");

                // specify repository path, just in case
                string repositoryPath = $"-RepositoryPath {Directory.GetParent(solutionFile).FullName}\\packages";

                foreach (var packageConfigFile in packageConfigs)
                {
                    Console.WriteLine();
                    Console.WriteLine();
                    Console.WriteLine($"INFO: working file is {packageConfigFile}");

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

                    var match = Regex.Match(slnFileContent, $"(?> = \\\")(?'projectname'[a-zA-Z0-9_.-]+)(?>\\\", \\\"{projectPathInSln})(?'projectpath'[a-zA-Z0-9_.-]+.nfproj)(\\\")");
                    if (!match.Success)
                    {
                        Console.WriteLine($"INFO: couldn't find a project matching this packages.config. *** SKIPPING ***.");
                        continue;
                    }

                    var projectToUpdate = Directory.GetFiles(solutionPath, match.Groups["projectpath"].Value, SearchOption.AllDirectories).FirstOrDefault();
                    var projectName = match.Groups["projectname"].Value;

                    Console.WriteLine();
                    Console.WriteLine($"Updating project '{Path.GetFileNameWithoutExtension(projectToUpdate)}'");

                    // load packages.config 
                    var packageReader = new NuGet.Packaging.PackagesConfigReader(XDocument.Load(packageConfigFile));
                    var packageList = packageReader.GetPackages();

                    if (!packageList.Any())
                    {
                        // no packages to update
                        Console.WriteLine();
                        Console.WriteLine($"INFO: no packages found to update. *** SKIPPING ***.");

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
                                Console.WriteLine();
                                Console.WriteLine($"nuspec file to update is '{Path.GetRelativePath(solutionPath, nuspecFileName)}'");
                            }
                        }

                        // list packages to check
                        Console.WriteLine();
                        Console.WriteLine();
                        Console.WriteLine("-- NuGet packages to update --");
                        foreach (var package in packageList)
                        {
                            Console.WriteLine($"{package.PackageIdentity}");
                        }
                        Console.WriteLine("--------- end of list --------");
                        Console.WriteLine();
                        Console.WriteLine();

                        // check all packages
                        foreach (PackageReference package in packageList)
                        {
                            // get package name and target version
                            string packageName = package.PackageIdentity.Id;
                            string packageOriginVersion = package.PackageIdentity.Version.ToNormalizedString();

                            Console.WriteLine();
                            Console.WriteLine($"Checking updates for {packageName}.{packageOriginVersion}");

                            string updateResult = "";
                            string updateParameters;

                            if (stablePackages && !packageName.StartsWith("UnitsNet."))
                            {
                                // don't allow prerelease for release, main branches and UnitsNet packages
                                // go with our Azure feed
                                updateParameters = $"{projectToUpdate} -Id {packageName} {repositoryPath} -FileConflictAction Overwrite";
                            }
                            else if (packageName.StartsWith("UnitsNet."))
                            {
                                // grab latest version from NuGet
                                string unitsNetPackageInfo = "";
                                if (!RunNugetCLI(
                                    "search",
                                    $" {packageName} -Verbosity quiet ",
                                    ref unitsNetPackageInfo))
                                {
                                    Environment.Exit(1);
                                }

                                var unitsNetVersion = unitsNetPackageInfo.Split("\r\n")[2].Split('|')[1].Trim();

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
                                    Console.WriteLine();
                                    Console.WriteLine(
                                        $"INFO: unexpected update outcome {Environment.NewLine}>>>>>>>{Environment.NewLine}{updateResult}{Environment.NewLine}>>>>>>>{Environment.NewLine}");
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
                                    Console.WriteLine();
                                    Console.WriteLine($"INFO: {packageName} not found in any nuget source. Skipping update.");
                                    Console.WriteLine();
                                    continue;
                                }

                                // grab target version
                                packageTargetVersion =
                                    packageToRefresh.PackageIdentity.Version.ToNormalizedString();

                                if (packageOriginVersion == packageTargetVersion)
                                {
                                    Console.WriteLine($"No newer version");
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
                                Console.WriteLine($"Skipping update of {packageName} because it's trying to use an alpha version!");

                                // exit with error 
                                Environment.Exit(1);
                            }
                            else
                            {
                                // build commit message
                                if (packageOriginVersion != packageTargetVersion)
                                {
                                    string updateMessage = $"Bumps {packageName} from {packageOriginVersion} to {packageTargetVersion}</br>";

                                    // append to commit message, if not already reported
                                    if (!commitMessage.ToString().Contains(updateMessage))
                                    {
                                        commitMessage.Append(updateMessage);

                                        // bump counter
                                        updateCount++;
                                    }

                                    Console.WriteLine($"Bumping {packageName} from {packageOriginVersion} to {packageTargetVersion}.");
                                }

                                // if this is the Test Framework, need to update the nfproj file too
                                if (packageName == "nanoFramework.TestFramework")
                                {
                                    // read nfproj file
                                    var nfprojFileContent = File.ReadAllText(projectToUpdate);

                                    var updatedProjContent = Regex.Replace(nfprojFileContent, $"(?<=packages\\\\nanoFramework\\.TestFramework\\.)(?'version'\\d+\\.\\d+\\.\\d+)(?=\\\\build\\\\)", packageTargetVersion, RegexOptions.ExplicitCapture);

                                    File.WriteAllText(projectToUpdate, updatedProjContent);
                                }

                                // load nuspec file content, if there is a nuspec file to update
                                if (nuspecFileName is not null)
                                {
                                    var nuspecFile = new XmlDocument();
                                    nuspecFile.Load(nuspecFileName);

                                    XmlNamespaceManager nsmgr = new XmlNamespaceManager(nuspecFile.NameTable);
                                    nsmgr.AddNamespace("package", "http://schemas.microsoft.com/packaging/2012/06/nuspec.xsd");

                                    // update version, if this dependency is listed
                                    var dependency = nuspecFile.SelectSingleNode($"descendant::package:dependency[@id='{packageName}']", nsmgr);
                                    if (dependency is not null)
                                    {
                                        dependency.Attributes["version"].Value = packageTargetVersion;

                                        Console.WriteLine($"Updating nuspec: '{dependency.Attributes["id"].Value}' ---> {packageTargetVersion}");

                                        // save back changes
                                        // developer note: using stream writer instead of Save(to file name) because of random issues with updated content
                                        // not being saved thus causing bogus updates on the nuspec content
                                        using StreamWriter nuspecStreamWriter = File.CreateText(nuspecFileName);
                                        nuspecFile.Save(nuspecStreamWriter);
                                        nuspecStreamWriter.Flush();
                                        nuspecStreamWriter.Close();
                                    }
                                    else
                                    {
                                        Console.WriteLine();
                                        Console.WriteLine($"INFO: {packageName} not listed in '{Path.GetRelativePath(solutionPath, nuspecFileName)}'");
                                    }

                                    // bump counters
                                    nuspecCounter++;
                                }
                            }
                        }
                    }
                }
            }

            // check if any packages where updated
            if (updateCount == 0)
            {
                Console.WriteLine();
                Console.WriteLine("INFO: No packages found to update...");
                return;
            }

            // sanity check for no nuspecs found
            if (nuspecCounter == 0)
            {
                Console.WriteLine();
                Console.WriteLine($"*** WARNING: No nuspecs files updated... Maybe worth checking ***");
            }

            Console.WriteLine();
            Console.WriteLine($"INFO: {updateCount} packages updated");

            // need this line so nfbot flags the PR appropriately
            commitMessage.Append("\n[version update]\n\n");

            // better add this warning line               
            commitMessage.Append("### :warning: This is an automated update. :warning:\n");

            Console.WriteLine($"INFO: generating PR information");

            Console.WriteLine($"INFO: creating branch '{newBranchName}' to perform updates");

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

            Console.WriteLine($"INFO: adding changes");

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
                Console.WriteLine($"INFO: checking out '{_baseBranch}'.");
                if (!RunGitCli($"checkout {_baseBranch}", workingDirectory))
                {
                    Console.WriteLine($"ERROR: ⚠️ failed to checkout '{_baseBranch}' when deleting '{branchToPr}'! Need to delete branch manually.");
                    Environment.Exit(1);
                }

                // delete branch with "force"
                Console.WriteLine($"INFO: deleting '{newBranchName}' branch.");
                if (!RunGitCli($"branch -D {newBranchName}", workingDirectory))
                {
                    Console.WriteLine($"ERROR: ⚠️ failed to delete '{newBranchName}'! Need to delete branch manually.");
                    Environment.Exit(1);
                }
            }
        }

        private static List<string> CollectSolutions(string workingDirectory, string[] solutionsToCheck)
        {
            if (solutionsToCheck is null)
            {
                return Directory.GetFiles(workingDirectory, "*.sln", SearchOption.AllDirectories).ToList();
            }

            if (solutionsToCheck.Length == 1 & solutionsToCheck[0] == "*.sln")
            {
                return Directory.GetFiles(workingDirectory, "*.sln", SearchOption.AllDirectories).ToList();
            }

            var listToReturn = new List<string>();
            foreach (var sln in solutionsToCheck)
            {
                var solutions = Directory.GetFiles(workingDirectory, $"{sln}", SearchOption.AllDirectories);

                if (solutions.Any())
                {
                    listToReturn.AddRange(solutions);
                }
                else
                {
                    Console.WriteLine($"ERROR: ⚠️ couldn't find solution '{sln}'! Please check solution name.");
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

                var updatePRs = openPRs.Where(pr => pr.User.Login == _gitHubUser && pr.Title == prTitle && pr.Body == commitMessage);

                if (updatePRs.Any())
                {
                    Console.WriteLine($"INFO: found existing PR with the same update: {repoOwner}/{libraryName}/pull/{updatePRs.First().Number}. Skipping PR creation.");

                    return PrCreationOutcome.QuitSubmission;
                }

                // go ahead and create PR
                Console.WriteLine($"INFO: creating PR against {repoOwner}/{libraryName}, head: {repoOwner}:{newBranchName}, base:{branchToPr}");

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

                Console.WriteLine($"INFO: created PR #{updatePr.Number} @ {repoOwner}/{libraryName}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: exception when submitting PR {ex.Message}:{ex.InnerException.Message}");
                Environment.Exit(1);
            }

            return PrCreationOutcome.Success;
        }

        private static bool ShouldAppendDefaultSourcesToNuGetCommand(bool useNuGetConfig, string arguments)
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
                ref dummy);
        }

        private static bool RunNugetCLI(
            string command,
            string arguments,
            ref string output)
        {
            if (ShouldAppendDefaultSourcesToNuGetCommand(_nuGetConfigFile is not null, arguments))
            {
                arguments += " -Source \"https://api.nuget.org/v3/index.json\"";
            }

            var cmd = Cli.Wrap(Path.Combine(AppContext.BaseDirectory, "NuGet.exe"))
                .WithArguments($" {command} {arguments} {(_nuGetConfigFile is not null ? _nuGetConfigFile : null)}")
                .WithValidation(CommandResultValidation.None);

            // setup cancellation token with a timeout of 2 minutes
            using var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromMinutes(2));

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
            var cmd = Cli.Wrap("git")
                .WithArguments($"{arguments}")
                .WithWorkingDirectory(workingDirectory)
                .WithValidation(CommandResultValidation.None);

            // setup cancellation token with a timeout of 2 minutes
            using var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromMinutes(2));

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
