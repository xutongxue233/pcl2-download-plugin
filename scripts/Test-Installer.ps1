[CmdletBinding()]
param([Parameter(Mandatory = $true)][string] $OfficialLauncherPath)
. (Join-Path $PSScriptRoot 'Common.ps1')
$OfficialLauncherPath = Select-Launcher $OfficialLauncherPath
if ((Get-FileHashValue $OfficialLauncherPath) -ine $script:SupportedLauncherHash) { throw 'Tests require the supported official EXE.' }
$testRoot = Assert-SafePath (Join-Path ([IO.Path]::GetTempPath()) ('pcl-plugin-installer-test-' + [Guid]::NewGuid().ToString('N')))
$null = New-Item -ItemType Directory -Path $testRoot
$payload = Join-Path $testRoot 'payload'
$payloadPlugin = Join-Path $payload 'PCL\Plugins\DownloadRestore'
$null = New-Item -ItemType Directory -Path $payloadPlugin -Force
[IO.File]::WriteAllText((Join-Path $payload 'PCL.PluginBootstrap.dll'), 'test bootstrap bytes; never executed')
[IO.File]::WriteAllText((Join-Path $payloadPlugin 'PCL.DownloadRestore.dll'), 'test plugin bytes; never executed')
[IO.File]::WriteAllText((Join-Path $payloadPlugin '0Harmony.dll'), 'test dependency bytes; never executed')
Set-Content -LiteralPath (Join-Path $payload 'PCL.PluginBootstrap.dll') -Stream Zone.Identifier -Value "[ZoneTransfer]`r`nZoneId=3`r`n"
$install = Join-Path $PSScriptRoot 'Install.ps1'
$uninstall = Join-Path $PSScriptRoot 'Uninstall.ps1'
$passed = New-Object 'Collections.Generic.List[string]'

