[CmdletBinding()]
param(
    [string] $InjectorDirectory,
    [string] $OutputPath,
    [string] $SourceOutputPath
)
. (Join-Path $PSScriptRoot 'Common.ps1')
if (-not $InjectorDirectory) { $InjectorDirectory = Join-Path $PSScriptRoot '..\artifacts\Injector' }
if (-not $OutputPath) { $OutputPath = Join-Path $PSScriptRoot '..\artifacts\PCL.DownloadRestore.Injector-2.99.0.0.zip' }
if (-not $SourceOutputPath) { $SourceOutputPath = Join-Path $PSScriptRoot '..\artifacts\PCL.DownloadRestore.Injector-Source.zip' }
$projectRoot = Get-FullPath (Join-Path $PSScriptRoot '..')
$InjectorDirectory = Assert-SafePath $InjectorDirectory
$OutputPath = Assert-SafePath $OutputPath
$SourceOutputPath = Assert-SafePath $SourceOutputPath
if ($OutputPath -ieq $SourceOutputPath) { throw 'Binary and source ZIP output paths must be different.' }
$exeName = 'PCL.DownloadRestore.Injector.exe'
$executable = Assert-SafePath (Join-Path $InjectorDirectory $exeName) $InjectorDirectory
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) { throw 'Run scripts/Build-Injector.ps1 first.' }
if ([Reflection.AssemblyName]::GetAssemblyName($executable).Name -ne 'PCL.DownloadRestore.Injector') { throw 'Only the built injector executable may be distributed.' }
$allowedFiles = @($exeName, 'README.md', 'VALIDATION.md', 'original-build.json', 'licenses\PCL-LICENCE.txt', 'licenses\Harmony-LICENSE.txt', 'SHA256SUMS.txt')
foreach ($item in @(Get-ChildItem -LiteralPath $InjectorDirectory -Recurse -Force)) {
    $null = Assert-SafePath $item.FullName $InjectorDirectory
    if ($item.PSIsContainer) { continue }
    $relative = $item.FullName.Substring($InjectorDirectory.Length + 1)
    if ($relative -notin $allowedFiles) { throw "Unexpected injector distribution file: $relative" }
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$outputDirectory = [IO.Path]::GetDirectoryName($OutputPath)
$null = New-Item -ItemType Directory -Path $outputDirectory -Force
$staging = Assert-SafePath (Join-Path $outputDirectory ('.injector-package-' + [Guid]::NewGuid().ToString('N'))) $outputDirectory
$temporaryZip = Join-Path $outputDirectory ('.injector-distribution-' + [Guid]::NewGuid().ToString('N') + '.zip')
try {
    $null = New-Item -ItemType Directory -Path (Join-Path $staging 'licenses') -Force
    [IO.File]::Copy($executable, (Join-Path $staging $exeName))
    # Refresh human-readable reports at packaging time after the final GUI tests.
    $copies = [ordered]@{
        'README.md' = (Join-Path $projectRoot 'INJECTOR.md')
        'VALIDATION.md' = (Join-Path $projectRoot 'INJECTOR-VALIDATION.md')
        'original-build.json' = (Join-Path $projectRoot 'tests\official-build.json')
        'licenses\PCL-LICENCE.txt' = (Join-Path $projectRoot 'licenses\PCL-LICENCE.txt')
        'licenses\Harmony-LICENSE.txt' = (Join-Path $projectRoot 'licenses\Harmony-LICENSE.txt')
    }
    foreach ($entry in $copies.GetEnumerator()) {
        $source = Assert-SafePath $entry.Value $projectRoot
        [IO.File]::Copy($source, (Join-Path $staging $entry.Key))
    }
    $sums = @()
    foreach ($file in @(Get-ChildItem -LiteralPath $staging -Recurse -File | Sort-Object FullName)) {
        $sums += (Get-FileHashValue $file.FullName) + '  ' + $file.FullName.Substring($staging.Length + 1).Replace('\', '/')
    }
    [IO.File]::WriteAllLines((Join-Path $staging 'SHA256SUMS.txt'), $sums, (New-Object Text.UTF8Encoding($false)))
    [IO.Compression.ZipFile]::CreateFromDirectory($staging, $temporaryZip, [IO.Compression.CompressionLevel]::Optimal, $false)
    $zip = [IO.Compression.ZipFile]::OpenRead($temporaryZip)
    try {
        $exeEntries = @($zip.Entries | Where-Object { $_.FullName -match '(?i)\.exe$' })
        if ($exeEntries.Count -ne 1 -or $exeEntries[0].FullName -ne $exeName) { throw 'The injector ZIP must contain exactly one executable: the injector itself.' }
        if (@($zip.Entries | Where-Object { $_.FullName -match '(?i)\.(dll|ps1|cmd|bat)$|(^|/)(refs|bin|obj|\.tools)/' }).Count -ne 0) { throw 'The injector ZIP contains an external dependency or script.' }
    } finally { $zip.Dispose() }
    [IO.File]::Copy($temporaryZip, $OutputPath, $true)
    Write-Host "Created single-executable injector package: $OutputPath"
    Write-Host "SHA256: $(Get-FileHashValue $OutputPath)"
} finally {
    if (Test-Path -LiteralPath $temporaryZip) { Remove-Item -LiteralPath $temporaryZip -Force }
    if (Test-Path -LiteralPath $staging) { $null = Assert-SafePath $staging $outputDirectory; Remove-Item -LiteralPath $staging -Recurse -Force }
}

$sourceDirectory = [IO.Path]::GetDirectoryName($SourceOutputPath)
$null = New-Item -ItemType Directory -Path $sourceDirectory -Force
$sourceStaging = Assert-SafePath (Join-Path $sourceDirectory ('.injector-source-' + [Guid]::NewGuid().ToString('N'))) $sourceDirectory
$sourceZip = Join-Path $sourceDirectory ('.injector-source-distribution-' + [Guid]::NewGuid().ToString('N') + '.zip')
try {
    $null = New-Item -ItemType Directory -Path $sourceStaging
    $allowedExtensions = @('.cs', '.csproj', '.xaml', '.ttf', '.ps1', '.cmd', '.md', '.txt', '.json', '.config', '.manifest', '.resx', '.ico', '.props', '.targets', '.sln', '.yml', '.yaml', '.gitignore', '.editorconfig')
    foreach ($file in @(Get-ChildItem -LiteralPath $projectRoot -Recurse -File -Force)) {
        $relative = $file.FullName.Substring($projectRoot.Length + 1)
        if ($relative -match '(^|[\\/])(artifacts|refs|references|\.tools|\.git|\.vs|\.idea|\.analysis|bin|obj|test-results|TestResults|payload|packages|\.packages|deps|node_modules)([\\/]|$)') { continue }
        if ($file.Extension -notin $allowedExtensions -and $file.Name -notin @('LICENSE', 'LICENCE', '.gitignore', '.gitattributes', '.editorconfig')) { continue }
        $null = Assert-SafePath $file.FullName $projectRoot
        $destination = Assert-SafePath (Join-Path $sourceStaging $relative) $sourceStaging
        $null = New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($destination)) -Force
        [IO.File]::Copy($file.FullName, $destination)
    }
    foreach ($required in @('src\Injector\PCL.DownloadRestore.Injector.csproj', 'src\Injector\app.manifest', 'src\Injector\MainWindow.xaml', 'src\Injector\MainWindow.xaml.cs', 'src\Injector\UI\PclStyles.xaml', 'src\Injector\UI\Resources\Font.ttf', 'src\Injector\UI\SOURCE-NOTES.md', 'scripts\Build-Injector.ps1', 'scripts\Package-Injector.ps1', 'INJECTOR.md', 'INJECTOR-VALIDATION.md')) {
        if (-not (Test-Path -LiteralPath (Join-Path $sourceStaging $required) -PathType Leaf)) { throw "Missing injector source file: $required" }
    }
    [IO.Compression.ZipFile]::CreateFromDirectory($sourceStaging, $sourceZip, [IO.Compression.CompressionLevel]::Optimal, $false)
    [IO.File]::Copy($sourceZip, $SourceOutputPath, $true)
    Write-Host "Created injector source package: $SourceOutputPath"
    Write-Host "SHA256: $(Get-FileHashValue $SourceOutputPath)"
} finally {
    if (Test-Path -LiteralPath $sourceZip) { Remove-Item -LiteralPath $sourceZip -Force }
    if (Test-Path -LiteralPath $sourceStaging) { $null = Assert-SafePath $sourceStaging $sourceDirectory; Remove-Item -LiteralPath $sourceStaging -Recurse -Force }
}
