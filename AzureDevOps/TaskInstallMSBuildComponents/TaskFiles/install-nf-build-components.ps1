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

# get extension information from MyGet VSIX feed
$vsixFeedXml = Join-Path  $($env:Agent_TempDirectory) "vs-extension-feed.xml"
$webClient.DownloadFile("https://www.myget.org/F/nanoframework-dev/vsix", $vsixFeedXml)
[xml]$feedDetails = Get-Content $vsixFeedXml

Write-Output "Host OS is $([System.Environment]::OSVersion.Version)"

# feed list VS2017 and VS2019 extensions
# index 0 is for VS2017, running on Windows Server 2016
if([System.Environment]::OSVersion.Version.Major -eq "10")
{
    $extensionUrl = $feedDetails.feed.entry[0].content.src
    $vsixPath = Join-Path  $($env:Agent_TempDirectory) "nanoFramework.Tools.VS2017.Extension.zip"
    # this was the original download URL that provides the last version, but the marketplace is blocking access to it
    # "https://marketplace.visualstudio.com/_apis/public/gallery/publishers/vs-publisher-1470366/vsextensions/nanoFrameworkVS2017Extension/0/vspackage 
}

# index 1 is for VS2019, running on Windows Server 2019
if([System.Environment]::OSVersion.Version.Major -eq "11")
{
    $extensionUrl = $feedDetails.feed.entry[1].content.src
    $vsixPath = Join-Path  $($env:Agent_TempDirectory) "nanoFramework.Tools.VS2019.Extension.zip"
}

# download VS extension
DownloadVsixFile $extensionUrl $vsixPath

# get path to 7zip
$sevenZip = "$PSScriptRoot\7zip\7z.exe"

# unzip extension
Write-Debug "Unzip extension content"
Invoke-VstsTool -FileName $sevenZip -Arguments " x $vsixPath -bd -o$env:Agent_TempDirectory\nf-extension" > $null

# copy build files to msbuild location
Write-Debug "Copy build files to msbuild location"
$msbuildPath = "C:\Program Files (x86)\Microsoft Visual Studio\2017\Enterprise\MSBuild"
Copy-Item -Path "$env:Agent_TempDirectory\nf-extension\`$MSBuild\nanoFramework" -Destination $msbuildPath -Recurse

Trace-VstsLeavingInvocation $MyInvocation
