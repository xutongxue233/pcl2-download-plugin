[CmdletBinding()]
param([string] $LauncherPath)
. (Join-Path $PSScriptRoot 'Common.ps1')

$LauncherPath = Select-Launcher $LauncherPath
Assert-LauncherStopped $LauncherPath
$initialExeHash = Get-FileHashValue $LauncherPath
$paths = Get-InstallPaths $LauncherPath
if (-not (Test-Path -LiteralPath $paths.State -PathType Leaf)) {
    if (Test-Path -LiteralPath $paths.Bootstrap) { throw 'Installation state is missing. Refusing to remove unverified files.' }
    if (Test-Path -LiteralPath $paths.Plugin) {
        foreach ($existing in @(Get-ChildItem -LiteralPath $paths.Plugin -Force)) {
            $null = Assert-SafePath $existing.FullName $paths.Plugin
            if ($existing.PSIsContainer -or $existing.Name -notin @('bootstrap.log', 'disabled')) {
                throw 'Installation state is missing and unregistered plugin files remain. Refusing to remove them.'
            }
        }
    }
    if (Test-OwnedManagerConfig $paths.Config) {
        throw 'The config still requests this plugin but installation state is missing. Preserve the config and original backup; automatic restoration cannot be verified.'
    }
    Write-Host 'DownloadRestore is not installed. No files were changed.'
    return
}
$state = Read-InstallState $paths $LauncherPath
Assert-InstallationUnchanged $paths $state -AllowMissingFiles
$installedConfig = [IO.File]::ReadAllBytes($paths.Config)
$originalConfig = $null
if ($state.OriginalConfigExisted) { $originalConfig = [IO.File]::ReadAllBytes($paths.Backup) }
$staging = Assert-SafePath (Join-Path $paths.Root ('.pcl-downloadrestore-uninstall-' + [Guid]::NewGuid().ToString('N'))) $paths.Root
$movedFiles = New-Object 'Collections.Generic.List[object]'
$configTouched = $false
$completed = $false
try {
    $null = New-Item -ItemType Directory -Path $staging
    Assert-LauncherStopped $LauncherPath
    Assert-InstallationUnchanged $paths $state -AllowMissingFiles
    # Restore the config first so a later launch cannot request a removed manager.
    $configTouched = $true
    if ($state.OriginalConfigExisted) { [IO.File]::WriteAllBytes($paths.Config, $originalConfig) }
    else { Remove-Item -LiteralPath $paths.Config -Force }
    $owned = @($paths.Bootstrap)
    $owned += @($state.PluginFiles | ForEach-Object { Get-OwnedPluginPath $paths.Plugin $_.Path })
    if ($state.OriginalConfigExisted) { $owned += $paths.Backup }
    $owned += $paths.State
    foreach ($source in $owned) {
        $null = Assert-SafePath $source $paths.Root
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { continue }
        $destination = Join-Path $staging ($movedFiles.Count.ToString() + '.backup')
        [IO.File]::Move($source, $destination)
        $movedFiles.Add([pscustomobject]@{ Source = $source; Backup = $destination })
    }
    if ((Get-FileHashValue $LauncherPath) -ine $initialExeHash) { throw 'The launcher changed during uninstallation.' }
    $completed = $true
} catch {
    $failure = $_
    try {
        for ($i = $movedFiles.Count - 1; $i -ge 0; $i--) {
            $entry = $movedFiles[$i]
            $null = Assert-SafePath $entry.Source $paths.Root
            [IO.File]::Move($entry.Backup, $entry.Source)
        }
        if ($configTouched) { [IO.File]::WriteAllBytes($paths.Config, $installedConfig) }
    } catch { Write-Warning "Rollback could not finish: $($_.Exception.Message). Recovery files remain in $staging" }
    throw $failure
} finally {
    if (Test-Path -LiteralPath $staging) {
        $null = Assert-SafePath $staging $paths.Root
        # Keep any backups when rollback failed; never destroy the recovery copy.
        if ($completed -or @(Get-ChildItem -LiteralPath $staging -Force).Count -eq 0) { Remove-Item -LiteralPath $staging -Recurse -Force }
    }
}
Remove-EmptyOwnedDirectories $paths.Plugin @($state.PluginFiles | ForEach-Object { $_.Path })
Write-Host 'Uninstalled DownloadRestore and restored the original config. The official EXE and unrelated files are unchanged.'
