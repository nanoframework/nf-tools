# Copyright (c) .NET Foundation and Contributors
# See LICENSE file in the project root for full license information.

[CmdletBinding()]
param()

Trace-VstsEnteringInvocation $MyInvocation

Import-Module $PSScriptRoot\ps_modules\VstsTaskSdk

Import-VstsLocStrings "$PSScriptRoot\Task.json"

# Get the inputs2
[string]$gitHubToken = Get-VstsInput -Name GitHubToken
[bool]$isPreview = Get-VstsInput -Name UsePreview -AsBool

function DownloadVsixFile($fileUrl, $downloadFileName)
{
    Write-Debug "  ⬇️  Downloading from: $fileUrl"
    Invoke-WebRequest -Uri $fileUrl -OutFile $downloadFileName -UseBasicParsing
}

$tempDir = $($env:Agent_TempDirectory)

# Find which VS version is installed
$VsWherePath = "${env:PROGRAMFILES(X86)}\Microsoft Visual Studio\Installer\vswhere.exe"

Write-Debug "  🔍 vswhere: $VsWherePath"

$VsInstance = $(&$VSWherePath -latest -property displayName)

Write-Output "🖥️  VS instance: $VsInstance"

$extensionSource = "unknown"

# handle preview version
if($isPreview -eq $true)
{
    Write-Output "🔍 Installing PREVIEW version of the extension"
    $extensionSource = "VSIX Gallery"

    # get extension information from Open VSIX Gallery feed
    $vsixFeedXml = Join-Path $($env:Agent_TempDirectory) "vs-extension-feed.xml"
    Invoke-WebRequest -Uri "https://www.vsixgallery.com/feed/author/nanoframework" -OutFile $vsixFeedXml -UseBasicParsing
    [xml]$feedDetails = Get-Content $vsixFeedXml -Raw

    $feedEntryCount = @($feedDetails.feed.entry).Count
    Write-Debug "  📡 Feed: $feedEntryCount entries found"

    # Define the extension IDs
    $vs2019Id = "455f2be5-bb07-451e-b351-a9faf3018dc9"
    $vs2022Id = "bf694e17-fa5f-4877-9317-6d3664b2689a"

    # feed list VS2019 and VS2022 extensions using the extension ID

    # VS2026 and VS2022 (use same extension)
    if($vsInstance.Contains('2026') -or $vsInstance.Contains('2022'))
    {
        Write-Debug "  🔍 Looking for VS2022/VS2026 extension (ID: $vs2022Id)"
        $vs2022Entry = $feedDetails.feed.entry | Where-Object { $_.Vsix.Id -eq $vs2022Id }

        if($vs2022Entry)
        {
            $extensionUrl = $vs2022Entry.content.src
            Write-Debug "  📦 Found: v$($vs2022Entry.Vsix.Version) - $extensionUrl"
            if($vsInstance.Contains('2026'))
            {
                $vsixPath = Join-Path $tempDir "nanoFramework.Tools.VS2026.Extension.zip"
            }
            else
            {
                $vsixPath = Join-Path $tempDir "nanoFramework.Tools.VS2022.Extension.zip"
            }
            $extensionVersion = $vs2022Entry.Vsix.Version
        }
        else
        {
            Write-Error "VS2022/VS2026 extension with ID '$vs2022Id' not found in VSIX gallery feed. Cannot continue."
            exit 1
        }
    }
    elseif($vsInstance.Contains('2019'))
    {
        Write-Debug "  🔍 Looking for VS2019 extension (ID: $vs2019Id)"
        $vs2019Entry = $feedDetails.feed.entry | Where-Object { $_.Vsix.Id -eq $vs2019Id }

        if($vs2019Entry)
        {
            $extensionUrl = $vs2019Entry.content.src
            Write-Debug "  📦 Found: v$($vs2019Entry.Vsix.Version) - $extensionUrl"
            $vsixPath = Join-Path $tempDir "nanoFramework.Tools.VS2019.Extension.zip"
            $extensionVersion = $vs2019Entry.Vsix.Version
        }
        else
        {
            Write-Error "VS2019 extension with ID '$vs2019Id' not found in VSIX gallery feed. Cannot continue."
            exit 1
        }
    }
    else
    {
        Write-Error "Unsupported VS instance '$vsInstance'. Expected display name to contain '2019', '2022', or '2026'."
        exit 1
    }
}
else
{
    $extensionSource = "GitHub"

    # Build headers for the request
    $headers = @{
        "User-Agent"    = "request"
        "Accept"        = "application/vnd.github.v3+json"
        "ContentType"   = "application/json"
    }

    if($gitHubToken)
    {
        Write-Debug "  🔑 Adding GitHub authentication header"
        $headers["Authorization"] = "Bearer $gitHubToken"
    }

    Write-Debug "  🌐 Fetching latest releases from GitHub"

    # Use Invoke-WebRequest to get the release JSON
    $response = Invoke-WebRequest -Uri 'https://api.github.com/repos/nanoframework/nf-Visual-Studio-extension/releases?per_page=10' -Headers $headers -UseBasicParsing

    # Convert the response content to JSON objects
    $releases = $response.Content | ConvertFrom-Json

    # Filter out draft releases
    $finalReleases = $releases | Where-Object { -not $_.draft }

    # Extract tags using the tag_name property
    $vs2022Release = $finalReleases | Where-Object { $_.tag_name -match '^v2022\.\d+\.\d+\.\d+$' } | Select-Object -First 1
    if($vs2022Release)
    {
        $vs2022Tag = $vs2022Release.tag_name
        Write-Debug "  📦 Latest VS2022 release: $vs2022Tag"
    }
    else
    {
        Write-Debug "  ⚠️  No VS2022 release found (pattern: v2022.x.x.x)"
    }

    $vs2019Release = $finalReleases | Where-Object { $_.tag_name -match '^v2019\.\d+\.\d+\.\d+$' } | Select-Object -First 1
    if($vs2019Release)
    {
        $vs2019Tag = $vs2019Release.tag_name
        Write-Debug "  📦 Latest VS2019 release: $vs2019Tag"
    }
    else
    {
        Write-Debug "  ⚠️  No VS2019 release found (pattern: v2019.x.x.x)"
    }

    # Get extension details according to VS version, starting from VS2026/VS2022 down to VS2019
    if($vsInstance.Contains('2026') -or $vsInstance.Contains('2022'))
    {
        if(-not $vs2022Tag)
        {
            Write-Error "No VS2022 release found on GitHub. Cannot install extension for '$vsInstance'."
            exit 1
        }
        Write-Debug "  📥 VS2022/VS2026 extension selected (tag: $vs2022Tag)"
        $extensionUrl = "https://github.com/nanoframework/nf-Visual-Studio-extension/releases/download/$vs2022Tag/nanoFramework.Tools.VS2022.Extension.vsix"
        if($vsInstance.Contains('2026'))
        {
            $vsixPath = Join-Path $tempDir "nanoFramework.Tools.VS2026.Extension.zip"
        }
        else
        {
            $vsixPath = Join-Path $tempDir "nanoFramework.Tools.VS2022.Extension.zip"
        }
        $extensionVersion = $vs2022Tag
    }
    elseif($vsInstance.Contains('2019'))
    {
        if(-not $vs2019Tag)
        {
            Write-Error "No VS2019 release found on GitHub. Cannot install extension for '$vsInstance'."
            exit 1
        }
        Write-Debug "  📥 VS2019 extension selected (tag: $vs2019Tag)"
        $extensionUrl = "https://github.com/nanoframework/nf-Visual-Studio-extension/releases/download/$vs2019Tag/nanoFramework.Tools.VS2019.Extension.vsix"
        $vsixPath = Join-Path $tempDir "nanoFramework.Tools.VS2019.Extension.zip"
        $extensionVersion = $vs2019Tag
    }
    else
    {
        Write-Error "Unsupported VS instance '$vsInstance'. Expected display name to contain '2019', '2022', or '2026'."
        exit 1
    }
}

# Download VS extension
Write-Debug "⬇️  Downloading v$extensionVersion from: $extensionUrl"
DownloadVsixFile $extensionUrl $vsixPath
Write-Debug "  ✔️  Saved to: $vsixPath"

# Unzip extension
Write-Debug "📂 Extracting VS extension"
Expand-Archive -LiteralPath $vsixPath -DestinationPath $tempDir\nf-extension\

# copy build files to msbuild location
$VsPath = $(&$VsWherePath -latest -property installationPath)

Write-Debug "🔧 Copying MSBuild files to: $VsPath\MSBuild"

$msbuildPath = $VsPath + "\MSBuild"

Copy-Item -Path "$tempDir\nf-extension\`$MSBuild\nanoFramework" -Destination $msbuildPath -Recurse

Write-Output "✅ Installed VS extension $extensionVersion (source: $extensionSource)"

Trace-VstsLeavingInvocation $MyInvocation
