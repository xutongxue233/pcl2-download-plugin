[CmdletBinding()]
param(
    [string]$OutputDirectory,
    [switch]$Network
)
$ErrorActionPreference = 'Stop'
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $PSScriptRoot '..\..\..\artifacts\bootstrap-tests' }
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
# 此哈希只确认官方回归测试样本，不用于生产环境的加载判断。
$officialFixtureSha256 = '6B6E1260CB5A1D6A303855F1C60CAB3D0BC1AEBBA7E40A964180188B271E7386'
$testRoot = [IO.Path]::GetFullPath($OutputDirectory)
[IO.Directory]::CreateDirectory($testRoot) | Out-Null
[IO.File]::WriteAllText((Join-Path $testRoot 'results.json'), '{"Status":"running"}')
$bootstrap = Join-Path $PSScriptRoot '..\bin\Release\net48\PCL.PluginBootstrap.dll'
$probeDirectory = Join-Path $PSScriptRoot 'StartupProbe\bin\Release\net48'
$hashProbe = Join-Path $PSScriptRoot 'HashProbe\bin\Release\net48\BootstrapHashProbe.exe'
foreach ($project in @('..\PCL.PluginBootstrap.csproj', 'StartupProbe\StartupProbe.csproj', 'HashProbe\HashProbe.csproj')) {
    & dotnet build (Join-Path $PSScriptRoot $project) -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw "Failed to build $project" }
}
$config = @'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <startup><supportedRuntime version="v4.0" sku=".NETFramework,Version=v4.8" /></startup>
  <runtime>
    <AppContextSwitchOverrides value="Switch.System.IO.UseLegacyPathHandling=false;Switch.System.IO.BlockLongPaths=false;Switch.System.Net.DontEnableSchUseStrongCrypto=false;Switch.System.Net.DontEnableSystemDefaultTlsVersions=false" />
    <appDomainManagerAssembly value="PCL.PluginBootstrap, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null" />
    <appDomainManagerType value="PCL.PluginBootstrap.PluginDomainManager" />
  </runtime>
</configuration>
'@
function Run-Probe([string]$Executable, [string]$Arguments = '') {
    $start = @{ FilePath = $Executable; WorkingDirectory = (Split-Path $Executable); WindowStyle = 'Hidden'; PassThru = $true }
    if ($Arguments) { $start.ArgumentList = $Arguments }
    $process = Start-Process @start
    if (!$process.WaitForExit(20000)) {
        Stop-Process -Id $process.Id -Force
        throw "Bootstrap probe timed out: $Executable"
    }
    if ($process.ExitCode -ne 0) { throw "Bootstrap probe failed ($($process.ExitCode)): $Executable" }
}
function Prepare-Probe([string]$Name, [string]$Executable) {
    $folder = Join-Path $testRoot $Name
    [IO.Directory]::CreateDirectory($folder) | Out-Null
    $target = Join-Path $folder ([IO.Path]::GetFileName($Executable))
    Copy-Item -LiteralPath $Executable -Destination $target -Force
    Copy-Item -LiteralPath $bootstrap -Destination (Join-Path $folder 'PCL.PluginBootstrap.dll') -Force
    [IO.File]::WriteAllText($target + '.config', $config)
    $previousLog = Join-Path $folder 'PCL\Plugins\DownloadRestore\bootstrap.log'
    if (Test-Path -LiteralPath $previousLog) { Remove-Item -LiteralPath $previousLog -Force }
    return $target
}

$minimal = Prepare-Probe 'unsupported-host' $hashProbe
$minimalPayload = Join-Path (Split-Path $minimal) 'PCL\Plugins\DownloadRestore'
[IO.Directory]::CreateDirectory($minimalPayload) | Out-Null
[IO.File]::WriteAllText((Join-Path $minimalPayload 'PCL.DownloadRestore.dll'), 'unused')
[IO.File]::WriteAllText((Join-Path $minimalPayload '0Harmony.dll'), 'unused')
$minimalHash = (Get-FileHash -LiteralPath $minimal -Algorithm SHA256).Hash
Run-Probe $minimal
if ((Get-Content -LiteralPath (Join-Path $minimalPayload 'bootstrap.log') -Raw) -notmatch 'Unsupported host assembly name') { throw 'Assembly-name rejection was not logged.' }
if ((Get-FileHash -LiteralPath $minimal -Algorithm SHA256).Hash -ne $minimalHash) { throw 'Minimal EXE changed.' }

$missing = Prepare-Probe 'missing-plugin' $hashProbe
Run-Probe $missing

$disabled = Prepare-Probe 'disabled-plugin' $hashProbe
$disabledPayload = Join-Path (Split-Path $disabled) 'PCL\Plugins\DownloadRestore'
[IO.Directory]::CreateDirectory($disabledPayload) | Out-Null
[IO.File]::WriteAllText((Join-Path $disabledPayload 'disabled'), '')
Run-Probe $disabled
if ((Get-Content -LiteralPath (Join-Path $disabledPayload 'bootstrap.log') -Raw) -notmatch 'is disabled') { throw 'Disabled plugin was not bypassed.' }

