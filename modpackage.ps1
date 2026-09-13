$projectName = "ValheimRAFT"
$modName = "zolantris-$projectName"
$versionFile = "$PSScriptRoot\build\valheimraft_version.props"
$versionNumber = ([xml](Get-Content -Path $versionFile)).Project.ChildNodes.Version
$artifactName = "$modName-$versionNumber-Krakend"

$buildOutputDir = "$PSScriptRoot\build\bin\$projectName\Debug"
$publishDir = "$PSScriptRoot\build\bin\$projectName\publish"
if (Test-Path $publishDir) {
    Remove-Item -Path $publishDir -Recurse -Force
}
New-Item -Path $pluginPath -ItemType Directory | Out-Null

$stagingDir = "$publishDir\staging"
$pluginPath = "$stagingDir\BepInEx\plugins\$modName"


Copy-Item -Path "$buildOutputDir\*" -Destination $pluginPath -Recurse
Compress-Archive -Path "$stagingDir\BepInEx" -DestinationPath "$publishDir\$artifactName.zip"
