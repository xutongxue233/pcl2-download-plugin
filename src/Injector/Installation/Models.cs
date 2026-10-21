using System.Collections.Generic;
using System.Runtime.Serialization;

namespace PCL.DownloadRestore.Injector.Installation
{
    public sealed class InspectionResult
    {
        public string Status { get; internal set; }
        public string Message { get; internal set; }
        public string LauncherSha256 { get; internal set; }
        public bool CanInstall { get; internal set; }
        public bool CanUninstall { get; internal set; }
        public bool CanUpgrade { get; internal set; }
        public string InstalledVersion { get; internal set; }
        public string AvailableVersion { get; internal set; }
        public bool IsDisabled { get; internal set; }
    }

    public sealed class OperationResult
    {
        public bool Success { get; internal set; }
        public string Message { get; internal set; }
        public string LauncherSha256 { get; internal set; }
    }

    // 保留 schema 1 的字段；schema 2 新增插件包版本，旧工具会拒绝未知格式而非覆盖。
    [DataContract]
    internal sealed class InstallState
    {
        [DataMember(Order = 0, IsRequired = true)] public int Schema { get; set; }
        [DataMember(Order = 1, IsRequired = true)] public string LauncherPath { get; set; }
        [DataMember(Order = 2, IsRequired = true)] public string LauncherSha256AtInstall { get; set; }
        [DataMember(Order = 3, IsRequired = true)] public string InstalledUtc { get; set; }
        [DataMember(Order = 4, IsRequired = true)] public bool OriginalConfigExisted { get; set; }
        [DataMember(Order = 5)] public string OriginalConfigSha256 { get; set; }
        [DataMember(Order = 6, IsRequired = true)] public string InstalledConfigSha256 { get; set; }
        [DataMember(Order = 7, IsRequired = true)] public string BootstrapSha256 { get; set; }
        [DataMember(Order = 8, IsRequired = true)] public List<PluginFileState> PluginFiles { get; set; }
        [DataMember(Order = 9, EmitDefaultValue = false)] public string PackageVersion { get; set; }
        [DataMember(Order = 10, EmitDefaultValue = false)] public string UpdatedUtc { get; set; }
    }

    [DataContract]
    internal sealed class PluginFileState
    {
        [DataMember(Order = 0, IsRequired = true)] public string Path { get; set; }
        [DataMember(Order = 1, IsRequired = true)] public string Sha256 { get; set; }
    }
}
