[CmdletBinding()]
param([string]$HostExecutable)
$ErrorActionPreference = 'Stop'
if (-not $HostExecutable) { $HostExecutable = Join-Path $PSScriptRoot '..\..\refs\Plain Craft Launcher 2.exe' }
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$testDirectory = Join-Path $root 'artifacts\runtime-integration'
[IO.Directory]::CreateDirectory($testDirectory) | Out-Null
& dotnet build (Join-Path $PSScriptRoot 'RuntimeIntegration.csproj') -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Failed to build runtime integration harness.' }
Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'bin\Release\net48') -File | Copy-Item -Destination $testDirectory -Force
$testHost = Join-Path $testDirectory 'Plain Craft Launcher 2.exe'
Copy-Item -LiteralPath $HostExecutable -Destination $testHost -Force
$testExecutable = Join-Path $testDirectory 'PCL.DownloadRestore.RuntimeIntegration.exe'
& $testExecutable $testHost (Join-Path $testDirectory 'results.txt')
if ($LASTEXITCODE -ne 0) { throw "Runtime integration failed; inspect $testDirectory\results.txt" }
