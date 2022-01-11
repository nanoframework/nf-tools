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
        private static string _nuGetConfigFile;

        static readonly GitHubClient _octokitClient = new(new ProductHeaderValue("nanodu"));
        private static RunningEnvironment _runningEnvironment;
        private static string _gitHubAuth;
        private static string[] _solutionsExclusionList;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="workingDirectory">Working directory. Required when updating a single repository.</param>
        /// <param name="stablePackages">Use stable NuGet package versions.</param>
        /// <param name="previewPackages">Use preview NuGet package versions.</param>
        /// <param name="solutionsToCheck">List of Solution(s) to update in the <paramref name="workingDirectory"/> directory.</param>
        /// <param name="reposToUpdate">List of repository(es) to update.</param>
        /// <param name="exclusionList">List of solution names to exclude from the update. Comma separated, name only.</param>
        /// <param name="args">List of Solutions files to check or repositories to update. According to option specified with <paramref name="solutionsToCheck"/> or <paramref name="reposToUpdate"/>.</param>
        static void Main(
            string workingDirectory = null,
            bool stablePackages = false,
            bool previewPackages = true,
            bool solutionsToCheck = false,
            bool reposToUpdate = false,
            string exclusionList = null,
            string[] args = null)
        {
            // sanity check 
            if (!solutionsToCheck && !reposToUpdate)
            {
                Console.WriteLine($"ERROR: need to specify update options. Either '--solutions-to-check' or '--repos-to-update'.");
                Environment.Exit(1);
            }

            // check build environment
            _runningEnvironment = RunningEnvironment.Other;

            if (Environment.GetEnvironmentVariable("Agent_HomeDirectory") is not null
                && Environment.GetEnvironmentVariable("Build_BuildNumber") is not null)
            {
                // these variables are only available on Azure Pipelines build
                _runningEnvironment = RunningEnvironment.AzurePipelines;
                Console.WriteLine($"Running in an Azure Pipeline");
            }
            else if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") is not null)
            {
                // this variable it's only set when running on a GitHub action
                _runningEnvironment = RunningEnvironment.GitHubAction;
                Console.WriteLine($"Running in a GitHub Action");
            }
            else
            {
                Console.WriteLine($"Running at _another_ machine");
            }

#if DEBUG
            var config = new ConfigurationBuilder()
            .AddUserSecrets<Program>()
            .Build();

            _octokitClient.Credentials = new Octokit.Credentials(config.GetSection("GITHUB_TOKEN").Value);

            // compute authorization header in format "AUTHORIZATION: basic 'encoded token'"
            _gitHubAuth = $"basic {Convert.ToBase64String(Encoding.UTF8.GetBytes($"nfbot:{config.GetSection("GITHUB_TOKEN").Value}"))}";
#else
            // setup git stuff
            RunGitCli("config --global gc.auto 0", "");
            RunGitCli("config --global user.name nfbot", "");
            RunGitCli("config --global user.email nanoframework@outlook.com", "");
            RunGitCli("config --global core.autocrlf true", "");

            // setup OctoKit authentication
            if (Environment.GetEnvironmentVariable("GITHUB_TOKEN") is null)
            {
                Console.WriteLine($"ERROR: environment variable with GitHub token not found");

                // exit with error
                Environment.Exit(1);
            }

            _octokitClient.Credentials = new Octokit.Credentials(Environment.GetEnvironmentVariable("GITHUB_TOKEN"));

            // compute authorization header in format "AUTHORIZATION: basic 'encoded token'"
            _gitHubAuth = $"basic {Convert.ToBase64String(Encoding.UTF8.GetBytes($"nfbot:{Environment.GetEnvironmentVariable("GITHUB_TOKEN")}"))}";
#endif

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

            // choose work-flow
            if (solutionsToCheck)
            {
                // this is updating solution(s) on a repository

                // go for the library update
                UpdateLibrary(
                    workingDirectory,
                    stablePackages,
                    previewPackages,
                    args);
            }
            else
            {
                // because it can take sometime for the package to become available on the NuGet providers
                // need to hang here for 1 minute
                Console.WriteLine("Waiting 1 minute to let package process flow in Azure Artifacts feed...");

                Thread.Sleep((int)TimeSpan.FromMinutes(1).TotalMilliseconds);

                if (_runningEnvironment == RunningEnvironment.AzurePipelines)
                {
                    // need this to remove definition of redirect stdErr (only on Azure Pipelines image fo VS2019)
                    Environment.SetEnvironmentVariable("GIT_REDIRECT_STDERR", "2>&1");
                }

                foreach (var repoName in args)
                {
                    // remove quotes, if any
                    var library = repoName.Replace("'", "");

                    string baseBranch = "develop";

                    Console.WriteLine();
                    Console.WriteLine("*******************************");
                    Console.WriteLine($"Updating {library}");

#if DEBUG
                    // working directory is user Temporary directory
                    workingDirectory = Path.GetTempPath();
#else
                    // working directory is Agent Temporary directory
                    workingDirectory = Environment.GetEnvironmentVariable("Agent_TempDirectory");
#endif

                    Console.WriteLine();
                    Console.WriteLine($"INFO: cloning '{library}' repository");

                    if (!RunGitCli($"clone --depth 1 https://github.com/nanoframework/{library} {library}", workingDirectory))
                    {
                        Environment.Exit(1);
                    }

                    workingDirectory = Path.Combine(workingDirectory, library);

                    // check for special repos that have sources on different location
                    if (library == "amqpnetlite")
                    {
                        args = Directory.GetFiles(
                            workingDirectory,
                            "amqp-nanoFramework.sln",
                            SearchOption.TopDirectoryOnly).Select(n => Path.GetFileName(n)).ToArray();

                        // CD-CI branch is not 'develop'
                        baseBranch = "nanoframework-dev";
                    }
                    else
                    {
                        args = Directory.GetFiles(
                            workingDirectory,
                            "*.sln",
                            SearchOption.TopDirectoryOnly).Select(n => Path.GetFileName(n)).ToArray();
                    }

                    if (!RunGitCli($"checkout --quiet {baseBranch}", workingDirectory))
                    {
                        Environment.Exit(1);
                    }

                    // go for the library update
                    UpdateLibrary(
                        workingDirectory,
                        false,
                        true,
                        args);
                }
            }

            // exit OK
            Environment.Exit(0);
        }

        static void UpdateLibrary(string workingDirectory,
                        bool stablePackages = false,
                        bool previewPackages = true,
                        string[] solutionsToCheck = default)
        {
            string releaseType = stablePackages ? "stable" : previewPackages ? "preview" : "?????";
            bool useStablePackages = stablePackages;

            // sanity checks
            if (workingDirectory is null)
            {
                // default to current directory
                workingDirectory = Environment.CurrentDirectory;
            }
            else if (!Directory.Exists(workingDirectory))
            {
                Console.WriteLine($"ERROR: directory '{workingDirectory}' does not exist!");
                Environment.Exit(1);
            }

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

            // check if this is running on a git repo
            string libraryName = null;
            string gitRepo = "";
            if (!RunGitCli("remote -v", ref gitRepo, workingDirectory))
            {
                Environment.Exit(1);
            }

            if (gitRepo.Contains("fatal: not a git repository"))
            {
                Console.WriteLine($"ERROR: working directory is not a git repository");
                Environment.Exit(1);
            }
            else
            {
                var repoName = Regex.Match(gitRepo, "(?:https:\\/\\/github\\.com\\/nanoframework\\/)(?'repoName'\\S+)(?:\\.git\\s\\(fetch\\)|\\s\\(fetch\\))");
                if (!repoName.Success)
                {
                    Console.WriteLine($"ERROR: couldn't determine repository name.");
                    Environment.Exit(1);
                }

                // need to remove .git from end of URL, if there
                libraryName = repoName.Groups["repoName"].Value.Replace(".git", "");
            }

            Console.WriteLine($"Repository is: '{libraryName ?? "null"}'");

            if ((!stablePackages && !previewPackages)
                || (stablePackages && previewPackages))
            {
                Console.WriteLine($"ERROR: can't specify stable and preview NuGet packages simultaneously!");
                Environment.Exit(1);
            }

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
            string newBranchName = "nfbot/update-dependencies/" + Guid.NewGuid().ToString();

            // collect solution(s)
            string[] solutionFiles = default;

            if (solutionsToCheck is not null)
            {
                string searchFilter = "";

                if (solutionsToCheck.Count() == 1
                    & solutionsToCheck[0] == "*.sln")
                {
                    searchFilter = "*.sln";
                }
                else
                {
                    foreach (var sln in solutionsToCheck)
                    {
                        if (!sln.EndsWith("sln"))
                        {
                            searchFilter += $"*{sln}*.sln;";
                        }
                        else
                        {
                            searchFilter += $"{sln};";
                        }
                    }

                    // remove trailing ';'
                    searchFilter = searchFilter.Substring(0, searchFilter.Length - 1);
                }

                solutionFiles = Directory.GetFiles(workingDirectory, $"{searchFilter}", SearchOption.AllDirectories);
            }
            else
            {
                solutionFiles = Directory.GetFiles(workingDirectory, "*.sln", SearchOption.AllDirectories);
            }
            
            // list solutions to check
            Console.WriteLine("");
            Console.WriteLine($"Solutions to check are:");

            foreach (var sln in solutionFiles)
            {
                Console.Write($"{Path.GetRelativePath(workingDirectory, sln)}");

                // check if this on is in the exclusion list
                if(_solutionsExclusionList.Contains(Path.GetFileNameWithoutExtension(sln)))
                {
                    Console.WriteLine(" *** EXCLUDED ***");
                }
                else
                {
                    Console.WriteLine("");
                }
            }

            // find NuGet.Config
            var nugetConfig = Directory.GetFiles(workingDirectory, "NuGet.Config", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (nugetConfig is not null)
            {
                Console.WriteLine($"INFO: working with '{nugetConfig}'");

                // compose option for nuget CLI 
                _nuGetConfigFile = $" -ConfigFile {nugetConfig}";
            }
            else
            {
                Console.WriteLine("INFO: couldn't find a nuget.config");
            }

            // setup list to nugets to skip update
            HashSet<PackageReference> nugetsToSkip = new();

            // go through each solution (filter out the ones in the exclusion list)
            foreach (var solutionFile in solutionFiles.Where(s => !_solutionsExclusionList.Contains(Path.GetFileNameWithoutExtension(s))))
            {
                Console.WriteLine();
                Console.WriteLine("************");
                Console.WriteLine($"Processing: '{solutionFile}'");

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
                Console.WriteLine($"INFO: restoring solution...");

                if (!RunNugetCLI("restore", solutionFile))
                {
                    Environment.Exit(1);
                }

                // find ALL packages.config files in the solution projects
                var packageConfigs = Directory.GetFiles(solutionPath, "packages.config", SearchOption.AllDirectories);

                Console.WriteLine($"Found {packageConfigs.Length} packages.config files...");

                // for solutions and projects outside the working directory, need to specify repository path
                string repositoryPath = "";

                if (solutionPath != workingDirectory)
                {
                    repositoryPath = $"-RepositoryPath {Directory.GetParent(solutionFile).FullName}\\packages";
                }

                foreach (var packageConfigFile in packageConfigs)
                {
                    Console.WriteLine();
                    Console.WriteLine($"INFO: working file is {packageConfigFile}");

                    // check if the project the packages.config belongs to it's in the solution 
                    var projectPath = Directory.GetParent(packageConfigFile).FullName;

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

                    var match = Regex.Match(slnFileContent, $"(?>\\\", \\\"{projectPathInSln})(?'project'[a-zA-Z0-9_.-]+.nfproj)(\\\")");
                    if (!match.Success)
                    {
                        Console.WriteLine($"INFO: couldn't find a project matching this packages.config. *** SKIPPING ***.");
                        continue;
                    }

                    var projectToUpdate = Directory.GetFiles(solutionPath, match.Groups["project"].Value, SearchOption.AllDirectories).FirstOrDefault();

                    Console.WriteLine($"Updating project {Path.GetFileNameWithoutExtension(projectPath)}");

                    // load packages.config 
                    var packageReader = new NuGet.Packaging.PackagesConfigReader(XDocument.Load(packageConfigFile));

                    // filter out Nerdbank.GitVersioning package and development dependencies
                    var packageList = packageReader.GetPackages().Where(p => !p.IsDevelopmentDependency && !p.PackageIdentity.Id.Contains("Nerdbank.GitVersioning"));

                    if (packageList.Any())
                    {
                        // list packages to check
                        Console.WriteLine();
                        Console.WriteLine("-- NuGet packages to update --");
                        foreach (var package in packageList)
                        {
                            Console.WriteLine($"{package.PackageIdentity}");
                        }
                        Console.WriteLine("--------- end of list --------");
                        Console.WriteLine();

                        // check all packages
                        foreach (var package in packageList)
                        {
                            // get package name and target version
                            string packageName = package.PackageIdentity.Id;
                            string packageOriginVersion = package.PackageIdentity.Version.ToNormalizedString();

                            // skip this package if it's on the list
                            if (nugetsToSkip.Any(p => p.PackageIdentity.Id == package.PackageIdentity.Id && p.PackageIdentity.Version == package.PackageIdentity.Version))
                            {
                                continue;
                            }

                            Console.WriteLine($"Attempting to update package {packageName}.{packageOriginVersion}");

                            string updateResult = "";

                            if (useStablePackages
                                && !packageName.StartsWith("UnitsNet."))
                            {
                                // don't allow prerelease for release, main branches and UnitsNet packages

                                // perform NuGet update
                                if (!RunNugetCLI(
                                    "update",
                                    $"{projectPath} -Id {packageName}  {repositoryPath} -FileConflictAction Overwrite",
                                    true,
                                    ref updateResult))
                                {
                                    Environment.Exit(1);
                                }
                            }
                            else if (packageName.StartsWith("UnitsNet."))
                            {
                                // grab latest version from NuGet
                                string unitsNetPackageInfo = "";
                                if (!RunNugetCLI(
                                    "search",
                                    $" {packageName} -Verbosity quiet -Source \"https://api.nuget.org/v3/index.json\"",
                                    false,
                                    ref unitsNetPackageInfo))
                                {
                                    Environment.Exit(1);
                                }

                                var unitsNetVersion = unitsNetPackageInfo.Split("\r\n")[2].Split('|')[1].Trim();

                                // perform NuGet update
                                if (!RunNugetCLI(
                                    "update",
                                    $"{projectToUpdate} -Id {packageName} -Version {unitsNetVersion} {repositoryPath} -FileConflictAction Overwrite",
                                    true,
                                    ref updateResult))
                                {
                                    Environment.Exit(1);
                                }
                            }
                            else
                            {
                                // all the rest, use prerelase packages

                                // perform NuGet update
                                if (!RunNugetCLI(
                                    "update",
                                    $"{projectToUpdate} -Id {packageName} -PreRelease  {repositoryPath} -FileConflictAction Overwrite",
                                    true,
                                    ref updateResult))
                                {
                                    Environment.Exit(1);
                                }
                            }

                            // check update outcome
                            var updateOutcome = Regex.Match(updateResult, $"(?:Successfully\\sinstalled\\s\\'{packageName.Replace(".", "\\.")}\\s)(?'newVersion'\\S+)(?:\\'\\sto\\s)");
                            if (!updateOutcome.Success)
                            {
                                Console.WriteLine($"No newer version, skipping");

                                // add to list of packages to skip
                                nugetsToSkip.Add(package);

                                continue;
                            }

                            // grab target version
                            var packageTargetVersion = updateOutcome.Groups["newVersion"].Value;

                            // sanity check
                            if (packageTargetVersion.Contains("alpha"))
                            {
                                Console.WriteLine($"Skipping update of {packageName} because it's trying to use an alpha version!");

                                // exit with error 
                                Environment.Exit(1);
                            }
                            else
                            {
                                // if we are updating samples repo, OK to move to next one
                                if (Environment.GetEnvironmentVariable("GITHUB_REPOSITORY") is not null &&
                                    Environment.GetEnvironmentVariable("GITHUB_REPOSITORY") == "nanoframework/Samples")
                                {
                                    updateCount++;

                                    // build commit message
                                    commitMessage.Append($"Bumps {packageName} from {packageOriginVersion} to {packageTargetVersion}</br>");

                                    // done here
                                    continue;
                                }

                                Console.WriteLine($"Bumping {packageName} from {packageOriginVersion} to {packageTargetVersion}.");

                                // update nuspec
                                // compose nuspec file
                                var nuspecFileName = Path.Combine(solutionPath, $"{Path.GetFileNameWithoutExtension(projectToUpdate)}.nuspec");

                                // sanity check for nuspec file
                                if (!File.Exists(nuspecFileName))
                                {
                                    Console.WriteLine("**********************************************");
                                    Console.WriteLine($"INFO: Can't find nuspec file '{nuspecFileName}'");
                                    Console.WriteLine("**********************************************");

                                    continue;
                                }
                                else
                                {
                                    Console.WriteLine($"Updating nuspec file '{nuspecFileName}'");

                                    // load nuspec file content
                                    var nuspecFile = new XmlDocument();
                                    nuspecFile.Load(nuspecFileName);

                                    XmlNamespaceManager nsmgr = new XmlNamespaceManager(nuspecFile.NameTable);
                                    nsmgr.AddNamespace("package", "http://schemas.microsoft.com/packaging/2012/06/nuspec.xsd");

                                    // update version
                                    nuspecFile.SelectSingleNode($"descendant::package:dependency[@id='{packageName}']", nsmgr).Attributes["version"].Value = packageTargetVersion;

                                    // save back changes
                                    nuspecFile.Save(nuspecFileName);

                                    // bump counter
                                    nuspecCounter++;
                                }

                                // build commit message
                                string updateMessage = $"Bumps {packageName} from {packageOriginVersion} to {packageTargetVersion}</br>";

                                // append to commit message, if not already reported
                                if (!commitMessage.ToString().Contains(updateMessage))
                                {
                                    commitMessage.Append(updateMessage);

                                    // bump counter
                                    updateCount++;
                                }
                            }
                        }
                    }
                }
            }

            // check if any packages where updated
            if (updateCount == 0)
            {
                Console.WriteLine("INFO: No packages found to update...");
            }
            else
            {
                // sanity check for no nuspecs found
                if (nuspecCounter == 0)
                {
                    Console.WriteLine($"ERROR: No nuspecs files updated!");
                    Environment.Exit(1);
                }

                Console.WriteLine($"INFO: {updateCount} packages updated");

                // need this line so nfbot flags the PR appropriately
                commitMessage.Append("\n[version update]\n\n");

                // better add this warning line               
                commitMessage.Append("### :warning: This is an automated update. :warning:\n");

                Console.WriteLine($"INFO: generating PR information");

                Console.WriteLine($"INFO: creating branch to perform updates");

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

                // add changes
                if (!RunGitCli("add -A", workingDirectory))
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

                try
                {
                    // create PR
                    // developer note: head must be in the format 'user:branch'
                    var updatePr = _octokitClient.PullRequest.Create("nanoFramework", libraryName, new NewPullRequest(prTitle, $"nanoframework:{newBranchName}", "develop")).Result;
                    // update PR body
                    var updatePrBody = new PullRequestUpdate() { Body = commitMessage.ToString() };
                    _ = _octokitClient.PullRequest.Update("nanoFramework", libraryName, updatePr.Number, updatePrBody).Result;

                    Console.WriteLine($"INFO: created PR #{updatePr.Number} @ nanoFramework/{libraryName}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"ERROR: exception when submitting PR {ex.Message}");
                    Environment.Exit(1);
                }
            }
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
            var cmd = Cli.Wrap(Path.Combine(AppContext.BaseDirectory, "NuGet.exe"))
                .WithArguments($" {command} {arguments} {(useNugetConfig ? _nuGetConfigFile : null)}")
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
            else
            {
                Console.WriteLine($"ERROR: nuget CLI exited with code {cliResult.ExitCode}");
                Console.WriteLine($"{cliResult.StandardError}");
                return false;
            }
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
            else
            {
                Console.WriteLine($"ERROR: git CLI exited with code {cliResult.ExitCode}");
                Console.WriteLine($"{cliResult.StandardError}");
                return false;
            }
        }
    }
}
