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
        Console.WriteLine($"Nuspec file is: '{nuspecFile ?? "null"}'");

        // sanity checks
        if (workingDirectory is not null
            && !Directory.Exists(workingDirectory))
        {
            Console.WriteLine($"ERROR: directory '{workingDirectory}' does not exist!");
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
                Console.WriteLine($"ERROR: solution file '{solutionToCheck}' does not exist!");
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
                Console.WriteLine($"ERROR: nuspec file '{solutionToCheck}' does not exist!");
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
            Console.WriteLine($"ERROR: solution file '{solutionToCheck}' doesn't have any nfproj file??");
            return 1;
        }

        // read nuspec file content
        NuspecReader nuspecReader = null;

        if (nuspecFile is null)
        {
            Console.WriteLine("INFO: No nuspec file in the build");
        }
        else
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

            var match = Regex.Match(slnFileContent, $"(?>\\\", \\\"{projectPathInSln})(?'project'[a-zA-Z0-9_.]+.nfproj)(\\\")");
            if (!match.Success)
            {
                Console.WriteLine($"INFO: couldn't find a project matching this packages.config. *** SKIPPING ***.");
                continue;
            }

            Console.WriteLine();
            Console.WriteLine($"Checking '" + match.Groups["project"] + "' project file...");

            // compose project path
            var projectToCheck = Path.Combine(projectPath.FullName, match.Groups["project"].Value);

            Console.WriteLine($"INFO: Reading packages.config for '{Path.GetFileNameWithoutExtension(projectToCheck)}'");

            // set chekc flag
            bool checkNuspec = false;

            Console.WriteLine("INFO: Building package list...");

            // load packages.config 
            var packageReader = new NuGet.Packaging.PackagesConfigReader(XDocument.Load(Path.Combine(Directory.GetParent(projectToCheck).FullName, "packages.config")));

            // filter out Nerdbank.GitVersioning package
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
                    Console.WriteLine("ERROR: couldn't read assembly name from project file");
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
                        Console.WriteLine();
                        Console.WriteLine("*****************************************************************");
                        Console.WriteLine($"Couldn't find it in '{Path.GetFileName(projectToCheck)}'");
                        Console.WriteLine("*****************************************************************");
                        Console.WriteLine();
                    }
                    else
                    {
                        Console.Write("nfproj OK! ");
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

                                        if (dependencyInfo.Dependencies.Any(d => d.Id == packageName && d.VersionRange.ToShortString() == packageVersion))
                                        {
                                            dependencyFound = true;

                                            // done here
                                            break;
                                        }
                                    }
                                }
                            }

                            if (!dependencyFound)
                            {
                                Console.WriteLine();
                                Console.WriteLine("*****************************************************************");
                                Console.WriteLine($"Couldn't find it in '{Path.GetFileName(nuspecFile)}'");
                                Console.WriteLine($"And it is not a dependency on any of the listed dependency packages");
                                Console.WriteLine("*****************************************************************");
                                Console.WriteLine();

                                // flag failed check
                                nuspecCheckFailed = true;
                            }
                            else
                            {
                                // this is a dependency from one of the declared dependencies
                                // it's OK that's not listed as a dependency
                                Console.WriteLine(" NOT listed in nuspec, but it's OK");

                                nuspecPackageMissing = false;
                            }
                        }
                        else
                        {
                            Console.WriteLine("nuspec OK! ");
                        }
                    }
                }
            }
            else
            {
                Console.WriteLine("ERROR: couldn't identify any nuget packages to check for??");
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
            Console.WriteLine();
            Console.WriteLine("********************************************************************");
            Console.WriteLine("nuspec wasn't checked!! Verify package ID/Title and/or assembly name");
            Console.WriteLine("********************************************************************");
            Console.WriteLine();

            // exit with error code
            return 1;
        }

        Console.WriteLine("Versions check completed!");

        // exit OK
        return 0;
    }
}
