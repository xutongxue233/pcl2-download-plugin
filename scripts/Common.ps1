Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:SupportedLauncherHash = '6B6E1260CB5A1D6A303855F1C60CAB3D0BC1AEBBA7E40A964180188B271E7386'
$script:BootstrapAssembly = 'PCL.PluginBootstrap, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null'
$script:BootstrapType = 'PCL.PluginBootstrap.PluginDomainManager'

function Get-FullPath([string] $Path) {
    return [IO.Path]::GetFullPath($Path)
}

function Assert-SafePath([string] $Path, [string] $Within) {
    $full = Get-FullPath $Path
    if ($Within) {
        $root = (Get-FullPath $Within).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
        if (-not $full.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Path is outside the expected directory: $full"
        }
    }
    $cursor = $full
    while ($cursor) {
        if (Test-Path -LiteralPath $cursor) {
            $item = Get-Item -LiteralPath $cursor -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Symbolic links and directory junctions are not supported: $cursor"
            }
        }
        $parent = [IO.Path]::GetDirectoryName($cursor.TrimEnd([IO.Path]::DirectorySeparatorChar))
        if ($parent -eq $cursor) { break }
        $cursor = $parent
    }
    return $full
}

function Get-FileHashValue([string] $Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
}

function Test-OwnedManagerConfig([string] $ConfigPath) {
    if (-not (Test-Path -LiteralPath $ConfigPath -PathType Leaf)) { return $false }
    $doc = New-Object Xml.XmlDocument
    $doc.XmlResolver = $null
    $settings = New-Object Xml.XmlReaderSettings
    $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null
    $reader = [Xml.XmlReader]::Create($ConfigPath, $settings)
    try { $doc.Load($reader) } finally { $reader.Dispose() }
    foreach ($node in @($doc.SelectNodes('/configuration/runtime/appDomainManagerType'))) {
        if ($node.GetAttribute('value').Split(',')[0].Trim() -ieq $script:BootstrapType) { return $true }
    }
    foreach ($node in @($doc.SelectNodes('/configuration/runtime/appDomainManagerAssembly'))) {
        if ($node.GetAttribute('value').Split(',')[0].Trim() -ieq 'PCL.PluginBootstrap') { return $true }
    }
    return $false
}

function Select-Launcher([string] $LauncherPath) {
    if (-not $LauncherPath) {
        Add-Type -AssemblyName System.Windows.Forms
        $dialog = New-Object Windows.Forms.OpenFileDialog
        $dialog.Title = 'Select the official PCL 2.99 executable'
        $dialog.Filter = 'PCL executable (*.exe)|*.exe'
        $dialog.CheckFileExists = $true
        try {
            if ($dialog.ShowDialog() -ne [Windows.Forms.DialogResult]::OK) { throw 'No launcher selected.' }
            $LauncherPath = $dialog.FileName
        } finally { $dialog.Dispose() }
    }
    $full = Assert-SafePath $LauncherPath
    if (-not (Test-Path -LiteralPath $full -PathType Leaf) -or [IO.Path]::GetExtension($full) -ine '.exe') {
        throw 'LauncherPath must be an existing .exe file.'
    }
    return $full
}

function Assert-LauncherStopped([string] $LauncherPath) {
    $name = [IO.Path]::GetFileNameWithoutExtension($LauncherPath)
    foreach ($process in @(Get-Process -Name $name -ErrorAction SilentlyContinue)) {
        try { $processPath = $process.Path } catch { $processPath = $null }
        if (-not $processPath -or $processPath -ieq $LauncherPath) {
            throw 'Close the selected launcher before installing or uninstalling the plugin.'
        }
    }
}

function Get-InstallPaths([string] $LauncherPath) {
    $root = [IO.Path]::GetDirectoryName($LauncherPath)
    $plugin = Assert-SafePath (Join-Path $root 'PCL\Plugins\DownloadRestore') $root
    return @{
        Root = $root
        Plugin = $plugin
        Bootstrap = (Assert-SafePath (Join-Path $root 'PCL.PluginBootstrap.dll') $root)
        Config = (Assert-SafePath ($LauncherPath + '.config') $root)
        State = (Assert-SafePath (Join-Path $plugin '.install-state.json') $root)
        Backup = (Assert-SafePath (Join-Path $plugin '.original-exe.config') $root)
    }
}

