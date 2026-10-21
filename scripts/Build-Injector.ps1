[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')][string] $Configuration = 'Release',
    [string] $LauncherPath
)
. (Join-Path $PSScriptRoot 'Common.ps1')
$projectRoot = Get-FullPath (Join-Path $PSScriptRoot '..')
$injectorProject = Join-Path $projectRoot 'src\Injector\PCL.DownloadRestore.Injector.csproj'
if (-not (Test-Path -LiteralPath $injectorProject -PathType Leaf)) { throw "Injector project not found: $injectorProject" }

$arguments = @{ Configuration = $Configuration }
if ($LauncherPath) { $arguments.LauncherPath = $LauncherPath }
& (Join-Path $PSScriptRoot 'Build.ps1') @arguments
& dotnet build $injectorProject --configuration $Configuration --nologo
if ($LASTEXITCODE -ne 0) { throw 'Injector compilation failed.' }

$outputDirectory = Assert-SafePath (Join-Path $projectRoot "src\Injector\bin\$Configuration\net48") $projectRoot
$executable = Assert-SafePath (Join-Path $outputDirectory 'PCL.DownloadRestore.Injector.exe') $projectRoot
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) { throw "Missing injector executable: $executable" }
if (@(Get-ChildItem -LiteralPath $outputDirectory -File -Filter '*.dll').Count -ne 0) {
    throw 'The injector output has external DLL dependencies. The user-facing injector must be a single executable.'
}

# Load the freshly built, trusted assembly from bytes so verification never locks
# the compiler output. No entry point or installer method is invoked here.
$assembly = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes($executable))
if ($assembly.GetName().Name -ne 'PCL.DownloadRestore.Injector') { throw 'Unexpected executable assembly identity.' }
$resourceFiles = [ordered]@{
    'payload.PCL.PluginBootstrap.dll' = (Join-Path $projectRoot 'artifacts\payload\PCL.PluginBootstrap.dll')
    'payload.PCL.DownloadRestore.dll' = (Join-Path $projectRoot 'artifacts\payload\PCL\Plugins\DownloadRestore\PCL.DownloadRestore.dll')
    'payload.0Harmony.dll' = (Join-Path $projectRoot 'artifacts\payload\PCL\Plugins\DownloadRestore\0Harmony.dll')
}
$resourceNames = @($assembly.GetManifestResourceNames())
foreach ($resource in $resourceFiles.GetEnumerator()) {
    if ($resource.Key -notin $resourceNames) { throw "Missing embedded payload: $($resource.Key)" }
    $stream = $assembly.GetManifestResourceStream($resource.Key)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $embeddedHash = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') }
    finally { $sha.Dispose(); $stream.Dispose() }
    if ($embeddedHash -ne (Get-FileHashValue $resource.Value)) { throw "Embedded payload is stale: $($resource.Key)" }
}
foreach ($resourceName in $resourceNames) {
    if ($resourceName -match '(?i)\.exe$' -or ($resourceName.StartsWith('payload.') -and $resourceName -notin @($resourceFiles.Keys))) {
        throw "Unexpected embedded distribution payload: $resourceName"
    }
}

$staging = Assert-SafePath (Join-Path $projectRoot 'artifacts\Injector') $projectRoot
$allowedFiles = @('PCL.DownloadRestore.Injector.exe', 'README.md', 'VALIDATION.md', 'original-build.json', 'licenses\PCL-LICENCE.txt', 'licenses\Harmony-LICENSE.txt', 'SHA256SUMS.txt')
if (Test-Path -LiteralPath $staging) {
    foreach ($file in @(Get-ChildItem -LiteralPath $staging -Recurse -File -Force)) {
        $null = Assert-SafePath $file.FullName $staging
        $relative = $file.FullName.Substring($staging.Length + 1)
        if ($relative -notin $allowedFiles) { throw "Unexpected existing injector staging file: $relative" }
    }
}
$null = New-Item -ItemType Directory -Path (Join-Path $staging 'licenses') -Force
$copies = [ordered]@{
    'PCL.DownloadRestore.Injector.exe' = $executable
    'README.md' = (Join-Path $projectRoot 'INJECTOR.md')
    'VALIDATION.md' = (Join-Path $projectRoot 'INJECTOR-VALIDATION.md')
    'original-build.json' = (Join-Path $projectRoot 'tests\official-build.json')
    'licenses\PCL-LICENCE.txt' = (Join-Path $projectRoot 'licenses\PCL-LICENCE.txt')
    'licenses\Harmony-LICENSE.txt' = (Join-Path $projectRoot 'licenses\Harmony-LICENSE.txt')
}
foreach ($entry in $copies.GetEnumerator()) {
    $source = Assert-SafePath $entry.Value $projectRoot
    $destination = Assert-SafePath (Join-Path $staging $entry.Key) $staging
    [IO.File]::Copy($source, $destination, $true)
}
$sums = @()
foreach ($file in @(Get-ChildItem -LiteralPath $staging -Recurse -File | Where-Object { $_.Name -ne 'SHA256SUMS.txt' } | Sort-Object FullName)) {
    $sums += (Get-FileHashValue $file.FullName) + '  ' + $file.FullName.Substring($staging.Length + 1).Replace('\', '/')
}
[IO.File]::WriteAllLines((Join-Path $staging 'SHA256SUMS.txt'), $sums, (New-Object Text.UTF8Encoding($false)))
Write-Host "Standalone C# injector staged: $staging"
Write-Host "Injector SHA256: $(Get-FileHashValue (Join-Path $staging 'PCL.DownloadRestore.Injector.exe'))"
Write-Host 'All three embedded payload resources match the freshly built plugin DLLs. No official PCL executable is staged.'
