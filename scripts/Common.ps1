Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'


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