function Get-OwnedPluginPath([string] $PluginRoot, [string] $RelativePath) {
    if (-not $RelativePath -or [IO.Path]::IsPathRooted($RelativePath) -or $RelativePath -match '(^|[\\/])\.\.?([\\/]|$)' -or $RelativePath.Contains(':')) {
        throw "Invalid installation manifest path: $RelativePath"
    }
    return Assert-SafePath (Join-Path $PluginRoot $RelativePath) $PluginRoot
}

function Read-InstallState($Paths, [string] $LauncherPath) {
    $state = Get-Content -LiteralPath $Paths.State -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($state.Schema -ne 1 -or $state.LauncherPath -ine $LauncherPath -or $state.BootstrapSha256 -notmatch '^[A-Fa-f0-9]{64}$' -or $state.InstalledConfigSha256 -notmatch '^[A-Fa-f0-9]{64}$') {
        throw 'Installation state does not match the selected launcher.'
    }
    $seen = @{}
    foreach ($file in @($state.PluginFiles)) {
        $null = Get-OwnedPluginPath $Paths.Plugin $file.Path
        if ($seen.ContainsKey($file.Path) -or $file.Path -in @('.install-state.json', '.original-exe.config') -or $file.Sha256 -notmatch '^[A-Fa-f0-9]{64}$') {
            throw 'Installation state contains an invalid or duplicate file entry.'
        }
        $seen[$file.Path] = $true
    }
    if (-not $seen.ContainsKey('PCL.DownloadRestore.dll') -or -not $seen.ContainsKey('0Harmony.dll')) {
        throw 'Installation state is missing required plugin files.'
    }
    return $state
}

function Assert-InstallationUnchanged($Paths, $State, [switch] $AllowMissingFiles) {
    if (-not (Test-Path -LiteralPath $Paths.Config -PathType Leaf) -or (Get-FileHashValue $Paths.Config) -ine $State.InstalledConfigSha256) {
        throw 'The launcher config changed after installation. Preserve your edits and restore the installed config before retrying; nothing was removed.'
    }
    $bootstrapPresent = Test-Path -LiteralPath $Paths.Bootstrap -PathType Leaf
    if ((-not $bootstrapPresent -and -not $AllowMissingFiles) -or ($bootstrapPresent -and (Get-FileHashValue $Paths.Bootstrap) -ine $State.BootstrapSha256)) {
        throw 'The bootstrap DLL is missing or modified. Refusing to overwrite or remove it.'
    }
    foreach ($file in @($State.PluginFiles)) {
        $path = Get-OwnedPluginPath $Paths.Plugin $file.Path
        $present = Test-Path -LiteralPath $path -PathType Leaf
        if ((-not $present -and -not $AllowMissingFiles) -or ($present -and (Get-FileHashValue $path) -ine $file.Sha256)) {
            throw "An installed plugin file is missing or modified: $($file.Path). Refusing destructive cleanup."
        }
    }
    if ($State.OriginalConfigExisted) {
        if (-not (Test-Path -LiteralPath $Paths.Backup -PathType Leaf) -or (Get-FileHashValue $Paths.Backup) -ine $State.OriginalConfigSha256) {
            throw 'The original config backup is missing or modified.'
        }
    }
}

