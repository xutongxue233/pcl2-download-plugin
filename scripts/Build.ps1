[CmdletBinding()]
param(
    [string]$LauncherPath,
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release'
)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ($LauncherPath) {
    $launcherHash = (Get-FileHash -LiteralPath $LauncherPath -Algorithm SHA256).Hash
    if ($launcherHash -ne '6B6E1260CB5A1D6A303855F1C60CAB3D0BC1AEBBA7E40A964180188B271E7386') {
        throw 'The provided executable is not the supported official PCL 2.99.0.0 build.'
    }
}

# Only our DLL projects are built. No launcher source or binary is required to compile them.
$pluginProject = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src\DownloadRestore') -Filter '*.csproj'
if (@($pluginProject).Count -ne 1) { throw 'Expected exactly one DownloadRestore project.' }
$bootstrapProject = Join-Path $projectRoot 'src\Bootstrap\PCL.PluginBootstrap.csproj'
foreach ($buildProject in @($bootstrapProject, $pluginProject.FullName)) {
    & dotnet build $buildProject --configuration $Configuration --nologo
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $buildProject" }
}

$payload = Join-Path $projectRoot 'artifacts\payload'
$pluginPayload = Join-Path $payload 'PCL\Plugins\DownloadRestore'
[IO.Directory]::CreateDirectory($pluginPayload) | Out-Null
$bootstrapOutput = Join-Path $projectRoot "src\Bootstrap\bin\$Configuration\net48"
$pluginOutput = Join-Path $projectRoot "src\DownloadRestore\bin\$Configuration\net48"
Copy-Item -LiteralPath (Join-Path $bootstrapOutput 'PCL.PluginBootstrap.dll') -Destination $payload -Force
# Explicit allowlist: never stage the launcher EXE or extracted embedded libraries.
foreach ($fileName in @('PCL.DownloadRestore.dll', '0Harmony.dll')) {
    $inputFile = Join-Path $pluginOutput $fileName
    if (-not (Test-Path -LiteralPath $inputFile)) { throw "Missing runtime dependency: $inputFile" }
    Copy-Item -LiteralPath $inputFile -Destination $pluginPayload -Force
}
$allowedPaths = @(
    'PCL.PluginBootstrap.dll',
    'PCL\Plugins\DownloadRestore\PCL.DownloadRestore.dll',
    'PCL\Plugins\DownloadRestore\0Harmony.dll'
)
foreach ($stagedFile in Get-ChildItem -LiteralPath $payload -Recurse -File -Force) {
    $relativePath = $stagedFile.FullName.Substring($payload.Length + 1)
    if ($relativePath -notin $allowedPaths) { throw "Unexpected payload file; refusing to package it: $relativePath" }
}
Write-Host "Plugin-only payload prepared: $payload"
