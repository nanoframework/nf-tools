# Second step of a mscorlib update
# Can only run after the first step is completed and all Nuget packages are available

[CmdletBinding()]
param (
    [Parameter(Mandatory)]
    [string]$previousCorlibVersion,
    [Parameter(Mandatory)]
    [string]$newCorlibVersion,
    [Parameter(Mandatory)]
    [string]$previousRuntimeEventsVersion,
    [Parameter(Mandatory)]
    [string]$newRuntimeEventsVersion,
    [Parameter(Mandatory)]
    [string]$previousStorageStreamsVersion,
    [Parameter(Mandatory)]
    [string]$newStorageStreamsVersion,
    [Parameter(Mandatory)]
    [string]$previousRuntimeNativeVersion,
    [Parameter(Mandatory)]
    [string]$newRuntimeNativeVersion
)

PowerShellGet\Install-Module posh-git -Scope CurrentUser -Force 

######################
# Windows.Devices.Gpio

# clone repo (only a shallow clone with last commit)
git clone https://github.com/nanoframework/lib-Windows.Devices.Gpio -b develop --depth 1 -q
cd lib-Windows.Devices.Gpio
git checkout -b update_nugets develop -q

# find files to update
$filesToUpdateCollection = (Get-ChildItem -Include *.nfproj, *.nuproj, packages.config -Recurse)

# perform the replace of the Nuget version on all files
foreach($file in $filesToUpdateCollection)
{
    $filecontent = Get-Content($file)
    attrib $file -r

    # replace mscorlib and RuntimeEvents
    $filecontent -replace $previousCorlibVersion, $newCorlibVersion -replace $previousRuntimeEventsVersion, $newRuntimeEventsVersion | Out-File $file -Encoding utf8
}

# commit changes
git add -A 2>&1
git commit -m "Update Nugets" -m "- Update mscorlib Nuget to $newCorlibVersion" -m "- Update Runtime.Events Nuget to $newRuntimeEventsVersion"   -q
git push --set-upstream origin update_nugets
git push origin -q

# move back to home folder
cd ..


#####################################
# Windows.Devices.SerialCommunication

# clone repo (only a shallow clone with last commit)
git clone https://github.com/nanoframework/lib-Windows.Devices.SerialCommunication -b develop --depth 1 -q
cd lib-Windows.Devices.SerialCommunication
git checkout -b update_nugets develop -q

# find files to update
$filesToUpdateCollection = (Get-ChildItem -Include *.nfproj, *.nuproj, packages.config -Recurse)

# perform the replace of the Nuget version on all files
foreach($file in $filesToUpdateCollection)
{
    $filecontent = Get-Content($file)
    attrib $file -r

    # replace mscorlib and Storage.Streams
    $filecontent -replace $previousCorlibVersion, $newCorlibVersion -replace $previousStorageStreamsVersion, $newStorageStreamsVersion | Out-File $file -Encoding utf8
}

# commit changes
git add -A 2>&1
git commit -m "Update Nugets" -m "- Update mscorlib Nuget to $newCorlibVersion" -m "- Update Storage.Streams Nuget to $newStorageStreamsVersion"   -q
git push --set-upstream origin update_nugets
git push origin -q

# move back to home folder
cd ..


#####################################
# System.Net

# clone repo (only a shallow clone with last commit)
git clone https://github.com/nanoframework/lib-System.Net -b develop --depth 1 -q
cd lib-System.Net
git checkout -b update_nugets develop -q

# find files to update
$filesToUpdateCollection = (Get-ChildItem -Include *.nfproj, *.nuproj, packages.config -Recurse)

# perform the replace of the Nuget version on all files
foreach($file in $filesToUpdateCollection)
{
    $filecontent = Get-Content($file)
    attrib $file -r

    # replace mscorlib, RuntimeEvents and Runtime.Native
    $filecontent -replace $previousCorlibVersion, $newCorlibVersion -replace $previousRuntimeEventsVersion, $newRuntimeEventsVersion -replace $previousRuntimeNativeVersion, $newRuntimeNativeVersion | Out-File $file -Encoding utf8
}

# commit changes
git add -A 2>&1
git commit -m "Update Nugets" -m "- Update mscorlib Nuget to $newCorlibVersion" -m "- Update Runtime.Events Nuget to $newRuntimeEventsVersion" -m "- Update Runtime.Native Nuget to $newRuntimeNativeVersion"  -q
git push --set-upstream origin update_nugets
git push origin -q

# move back to home folder
cd ..