$maintenance = Prepare-Probe 'maintenance-mode' $hashProbe
$maintenancePayload = Join-Path (Split-Path $maintenance) 'PCL\Plugins\DownloadRestore'
[IO.Directory]::CreateDirectory($maintenancePayload) | Out-Null
[IO.File]::WriteAllText((Join-Path $maintenancePayload 'PCL.DownloadRestore.dll'), 'unused')
[IO.File]::WriteAllText((Join-Path $maintenancePayload '0Harmony.dll'), 'unused')
Run-Probe $maintenance '--memory'
if (Test-Path -LiteralPath (Join-Path $maintenancePayload 'bootstrap.log')) { throw 'Maintenance-mode startup unexpectedly initialized the plugin.' }

$officialSource = Join-Path $root 'refs\Plain Craft Launcher 2.exe'
if ((Get-FileHash -LiteralPath $officialSource -Algorithm SHA256).Hash -ne $officialFixtureSha256) { throw 'Unexpected official regression fixture SHA-256.' }
$official = Prepare-Probe 'official-host-startup' $officialSource
$networkMarker = Join-Path (Split-Path $official) 'network-probe'
if ($Network) { [IO.File]::WriteAllText($networkMarker, '') } else { [IO.File]::Delete($networkMarker) }
$officialPayload = Join-Path (Split-Path $official) 'PCL\Plugins\DownloadRestore'
[IO.Directory]::CreateDirectory($officialPayload) | Out-Null
Copy-Item -LiteralPath (Join-Path $probeDirectory 'PCL.DownloadRestore.dll') -Destination $officialPayload -Force
Copy-Item -LiteralPath (Join-Path $probeDirectory '0Harmony.dll') -Destination $officialPayload -Force
$officialHash = (Get-FileHash -LiteralPath $official -Algorithm SHA256).Hash
Run-Probe $official
if ((Get-Content -LiteralPath (Join-Path $officialPayload 'bootstrap.log') -Raw) -notmatch 'STARTUP_PROBE_PASS') { throw 'Official constructor startup probe did not pass.' }
if ((Get-FileHash -LiteralPath $official -Algorithm SHA256).Hash -ne $officialHash) { throw 'Official EXE changed.' }
$officialLog = Get-Content -LiteralPath (Join-Path $officialPayload 'bootstrap.log') -Raw
if ($Network -and ([regex]::Matches($officialLog, 'NETWORK_PROBE .* => 200,').Count -ne 2)) { throw 'Official and mirror HTTPS probes did not both succeed.' }

# 仅修改隔离测试副本的 PE 尾部，验证改变 EXE 哈希不会阻止兼容的启动钩子。
$changed = Prepare-Probe 'changed-host-bytes' $officialSource
$changedStream = [IO.File]::Open($changed, [IO.FileMode]::Append, [IO.FileAccess]::Write)
try { $changedStream.WriteByte(0) } finally { $changedStream.Dispose() }
$changedHash = (Get-FileHash -LiteralPath $changed -Algorithm SHA256).Hash
if ($changedHash -eq $officialFixtureSha256) { throw 'Changed-host fixture must have a different SHA-256.' }
$changedPayload = Join-Path (Split-Path $changed) 'PCL\Plugins\DownloadRestore'
[IO.Directory]::CreateDirectory($changedPayload) | Out-Null
Copy-Item -LiteralPath (Join-Path $probeDirectory 'PCL.DownloadRestore.dll') -Destination $changedPayload -Force
Copy-Item -LiteralPath (Join-Path $probeDirectory '0Harmony.dll') -Destination $changedPayload -Force
Run-Probe $changed
if ((Get-Content -LiteralPath (Join-Path $changedPayload 'bootstrap.log') -Raw) -notmatch 'STARTUP_PROBE_PASS') { throw 'Changed-host startup probe did not pass.' }
if ((Get-FileHash -LiteralPath $changed -Algorithm SHA256).Hash -ne $changedHash) { throw 'Changed-host EXE was modified during startup.' }

$results = [ordered]@{
    DomainManagerAutostart = 'pass'
    UnsupportedAssemblyNameRejected = 'pass'
    MissingPluginContinues = 'pass'
    DisabledPluginContinues = 'pass'
    MaintenanceModeContinues = 'pass'
    OfficialConstructorPostfix = 'pass'
    OfficialEmbeddedResolver = 'pass'
    MainStaThreadAfterHostStartupBeforeMainWindow = 'pass'
    ChangedHostHashAcceptedByStartupHook = 'pass'
    OfficialExeUnchangedSha256 = $officialHash
}
if ($Network) { $results['OfficialAndMirrorHttpsWithCertificateVerification'] = 'pass' }
$results | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $testRoot 'results.json') -Encoding utf8
$results | ConvertTo-Json
