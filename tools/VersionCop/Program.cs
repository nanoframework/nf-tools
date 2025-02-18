// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml.Linq;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Protocol.Core.Types;

class Program
{
    private static IEnumerable<SourceRepository> _nugetRepositories;
    private static DependencyInfoResource _dependencyInfoResourceNuGet;
    private static readonly string _versionReplacementValue = $"9.99.999.9999+{Guid.NewGuid():N}";

    /// <param name="solutionToCheck">Path to the solution to check.</param>
    /// <param name="workingDirectory">Path of the working directory where solutions will be searched.</param>
    /// <param name="nuspecFile">Path of nuspec file to be used for version check.</param>
    /// <param name="analyseNuspec"><see langword="true"/> to perform analysis of nuspec reporting dependencies that can be removed.</param>
    static int Main(
        string solutionToCheck,
        string workingDirectory = null,
        string nuspecFile = null,
        bool analyseNuspec = false)
    {
        Console.ForegroundColor = ConsoleColor.White;

        Console.WriteLine($"Solution is: '{solutionToCheck}'");
        Console.WriteLine($"Working directory is: '{workingDirectory ?? "null"}'");
        Console.WriteLine($"Nuspec file is: '{nuspecFile ?? "*** NONE PROVIDED ***"}'");

        if (analyseNuspec)
        {
            Console.WriteLine("Performing analysis on nuspec declarations that should be removed.");
        }

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
            workingDirectory = Path.GetDirectoryName(Path.GetFullPath(solutionToCheck));
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
        bool nuspecDependenciesToRemove = false;

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
            nuspecReader = GetNuspecReader(nuspecFile);
        }


        // setup NuGet source and dependency resolver
        PackageSourceProvider sourceProvider = new(NullSettings.Instance, new[]
        {
            new PackageSource("https://api.nuget.org/v3/index.json")
        });

        var sourceRepositoryProvider = new SourceRepositoryProvider(sourceProvider, Repository.Provider.GetCoreV3());
        _nugetRepositories = sourceRepositoryProvider.GetRepositories();

        _dependencyInfoResourceNuGet = _nugetRepositories.ElementAt(0).GetResource<DependencyInfoResource>();

        // read solution file content
        var slnFileContent = File.ReadAllText(solutionToCheck);

        // find ALL packages.config files in the solution projects
        var packageConfigs = Directory.GetFiles(workingDirectory, "packages.config", SearchOption.AllDirectories);

        Console.WriteLine($"Found {packageConfigs.Length} packages.config files...");

