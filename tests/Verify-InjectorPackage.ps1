[CmdletBinding()]
param(
    [string] $PackagePath,
    [string] $SourcePackagePath,
    [string] $HostExecutable,
    [string] $OutputDirectory
)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if (-not $PackagePath) { $PackagePath = Join-Path $projectRoot 'artifacts\PCL.DownloadRestore.Injector.zip' }
if (-not $SourcePackagePath) { $SourcePackagePath = Join-Path $projectRoot 'artifacts\PCL.DownloadRestore.Injector-Source.zip' }
if (-not $HostExecutable) { $HostExecutable = Join-Path $projectRoot 'refs\Plain Craft Launcher 2.exe' }
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $projectRoot 'artifacts\injector-package-verification' }
$PackagePath = [IO.Path]::GetFullPath($PackagePath)
$SourcePackagePath = [IO.Path]::GetFullPath($SourcePackagePath)
$HostExecutable = [IO.Path]::GetFullPath($HostExecutable)
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$supportedHash = '6B6E1260CB5A1D6A303855F1C60CAB3D0BC1AEBBA7E40A964180188B271E7386'
$exeName = 'PCL.DownloadRestore.Injector.exe'
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Get-StreamHash([IO.Stream] $Stream) {
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($algorithm.ComputeHash($Stream)).Replace('-', '') }
    finally { $algorithm.Dispose() }
}

function Get-PathHash([string] $Path) {
    $stream = [IO.File]::OpenRead($Path)
    try { return Get-StreamHash $stream } finally { $stream.Dispose() }
}

