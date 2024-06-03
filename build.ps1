# Stop executing script if a cmdlet fails
$ErrorActionPreference = "Stop"

$publishFolder = "Publish"

Write-Output "`nDeleting existing Publish folder..."
if (Test-Path $publishFolder) {
    Remove-Item $publishFolder -Recurse -Force
}

Write-Output "`nStarting build..."
dotnet publish AssEmbly.DebuggerGUI.csproj -p:PublishProfile="Properties/PublishProfiles/FolderProfile.pubxml" -p:TreatWarningsAsErrors=true -warnaserror

if ($LastExitCode -ne 0) {
    exit $LastExitCode
}

Write-Output ""
$zipName = "AssEmbly.DebuggerGUI.zip"
Write-Output "Compressing into $zipName..."
$zipPath = Join-Path $publishFolder $zipName
Get-ChildItem -Path $publishFolder, "LICENSE" -Exclude "*.pdb" |
    Compress-Archive -DestinationPath $zipPath -CompressionLevel Optimal

Write-Output "Done."
