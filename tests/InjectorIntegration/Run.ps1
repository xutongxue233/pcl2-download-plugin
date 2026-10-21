[CmdletBinding()]
param(
    [string]$HostExecutable,
    [string]$LegacyReleaseDirectory
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
if (-not $HostExecutable) { $HostExecutable = Join-Path $root 'refs\Plain Craft Launcher 2.exe' }
if ($LegacyReleaseDirectory) {
    # 旧版发行包必须由调用者明确提供；测试不会下载或替换历史附件。
    $LegacyReleaseDirectory = [IO.Path]::GetFullPath($LegacyReleaseDirectory)
    if (-not [IO.Directory]::Exists($LegacyReleaseDirectory)) { throw "Legacy release directory does not exist: $LegacyReleaseDirectory" }
}
& dotnet build (Join-Path $PSScriptRoot 'InjectorIntegration.csproj') -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Failed to build injector integration harness.' }
$output = Join-Path $root 'artifacts\injector-integration'
[IO.Directory]::CreateDirectory($output) | Out-Null
$runner = Join-Path $PSScriptRoot 'bin\Release\net48\PCL.DownloadRestore.InjectorIntegration.exe'
$runnerArguments = @($root, $HostExecutable, $output)
if ($LegacyReleaseDirectory) { $runnerArguments += $LegacyReleaseDirectory }
& $runner @runnerArguments
if ($LASTEXITCODE -ne 0) { throw "Injector integration failed; inspect $output\results.json" }