function Assert-Condition([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
    $script:checks.Add($Message)
    Write-Host "PASS $Message"
}

function Get-ZipFiles([IO.Compression.ZipArchive] $Archive) {
    $files = @{}
    $allNames = @{}
    foreach ($entry in $Archive.Entries) {
        $name = $entry.FullName.Replace('\', '/')
        if (-not $name -or $name.StartsWith('/') -or $name.Contains(':') -or $name -match '(^|/)(\.|\.\.)(/|$)' -or $name.Contains('//')) {
            throw "Unsafe ZIP entry: $($entry.FullName)"
        }
        if ($allNames.ContainsKey($name)) { throw "Duplicate ZIP entry: $name" }
        $allNames[$name] = $true
        if ($name.EndsWith('/')) { continue }
        $files[$name] = $entry
    }
    return $files
}

function Test-ZipChecksums([hashtable] $Files, [bool] $Required) {
    if (-not $Files.ContainsKey('SHA256SUMS.txt')) {
        if ($Required) { throw 'Package is missing SHA256SUMS.txt.' }
        return 0
    }
    $reader = New-Object IO.StreamReader($Files['SHA256SUMS.txt'].Open(), [Text.Encoding]::UTF8, $true)
    try { $lines = $reader.ReadToEnd() -split "`r?`n" } finally { $reader.Dispose() }
    $seen = @{}
    foreach ($line in $lines) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        if ($line -notmatch '^([a-fA-F0-9]{64})  (.+)$') { throw "Malformed checksum line: $line" }
        $expected = $Matches[1]
        $name = $Matches[2]
        if ($name.Contains('\') -or $name -eq 'SHA256SUMS.txt' -or -not $Files.ContainsKey($name) -or $seen.ContainsKey($name)) {
            throw "Invalid checksum entry: $name"
        }
        $stream = $Files[$name].Open()
        try { $actual = Get-StreamHash $stream } finally { $stream.Dispose() }
        if ($actual -ne $expected) { throw "Checksum mismatch: $name" }
        $seen[$name] = $true
    }
    if ($seen.Count -ne ($Files.Count - 1)) { throw 'SHA256SUMS.txt does not cover every distributed file exactly once.' }
    return $seen.Count
}

function Invoke-PackagedInjector([string] $Action, [string] $ExpectedStatus, [int] $Sequence) {
    $reportPath = Join-Path $script:runDirectory ('{0:D2}-{1}.json' -f $Sequence, $Action)
    $start = New-Object Diagnostics.ProcessStartInfo
    $start.FileName = $script:isolatedInjector
    $start.Arguments = '--' + $Action + ' "' + $script:fixtureHost + '" --report "' + $reportPath + '"'
    $start.WorkingDirectory = [IO.Path]::GetDirectoryName($script:isolatedInjector)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.EnvironmentVariables['PATH'] = $script:emptyPath
    $process = New-Object Diagnostics.Process
    $process.StartInfo = $start
    try {
        if (-not $process.Start()) { throw "Could not start injector $Action." }
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(60000)) { $process.Kill(); throw "Injector $Action exceeded 60 seconds." }
        $process.WaitForExit()
        $textOut = $stdout.Result
        $textErr = $stderr.Result
        [IO.File]::WriteAllText(($reportPath + '.stdout.txt'), $textOut)
        [IO.File]::WriteAllText(($reportPath + '.stderr.txt'), $textErr)
        Assert-Condition ($process.ExitCode -eq 0) "$Sequence $Action fresh process exited successfully with an empty PATH"
    } finally { $process.Dispose() }
    $result = [IO.File]::ReadAllText($reportPath) | ConvertFrom-Json
    Assert-Condition ($result.Success -and $result.Action -eq ('--' + $Action) -and $result.Status -eq $ExpectedStatus) "$Sequence $Action JSON confirms $ExpectedStatus"
    Assert-Condition ($result.LauncherSha256 -eq $supportedHash -and (Get-PathHash $script:fixtureHost) -eq $supportedHash) "$Sequence $Action preserves the official launcher bytes"
    if ($ExpectedStatus -eq 'Installed') {
        Assert-Condition ($result.CanUninstall -eq $true) "$Sequence $Action confirms the plugin can be uninstalled"
    } else {
        Assert-Condition ($result.CanInstall -eq $true -and $result.CanUninstall -eq $false) "$Sequence $Action confirms a clean installable launcher"
    }
    $script:steps.Add([ordered]@{ Sequence = $Sequence; Action = $Action; Report = $reportPath; Result = $result })
}

$checks = New-Object 'Collections.Generic.List[string]'
$steps = New-Object 'Collections.Generic.List[object]'
[IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null
$runDirectory = Join-Path $OutputDirectory ('run-' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
[IO.Directory]::CreateDirectory($runDirectory) | Out-Null
$summary = [ordered]@{
    StartedUtc = [DateTime]::UtcNow.ToString('o')
    AllPassed = $false
    PackagePath = $PackagePath
    SourcePackagePath = $SourcePackagePath
    OfficialHostPath = $HostExecutable
    SupportedLauncherSha256 = $supportedHash
    RunDirectory = $runDirectory
    PackageSha256 = $null
    SourcePackageSha256 = $null
    PackageFileCount = 0
    ChecksumFileCount = 0
    SourceFileCount = 0
    SourceChecksumFileCount = 0
    InjectorSha256 = $null
    OriginalConfigSha256 = $null
    RestoredConfigSha256 = $null
    Checks = @()
    Steps = @()
    Error = $null
}
try {
    foreach ($file in @($PackagePath, $SourcePackagePath, $HostExecutable)) {
        if (-not [IO.File]::Exists($file)) { throw "Required input is missing: $file" }
    }
    Assert-Condition ((Get-PathHash $HostExecutable) -eq $supportedHash) 'Official fixture source matches the supported unmodified PCL build'
    $summary.PackageSha256 = Get-PathHash $PackagePath
    $summary.SourcePackageSha256 = Get-PathHash $SourcePackagePath
    $toolDirectory = Join-Path $runDirectory 'tool'
    [IO.Directory]::CreateDirectory($toolDirectory) | Out-Null
    $isolatedInjector = Join-Path $toolDirectory $exeName
    $archive = [IO.Compression.ZipFile]::OpenRead($PackagePath)
    try {
        $files = Get-ZipFiles $archive
        $allowed = @($exeName, 'README.md', 'VALIDATION.md', 'original-build.json', 'licenses/PCL-LICENCE.txt', 'licenses/Harmony-LICENSE.txt', 'SHA256SUMS.txt')
        Assert-Condition ($files.Count -eq $allowed.Count -and @($files.Keys | Where-Object { $_ -cnotin $allowed }).Count -eq 0) 'Binary ZIP contains exactly the injector, documentation, licenses and checksums'
        $executables = @($files.Keys | Where-Object { $_ -match '(?i)\.exe$' })
        Assert-Condition ($executables.Count -eq 1 -and $executables[0] -ceq $exeName) 'Binary ZIP contains exactly one executable: PCL.DownloadRestore.Injector.exe'
        Assert-Condition (@($files.Keys | Where-Object { $_ -match '(?i)\.(dll|ps1|psm1|cmd|bat|vbs|pdb)$' }).Count -eq 0) 'Binary ZIP contains no external DLLs, scripts or host executable'
        $summary.PackageFileCount = $files.Count
        $summary.ChecksumFileCount = Test-ZipChecksums $files $true
        Assert-Condition ($summary.ChecksumFileCount -eq ($files.Count - 1)) 'Every binary package file matches its SHA256SUMS.txt entry'
        $source = $files[$exeName].Open()
        $destination = [IO.File]::Open($isolatedInjector, [IO.FileMode]::CreateNew)
        try { $source.CopyTo($destination) } finally { $destination.Dispose(); $source.Dispose() }
    } finally { $archive.Dispose() }
    $summary.InjectorSha256 = Get-PathHash $isolatedInjector
    Assert-Condition ([Reflection.AssemblyName]::GetAssemblyName($isolatedInjector).Name -eq 'PCL.DownloadRestore.Injector') 'Extracted executable has the expected managed assembly identity'
    $archive = [IO.Compression.ZipFile]::OpenRead($SourcePackagePath)
    try {
        $files = Get-ZipFiles $archive
        $forbidden = @($files.Keys | Where-Object {
            $_ -match '(?i)\.(exe|dll|pdb|zip|nupkg|jar)$' -or
            $_ -match '(?i)(^|/)(artifacts|refs|references|\.tools|\.git|bin|obj|test-results|payload|packages|\.packages|deps|\.analysis|PCL-2\.[^/]*)(/|$)'
        })
        Assert-Condition ($forbidden.Count -eq 0) 'Source ZIP excludes official host binaries, dependencies and generated build/test artifacts'
        foreach ($required in @('src/Injector/PCL.DownloadRestore.Injector.csproj', 'src/Injector/app.manifest', 'src/Injector/Program.cs', 'src/Injector/MainWindow.xaml', 'src/Injector/MainWindow.xaml.cs', 'src/Injector/UI/PclStyles.xaml', 'src/Injector/UI/SOURCE-NOTES.md', 'src/Injector/Installation/InstallationService.cs', 'src/Bootstrap/PluginDomainManager.cs', 'src/DownloadRestore/EntryPoint.cs', 'scripts/Build-Injector.ps1', 'scripts/Package-Injector.ps1', 'tests/Verify-InjectorPackage.ps1')) {
            Assert-Condition ($files.ContainsKey($required)) "Source ZIP includes $required"
        }
        $summary.SourceFileCount = $files.Count
        $summary.SourceChecksumFileCount = Test-ZipChecksums $files $false
    } finally { $archive.Dispose() }
    $fixtureDirectory = Join-Path $runDirectory 'fixture'
    [IO.Directory]::CreateDirectory($fixtureDirectory) | Out-Null
    $fixtureHost = Join-Path $fixtureDirectory 'Plain Craft Launcher 2.exe'
    [IO.File]::Copy($HostExecutable, $fixtureHost)
    $configurationPath = $fixtureHost + '.config'
    $configuration = "<?xml version=`"1.0`" encoding=`"utf-8`"?>`r`n<configuration>`r`n  <!-- Preserve exact BOM, CRLF and custom settings. -->`r`n  <appSettings><add key=`"fixture`" value=`"unchanged`" /></appSettings>`r`n</configuration>`r`n"
    [IO.File]::WriteAllText($configurationPath, $configuration, (New-Object Text.UTF8Encoding($true)))
    $summary.OriginalConfigSha256 = Get-PathHash $configurationPath
    $emptyPath = Join-Path $runDirectory 'empty-path'
    [IO.Directory]::CreateDirectory($emptyPath) | Out-Null
    Invoke-PackagedInjector 'inspect' 'Ready' 1
    Invoke-PackagedInjector 'install' 'Installed' 2
    Invoke-PackagedInjector 'inspect' 'Installed' 3
    foreach ($relative in @('PCL.PluginBootstrap.dll', 'PCL\Plugins\DownloadRestore\PCL.DownloadRestore.dll', 'PCL\Plugins\DownloadRestore\0Harmony.dll', 'PCL\Plugins\DownloadRestore\.install-state.json')) {
        Assert-Condition ([IO.File]::Exists((Join-Path $fixtureDirectory $relative))) "Install materializes $relative from the single EXE"
    }
    $installedXml = New-Object Xml.XmlDocument
    $installedXml.Load($configurationPath)
    Assert-Condition ($installedXml.SelectSingleNode('/configuration/runtime/appDomainManagerType').GetAttribute('value') -eq 'PCL.PluginBootstrap.PluginDomainManager') 'Installed configuration enables the app-local bootstrap'
    Invoke-PackagedInjector 'uninstall' 'Ready' 4
    Invoke-PackagedInjector 'inspect' 'Ready' 5
    $summary.RestoredConfigSha256 = Get-PathHash $configurationPath
    Assert-Condition ($summary.OriginalConfigSha256 -eq $summary.RestoredConfigSha256) 'Uninstall restores the original BOM and CRLF configuration byte for byte'
    foreach ($relative in @('PCL.PluginBootstrap.dll', 'PCL\Plugins\DownloadRestore\PCL.DownloadRestore.dll', 'PCL\Plugins\DownloadRestore\0Harmony.dll', 'PCL\Plugins\DownloadRestore\.install-state.json')) {
        Assert-Condition (-not [IO.File]::Exists((Join-Path $fixtureDirectory $relative))) "Uninstall removes owned $relative"
    }
    Assert-Condition (@([IO.Directory]::GetFiles($toolDirectory)).Count -eq 1) 'Injector lifecycle needed no companion DLLs, configuration or scripts'
    Assert-Condition ((Get-PathHash $HostExecutable) -eq $supportedHash) 'Original supplied launcher remains unchanged after all package tests'
    $summary.AllPassed = $true
} catch {
    $summary.Error = $_.Exception.ToString()
    throw
} finally {
    $summary.Checks = @($checks.ToArray())
    $summary.Steps = @($steps.ToArray())
    $summary.CompletedUtc = [DateTime]::UtcNow.ToString('o')
    $json = $summary | ConvertTo-Json -Depth 10
    [IO.File]::WriteAllText((Join-Path $runDirectory 'results.json'), $json, (New-Object Text.UTF8Encoding($false)))
    [IO.File]::WriteAllText((Join-Path $OutputDirectory 'results.json'), $json, (New-Object Text.UTF8Encoding($false)))
}
Write-Host "Package verification passed. Evidence: $OutputDirectory\results.json"
