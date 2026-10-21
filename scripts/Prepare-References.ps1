[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$HostExecutable,
    [string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $PSScriptRoot '..\refs' }
$hostPath = (Resolve-Path -LiteralPath $HostExecutable).Path
$referenceDirectory = [IO.Path]::GetFullPath($OutputDirectory)
[IO.Directory]::CreateDirectory($referenceDirectory) | Out-Null
$toolDirectory = Join-Path $PSScriptRoot '..\.tools\ResourceExtractor'
[IO.Directory]::CreateDirectory($toolDirectory) | Out-Null
@'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
</Project>
'@ | Set-Content -LiteralPath (Join-Path $toolDirectory 'ResourceExtractor.csproj') -Encoding utf8
@'
using System.Collections;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Resources;
using System.Security.Cryptography;
using System.Text.Json;

// 只读取 PE 元数据和原始资源，不加载或执行启动器。
var expected = new HashSet<string>(new[] { "NAudio", "Newtonsoft.Json", "Ookii.Dialogs.Wpf", "Imazen.WebP", "CacheCow.Common", "CacheCow.Client.FileStore", "CacheCow.Client", "ThrottleDebounce", "System.Net.Http.Formatting", "Microsoft.Win32.Registry", "PCLCS", "MeloongCore.Wpf", "MeloongCore" }, StringComparer.Ordinal);
var found = new Dictionary<string, string>(StringComparer.Ordinal);
using var source = File.OpenRead(args[0]);
using var pe = new PEReader(source);
var metadata = pe.GetMetadataReader();
var resources = pe.GetSectionData(pe.PEHeaders.CorHeader!.ResourcesDirectory.RelativeVirtualAddress);
foreach (var handle in metadata.ManifestResources)
{
    var resource = metadata.GetManifestResource(handle);
    if (!resource.Implementation.IsNil || !metadata.GetString(resource.Name).EndsWith(".resources", StringComparison.Ordinal)) continue;
    var block = resources.GetReader(checked((int)resource.Offset), resources.Length - checked((int)resource.Offset));
    var bytes = block.ReadBytes(block.ReadInt32());
    using var reader = new ResourceReader(new MemoryStream(bytes, false));
    var enumerator = reader.GetEnumerator();
    while (enumerator.MoveNext())
    {
        var name = (string)enumerator.Key; // 不访问 Value，避免触发对象反序列化。
        if (!expected.Contains(name)) continue;
        reader.GetResourceData(name, out var resourceType, out var raw);
        if (resourceType != "ResourceTypeCode.ByteArray") throw new InvalidDataException($"Unexpected resource type for {name}: {resourceType}");
        var length = BitConverter.ToInt32(raw, 0);
        if (length < 2 || length != raw.Length - 4 || raw[4] != 'M' || raw[5] != 'Z') throw new InvalidDataException($"Invalid DLL resource {name}");
        var dll = raw.AsSpan(4, length).ToArray();
        File.WriteAllBytes(Path.Combine(args[1], name + ".dll"), dll);
        found.Add(name + ".dll", Convert.ToHexString(SHA256.HashData(dll)));
    }
}
if (found.Count != expected.Count) throw new InvalidDataException($"Expected {expected.Count} embedded libraries, found {found.Count}.");
var hostCopy = Path.Combine(args[1], "Plain Craft Launcher 2.exe");
File.Copy(args[0], hostCopy, true);
found.Add("Plain Craft Launcher 2.exe", Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(hostCopy))));
File.WriteAllText(Path.Combine(args[1], "sha256.json"), JsonSerializer.Serialize(found, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Extracted {expected.Count} original libraries and the unchanged host build reference to {args[1]}");
'@ | Set-Content -LiteralPath (Join-Path $toolDirectory 'Program.cs') -Encoding utf8
& dotnet run --project (Join-Path $toolDirectory 'ResourceExtractor.csproj') --configuration Release -- $hostPath $referenceDirectory
if ($LASTEXITCODE -ne 0) { throw "Resource extraction failed ($LASTEXITCODE)." }
