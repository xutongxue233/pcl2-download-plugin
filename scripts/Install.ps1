[CmdletBinding()]
param(
    [string] $LauncherPath,
    [string] $PayloadPath
)
. (Join-Path $PSScriptRoot 'Common.ps1')
if (-not $PayloadPath) { $PayloadPath = Join-Path $PSScriptRoot '..\payload' }

$LauncherPath = Select-Launcher $LauncherPath
Assert-LauncherStopped $LauncherPath
$initialExeHash = Get-FileHashValue $LauncherPath
if ($initialExeHash -ine $script:SupportedLauncherHash) {
    throw "Unsupported launcher. Only the verified official PCL 2.99.0.0 EXE is supported. SHA256: $initialExeHash"
}
$paths = Get-InstallPaths $LauncherPath
if (Test-Path -LiteralPath $paths.State) {
    $state = Read-InstallState $paths $LauncherPath
    Assert-InstallationUnchanged $paths $state
    $disabled = Assert-SafePath (Join-Path $paths.Plugin 'disabled') $paths.Plugin
    if (Test-Path -LiteralPath $disabled -PathType Leaf) {
        Remove-Item -LiteralPath $disabled -Force
        Write-Host 'DownloadRestore was installed and disabled. It is now enabled for the next launch.'
        return
    }
    Write-Host 'DownloadRestore is already installed. No files were changed.'
    return
}
if (Test-Path -LiteralPath $paths.Bootstrap) { throw 'PCL.PluginBootstrap.dll already exists without matching installation state. Refusing to overwrite it.' }
if (Test-Path -LiteralPath $paths.Plugin) {
    foreach ($existing in @(Get-ChildItem -LiteralPath $paths.Plugin -Force)) {
        $null = Assert-SafePath $existing.FullName $paths.Plugin
        if ($existing.PSIsContainer -or $existing.Name -notin @('bootstrap.log', 'disabled')) {
            throw 'The DownloadRestore plugin directory contains unregistered files. Move them aside first; diagnostics and a disabled marker may remain.'
        }
    }
}

