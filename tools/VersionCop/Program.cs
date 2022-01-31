//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using NuGet.Common;
using NuGet.Configuration;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Protocol.Core.Types;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml.Linq;

class Program
{
    /// <param name="solutionToCheck">Path to the solution to check.</param>
    /// <param name="workingDirectory">Path of the working directory where solutions will be searched.</param>
    /// <param name="nuspecFile">Path of nuspec file to be used for version check.</param>
    static int Main(
        string solutionToCheck,
        string workingDirectory = null,
        string nuspecFile = null)
    {
        Console.WriteLine($"Solution is: '{solutionToCheck}'");
        Console.WriteLine($"Working directory is: '{workingDirectory ?? "null"}'");
        Console.WriteLine($"Nuspec file is: '{nuspecFile ?? "*** NONE PROVIDED ***"}'");

        // sanity checks
        if (workingDirectory is not null
            && !Directory.Exists(workingDirectory))
        {
            Console.ForegroundColor = ConsoleColor.Red;

            Console.WriteLine($"ERROR: directory '{workingDirectory}' does not exist!");

            Console.ForegroundColor = ConsoleColor.White;

            return 1;
        }

        if (!File.Exists(solutionToCheck))
        {
            // try with path
            if (workingDirectory is not null
                && File.Exists(Path.Combine(workingDirectory, solutionToCheck)))
            {
                // store full path to solution
                solutionToCheck = Path.Combine(workingDirectory, solutionToCheck);
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;

                Console.WriteLine($"ERROR: solution file '{solutionToCheck}' does not exist!");

                Console.ForegroundColor = ConsoleColor.White;

                return 1;
            }
        }

        if (nuspecFile is not null
            && !File.Exists(nuspecFile))
        {
            // try with path
            if (workingDirectory is not null
                && File.Exists(Path.Combine(workingDirectory, nuspecFile)))
            {
                // store full path to solution
                nuspecFile = Path.Combine(workingDirectory, nuspecFile);
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;

                Console.WriteLine($"ERROR: nuspec file '{solutionToCheck}' does not exist!");

                Console.ForegroundColor = ConsoleColor.White;

                return 1;
            }
        }

        // grab working directory from solution, if it's empty
        if (workingDirectory is null)
        {
            workingDirectory = Path.GetFullPath(solutionToCheck);
        }

        // handle mscorlib library
        if (solutionToCheck.EndsWith("nanoFramework.CoreLibrary.sln"))
        {
            Console.WriteLine("INFO: This is mscorlib, skipping this check!");
            return 0;
        }

        Console.WriteLine($"INFO: parsing '{Path.GetFileNameWithoutExtension(solutionToCheck)}'");

        // declare flags
        bool projectCheckFailed = true;
        bool nuspecCheckFailed = true;
        bool nuspecChecked = false;

        // check for nfproj
        if (!File.ReadAllText(solutionToCheck).Contains(".nfproj"))
        {
            Console.ForegroundColor = ConsoleColor.Red;

            Console.WriteLine($"ERROR: solution file '{solutionToCheck}' doesn't have any nfproj file??");

            Console.ForegroundColor = ConsoleColor.White;

            return 1;
        }

        // read nuspec file content
        NuspecReader nuspecReader = null;

        if (nuspecFile is not null)
        {
            nuspecReader = new NuspecReader(XDocument.Load(nuspecFile));
        }

        // read solution file content
        var slnFileContent = File.ReadAllText(solutionToCheck);

        // find ALL packages.config files in the solution projects
        var packageConfigs = Directory.GetFiles(workingDirectory, "packages.config", SearchOption.AllDirectories);

        Console.WriteLine($"Found {packageConfigs.Length} packages.config files...");

        foreach (var packageConfigFile in packageConfigs)
        {
            Console.WriteLine();
            Console.WriteLine($"INFO: working file is {packageConfigFile}");

            // check if the project the packages.config belongs to it's in the solution 
            var projectPath = Directory.GetParent(packageConfigFile);

            var projectPathInSln = Path.GetRelativePath(workingDirectory, Directory.GetParent(packageConfigFile).FullName);

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
                Console.ForegroundColor = ConsoleColor.Yellow;

                Console.WriteLine($"INFO: couldn't find a project matching this packages.config. *** SKIPPING ***.");

                Console.ForegroundColor = ConsoleColor.White;

                continue;
            }

            Console.WriteLine();
            Console.WriteLine($"Checking '" + match.Groups["projectname"] + "' project file...");

            // compose project path
            var projectToCheck = Directory.GetFiles(workingDirectory, match.Groups["projectpath"].Value, SearchOption.AllDirectories).FirstOrDefault();
            var projectName = match.Groups["projectname"].Value;

            Console.WriteLine($"INFO: Reading packages.config for '{Path.GetFileNameWithoutExtension(projectToCheck)}'");

            string nuspecFileName = string.Empty;

            // set check flag
            bool checkNuspec = false;

            // try to find nuspec for this in case none was specified
            if (nuspecFile is null)
            {
                nuspecFileName = Directory.GetFiles(workingDirectory, $"*{Path.GetFileNameWithoutExtension(projectToCheck)}.nuspec", SearchOption.TopDirectoryOnly).FirstOrDefault();

                if (nuspecFileName is not null)
                {
                    // report finding
                    Console.WriteLine($"INFO: found matching nuspec file '{Path.GetFileName(nuspecFileName)}'");

                    // load nuspec file
                    // NOTE: this is replacing as well $version$ by 9.99.99.999
                    nuspecReader = GetNuspecReader(nuspecFileName);
                }
                else
                {
                    // try again with project name
                    nuspecFileName = Directory.GetFiles(workingDirectory, $"*{projectName}.nuspec", SearchOption.TopDirectoryOnly).FirstOrDefault();

                    if (nuspecFileName is not null)
                    {
                        // report finding
                        Console.WriteLine($"INFO: found matching nuspec file '{Path.GetFileName(nuspecFileName)}'");

                        // load nuspec file
                        // NOTE: this is replacing as well $version$ by 9.99.99.999
                        nuspecReader = GetNuspecReader(nuspecFileName);
                    }
                    else
                    {
                        // better report this...
                        Console.ForegroundColor = ConsoleColor.Yellow;

                        Console.WriteLine("INFO: couldn't find a nuspec file for the project...");
                        Console.WriteLine($"INFO: tried '*{Path.GetFileNameWithoutExtension(projectToCheck)}.nuspec'");
                        Console.WriteLine($"INFO: and also '*{projectName}.nuspec'");

                        Console.ForegroundColor = ConsoleColor.White;

                        // make sure nuspec reader is null
                        nuspecReader = null;
                    }
                }
            }

            Console.WriteLine("INFO: Building package list...");

            // load packages.config 
            var packageReader = new NuGet.Packaging.PackagesConfigReader(XDocument.Load(Path.Combine(Directory.GetParent(projectToCheck).FullName, "packages.config")));

            // filter out these packages: Nerdbank.GitVersioning
            var packageList = packageReader.GetPackages().Where(p => !p.IsDevelopmentDependency && !p.PackageIdentity.Id.Contains("Nerdbank.GitVersioning"));

            if (packageList.Any())
            {
                // list packages to check
                Console.WriteLine();
                Console.WriteLine("-- NuGet packages to check --");
                foreach (var package in packageList)
                {
                    Console.WriteLine($"{package.PackageIdentity}");
                }
                Console.WriteLine("-------- end of list --------");
                Console.WriteLine();

                // read content from nfproj file
                var projecFileContent = File.ReadAllText(projectToCheck);

                // get assembly name from project file
                string assemblyName;
                var nameRegex = Regex.Match(projecFileContent, "(?>\\<AssemblyName\\>)(?'assemblyname'[a-zA-Z0-9_.]+)|(?>\\<\\/AssemblyName\\>)");
                if (nameRegex.Success)
                {
                    assemblyName = nameRegex.Groups["assemblyname"].Value;
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;

                    Console.WriteLine("ERROR: couldn't read assembly name from project file");

                    Console.ForegroundColor = ConsoleColor.White;

                    return 1;
                }

                Console.WriteLine($"INFO: AssemblyName is '{assemblyName}'");

                // reset flags
                projectCheckFailed = false;
                nuspecCheckFailed = false;
                bool nuspecPackageMissing = false;
                checkNuspec = true;

                Console.WriteLine();
                Console.WriteLine(">>>>>>>>>>>>>>>>>>>>>>>>");

                // check all packages
                foreach (var package in packageList)
                {
                    // get package name and target version
                    string packageName = package.PackageIdentity.Id;
                    string packageVersion = package.PackageIdentity.Version.ToNormalizedString();

                    Console.Write($"Checking {package.PackageIdentity}... ");

                    // find package in project file
                    var packageIdRegexed = package.PackageIdentity.ToString().Replace(".", "\\.");
                    var packageInProjecFile = Regex.Match(projecFileContent, $"(?'package'packages\\\\{packageIdRegexed})");
                    if (packageInProjecFile.Success)
                    {
                        assemblyName = nameRegex.Groups["assemblyname"].Value;
                    }
                    else
                    {
                        // flag failed check
                        projectCheckFailed = true;
                    }

                    // flag for nuspec missing
                    nuspecPackageMissing = true;

                    if (!projectCheckFailed
                        && checkNuspec
                        && nuspecReader is not null)
                    {
                        // this check here tries to determine if the package ID and Title resemble with the assembly name
                        // it looks a bit convoluted but these variations are required to deal with:
                        // - libraries that have (or not) nanoFramework prefix
                        // - libraries that have variations on the ID, like System.Net.Http.Client/Server
                        if (!((nuspecReader.GetId().Contains(assemblyName)
                            && nuspecReader.GetTitle().Contains(assemblyName))
                            || (nuspecReader.GetId().Contains($"nanoFramework.{assemblyName}")
                            && nuspecReader.GetTitle().Contains($"nanoFramework.{assemblyName}"))
                            ))
                        {
                            checkNuspec = false;
                        }

                        if (checkNuspec)
                        {
                            // nuspec is being checked: set flag
                            nuspecChecked = true;

                            foreach (var dependency in nuspecReader.GetDependencyGroups(true))
                            {
                                if (nuspecPackageMissing)
                                {
                                    foreach (var dependencyPackage in dependency.Packages)
                                    {
                                        if (dependencyPackage.Id == packageName
                                            && dependencyPackage.VersionRange.OriginalString == packageVersion)
                                        {
                                            nuspecPackageMissing = false;

                                            // done here
                                            break;
                                        }
                                    }
                                }
                            }
                        }
                    }

                    if (projectCheckFailed)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;

                        Console.WriteLine();
                        Console.WriteLine("*****************************************************************");
                        Console.WriteLine($"Couldn't find it in '{Path.GetFileName(projectToCheck)}'");
                        Console.WriteLine("*****************************************************************");
                        Console.WriteLine();

                        Console.ForegroundColor = ConsoleColor.White;
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Green;

                        Console.Write("nfproj OK! ");

                        Console.ForegroundColor = ConsoleColor.White;
                    }

                    // nuspec check outcome, if any
                    if (nuspecReader is null || !checkNuspec)
                    {
                        Console.WriteLine();
                    }
                    else
                    {
                        if (nuspecPackageMissing)
                        {
                            bool dependencyFound = false;

                            string hintMessage = null;

                            // setup NuGet source and dependency resolver
                            PackageSourceProvider sourceProvider = new(NullSettings.Instance, new[]
                            {
                                new PackageSource("https://api.nuget.org/v3/index.json")
                            });
                            var sourceRepositoryProvider = new SourceRepositoryProvider(sourceProvider, Repository.Provider.GetCoreV3());
                            var repositories = sourceRepositoryProvider.GetRepositories();

                            var dependencyInfoResource = repositories.First().GetResource<DependencyInfoResource>();

                            // find out if this is a dependency from one of the listed packages
                            foreach (var dependency in nuspecReader.GetDependencyGroups(true))
                            {
                                if (!dependencyFound)
                                {
                                    foreach (var dependencyPackage in dependency.Packages)
                                    {
                                        PackageIdentity packageIdentity = new(
                                            dependencyPackage.Id,
                                            new NuGet.Versioning.NuGetVersion(dependencyPackage.VersionRange.OriginalString));

                                        var dependencyInfo = dependencyInfoResource.ResolvePackage(
                                            packageIdentity,
                                            dependency.TargetFramework,
                                            new SourceCacheContext(),
                                            new NullLogger(),
                                            CancellationToken.None).Result;

                                        if (dependencyInfo is not null
                                            && dependencyInfo.Dependencies.Any(d => d.Id == packageName && d.VersionRange.ToShortString() == packageVersion))
                                        {
                                            dependencyFound = true;

                                            // done here
                                            break;
                                        }

                                        // check on package name BUT version mismatch
                                        if (dependencyInfo is not null
                                           && dependencyInfo.Dependencies.Any(d => d.Id == packageName))
                                        {
                                            // looks like this is it!
                                            hintMessage = $"Found it as dependency of '{dependencyPackage.Id}' {Environment.NewLine}with requested version being '{packageVersion}' BUT {Environment.NewLine}NuGet package is declaring version as '{dependencyInfo.Dependencies.First(d => d.Id == packageName).VersionRange.ToShortString()}'";

                                            // done here
                                            break;
                                        }
                                    }
                                }
                            }

                            if (!dependencyFound)
                            {
                                Console.ForegroundColor = ConsoleColor.Red;

                                Console.WriteLine();
                                Console.WriteLine("*****************************************************************");

                                if (hintMessage is null)
                                {
                                    Console.WriteLine($"Couldn't find it in '{Path.GetFileName(nuspecFileName)}'");
                                    Console.WriteLine($"And it is not a dependency on any of the listed dependency packages");
                                }
                                else
                                {
                                    Console.WriteLine(hintMessage);
                                }

                                Console.WriteLine("*****************************************************************");
                                Console.WriteLine();

                                Console.ForegroundColor = ConsoleColor.White;

                                // flag failed check
                                nuspecCheckFailed = true;
                            }
                            else
                            {
                                // this is a dependency from one of the declared dependencies
                                // it's OK that's not listed as a dependency

                                Console.ForegroundColor = ConsoleColor.Yellow;

                                Console.WriteLine(" NOT listed in nuspec, but that's OK");

                                Console.ForegroundColor = ConsoleColor.White;

                                nuspecPackageMissing = false;
                            }
                        }
                        else
                        {
                            Console.ForegroundColor = ConsoleColor.Green;

                            Console.WriteLine("nuspec OK! ");

                            Console.ForegroundColor = ConsoleColor.White;
                        }
                    }
                }
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;

                Console.WriteLine("ERROR: couldn't identify any nuget packages to check for??");

                Console.ForegroundColor = ConsoleColor.White;
                return 1;
            }

