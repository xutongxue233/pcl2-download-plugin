[CmdletBinding()]
param([string]$HostExecutable)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
if (-not $HostExecutable) { $HostExecutable = Join-Path $root 'refs\Plain Craft Launcher 2.exe' }
& dotnet build (Join-Path $PSScriptRoot 'InjectorIntegration.csproj') -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Failed to build injector integration harness.' }
$output = Join-Path $root 'artifacts\injector-integration'
[IO.Directory]::CreateDirectory($output) | Out-Null
$runner = Join-Path $PSScriptRoot 'bin\Release\net48\PCL.DownloadRestore.InjectorIntegration.exe'
& $runner $root $HostExecutable $output
if ($LASTEXITCODE -ne 0) { throw "Injector integration failed; inspect $output\results.json" }