$PayloadPath = Assert-SafePath $PayloadPath
$sourceBootstrap = Assert-SafePath (Join-Path $PayloadPath 'PCL.PluginBootstrap.dll') $PayloadPath
$sourcePlugin = Assert-SafePath (Join-Path $PayloadPath 'PCL\Plugins\DownloadRestore') $PayloadPath
foreach ($required in @($sourceBootstrap, (Join-Path $sourcePlugin 'PCL.DownloadRestore.dll'), (Join-Path $sourcePlugin '0Harmony.dll'))) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "Missing payload file: $required" }
}
$pluginFiles = @()
foreach ($sourceFile in @(Get-ChildItem -LiteralPath $sourcePlugin -Recurse -Force)) {
    $null = Assert-SafePath $sourceFile.FullName $sourcePlugin
    if ($sourceFile.PSIsContainer) { continue }
    $relative = $sourceFile.FullName.Substring($sourcePlugin.Length + 1)
    if ($relative -match '(^|[\\/])\.install-state\.json$|(^|[\\/])\.original-exe\.config$|\.exe$|\.log$') {
        throw "Unexpected payload file: $relative"
    }
    $pluginFiles += [pscustomobject]@{ Path = $relative; Sha256 = (Get-FileHashValue $sourceFile.FullName) }
}
$originalConfigExisted = Test-Path -LiteralPath $paths.Config -PathType Leaf
$originalBytes = $null
$originalHash = $null
if ($originalConfigExisted) {
    $originalBytes = [IO.File]::ReadAllBytes($paths.Config)
    $originalHash = Get-FileHashValue $paths.Config
}
$patchedBytes = Get-PatchedConfigBytes $originalBytes
$staging = Assert-SafePath (Join-Path $paths.Root ('.pcl-downloadrestore-install-' + [Guid]::NewGuid().ToString('N'))) $paths.Root
$createdFiles = New-Object 'Collections.Generic.List[string]'
$createdDirs = New-Object 'Collections.Generic.List[string]'
$configTouched = $false
try {
    $null = New-Item -ItemType Directory -Path $staging
    $stagedConfig = Join-Path $staging 'launcher.config'
    [IO.File]::WriteAllBytes($stagedConfig, $patchedBytes)
    $state = [ordered]@{
        Schema = 1
        LauncherPath = $LauncherPath
        LauncherSha256AtInstall = $initialExeHash
        InstalledUtc = [DateTime]::UtcNow.ToString('o')
        OriginalConfigExisted = [bool]$originalConfigExisted
        OriginalConfigSha256 = $originalHash
        InstalledConfigSha256 = (Get-FileHashValue $stagedConfig)
        BootstrapSha256 = (Get-FileHashValue $sourceBootstrap)
        PluginFiles = @($pluginFiles)
    }
    $stagedState = Join-Path $staging 'state.json'
    [IO.File]::WriteAllText($stagedState, ($state | ConvertTo-Json -Depth 6), (New-Object Text.UTF8Encoding($false)))
    # Recheck immediately before changing the installation.
    Assert-LauncherStopped $LauncherPath
    if ((Get-FileHashValue $LauncherPath) -ine $initialExeHash) { throw 'The launcher changed during installation.' }
    if ($originalConfigExisted) {
        if ((Get-FileHashValue $paths.Config) -ine $originalHash) { throw 'The config changed during installation.' }
    } elseif (Test-Path -LiteralPath $paths.Config) { throw 'A config appeared during installation.' }

    foreach ($dir in @((Join-Path $paths.Root 'PCL'), (Join-Path $paths.Root 'PCL\Plugins'), $paths.Plugin)) {
        $null = Assert-SafePath $dir $paths.Root
        if (-not (Test-Path -LiteralPath $dir)) { $null = New-Item -ItemType Directory -Path $dir; $createdDirs.Add($dir) }
    }
    foreach ($file in $pluginFiles) {
        $destination = Get-OwnedPluginPath $paths.Plugin $file.Path
        $directory = [IO.Path]::GetDirectoryName($destination)
        $null = New-Item -ItemType Directory -Path $directory -Force
        $createdFiles.Add($destination)
        [IO.File]::Copy((Join-Path $sourcePlugin $file.Path), $destination, $false)
        if ((Get-FileHashValue $destination) -ine $file.Sha256) { throw "Payload changed while copying: $($file.Path)" }
        if ([IO.Path]::GetExtension($destination) -ieq '.dll') { Unblock-File -LiteralPath $destination }
    }
    if ($originalConfigExisted) { $createdFiles.Add($paths.Backup); [IO.File]::WriteAllBytes($paths.Backup, $originalBytes) }
    $createdFiles.Add($paths.Bootstrap)
    [IO.File]::Copy($sourceBootstrap, $paths.Bootstrap, $false)
    if ((Get-FileHashValue $paths.Bootstrap) -ine $state.BootstrapSha256) { throw 'Bootstrap changed while copying.' }
    Unblock-File -LiteralPath $paths.Bootstrap
    $createdFiles.Add($paths.State)
    [IO.File]::Copy($stagedState, $paths.State, $false)
    $configTouched = $true
    [IO.File]::WriteAllBytes($paths.Config, $patchedBytes)
    if ((Get-FileHashValue $LauncherPath) -ine $initialExeHash) { throw 'The launcher changed during installation.' }
    Assert-InstallationUnchanged $paths (Read-InstallState $paths $LauncherPath)
    $disabled = Assert-SafePath (Join-Path $paths.Plugin 'disabled') $paths.Plugin
    if (Test-Path -LiteralPath $disabled -PathType Leaf) { Remove-Item -LiteralPath $disabled -Force }
    Write-Host 'Installed DownloadRestore. The official EXE is unchanged. Start PCL normally; the plugin loads on every launch.'
} catch {
    $failure = $_
    try {
        if ($configTouched) {
            if ($originalConfigExisted) {
                if (-not (Test-Path -LiteralPath $paths.Config -PathType Leaf) -or (Get-FileHashValue $paths.Config) -ine $originalHash) {
                    [IO.File]::WriteAllBytes($paths.Config, $originalBytes)
                }
            }
            elseif (Test-Path -LiteralPath $paths.Config) { Remove-Item -LiteralPath $paths.Config -Force }
        }
        for ($i = $createdFiles.Count - 1; $i -ge 0; $i--) {
            $null = Assert-SafePath $createdFiles[$i] $paths.Root
            if (Test-Path -LiteralPath $createdFiles[$i] -PathType Leaf) { Remove-Item -LiteralPath $createdFiles[$i] -Force }
        }
        Remove-EmptyOwnedDirectories $paths.Plugin @($pluginFiles | ForEach-Object { $_.Path })
        for ($i = $createdDirs.Count - 1; $i -ge 0; $i--) {
            if ((Test-Path -LiteralPath $createdDirs[$i]) -and @(Get-ChildItem -LiteralPath $createdDirs[$i] -Force).Count -eq 0) { Remove-Item -LiteralPath $createdDirs[$i] -Force }
        }
    } catch { Write-Warning "Rollback could not finish: $($_.Exception.Message). Keep the installation state and original config backup for recovery." }
    throw $failure
} finally {
    if (Test-Path -LiteralPath $staging) {
        $null = Assert-SafePath $staging $paths.Root
        Remove-Item -LiteralPath $staging -Recurse -Force
    }
}