        foreach (var packageConfigFile in packageConfigs)
        {
            Console.WriteLine();
            Console.WriteLine($"INFO: working file is {packageConfigFile}");

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
            var packageReader = new PackagesConfigReader(XDocument.Load(Path.Combine(Directory.GetParent(projectToCheck).FullName, "packages.config")));

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
                bool refMissingFromNuspec = false;
                bool idMismatchInNuspec = false;
                string idFoundInNuspec = "";
                checkNuspec = true;

                Console.WriteLine();
                Console.WriteLine(">>>>>>>>>>>>>>>>>>>>>>>>");

                // check all packages
                var packageIdentities = packageList.Select(package => package.PackageIdentity);

                foreach (var packageIdentity in packageIdentities)
                {
                    // get package name and target version
                    string packageName = packageIdentity.Id;
                    string packageVersion = packageIdentity.Version.ToNormalizedString();

                    Console.Write($"Checking {packageIdentity}... ");

                    // find package in project file
                    var packageIdRegexed = packageIdentity.ToString().Replace(".", "\\.");
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
                    refMissingFromNuspec = true;

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
                                if (refMissingFromNuspec)
                                {
                                    foreach (var dependencyPackage in dependency.Packages)
                                    {
                                        if (dependencyPackage.Id == packageName)
                                        {
                                            // package found
                                            // now check version
                                            if (dependencyPackage.VersionRange.OriginalString == packageVersion)
                                            {
                                                refMissingFromNuspec = false;
                                            }
                                            else
                                            {
                                                // version doesn't match!
                                                nuspecCheckFailed = true;
                                                idMismatchInNuspec = true;
                                                idFoundInNuspec = dependencyPackage.VersionRange.OriginalString;
                                            }

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

                        if (idMismatchInNuspec)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;

                            Console.WriteLine();
                            Console.WriteLine("*****************************************************************");
                            Console.WriteLine($"Found in nuspec, but declared dependency version doesn't match!");
                            Console.WriteLine($"expecting '{packageVersion}' but found '{idFoundInNuspec}'");
                            Console.WriteLine("*****************************************************************");

                            Console.ForegroundColor = ConsoleColor.White;
                        }
                    }

                    // nuspec check outcome, if any
                    if (nuspecReader is null || !checkNuspec)
                    {
                        Console.WriteLine();
                    }
                    else
                    {
                        if (refMissingFromNuspec || analyseNuspec)
                        {
                            bool dependencyFound = false;
                            bool isDeclaredDependency = !refMissingFromNuspec;

                            string hintMessage = null;

                            string dependencyPackageId = "";

                            // find out if this is a dependency from one of the listed packages
                            foreach (var dependency in nuspecReader.GetDependencyGroups(true))
                            {
                                if (!dependencyFound)
                                {
                                    foreach (var dependencyPackage in dependency.Packages)
                                    {
                                        dependencyFound = FindDependency(
                                            packageName,
                                            packageVersion,
                                            !analyseNuspec && isDeclaredDependency,
                                            dependencyPackage,
                                            dependency.TargetFramework,
                                            ref dependencyPackageId,
                                            ref hintMessage);

                                        if (dependencyFound)
                                        {
                                            break;
                                        }
                                    }

                                    if (!dependencyFound)
                                    {
                                        foreach (var dependencyPackage in dependency.Packages)
                                        {
                                            dependencyFound = FindDependency(
                                                packageName,
                                                packageVersion,
                                                !refMissingFromNuspec && isDeclaredDependency,
                                                dependencyPackage,
                                                dependency.TargetFramework,
                                                ref dependencyPackageId,
                                                ref hintMessage,
                                                true);

                                            if (dependencyFound)
                                            {
                                                break;
                                            }
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

                                if (!idMismatchInNuspec)
                                {
                                    if (!isDeclaredDependency)
                                    {
                                        Console.ForegroundColor = ConsoleColor.Yellow;

                                        Console.WriteLine(" NOT listed in nuspec, but that's OK");
                                    }
                                    else
                                    {
                                        Console.ForegroundColor = ConsoleColor.Green;

                                        Console.WriteLine("nuspec OK! ");
                                    }
                                }

                                if (analyseNuspec && (!refMissingFromNuspec || idMismatchInNuspec) && packageName != dependencyPackageId && packageName != "nanoFramework.CoreLibrary")
                                {
                                    Console.ForegroundColor = ConsoleColor.Yellow;

                                    Console.WriteLine($"*** OK to remove it because it's declared as a dependency of '{dependencyPackageId}' ***");

                                    // flag this
                                    nuspecDependenciesToRemove = true;
                                }

                                Console.ForegroundColor = ConsoleColor.White;
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

            if (analyseNuspec && nuspecDependenciesToRemove)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;

                Console.WriteLine("INFO: found nuspec declarations that could be removed.");

                Console.ForegroundColor = ConsoleColor.White;
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

    private static bool FindDependency(string packageName,
                                       string packageVersion,
                                       bool isDeclaredDependency,
                                       PackageDependency dependencyPackage,
                                       NuGet.Frameworks.NuGetFramework targetFramework,
                                       ref string dependencyPackageId,
                                       ref string hintMessage,
                                       bool recurring = false)
    {
        PackageIdentity packageIdentity = new(
            dependencyPackage.Id,
            new NuGet.Versioning.NuGetVersion(dependencyPackage.VersionRange.ToShortString()));

        SourcePackageDependencyInfo dependencyInfo;

        if (recurring)
        {
            // 2nd round
            dependencyInfo = _dependencyInfoResourceNuGet.ResolvePackage(
                packageIdentity,
                targetFramework,
                new SourceCacheContext(),
                new NullLogger(),
                CancellationToken.None).Result;

            bool dependencyFound = false;

            if (dependencyInfo is null)
            {
                // try to find it in NuGet

                dependencyInfo = _dependencyInfoResourceNuGet.ResolvePackage(
                    packageIdentity,
                    targetFramework,
                    new SourceCacheContext(),
                    new NullLogger(),
                    CancellationToken.None).Result;
            }

            if (dependencyInfo is not null)
            {
                foreach (var nextLevelDependencyPackage in dependencyInfo.Dependencies)
                {
                    dependencyFound = FindDependency(
                        packageName,
                        packageVersion,
                        true,
                        nextLevelDependencyPackage,
                        targetFramework,
                        ref dependencyPackageId,
                        ref hintMessage,
                        false);

                    if (dependencyFound)
                    {
                        dependencyPackageId = nextLevelDependencyPackage.Id;

                        break;
                    }
                }

                if (!dependencyFound)
                {
                    foreach (var nextLevelDependencyPackage in dependencyInfo.Dependencies)
                    {
                        dependencyFound = FindDependency(
                            packageName,
                            packageVersion,
                            true,
                            nextLevelDependencyPackage,
                            targetFramework,
                            ref dependencyPackageId,
                            ref hintMessage,
                            true);

                        if (dependencyFound)
                        {
                            dependencyPackageId = nextLevelDependencyPackage.Id;

                            break;
                        }
                    }
                }
            }

            if (!dependencyFound && packageIdentity.Id == packageName)
            {
                dependencyPackageId = packageName;

                if (packageIdentity.Version.ToNormalizedString() != packageVersion)
                {
                    hintMessage = $"Found it as dependency of '{dependencyPackageId}' {Environment.NewLine}with requested version being '{packageVersion}' BUT {Environment.NewLine}NuGet package is declaring version as '{packageIdentity.Version.ToNormalizedString()}'";
                }

                // done here
                return true;
            }

            return dependencyFound;
        }

        if (isDeclaredDependency && packageIdentity.Id == packageName)
        {
            if (packageIdentity.Version.ToNormalizedString() != packageVersion)
            {
                hintMessage = $"Found it as dependency of '{dependencyPackageId}' {Environment.NewLine}with requested version being '{packageVersion}' BUT {Environment.NewLine}NuGet package is declaring version as '{packageIdentity.Version.ToNormalizedString()}'";
            }

            // done here
            return true;
        }

        // 1st round on NuGet, if feed is available AND this a recurring call
        if (_dependencyInfoResourceNuGet is not null
            && recurring)
        {
            dependencyInfo = _dependencyInfoResourceNuGet.ResolvePackage(
                packageIdentity,
                targetFramework,
                new SourceCacheContext(),
                new NullLogger(),
                CancellationToken.None).Result;

            if (dependencyInfo is not null
                && dependencyInfo.Dependencies.Any(d => d.Id == packageName && d.VersionRange.ToShortString() == packageVersion))
            {
                dependencyPackageId = dependencyInfo.Id;

                // done here
                return true;
            }

            // check on package name BUT version mismatch
            if (dependencyInfo is not null
               && dependencyInfo.Dependencies.Any(d => d.Id == packageName))
            {
                // looks like this is it!
                hintMessage = $"Found it as dependency of '{dependencyPackage.Id}' {Environment.NewLine}with requested version being '{packageVersion}' BUT {Environment.NewLine}NuGet package is declaring version as '{dependencyInfo.Dependencies.First(d => d.Id == packageName).VersionRange.ToShortString()}'";

                // done here
                return false;
            }
        }

        // 2nd round
        dependencyInfo = _dependencyInfoResourceNuGet.ResolvePackage(
            packageIdentity,
            targetFramework,
            new SourceCacheContext(),
            new NullLogger(),
            CancellationToken.None).Result;

        if (dependencyInfo is not null
            && dependencyInfo.Dependencies.Any(d => d.Id == packageName && d.VersionRange.ToShortString() == packageVersion))
        {
            dependencyPackageId = dependencyInfo.Id;

            // done here
            return true;
        }

        // check on package name BUT version mismatch
        if (dependencyInfo is not null
           && dependencyInfo.Dependencies.Any(d => d.Id == packageName))
        {
            // looks like this is it!
            hintMessage = $"Found it as dependency of '{dependencyPackage.Id}' {Environment.NewLine}with requested version being '{packageVersion}' BUT {Environment.NewLine}NuGet package is declaring version as '{dependencyInfo.Dependencies.First(d => d.Id == packageName).VersionRange.ToShortString()}'";

            // done here
            return false;
        }

        return false;
    }

    private static NuspecReader GetNuspecReader(string nuspecFileName)
    {
        string nuspecAsText = File.ReadAllText(nuspecFileName);

        // It is valid to use the variable $version$ in nuspec
        // as it will be replaced with correct value when the package is created.
        // The replacement value is unique, so it will not match any literal version value.
        // The assumption is that all packages share the same value of $version$
        // at the time of packaging.
        nuspecAsText = nuspecAsText.Replace("$version$", _versionReplacementValue);

        return new NuspecReader(XDocument.Parse(nuspecAsText));
    }
}
