[CmdletBinding()]
param()

Trace-VstsEnteringInvocation $MyInvocation

Import-Module $PSScriptRoot\ps_modules\VstsTaskSdk

Import-VstsLocStrings "$PSScriptRoot\Task.json"

[System.Net.WebClient]$webClient = New-Object System.Net.WebClient
$webClient.UseDefaultCredentials = $true

function DownloadVsixFile($fileUrl, $downloadFileName)
{
    Write-Debug "Download VSIX file from $fileUrl to $downloadFileName"
    $webClient.DownloadFile($fileUrl,$downloadFileName)
}

# get extension information from Open VSIX Gallery feed
$vsixFeedXml = Join-Path  $($env:Agent_TempDirectory) "vs-extension-feed.xml"
$webClient.DownloadFile("http://vsixgallery.com/feed/author/nanoframework", $vsixFeedXml)
[xml]$feedDetails = Get-Content $vsixFeedXml

# find which entry corresponds to which VS version
for ($i = 0; $i -lt $feedDetails.feed.entry.Count; $i++) {

    if($feedDetails.feed.entry[$i].id -eq '455f2be5-bb07-451e-b351-a9faf3018dc9')
    {
        Write-Output "VS2019->$i"
        $idVS2019 = $i
    }
    elseif($feedDetails.feed.entry[$i].id -eq 'bf694e17-fa5f-4877-9317-6d3664b2689a')
    {
        Write-Output "VS2022->$i"
        $idVS2022 = $i
    }
    elseif($feedDetails.feed.entry[$i].id -eq '47973986-ed3c-4b64-ba40-a9da73b44ef7')
    {
        Write-Output "VS2017->$i"
        $idVS2017 = $i
    }
    else {
        Write-Output "?????[$i]"
    }
}

# find which VS version is installed
$VsWherePath = "${env:PROGRAMFILES(X86)}\Microsoft Visual Studio\Installer\vswhere.exe"

Write-Output "VsWherePath is: $VsWherePath"

$VsInstance = $(&$VSWherePath -latest -property displayName)

Write-Output "Latest VS is: $VsInstance"

# grab extension details according to VS version, starting from VS2022 down to VS2017
if($vsInstance.Contains('2022'))
{
    $extensionUrl = $feedDetails.feed.entry[$idVS2022].content.src
    $vsixPath = Join-Path  $($env:Agent_TempDirectory) "nanoFramework.Tools.VS2022.Extension.zip"
    $extensionVersion = $feedDetails.feed.entry[$idVS2022].Vsix.Version
}
elseif($vsInstance.Contains('2019'))
{
    $extensionUrl = $feedDetails.feed.entry[$idVS2019].content.src
    $vsixPath = Join-Path  $($env:Agent_TempDirectory) "nanoFramework.Tools.VS2019.Extension.zip"
    $extensionVersion = $feedDetails.feed.entry[$idVS2019].Vsix.Version
}
elseif($vsInstance.Contains('2017'))
{
    $extensionUrl = $feedDetails.feed.entry[$idVS2017].content.src
    $vsixPath = Join-Path  $($env:Agent_TempDirectory) "nanoFramework.Tools.VS2017.Extension.zip"
    $extensionVersion = $feedDetails.feed.entry[$idVS2017].Vsix.Version
}

# download VS extension
DownloadVsixFile $extensionUrl $vsixPath

# get path to 7zip
$sevenZip = "$PSScriptRoot\7zip\7z.exe"

# unzip extension
Write-Debug "Unzip extension content"
Invoke-VstsTool -FileName $sevenZip -Arguments " x $vsixPath -bd -o$env:Agent_TempDirectory\nf-extension" > $null

# copy build files to msbuild location
$VsPath = $(&$VsWherePath -latest -property installationPath)

Write-Debug "Copy build files to msbuild location"

$msbuildPath = $VsPath + "\MSBuild"

Copy-Item -Path "$env:Agent_TempDirectory\nf-extension\`$MSBuild\nanoFramework" -Destination $msbuildPath -Recurse

Write-Output "Installed VS extension v$extensionVersion"

Trace-VstsLeavingInvocation $MyInvocation
