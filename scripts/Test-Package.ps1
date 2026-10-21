[CmdletBinding()]
param()
. (Join-Path $PSScriptRoot 'Common.ps1')
$testRoot = Assert-SafePath (Join-Path ([IO.Path]::GetTempPath()) ('pcl-plugin-package-test-' + [Guid]::NewGuid().ToString('N')))
$null = New-Item -ItemType Directory -Path $testRoot
try {
    $payload = Join-Path $testRoot 'payload'
    $plugin = Join-Path $payload 'PCL\Plugins\DownloadRestore'
    $null = New-Item -ItemType Directory -Path $plugin -Force
    [IO.File]::WriteAllText((Join-Path $payload 'PCL.PluginBootstrap.dll'), 'synthetic packaging fixture')
    [IO.File]::WriteAllText((Join-Path $plugin 'PCL.DownloadRestore.dll'), 'synthetic packaging fixture')
    [IO.File]::WriteAllText((Join-Path $plugin '0Harmony.dll'), 'synthetic packaging fixture')
    $binaryZip = Join-Path $testRoot 'plugin.zip'
    $sourceZip = Join-Path $testRoot 'source.zip'
    & (Join-Path $PSScriptRoot 'Package.ps1') -PayloadPath $payload -OutputPath $binaryZip -SourceOutputPath $sourceZip
    $binary = [IO.Compression.ZipFile]::OpenRead($binaryZip)
    $source = [IO.Compression.ZipFile]::OpenRead($sourceZip)
    try {
        $binaryEntries = @($binary.Entries | ForEach-Object { $_.FullName.Replace('\', '/') })
        $sourceEntries = @($source.Entries | ForEach-Object { $_.FullName.Replace('\', '/') })
        if (Test-Path -LiteralPath (Join-Path $PSScriptRoot '..\VALIDATION.md') -PathType Leaf) {
            if ('VALIDATION.md' -notin $binaryEntries -or 'VALIDATION.md' -notin $sourceEntries) { throw 'Validation report is missing from a package.' }
            $sumEntry = $binary.Entries | Where-Object { $_.FullName -eq 'SHA256SUMS.txt' }
            $reader = New-Object IO.StreamReader($sumEntry.Open())
            try { if ($reader.ReadToEnd() -notmatch '(?m)^[A-F0-9]{64}  VALIDATION\.md\r?$') { throw 'Validation report is missing from SHA256SUMS.' } }
            finally { $reader.Dispose() }
        }
        foreach ($required in @('Install.cmd', 'Uninstall.cmd', 'scripts/Install.ps1', 'scripts/Uninstall.ps1', 'scripts/Common.ps1', 'README.md', 'SHA256SUMS.txt', 'licenses/PCL-LICENCE.txt', 'licenses/Harmony-LICENSE.txt')) {
            if ($required -notin $binaryEntries) { throw "Missing package entry: $required" }
        }
        if (@($binaryEntries | Where-Object { $_ -match '\.dll$' }).Count -ne 3) { throw 'The binary ZIP must have exactly three runtime DLLs.' }
        if (@($binaryEntries | Where-Object { $_ -match '\.exe$|(^|/)(refs|bin|obj|\.tools)/' }).Count -ne 0) { throw 'Binary ZIP leaked a forbidden file.' }
        if (@($sourceEntries | Where-Object { $_ -match '\.(exe|dll|nupkg|zip|pdb)$|(^|/)(refs|bin|obj|\.tools|artifacts|test-results)/' }).Count -ne 0) { throw 'Source ZIP leaked a forbidden file.' }
        foreach ($required in @('scripts/Build.ps1', 'scripts/Install.ps1', 'src/Bootstrap/PluginDomainManager.cs', 'src/DownloadRestore/RuntimePatches.cs', 'src/Injector/MainWindow.xaml', 'src/Injector/app.manifest', 'licenses/Harmony-LICENSE.txt')) {
            if ($required -notin $sourceEntries) { throw "Missing source entry: $required" }
        }
    } finally { $binary.Dispose(); $source.Dispose() }
    $freshProject = Join-Path $testRoot 'fresh-process-project'
    foreach ($directory in @('scripts', 'licenses', 'docs', 'artifacts')) {
        $null = New-Item -ItemType Directory -Path (Join-Path $freshProject $directory) -Force
    }
    foreach ($name in @('Common.ps1', 'Package.ps1')) {
        [IO.File]::Copy((Join-Path $PSScriptRoot $name), (Join-Path $freshProject ('scripts\' + $name)))
    }
    foreach ($name in @('Install.ps1', 'Uninstall.ps1')) {
        [IO.File]::Copy((Join-Path $PSScriptRoot $name), (Join-Path $freshProject ('scripts\' + $name)))
    }
    foreach ($name in @('Install.cmd', 'Uninstall.cmd', 'README.md', 'docs\LEGACY-SCRIPT-INSTALL.md', 'licenses\PCL-LICENCE.txt', 'licenses\Harmony-LICENSE.txt')) {
        [IO.File]::Copy((Join-Path $PSScriptRoot ('..\' + $name)), (Join-Path $freshProject $name))
    }
    Copy-Item -LiteralPath $payload -Destination (Join-Path $freshProject 'artifacts\payload') -Recurse
    $windowsPowerShell = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
    & $windowsPowerShell -NoLogo -NoProfile -ExecutionPolicy Bypass -File (Join-Path $freshProject 'scripts\Package.ps1')
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath (Join-Path $freshProject 'artifacts\PCL.DownloadRestore-2.99.0.0.zip')) -or -not (Test-Path -LiteralPath (Join-Path $freshProject 'artifacts\PCL.DownloadRestore-Source.zip'))) {
        throw 'Fresh Windows PowerShell5.1 -File package process failed with default payload/output paths.'
    }
    $before = Get-FileHashValue $binaryZip
    [IO.File]::WriteAllText((Join-Path $plugin 'MeloongCore.dll'), 'must never be distributed')
    $rejected = $false
    try { & (Join-Path $PSScriptRoot 'Package.ps1') -PayloadPath $payload -OutputPath $binaryZip -SourceOutputPath $sourceZip } catch { $rejected = $true }
    if (-not $rejected -or (Get-FileHashValue $binaryZip) -ne $before) { throw 'Unexpected payload was not rejected before archive replacement.' }
    Write-Host 'PASS: plugin/source package contents, required notices, exact three-DLL allowlist, source exclusions, forbidden payload rejection, fresh PS5.1 -File default paths.'
} finally {
    $null = Assert-SafePath $testRoot ([IO.Path]::GetTempPath())
    Remove-Item -LiteralPath $testRoot -Recurse -Force
}
