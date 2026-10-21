$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
& dotnet build (Join-Path $PSScriptRoot 'NetworkProbe.csproj') -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Probe build failed.' }
$output = Join-Path $root 'artifacts\tls-probes'
$manager = '<appDomainManagerAssembly value="PCL.PluginBootstrap, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null"/><appDomainManagerType value="PCL.PluginBootstrap.PluginDomainManager"/>'
$io = 'Switch.System.IO.UseLegacyPathHandling=false;Switch.System.IO.BlockLongPaths=false'
$tls = ';Switch.System.Net.DontEnableSchUseStrongCrypto=false;Switch.System.Net.DontEnableSystemDefaultTlsVersions=false'
foreach ($mode in 'baseline','manager-legacy','manager-secure') {
    $folder = Join-Path $output $mode
    [IO.Directory]::CreateDirectory($folder) | Out-Null
    $exe = Join-Path $folder 'NetworkProbe.exe'
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'bin\Release\net48\NetworkProbe.exe') -Destination $exe -Force
    Copy-Item -LiteralPath (Join-Path $root 'src\Bootstrap\bin\Release\net48\PCL.PluginBootstrap.dll') -Destination $folder -Force
    $hook = if ($mode -eq 'baseline') { '' } else { $manager }
    $switches = if ($mode -eq 'manager-secure') { $io + $tls } else { $io }
    [IO.File]::WriteAllText($exe + '.config', '<configuration><startup><supportedRuntime version="v4.0" sku=".NETFramework,Version=v4.8"/></startup><runtime><AppContextSwitchOverrides value="' + $switches + '"/>' + $hook + '</runtime></configuration>')
    & $exe | Tee-Object -FilePath (Join-Path $folder 'result.txt')
}
