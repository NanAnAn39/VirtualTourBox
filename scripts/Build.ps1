param([string]$Runtime = "win-x64")
$ErrorActionPreference = "Stop"
if ($Runtime -ne "win-x64") { throw "This release script supports win-x64 only." }
$repoRoot = Split-Path -Parent $PSScriptRoot
$releasePath = Join-Path $repoRoot "dist/VirtualTourBox-v0.2.5-preview-win-x64"
$bridgePath = Join-Path $releasePath "BleHid"
function Invoke-DotNet {
    param([string[]]$Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed with exit code $LASTEXITCODE" }
}
Invoke-DotNet -Arguments @("run","--project",(Join-Path $repoRoot "tests/HoldGestureChecks"),"-c","Release")
Invoke-DotNet -Arguments @("publish",(Join-Path $repoRoot "src/VirtualTourBox/VirtualTourBox.csproj"),"-c","Release","-r",$Runtime,"--self-contained","true","-p:PublishSingleFile=true","-p:IncludeNativeLibrariesForSelfExtract=true","-p:DebugType=None","-p:DebugSymbols=false","-o",$releasePath)
Invoke-DotNet -Arguments @("publish",(Join-Path $repoRoot "third_party/windows-ble-hid/src/BleHid.Cli/BleHid.Cli.csproj"),"-c","Release","-r",$Runtime,"--self-contained","true","-p:PublishSingleFile=true","-p:IncludeNativeLibrariesForSelfExtract=true","-p:DebugType=None","-p:DebugSymbols=false","-o",$bridgePath)
foreach($capability in @("combo","status","hold","drag")) {
    Set-Content -LiteralPath (Join-Path $bridgePath "VirtualTourBox.$capability-enabled") -Value "Built from the included Virtual TourBox CLI source." -Encoding utf8
}
foreach($file in @("README.md","LICENSE.txt","THIRD_PARTY_NOTICES.md","CHANGELOG.md")) {
    Copy-Item -LiteralPath (Join-Path $repoRoot $file) -Destination $releasePath -Force
}
Copy-Item -LiteralPath (Join-Path $repoRoot "licenses") -Destination $releasePath -Recurse -Force
$upstreamLicensePath = Join-Path $releasePath "licenses/windows-ble-hid"
New-Item -ItemType Directory -Path $upstreamLicensePath -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $repoRoot "third_party/windows-ble-hid/LICENSE") -Destination $upstreamLicensePath -Force
Copy-Item -LiteralPath (Join-Path $repoRoot "assets") -Destination $releasePath -Recurse -Force
Copy-Item -LiteralPath (Join-Path $repoRoot "docs") -Destination $releasePath -Recurse -Force
Write-Output "Release directory: $releasePath"
