# Second step of a mscorlib update
# Can only run after the first step is completed and all Nuget packages are available

[CmdletBinding()]
param (
    [Parameter(Mandatory)]
    [string]$previous_CorlibVersion,
    [Parameter(Mandatory)]
    [string]$new_CorlibVersion,
    [Parameter(Mandatory)]
    [string]$previous_RuntimeEventsVersion,
    [Parameter(Mandatory)]
    [string]$new_RuntimeEventsVersion,
    [Parameter(Mandatory)]
    [string]$previous_StorageStreamsVersion,
    [Parameter(Mandatory)]
    [string]$new_StorageStreamsVersion,
    [Parameter(Mandatory)]
    [string]$previous_RuntimeNativeVersion,
    [Parameter(Mandatory)]
    [string]$new_RuntimeNativeVersion,
    [Parameter(Mandatory)]
    [string]$previous_DevicesGpioVersion,
    [Parameter(Mandatory)]
    [string]$new_DevicesGpioVersion
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
    $filecontent -replace $previous_CorlibVersion, $new_CorlibVersion -replace $previous_RuntimeEventsVersion, $new_RuntimeEventsVersion | Out-File $file -Encoding utf8
}

# commit changes
git add -A 2>&1
git commit -m "Update Nugets" -m "- Update mscorlib Nuget to $new_CorlibVersion" -m "- Update Runtime.Events Nuget to $new_RuntimeEventsVersion"   -q
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
    $filecontent -replace $previous_CorlibVersion, $new_CorlibVersion -replace $previous_StorageStreamsVersion, $new_StorageStreamsVersion | Out-File $file -Encoding utf8
}

# commit changes
git add -A 2>&1
git commit -m "Update Nugets" -m "- Update mscorlib Nuget to $new_CorlibVersion" -m "- Update Storage.Streams Nuget to $new_StorageStreamsVersion"   -q
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
    $filecontent -replace $previous_CorlibVersion, $new_CorlibVersion -replace $previous_RuntimeEventsVersion, $new_RuntimeEventsVersion -replace $previous_RuntimeNativeVersion, $new_RuntimeNativeVersion | Out-File $file -Encoding utf8
}

# commit changes
git add -A 2>&1
git commit -m "Update Nugets" -m "- Update mscorlib Nuget to $new_CorlibVersion" -m "- Update Runtime.Events Nuget to $new_RuntimeEventsVersion" -m "- Update Runtime.Native Nuget to $new_RuntimeNativeVersion"  -q
git push --set-upstream origin update_nugets
git push origin -q

# move back to home folder
cd ..
