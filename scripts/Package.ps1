[CmdletBinding()]
param(
    [string] $PayloadPath,
    [string] $OutputPath,
    [string] $SourceOutputPath
)
. (Join-Path $PSScriptRoot 'Common.ps1')
if (-not $PayloadPath) { $PayloadPath = Join-Path $PSScriptRoot '..\artifacts\payload' }
if (-not $OutputPath) { $OutputPath = Join-Path $PSScriptRoot '..\artifacts\PCL.DownloadRestore-2.99.0.0.zip' }
if (-not $SourceOutputPath) { $SourceOutputPath = Join-Path $PSScriptRoot '..\artifacts\PCL.DownloadRestore-Source.zip' }
$projectRoot = Get-FullPath (Join-Path $PSScriptRoot '..')
$PayloadPath = Assert-SafePath $PayloadPath
$OutputPath = Assert-SafePath $OutputPath
$SourceOutputPath = Assert-SafePath $SourceOutputPath
$outputDirectory = [IO.Path]::GetDirectoryName($OutputPath)
$null = New-Item -ItemType Directory -Path $outputDirectory -Force
$staging = Assert-SafePath (Join-Path $outputDirectory ('.package-' + [Guid]::NewGuid().ToString('N'))) $outputDirectory
$allowedPayload = @('PCL.PluginBootstrap.dll', 'PCL\Plugins\DownloadRestore\PCL.DownloadRestore.dll', 'PCL\Plugins\DownloadRestore\0Harmony.dll')
foreach ($required in $allowedPayload) {
    $source = Assert-SafePath (Join-Path $PayloadPath $required) $PayloadPath
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Build the payload first. Missing: $source" }
}
foreach ($item in @(Get-ChildItem -LiteralPath $PayloadPath -Recurse -Force)) {
    $null = Assert-SafePath $item.FullName $PayloadPath
    if ($item.PSIsContainer) { continue }
    $relative = $item.FullName.Substring($PayloadPath.Length + 1)
    if ($relative -notin $allowedPayload) { throw "Only the three documented runtime DLLs may be distributed. Unexpected payload: $relative" }
}
try {
    $null = New-Item -ItemType Directory -Path $staging
    $null = New-Item -ItemType Directory -Path (Join-Path $staging 'scripts')
    $null = New-Item -ItemType Directory -Path (Join-Path $staging 'licenses')
    foreach ($name in @('Install.cmd', 'Uninstall.cmd')) {
        [IO.File]::Copy((Join-Path $projectRoot $name), (Join-Path $staging $name), $false)
    }
    [IO.File]::Copy((Join-Path $projectRoot 'docs\LEGACY-SCRIPT-INSTALL.md'), (Join-Path $staging 'README.md'), $false)
    $validationReport = Join-Path $projectRoot 'VALIDATION.md'
    if (Test-Path -LiteralPath $validationReport -PathType Leaf) {
        $null = Assert-SafePath $validationReport $projectRoot
        [IO.File]::Copy($validationReport, (Join-Path $staging 'VALIDATION.md'), $false)
    }
    foreach ($name in @('Common.ps1', 'Install.ps1', 'Uninstall.ps1')) {
        [IO.File]::Copy((Join-Path $PSScriptRoot $name), (Join-Path $staging ('scripts\' + $name)), $false)
    }
    $licenseRoot = Join-Path $projectRoot 'licenses'
    foreach ($name in @('PCL-LICENCE.txt', 'Harmony-LICENSE.txt')) {
        $source = Assert-SafePath (Join-Path $licenseRoot $name) $projectRoot
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Missing license notice: $source" }
        [IO.File]::Copy($source, (Join-Path $staging ('licenses\' + $name)), $false)
    }
    $originEvidence = Join-Path $projectRoot 'tests\official-build.json'
    if (Test-Path -LiteralPath $originEvidence -PathType Leaf) {
        $null = Assert-SafePath $originEvidence $projectRoot
        [IO.File]::Copy($originEvidence, (Join-Path $staging 'official-build.json'), $false)
    }
    Copy-Item -LiteralPath $PayloadPath -Destination (Join-Path $staging 'payload') -Recurse
    $entries = @()
    foreach ($file in @(Get-ChildItem -LiteralPath $staging -Recurse -File | Sort-Object FullName)) {
        $relative = $file.FullName.Substring($staging.Length + 1).Replace('\', '/')
        $entries += (Get-FileHashValue $file.FullName) + '  ' + $relative
    }
    [IO.File]::WriteAllLines((Join-Path $staging 'SHA256SUMS.txt'), $entries, (New-Object Text.UTF8Encoding($false)))
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    # The ZIP is created outside the staged directory to avoid including itself.
    $temporaryZip = Join-Path $outputDirectory ('.distribution-' + [Guid]::NewGuid().ToString('N') + '.zip')
    try {
        [IO.Compression.ZipFile]::CreateFromDirectory($staging, $temporaryZip, [IO.Compression.CompressionLevel]::Optimal, $false)
        $zip = [IO.Compression.ZipFile]::OpenRead($temporaryZip)
        try {
            foreach ($entry in $zip.Entries) {
                if ($entry.FullName -match '(?i)\.exe$|(^|/)refs/|(^|/)\.install-state\.json$|(^|/)\.original-exe\.config$') { throw "Forbidden archive entry: $($entry.FullName)" }
            }
        } finally { $zip.Dispose() }
        [IO.File]::Copy($temporaryZip, $OutputPath, $true)
    } finally { if (Test-Path -LiteralPath $temporaryZip) { Remove-Item -LiteralPath $temporaryZip -Force } }
    Write-Host "Created plugin-only package: $OutputPath"
    Write-Host "SHA256: $(Get-FileHashValue $OutputPath)"
} finally {
    if (Test-Path -LiteralPath $staging) {
        $null = Assert-SafePath $staging $outputDirectory
        Remove-Item -LiteralPath $staging -Recurse -Force
    }
}

$sourceOutputDirectory = [IO.Path]::GetDirectoryName($SourceOutputPath)
$null = New-Item -ItemType Directory -Path $sourceOutputDirectory -Force
$sourceStaging = Assert-SafePath (Join-Path $sourceOutputDirectory ('.source-package-' + [Guid]::NewGuid().ToString('N'))) $sourceOutputDirectory
$sourceZip = Join-Path $sourceOutputDirectory ('.source-distribution-' + [Guid]::NewGuid().ToString('N') + '.zip')
try {
    $null = New-Item -ItemType Directory -Path $sourceStaging
    $allowedExtensions = @('.cs', '.csproj', '.xaml', '.ttf', '.ps1', '.cmd', '.md', '.txt', '.json', '.config', '.manifest', '.resx', '.ico', '.props', '.targets', '.sln', '.yml', '.yaml', '.gitignore', '.editorconfig')
    foreach ($file in @(Get-ChildItem -LiteralPath $projectRoot -Recurse -File -Force)) {
        $relative = $file.FullName.Substring($projectRoot.Length + 1)
        if ($relative -match '(^|[\\/])(artifacts|refs|references|\.tools|\.git|\.vs|\.idea|\.analysis|bin|obj|test-results|TestResults|payload|packages|\.packages|deps|node_modules)([\\/]|$)') { continue }
        if ($file.Extension -notin $allowedExtensions -and $file.Name -notin @('LICENSE', 'LICENCE', '.gitignore', '.gitattributes', '.editorconfig')) { continue }
        $null = Assert-SafePath $file.FullName $projectRoot
        $destination = Assert-SafePath (Join-Path $sourceStaging $relative) $sourceStaging
        $null = New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($destination)) -Force
        [IO.File]::Copy($file.FullName, $destination)
    }
    [IO.Compression.ZipFile]::CreateFromDirectory($sourceStaging, $sourceZip, [IO.Compression.CompressionLevel]::Optimal, $false)
    [IO.File]::Copy($sourceZip, $SourceOutputPath, $true)
    Write-Host "Created source package: $SourceOutputPath"
    Write-Host "SHA256: $(Get-FileHashValue $SourceOutputPath)"
} finally {
    if (Test-Path -LiteralPath $sourceZip) { Remove-Item -LiteralPath $sourceZip -Force }
    if (Test-Path -LiteralPath $sourceStaging) {
        $null = Assert-SafePath $sourceStaging $sourceOutputDirectory
        Remove-Item -LiteralPath $sourceStaging -Recurse -Force
    }
}