function Assert-Test([bool] $Condition, [string] $Message) { if (-not $Condition) { throw "TEST FAILED: $Message" } }
function New-Case([string] $Name) {
    $directory = Join-Path $testRoot $Name
    $null = New-Item -ItemType Directory -Path $directory
    $exe = Join-Path $directory 'renamed official launcher.exe'
    [IO.File]::Copy($OfficialLauncherPath, $exe)
    return $exe
}
function Expect-Failure([scriptblock] $Action, [string] $Name) {
    $failed = $false
    try { & $Action } catch { $failed = $true }
    Assert-Test $failed $Name
}
try {
    $packagedRoot = Join-Path $testRoot 'packaged-layout'
    $packagedScripts = Join-Path $packagedRoot 'scripts'
    $null = New-Item -ItemType Directory -Path $packagedScripts -Force
    foreach ($name in @('Common.ps1', 'Install.ps1', 'Uninstall.ps1')) {
        [IO.File]::Copy((Join-Path $PSScriptRoot $name), (Join-Path $packagedScripts $name))
    }
    Copy-Item -LiteralPath $payload -Destination (Join-Path $packagedRoot 'payload') -Recurse
    $exe = New-Case 'fresh-process-defaults'
    $paths = Get-InstallPaths $exe
    $windowsPowerShell = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
    & $windowsPowerShell -NoLogo -NoProfile -ExecutionPolicy Bypass -STA -File (Join-Path $packagedScripts 'Install.ps1') -LauncherPath $exe
    Assert-Test ($LASTEXITCODE -eq 0) 'fresh PS5.1 process install with default payload failed'
    Assert-Test (Test-Path -LiteralPath $paths.State -PathType Leaf) 'fresh-process installation has no state'
    & $windowsPowerShell -NoLogo -NoProfile -ExecutionPolicy Bypass -STA -File (Join-Path $packagedScripts 'Uninstall.ps1') -LauncherPath $exe
    Assert-Test ($LASTEXITCODE -eq 0 -and -not (Test-Path -LiteralPath $paths.Config)) 'fresh PS5.1 process uninstall failed'
    Assert-Test ((Get-FileHashValue $exe) -eq $script:SupportedLauncherHash) 'fresh-process install changed official EXE'
    $passed.Add('fresh Windows PowerShell5.1 -File process installs packaged layout with default payload and uninstalls')

    $exe = New-Case 'fresh'
    $paths = Get-InstallPaths $exe
    & $install -LauncherPath $exe -PayloadPath $payload
    Assert-Test ((Get-FileHashValue $exe) -eq $script:SupportedLauncherHash) 'EXE changed after install'
    Assert-Test (@(Get-Item -LiteralPath $paths.Bootstrap -Stream Zone.Identifier -ErrorAction SilentlyContinue).Count -eq 0) 'copied bootstrap retained Internet zone mark'
    [xml]$xml = [IO.File]::ReadAllText($paths.Config)
    Assert-Test ($xml.configuration.runtime.appDomainManagerType.value -eq $script:BootstrapType) 'manager not installed'
    Assert-Test ($xml.configuration.startup.supportedRuntime.sku -eq '.NETFramework,Version=v4.8') 'new config has no explicit net48 target'
    Assert-Test ($xml.configuration.runtime.AppContextSwitchOverrides.value.Contains('Switch.System.IO.UseLegacyPathHandling=false')) 'modern path handling switch missing'
    Assert-Test ($xml.configuration.runtime.AppContextSwitchOverrides.value.Contains('Switch.System.Net.DontEnableSchUseStrongCrypto=false') -and $xml.configuration.runtime.AppContextSwitchOverrides.value.Contains('Switch.System.Net.DontEnableSystemDefaultTlsVersions=false')) 'modern TLS defaults missing'
    $stateHash = Get-FileHashValue $paths.State
    & $install -LauncherPath $exe -PayloadPath $payload
    Assert-Test ((Get-FileHashValue $paths.State) -eq $stateHash) 'idempotent install changed state'
    [IO.File]::WriteAllText((Join-Path $paths.Plugin 'bootstrap.log'), 'preserve diagnostic log')
    [IO.File]::WriteAllText((Join-Path $paths.Plugin 'disabled'), '')
    & $uninstall -LauncherPath $exe
    Assert-Test (-not (Test-Path -LiteralPath $paths.Config)) 'new config not removed'
    Assert-Test (-not (Test-Path -LiteralPath $paths.Bootstrap)) 'bootstrap not removed'
    Assert-Test ([IO.File]::ReadAllText((Join-Path $paths.Plugin 'bootstrap.log')) -eq 'preserve diagnostic log') 'log not preserved'
    & $uninstall -LauncherPath $exe
    Assert-Test ([IO.File]::ReadAllText((Join-Path $paths.Plugin 'bootstrap.log')) -eq 'preserve diagnostic log') 'repeated uninstall changed diagnostics'
    $passed.Add('repeated uninstall treats retained diagnostics and disabled marker as already uninstalled')
    & $install -LauncherPath $exe -PayloadPath $payload
    Assert-Test (-not (Test-Path -LiteralPath (Join-Path $paths.Plugin 'disabled'))) 'reinstall did not enable plugin'
    [IO.File]::WriteAllText((Join-Path $paths.Plugin 'user.txt'), 'keep me')
    & $uninstall -LauncherPath $exe
    Assert-Test ([IO.File]::ReadAllText((Join-Path $paths.Plugin 'user.txt')) -eq 'keep me') 'unknown user file deleted'
    $passed.Add('fresh install, renamed EXE, repeat install, uninstall, log-preserving reinstall, disabled reset, unknown-file preservation')

    $exe = New-Case 'existing-config'
    $paths = Get-InstallPaths $exe
    $unrelated = Join-Path $paths.Root 'unrelated.txt'
    [IO.File]::WriteAllText($unrelated, 'keep unrelated downloaded data')
    Set-Content -LiteralPath $unrelated -Stream Zone.Identifier -Value "[ZoneTransfer]`r`nZoneId=3`r`n"
    $originalText = '<?xml version="1.0" encoding="utf-16"?>' + "`r`n" + '<configuration><!-- user comment --><startup><supportedRuntime version="v4.0" /></startup><runtime><gcServer enabled="false" /></runtime></configuration>' + "`r`n"
    [IO.File]::WriteAllText($paths.Config, $originalText, [Text.Encoding]::Unicode)
    $originalHash = Get-FileHashValue $paths.Config
    & $install -LauncherPath $exe -PayloadPath $payload
    $installedBytes = [IO.File]::ReadAllBytes($paths.Config)
    [IO.File]::AppendAllText($paths.Config, '<!-- later edit -->')
    Expect-Failure { & $uninstall -LauncherPath $exe } 'edited config not rejected'
    Assert-Test (Test-Path -LiteralPath $paths.State) 'state removed on conflict'
    [IO.File]::WriteAllBytes($paths.Config, $installedBytes)
    & $uninstall -LauncherPath $exe
    Assert-Test ((Get-FileHashValue $paths.Config) -eq $originalHash) 'original config bytes not restored'
    Assert-Test (@(Get-Item -LiteralPath $unrelated -Stream Zone.Identifier -ErrorAction SilentlyContinue).Count -eq 1) 'unrelated download zone mark changed'
    $passed.Add('existing UTF-16 config exact restoration and edited-config conflict refusal')

    $exe = New-Case 'existing-runtime-switches'
    $paths = Get-InstallPaths $exe
    [IO.File]::WriteAllText($paths.Config, '<configuration><runtime><AppContextSwitchOverrides value="Custom.Unrelated=true;Switch.System.IO.UseLegacyPathHandling=true;Switch.System.IO.BlockLongPaths=true;Switch.System.Net.DontEnableSchUseStrongCrypto=true;Switch.System.Net.DontEnableSystemDefaultTlsVersions=true" /></runtime></configuration>')
    $originalHash = Get-FileHashValue $paths.Config
    & $install -LauncherPath $exe -PayloadPath $payload
    [xml]$xml = [IO.File]::ReadAllText($paths.Config)
    $switchValue = $xml.configuration.runtime.AppContextSwitchOverrides.value
    Assert-Test ($switchValue.Contains('Custom.Unrelated=true') -and $switchValue.Contains('Switch.System.IO.UseLegacyPathHandling=false') -and $switchValue.Contains('Switch.System.IO.BlockLongPaths=false')) 'runtime switches not merged correctly'
    Assert-Test ($switchValue.Contains('Switch.System.Net.DontEnableSchUseStrongCrypto=false') -and $switchValue.Contains('Switch.System.Net.DontEnableSystemDefaultTlsVersions=false')) 'existing TLS switches not updated'
    & $uninstall -LauncherPath $exe
    Assert-Test ((Get-FileHashValue $paths.Config) -eq $originalHash) 'original switch values not restored exactly'
    [IO.File]::WriteAllText($paths.Config, '<configuration><runtime><AppContextSwitchOverrides value="Custom.Duplicate=true;Custom.Duplicate=false" /></runtime></configuration>')
    Expect-Failure { & $install -LauncherPath $exe -PayloadPath $payload } 'duplicate runtime switch not rejected'
    Assert-Test (-not (Test-Path -LiteralPath $paths.Bootstrap)) 'duplicate switch conflict changed payload'
    $passed.Add('AppContext path switches merged, unrelated switches preserved, original values restored, duplicate switches refused')

    $exe = New-Case 'manager-conflict'
    [IO.File]::WriteAllText(($exe + '.config'), '<configuration><runtime><appDomainManagerType value="Other.Manager" /></runtime></configuration>')
    Expect-Failure { & $install -LauncherPath $exe -PayloadPath $payload } 'existing manager not rejected'
    Assert-Test (-not (Test-Path -LiteralPath (Join-Path ([IO.Path]::GetDirectoryName($exe)) 'PCL.PluginBootstrap.dll'))) 'manager conflict left a DLL'
    $passed.Add('other AppDomain manager refused before mutation')

    $exe = New-Case 'dangling-config'
    [IO.File]::WriteAllText(($exe + '.config'), '<configuration><runtime><appDomainManagerType value="PCL.PluginBootstrap.PluginDomainManager" /></runtime></configuration>')
    $danglingHash = Get-FileHashValue ($exe + '.config')
    Expect-Failure { & $uninstall -LauncherPath $exe } 'dangling manager reported already uninstalled'
    Assert-Test ((Get-FileHashValue ($exe + '.config')) -eq $danglingHash) 'dangling manager config modified without backup'
    $passed.Add('missing state with dangling manager config refuses unverified cleanup')

    $exe = New-Case 'unsupported'
    [IO.File]::AppendAllText($exe, 'changed')
    Expect-Failure { & $install -LauncherPath $exe -PayloadPath $payload } 'unsupported EXE not rejected'
    Assert-Test (-not (Test-Path -LiteralPath ($exe + '.config'))) 'unsupported install created config'
    $passed.Add('unsupported EXE rejected without config mutation')

    $exe = New-Case 'updated-and-missing'
    $paths = Get-InstallPaths $exe
    & $install -LauncherPath $exe -PayloadPath $payload
    [IO.File]::AppendAllText($exe, 'simulated update')
    $updatedHash = Get-FileHashValue $exe
    Remove-Item -LiteralPath $paths.Bootstrap
    Remove-Item -LiteralPath (Join-Path $paths.Plugin '0Harmony.dll')
    & $uninstall -LauncherPath $exe
    Assert-Test (-not (Test-Path -LiteralPath $paths.Config)) 'missing DLL prevented config recovery'
    Assert-Test ((Get-FileHashValue $exe) -eq $updatedHash) 'upgraded EXE changed on uninstall'
    $passed.Add('uninstall works after EXE update and missing plugin DLLs')

    $exe = New-Case 'write-failure'
    $paths = Get-InstallPaths $exe
    [IO.File]::WriteAllText($paths.Config, '<configuration />')
    $originalHash = Get-FileHashValue $paths.Config
    $lock = [IO.File]::Open($paths.Config, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    try { Expect-Failure { & $install -LauncherPath $exe -PayloadPath $payload } 'locked config accepted' } finally { $lock.Dispose() }
    Assert-Test ((Get-FileHashValue $paths.Config) -eq $originalHash) 'rollback changed original config'
    Assert-Test (-not (Test-Path -LiteralPath $paths.Bootstrap)) 'rollback left bootstrap'
    Assert-Test (-not (Test-Path -LiteralPath $paths.State)) 'rollback left state'
    $passed.Add('failed config write rolls back owned payload and preserves original config')

    $exe = New-Case 'collision'
    $paths = Get-InstallPaths $exe
    [IO.File]::WriteAllText($paths.Bootstrap, 'unrelated')
    Expect-Failure { & $install -LauncherPath $exe -PayloadPath $payload } 'unregistered bootstrap overwritten'
    Assert-Test ([IO.File]::ReadAllText($paths.Bootstrap) -eq 'unrelated') 'collision file changed'
    $passed.Add('unregistered bootstrap collision preserved')

    $exe = New-Case ('spaced path ' + [char]0x6D4B + [char]0x8BD5)
    $paths = Get-InstallPaths $exe
    & $install -LauncherPath $exe -PayloadPath $payload
    $installedHash = Get-FileHashValue $paths.Config
    $lockedDll = Join-Path $paths.Plugin '0Harmony.dll'
    $lock = [IO.File]::Open($lockedDll, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    try { Expect-Failure { & $uninstall -LauncherPath $exe } 'locked DLL accepted' } finally { $lock.Dispose() }
    Assert-Test ((Get-FileHashValue $paths.Config) -eq $installedHash) 'uninstall failure did not restore installed config'
    Assert-Test (Test-Path -LiteralPath $paths.Bootstrap) 'uninstall failure did not restore bootstrap'
    Assert-InstallationUnchanged $paths (Read-InstallState $paths $exe)
    & $uninstall -LauncherPath $exe
    Assert-Test ((Get-FileHashValue $exe) -eq $script:SupportedLauncherHash) 'Chinese path EXE changed'
    $passed.Add('Chinese and spaced path; failed uninstall restores payload/config and can be retried')

    $exe = New-Case 'junction'
    $realDirectory = [IO.Path]::GetDirectoryName($exe)
    $junction = Join-Path $testRoot 'linked-launcher'
    $null = New-Item -ItemType Junction -Path $junction -Target $realDirectory
    try { Expect-Failure { & $install -LauncherPath (Join-Path $junction ([IO.Path]::GetFileName($exe))) -PayloadPath $payload } 'junction path accepted' }
    finally { [IO.Directory]::Delete($junction) }
    $passed.Add('junction path rejected')

    Expect-Failure { Assert-LauncherStopped (Join-Path $PSHOME 'powershell.exe') } 'running target not refused'
    $passed.Add('running executable refused before mutation')

    Write-Host "PASS: $($passed.Count) installer integration groups (synthetic payload DLLs, official EXE never executed)."
    $passed | ForEach-Object { Write-Host "  $_" }
} finally {
    $null = Assert-SafePath $testRoot ([IO.Path]::GetTempPath())
    Remove-Item -LiteralPath $testRoot -Recurse -Force
}