function Get-PatchedConfigBytes([byte[]] $OriginalBytes) {
    $doc = New-Object Xml.XmlDocument
    $doc.PreserveWhitespace = $true
    $doc.XmlResolver = $null
    if ($null -ne $OriginalBytes -and $OriginalBytes.Length -gt 0) {
        $settings = New-Object Xml.XmlReaderSettings
        $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
        $settings.XmlResolver = $null
        $inputStream = New-Object IO.MemoryStream(,$OriginalBytes)
        $reader = [Xml.XmlReader]::Create($inputStream, $settings)
        try { $doc.Load($reader) } finally { $reader.Dispose(); $inputStream.Dispose() }
    } else {
        $null = $doc.AppendChild($doc.CreateXmlDeclaration('1.0', 'utf-8', $null))
        $null = $doc.AppendChild($doc.CreateElement('configuration'))
    }
    if ($doc.DocumentElement.Name -ne 'configuration' -or $doc.DocumentElement.NamespaceURI) { throw 'Unsupported launcher config format.' }
    $startupNodes = @($doc.SelectNodes('/configuration/startup'))
    if ($startupNodes.Count -gt 1) { throw 'The config contains duplicate startup elements.' }
    if ($startupNodes.Count -eq 0) {
        $startup = $doc.CreateElement('startup')
        $supportedRuntime = $doc.CreateElement('supportedRuntime')
        $supportedRuntime.SetAttribute('version', 'v4.0')
        $supportedRuntime.SetAttribute('sku', '.NETFramework,Version=v4.8')
        $null = $startup.AppendChild($supportedRuntime)
        $null = $doc.DocumentElement.AppendChild($startup)
    }
    $runtimeNodes = @($doc.SelectNodes('/configuration/runtime'))
    if ($runtimeNodes.Count -gt 1) { throw 'The config contains duplicate runtime elements.' }
    if ($runtimeNodes.Count -eq 0) {
        $runtime = $doc.CreateElement('runtime')
        $null = $doc.DocumentElement.AppendChild($runtime)
    } else { $runtime = $runtimeNodes[0] }
    if ($runtime.SelectSingleNode('appDomainManagerAssembly') -or $runtime.SelectSingleNode('appDomainManagerType')) {
        throw 'The selected launcher already uses an AppDomain manager. It cannot be replaced safely.'
    }
    # The manager runs before the host selects .NET 4.8 compatibility defaults.
    # Keep PCL's existing extended-path handling valid from the first managed IO.
    $switchNodes = @($runtime.SelectNodes('AppContextSwitchOverrides'))
    if ($switchNodes.Count -gt 1) { throw 'The config contains duplicate AppContextSwitchOverrides elements.' }
    if ($switchNodes.Count -eq 0) {
        $switchNode = $doc.CreateElement('AppContextSwitchOverrides')
        $null = $runtime.AppendChild($switchNode)
    } else { $switchNode = $switchNodes[0] }
    $switches = [ordered]@{}
    foreach ($entry in $switchNode.GetAttribute('value').Split(';')) {
        if (-not $entry.Trim()) { continue }
        $parts = $entry.Split('=')
        if ($parts.Length -ne 2 -or -not $parts[0].Trim() -or -not $parts[1].Trim()) { throw 'The config contains an invalid AppContext switch entry.' }
        $key = $parts[0].Trim()
        if ($switches.Contains($key)) { throw "The config contains a duplicate AppContext switch: $key" }
        $switches[$key] = $parts[1].Trim()
    }
    foreach ($requiredSwitch in @('Switch.System.IO.UseLegacyPathHandling', 'Switch.System.IO.BlockLongPaths',
        'Switch.System.Net.DontEnableSchUseStrongCrypto', 'Switch.System.Net.DontEnableSystemDefaultTlsVersions')) {
        # Use the documented canonical casing even if the existing key differs.
        if ($switches.Contains($requiredSwitch)) { $switches.Remove($requiredSwitch) }
        $switches[$requiredSwitch] = 'false'
    }
    $switchNode.SetAttribute('value', (($switches.GetEnumerator() | ForEach-Object { $_.Key + '=' + $_.Value }) -join ';'))
    $assembly = $doc.CreateElement('appDomainManagerAssembly')
    $assembly.SetAttribute('value', $script:BootstrapAssembly)
    $null = $runtime.AppendChild($assembly)
    $type = $doc.CreateElement('appDomainManagerType')
    $type.SetAttribute('value', $script:BootstrapType)
    $null = $runtime.AppendChild($type)
    $outputStream = New-Object IO.MemoryStream
    try { $doc.Save($outputStream); return ,$outputStream.ToArray() } finally { $outputStream.Dispose() }
}

function Remove-EmptyOwnedDirectories([string] $PluginRoot, [string[]] $RelativeFiles) {
    $parents = @{}
    foreach ($relative in $RelativeFiles) {
        $cursor = [IO.Path]::GetDirectoryName((Get-OwnedPluginPath $PluginRoot $relative))
        while ($cursor -and $cursor.StartsWith($PluginRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
            $parents[$cursor] = $true
            $cursor = [IO.Path]::GetDirectoryName($cursor)
        }
    }
    foreach ($dir in @($parents.Keys | Sort-Object Length -Descending)) {
        $null = Assert-SafePath $dir $PluginRoot
        if ((Test-Path -LiteralPath $dir -PathType Container) -and @(Get-ChildItem -LiteralPath $dir -Force).Count -eq 0) {
            Remove-Item -LiteralPath $dir -Force
        }
    }
    if ((Test-Path -LiteralPath $PluginRoot -PathType Container) -and @(Get-ChildItem -LiteralPath $PluginRoot -Force).Count -eq 0) {
        $null = Assert-SafePath $PluginRoot
        Remove-Item -LiteralPath $PluginRoot -Force
    }
}
