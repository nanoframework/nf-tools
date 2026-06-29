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
    private static Dictionary<PackageIdentity, SourcePackageDependencyInfo> _packageDependencyCache = new();
    private static bool _isGitHubActions;
    private static bool _isAzureDevOps;

    /// <param name="solutionToCheck">Path to the solution to check.</param>
    /// <param name="workingDirectory">Path of the working directory where solutions will be searched.</param>
    /// <param name="nuspecFile">Path of nuspec file to be used for version check.</param>
    /// <param name="analyseNuspec"><see langword="true"/> to perform analysis of nuspec reporting dependencies that can be removed.</param>
    /// <param name="excludePaths">Comma-separated list of paths to exclude from the search.</param>
    static int Main(
        string solutionToCheck,
        string workingDirectory = null,
        string nuspecFile = null,
        bool analyseNuspec = false,
        string excludePaths = null)
    {
        // Detect running environment
        DetectEnvironment();

        Console.WriteLine($"📋 Solution:           '{solutionToCheck}'");
        Console.WriteLine($"📂 Working directory:  '{workingDirectory ?? "null"}'");
        Console.WriteLine($"📄 Nuspec file:        '{nuspecFile ?? "*** NONE PROVIDED ***"}'");
        Console.WriteLine($"🚫 Exclude paths:      '{excludePaths ?? "*** NONE PROVIDED ***"}'");

        if (analyseNuspec)
        {
            Console.WriteLine("🔍 Performing analysis on nuspec declarations that should be removed.");
        }

        // sanity checks
        if (workingDirectory is not null
            && !Directory.Exists(workingDirectory))
        {
            WriteError($"❌ Directory '{workingDirectory}' does not exist!");
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
                WriteError($"❌ Solution file '{solutionToCheck}' does not exist!");
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
                WriteError($"❌ Nuspec file '{solutionToCheck}' does not exist!");
                return 1;
            }
        }

        // grab working directory from solution, if it's empty
        if (workingDirectory is null)
        {
            workingDirectory = Path.GetDirectoryName(Path.GetFullPath(solutionToCheck));
        }

        // handle mscorlib library
        if (solutionToCheck.EndsWith("nanoFramework.CoreLibrary.sln", StringComparison.OrdinalIgnoreCase) ||
            solutionToCheck.EndsWith("nanoFramework.CoreLibrary.slnx", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("ℹ️  This is mscorlib, skipping this check!");
            return 0;
        }

        Console.WriteLine();
        WriteGroupStart($"🔍 Parsing '{Path.GetFileNameWithoutExtension(solutionToCheck)}'");

        // declare flags
        bool projectCheckFailed = true;
        bool nuspecCheckFailed = true;
        bool nuspecChecked = false;
        bool nuspecDependenciesToRemove = false;

        // check for nfproj
        if (!File.ReadAllText(solutionToCheck).Contains(".nfproj"))
        {
            WriteError($"❌ Solution file '{solutionToCheck}' doesn't have any nfproj file??");
            WriteGroupEnd();
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
        bool isSlnx = solutionToCheck.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase);

        // find ALL packages.config files in the solution projects
        var packageConfigs = Directory.GetFiles(workingDirectory, "packages.config", SearchOption.AllDirectories);

        // filter out excluded paths if provided
        if (excludePaths is not null)
        {
            var excludePathList = excludePaths.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            
            packageConfigs = packageConfigs.Where(config =>
            {
                foreach (var excludePath in excludePathList)
                {
                    // Check if exclude path is an absolute path
                    if (Path.IsPathRooted(excludePath))
                    {
                        // For absolute paths, check if config path starts with the exclude path
                        if (config.StartsWith(excludePath, StringComparison.OrdinalIgnoreCase))
                        {
                            return false;
                        }
                    }
                    else
                    {
                        // For relative paths, check if any path segment matches
                        var relativePath = Path.GetRelativePath(workingDirectory, config);
                        var pathSegments = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                        
                        if (pathSegments.Any(segment => segment.Equals(excludePath, StringComparison.OrdinalIgnoreCase)))
                        {
                            return false;
                        }
                    }
                }
                
                return true;
            }).ToArray();
        }

        Console.WriteLine($"  📦 Found {packageConfigs.Length} packages.config file(s)");

        // Build cache of package dependencies to avoid duplicate API calls
        // Use a generic .NET target framework for cache building
        BuildPackageCache(packageConfigs, NuGet.Frameworks.NuGetFramework.AnyFramework);

        // Parse slnx document once if needed (avoids repeated parsing for each package.config)
        XDocument slnxDoc = null;
        if (isSlnx)
        {
            slnxDoc = XDocument.Parse(slnFileContent);
        }

        foreach (var packageConfigFile in packageConfigs)
        {
            Console.WriteLine();
            WriteGroupStart($"📂 Project: {Path.GetFileName(Path.GetDirectoryName(packageConfigFile))}");

            var packageConfigRelPath = Path.GetRelativePath(workingDirectory, Directory.GetParent(packageConfigFile).FullName);

            string projectToCheck;
            string projectName;

            if (isSlnx)
            {
                // slnx is XML-based: <Project Path="folder\Project.nfproj" /> or <Project Path="Project.nfproj" />
                var projectElement = slnxDoc.Descendants("Project")
                    .FirstOrDefault(el =>
                    {
                        var path = el.Attribute("Path")?.Value;
                        if (path == null) return false;
                        
                        // Normalize separators BEFORE parsing so it works cross-platform
                        // (Path.GetDirectoryName won't recognize \ on non-Windows OSes)
                        path = path.Replace('\\', Path.DirectorySeparatorChar);
                        var dir = Path.GetDirectoryName(path) ?? string.Empty;
                        var normalizedRelPath = packageConfigRelPath.Replace('/', '\\').Replace('\\', Path.DirectorySeparatorChar);
                        
                        // Handle root-level projects (empty directory or ".")
                        if (string.IsNullOrEmpty(dir))
                        {
                            return normalizedRelPath == "." || string.IsNullOrEmpty(normalizedRelPath);
                        }
                        
                        return string.Equals(dir, normalizedRelPath, StringComparison.OrdinalIgnoreCase);
                    });

                if (projectElement == null)
                {
                    Console.WriteLine($"  ⚠️  Couldn't find a project matching this packages.config - skipping");
                    WriteGroupEnd();
                    continue;
                }

                var projectPath = projectElement.Attribute("Path").Value;
                // Normalize separators to match the current OS
                projectPath = projectPath.Replace('\\', Path.DirectorySeparatorChar);
                // Construct full path directly from working directory + relative path in slnx
                projectToCheck = Path.Combine(workingDirectory, projectPath);
                projectName = Path.GetFileNameWithoutExtension(projectPath);
            }
            else
            {
                var projectPathInSln = packageConfigRelPath == "."
                    ? ""
                    : packageConfigRelPath.Replace("\\", "\\\\")
                                         .Replace(".", "\\.")
                                         + "\\\\"; // add trailing \ to match whatever will be in the solution file

                var match = Regex.Match(slnFileContent, $"(?> = \\\")(?'projectname'[a-zA-Z0-9_.-]+)(?>\\\", \\\"{projectPathInSln})(?'projectpath'[a-zA-Z0-9_.-]+.nfproj)(\\\")");
                if (!match.Success)
                {
                    Console.WriteLine($"  ⚠️  Couldn't find a project matching this packages.config - skipping");
                    WriteGroupEnd();
                    continue;
                }

                projectToCheck = Directory.GetFiles(workingDirectory, match.Groups["projectpath"].Value, SearchOption.AllDirectories).FirstOrDefault();
                projectName = match.Groups["projectname"].Value;
            }

            // Guard against null projectToCheck (file not found on disk)
            if (projectToCheck is null)
            {
                Console.WriteLine($"  ⚠️  Couldn't resolve project file on disk - skipping");
                WriteGroupEnd();
                continue;
            }

            Console.WriteLine($"  📄 {Path.GetFileNameWithoutExtension(projectToCheck)}");

            string nuspecFileName = string.Empty;

            // set check flag
            bool checkNuspec = false;

            // try to find nuspec for this in case none was specified
            if (nuspecFile is null)
            {
                var projectFileNameWithoutExtension = Path.GetFileNameWithoutExtension(projectToCheck);
                var projectDirectory = Directory.GetParent(projectToCheck).FullName;

                // Define search locations and patterns to try
                var searchLocations = new[]
                {
                    (directory: workingDirectory, pattern: $"*{projectFileNameWithoutExtension}.nuspec"),
                    (directory: workingDirectory, pattern: $"*{projectName}.nuspec"),
                    (directory: projectDirectory, pattern: $"*{projectFileNameWithoutExtension}.nuspec"),
                    (directory: projectDirectory, pattern: $"*{projectName}.nuspec")
                };

                // Try each location and pattern
                foreach (var (directory, pattern) in searchLocations)
                {
                    nuspecFileName = Directory.GetFiles(directory, pattern, SearchOption.TopDirectoryOnly).FirstOrDefault();

                    if (nuspecFileName is not null)
                    {
                        Console.WriteLine($"  📋 Nuspec: '{Path.GetFileName(nuspecFileName)}'");

                        // load nuspec file
                        nuspecReader = GetNuspecReader(nuspecFileName);
                        break;
                    }
                }

                if (nuspecFileName is null)
                {
                    WriteWarning($"⚠️  No nuspec file found for project");
                    Console.WriteLine($"    Tried '*{Path.GetFileNameWithoutExtension(projectToCheck)}.nuspec' and '*{projectName}.nuspec'");

                    // make sure nuspec reader is null
                    nuspecReader = null;
                }
            }

            // load packages.config 
            var packageReader = new PackagesConfigReader(XDocument.Load(Path.Combine(Directory.GetParent(projectToCheck).FullName, "packages.config")));
            var projectFileDirectory = Directory.GetParent(projectToCheck).FullName;

            // filter out these packages: Nerdbank.GitVersioning
            var packageList = packageReader.GetPackages().Where(p => !p.IsDevelopmentDependency && !p.PackageIdentity.Id.Contains("Nerdbank.GitVersioning"));

            if (packageList.Any())
            {
                // list packages to check
                Console.WriteLine();
                Console.WriteLine("  📦 Packages to check:");
                foreach (var package in packageList)
                {
                    Console.WriteLine($"    • {package.PackageIdentity}");
                }
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
                    WriteError($"❌ Couldn't read assembly name from project file");
                    WriteGroupEnd();
                    return 1;
                }

                // reset flags
                projectCheckFailed = false;
                nuspecCheckFailed = false;
                nuspecDependenciesToRemove = false;
                bool refMissingFromNuspec = false;
                bool idMismatchInNuspec = false;
                string idFoundInNuspec = "";
                checkNuspec = true;

                // check all packages
                var packageIdentities = packageList.Select(package => package.PackageIdentity);

                foreach (var packageIdentity in packageIdentities)
                {
                    // get package name and target version
                    string packageName = packageIdentity.Id;
                    string packageVersion = packageIdentity.Version.ToNormalizedString();

                    Console.Write($"  🔄 {packageIdentity}... ");

                    bool packageFoundInProject = IsPackageReferencedInProject(projecFileContent, packageName, packageVersion);
                    if (!packageFoundInProject)
                    {
                        // flag failed check
                        projectCheckFailed = true;
                    }

                    // flag for nuspec missing
                    refMissingFromNuspec = true;

                    if (packageFoundInProject
                        && checkNuspec
                        && nuspecReader is not null)
                    {
                        // this check here tries to determine if the package ID and Title match with the assembly name
                        // it looks a bit convoluted but these variations are required to deal with:
                        // - libraries that have (or not) nanoFramework prefix
                        // - libraries that have variations on the ID, like System.Net.Http.Client/Server
                        // - libraries that have additional segments like nanoFramework.UnitsNet.AbsorbedDoseOfIonizingRadiation
                        
                        bool idMatches = nuspecReader.GetId().EndsWith(assemblyName, StringComparison.OrdinalIgnoreCase) ||
                                         nuspecReader.GetId().Equals(assemblyName, StringComparison.OrdinalIgnoreCase) ||
                                         nuspecReader.GetId().Contains($".{assemblyName}", StringComparison.OrdinalIgnoreCase);
                        
                        // Title check is more lenient - it's OK if title doesn't contain assembly name
                        // as long as ID matches, or if title is similar to ID
                        bool titleMatches = string.IsNullOrEmpty(nuspecReader.GetTitle()) || 
                                           nuspecReader.GetTitle().Contains(assemblyName, StringComparison.OrdinalIgnoreCase) ||
                                           nuspecReader.GetTitle().Equals(nuspecReader.GetId(), StringComparison.OrdinalIgnoreCase);
                        
                        if (!idMatches)
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

                    if (!packageFoundInProject)
                    {
                        Console.WriteLine();
                        WriteError($"❌ Couldn't find '{packageIdentity}' in '{Path.GetFileName(projectToCheck)}'");
                    }
                    else
                    {
                        Console.Write("nfproj ✅ ");

                        if (idMismatchInNuspec)
                        {
                            Console.WriteLine();
                            WriteError($"❌ Found in nuspec but declared dependency version doesn't match!");
                            Console.WriteLine($"    Expecting '{packageVersion}' but found '{idFoundInNuspec}'");
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
                                Console.WriteLine();
                                WriteError($"❌ Couldn't find '{packageName}' in nuspec!");

                                if (hintMessage is null)
                                {
                                    Console.WriteLine($"    Not listed in '{Path.GetFileName(nuspecFileName)}' and not a dependency of any listed package");
                                }
                                else
                                {
                                    Console.WriteLine($"    {hintMessage}");
                                }

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
                                        Console.WriteLine("nuspec ✅ not listed (transitive dependency)");
                                    }
                                    else
                                    {
                                        Console.WriteLine("nuspec ✅");
                                    }
                                }

                                if (analyseNuspec && !idMismatchInNuspec && !refMissingFromNuspec && packageName != dependencyPackageId && packageName != "nanoFramework.CoreLibrary")
                                {
                                    Console.WriteLine($"    💡 OK to remove - declared as dependency of '{dependencyPackageId}'");

                                    // flag this
                                    nuspecDependenciesToRemove = true;
                                }
                            }
                        }
                        else
                        {
                            Console.WriteLine("nuspec ✅");
                        }
                    }
                }
            }
            else
            {
                WriteError($"❌ Couldn't identify any NuGet packages to check!");
                WriteGroupEnd();
                return 1;
            }

            Console.WriteLine();
            WriteGroupEnd();

            if (projectCheckFailed || nuspecCheckFailed)
            {
                return 1;
            }

            if (analyseNuspec && nuspecDependenciesToRemove)
            {
                WriteWarning($"⚠️  Found nuspec declarations that could be removed.");
            }
        }

        if (!nuspecChecked)
        {
            Console.WriteLine();
            WriteError($"❌ Nuspec was not checked! Verify package ID/Title and/or assembly name.");
            Console.WriteLine();

            // exit with error code
            return 1;
        }

        Console.WriteLine();
        Console.WriteLine("✅ Versions check completed successfully!");

        // exit OK
        return 0;
    }

    /// <summary>
    /// Builds a cache of package dependency information for all packages across all config files.
    /// This eliminates duplicate NuGet API calls and significantly improves performance.
    /// </summary>
    private static void BuildPackageCache(string[] packageConfigs, NuGet.Frameworks.NuGetFramework targetFramework)
    {
        Console.WriteLine();
        WriteGroupStart($"📦 Building package dependency cache");

        // Collect all unique package identities across all config files
        HashSet<PackageIdentity> uniquePackages = new();

        foreach (var packageConfig in packageConfigs)
        {
            var packageReader = new PackagesConfigReader(XDocument.Load(packageConfig));
            var packageList = packageReader.GetPackages()
                .Where(p => !p.IsDevelopmentDependency && !p.PackageIdentity.Id.Contains("Nerdbank.GitVersioning"));

            foreach (var package in packageList)
            {
                uniquePackages.Add(package.PackageIdentity);
            }
        }

        Console.WriteLine($"  🔄 {uniquePackages.Count} unique package(s) to resolve");

        int cachedCount = 0;
        int failedCount = 0;

        // Fetch dependency info for each unique package
        foreach (var packageIdentity in uniquePackages)
        {
            if (_packageDependencyCache.ContainsKey(packageIdentity))
            {
                // Already cached
                continue;
            }

            try
            {
                var dependencyInfo = _dependencyInfoResourceNuGet.ResolvePackage(
                    packageIdentity,
                    targetFramework,
                    new SourceCacheContext(),
                    new NullLogger(),
                    CancellationToken.None).Result;

                if (dependencyInfo is not null)
                {
                    _packageDependencyCache[packageIdentity] = dependencyInfo;
                    cachedCount++;
                }
                else
                {
                    // Store null to indicate package wasn't found, avoiding repeated lookups
                    _packageDependencyCache[packageIdentity] = null;
                    failedCount++;
                }
            }
            catch (Exception ex)
            {
                WriteWarning($"⚠️  Failed to fetch dependency info for {packageIdentity}: {ex.Message}");
                // Store null to avoid retrying
                _packageDependencyCache[packageIdentity] = null;
                failedCount++;
            }
        }

        if (failedCount > 0)
        {
            Console.WriteLine($"  ✅ Cache built: {cachedCount} resolved, {failedCount} failed");
        }
        else
        {
            Console.WriteLine($"  ✅ Cache built: {cachedCount} package(s) resolved");
        }

        WriteGroupEnd();
    }

    /// <summary>
    /// Gets package dependency info from cache or fetches it from NuGet if not cached.
    /// </summary>
    private static SourcePackageDependencyInfo GetDependencyInfo(
        PackageIdentity packageIdentity,
        NuGet.Frameworks.NuGetFramework targetFramework)
    {
        // Check cache first
        if (_packageDependencyCache.TryGetValue(packageIdentity, out var cachedInfo))
        {
            return cachedInfo;
        }

        // Not in cache, fetch from NuGet (this should rarely happen if cache was built properly)
        try
        {
            var dependencyInfo = _dependencyInfoResourceNuGet.ResolvePackage(
                packageIdentity,
                targetFramework,
                new SourceCacheContext(),
                new NullLogger(),
                CancellationToken.None).Result;

            // Cache the result
            _packageDependencyCache[packageIdentity] = dependencyInfo;

            return dependencyInfo;
        }
        catch
        {
            // Cache null on failure to avoid retrying
            _packageDependencyCache[packageIdentity] = null;
            return null;
        }
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
            // 2nd round - use cache
            dependencyInfo = GetDependencyInfo(packageIdentity, targetFramework);

            bool dependencyFound = false;

            if (dependencyInfo is null)
            {
                // try to find it in NuGet - use cache
                dependencyInfo = GetDependencyInfo(packageIdentity, targetFramework);
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
            dependencyInfo = GetDependencyInfo(packageIdentity, targetFramework);

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

        // 2nd round - use cache
        dependencyInfo = GetDependencyInfo(packageIdentity, targetFramework);

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

    private static void DetectEnvironment()
    {
        // Check for Azure Pipelines environment first - takes priority
        _isAzureDevOps = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SYSTEM_TEAMFOUNDATIONCOLLECTIONURI"));

        // Check for GitHub Actions environment only if not already identified as Azure DevOps
        _isGitHubActions = !_isAzureDevOps && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"));
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

    private static NuspecReader GetNuspecReader(string nuspecFileName)
    {
        string nuspecAsText = File.ReadAllText(nuspecFileName);

        // It is valid to use the variable $version$ in nuspec
        // as it will be replaced with correct value when the package is created.
        // The replacement value is unique, so it will not match any literal version value.
        // The assumption is that all packages share the same value of $version$
        // at the time of packaging.
        nuspecAsText = nuspecAsText.Replace("$version$", "9.99.999.9999");

        return new NuspecReader(XDocument.Parse(nuspecAsText));
    }

    private static bool IsPackageReferencedInProject(string projectFileContent, string packageName, string packageVersion)
    {
        var packagePath = $"packages\\{packageName}.{packageVersion}";
        if (projectFileContent.Contains(packagePath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var packagePathForwardSlash = packagePath.Replace('\\', '/');
        if (projectFileContent.Contains(packagePathForwardSlash, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var normalizedPackageName = NormalizePackageOrReferenceName(packageName);

        try
        {
            var projectXml = XDocument.Parse(projectFileContent);
            var references = projectXml.Descendants().Where(x => x.Name.LocalName == "Reference");

            foreach (var reference in references)
            {
                var includeValue = reference.Attribute("Include")?.Value;
                if (!string.IsNullOrEmpty(includeValue))
                {
                    var includeName = includeValue.Split(',')[0].Trim();
                    if (ReferenceNameMatchesPackage(includeName, packageName, normalizedPackageName))
                    {
                        return true;
                    }
                }

                var hintPath = reference.Elements().FirstOrDefault(x => x.Name.LocalName == "HintPath")?.Value;
                if (!string.IsNullOrEmpty(hintPath))
                {
                    var assemblyFileName = Path.GetFileNameWithoutExtension(hintPath);
                    if (!string.IsNullOrEmpty(assemblyFileName)
                        && ReferenceNameMatchesPackage(assemblyFileName, packageName, normalizedPackageName))
                    {
                        return true;
                    }
                }
            }
        }
        catch
        {
            // if parsing fails keep default false outcome
        }

        return false;
    }

    private static bool ReferenceNameMatchesPackage(string referenceName, string packageName, string normalizedPackageName)
    {
        if (string.IsNullOrEmpty(referenceName))
        {
            return false;
        }

        return referenceName.Equals(packageName, StringComparison.OrdinalIgnoreCase)
            || referenceName.Equals(normalizedPackageName, StringComparison.OrdinalIgnoreCase)
            || NormalizePackageOrReferenceName(referenceName).Equals(normalizedPackageName, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePackageOrReferenceName(string value)
    {
        const string nanoFrameworkPrefix = "nanoFramework.";

        if (value.StartsWith(nanoFrameworkPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return value.Substring(nanoFrameworkPrefix.Length);
        }

        return value;
    }
}
