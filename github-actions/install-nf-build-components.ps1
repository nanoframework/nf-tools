[System.Net.WebClient]$webClient = New-Object System.Net.WebClient
$webClient.UseDefaultCredentials = $true

function DownloadVsixFile($fileUrl, $downloadFileName)
{
    Write-Host "Download VSIX file from $fileUrl to $downloadFileName"
    $webClient.DownloadFile($fileUrl,$downloadFileName)
}

"Downloaded extension" | Write-Host

$tempDir = $Env:RUNNER_TEMP

# Get extension information from VSIX Gallery feed
$vsixFeedXml = Join-Path -Path $tempDir -ChildPath "vs-extension-feed.xml"
$webClient.DownloadFile("http://vsixgallery.com/feed/author/nanoframework", $vsixFeedXml)
[xml]$feedDetails = Get-Content $vsixFeedXml

# Find which VS version is installed
$VsWherePath = "${env:PROGRAMFILES(X86)}\Microsoft Visual Studio\Installer\vswhere.exe"

Write-Output "VsWherePath is: $VsWherePath"

$VsInstance = $(&$VSWherePath -latest -property displayName) | Write-Host

$idVS2019 = 0 #Should probably target the specific VISX ID or "title" since this can change over time (and will likely be needed for VS2021).
#$idVS2017 = 1

$extensionUrl = $feedDetails.feed.entry[$idVS2019].content.src
$vsixPath = Join-Path -Path $tempDir -ChildPath "nf-extension.zip"
$extensionVersion = $feedDetails.feed.entry[$idVS2019].Vsix.Version

# Download VS extension
DownloadVsixFile $extensionUrl $vsixPath

# unzip extension
Write-Host "Unzip extension content"
Expand-Archive -LiteralPath $vsixPath -DestinationPath $tempDir\nf-extension\ | Write-Host

# Copy build files to msbuild location
$VsPath = $(&$VsWherePath -latest -property installationPath)

Write-Host "Copy build files to msbuild location"
$msbuildPath = Join-Path -Path $VsPath -ChildPath "\MSBuild"
Copy-Item -Path "$tempDir\nf-extension\`$MSBuild\nanoFramework" -Destination $msbuildPath -Recurse

Write-Host "Installed VS extension v$extensionVersion"