            Console.WriteLine();
            Console.WriteLine("<<<<<<<<<<<<<<<<<<<<<<<<");
            Console.WriteLine();

            if (projectCheckFailed || nuspecCheckFailed)
            {
                return 1;
            }
        }

        if (!nuspecChecked)
        {
            Console.ForegroundColor = ConsoleColor.Red;

            Console.WriteLine();
            Console.WriteLine("********************************************************************");
            Console.WriteLine("nuspec wasn't checked!! Verify package ID/Title and/or assembly name");
            Console.WriteLine("********************************************************************");
            Console.WriteLine();

            Console.ForegroundColor = ConsoleColor.White;

            // exit with error code
            return 1;
        }

        Console.WriteLine("Versions check completed!");

        // exit OK
        return 0;
    }

    private static NuspecReader GetNuspecReader(string nuspecFileName)
    {
        string originalVersion = "version=\"$version$\"";
        string replacementVersion = "version=\"9.99.999.9999\"";
        string nuspecContent = string.Empty;

        // handle edge cases in nanoFramework libraries
        if (nuspecFileName.EndsWith("nanoFramework.Logging.Serial.nuspec")
            || nuspecFileName.EndsWith("nanoFramework.Logging.Stream.nuspec")
            || nuspecFileName.EndsWith("nanoFramework.Logging.Syslog.nuspec"))
        {
            // these two use a hack in the version for nanoFramework.Logging dependency
            // need to replace it with a valid version string, read and then replace it back
            nuspecContent = File.ReadAllText(nuspecFileName);

            nuspecContent = nuspecContent.Replace(originalVersion, replacementVersion);

            File.WriteAllText(nuspecFileName, nuspecContent);
        }

        var nuspecReader = new NuspecReader(XDocument.Load(nuspecFileName));

        // replace back the original version string
        if (nuspecFileName.EndsWith("nanoFramework.Logging.Serial.nuspec")
            || nuspecFileName.EndsWith("nanoFramework.Logging.Stream.nuspec")
            || nuspecFileName.EndsWith("nanoFramework.Logging.Syslog.nuspec"))
        {
            nuspecContent = nuspecContent.Replace(replacementVersion, originalVersion);

            File.WriteAllText(nuspecFileName, nuspecContent);
        }

        return nuspecReader;
    }
}
