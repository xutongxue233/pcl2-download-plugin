using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PCL.DownloadRestore.Injector.Installation
{
    public sealed partial class InstallationService
    {
        private bool HasStandardPayload(InstallState state) => state.PluginFiles.Count == 2 &&
            state.PluginFiles.Any(file => Same(file.Path, "PCL.DownloadRestore.dll")) &&
            state.PluginFiles.Any(file => Same(file.Path, "0Harmony.dll"));

        private bool MatchesBundledPayload(InstallState state) => HasStandardPayload(state) &&
            Same(state.BootstrapSha256, payloadHashes[BootstrapKey]) && state.PluginFiles.All(file =>
                Same(file.Sha256, payloadHashes[PluginPrefix + (Same(file.Path, "0Harmony.dll") ? "0Harmony.dll" : "PCL.DownloadRestore.dll")]));

        private InspectionResult InspectRegistered(InstallationPaths paths, InstallState state, string hash)
        {
            bool matches = MatchesBundledPayload(state);
            string version = state.Schema == 2 ? state.PackageVersion : matches ? packageVersion : PluginVersions.IdentifyLegacy(state);
            bool disabled = File.Exists(paths.Disabled);
            bool missing = !File.Exists(paths.Bootstrap) || state.PluginFiles.Any(file => !File.Exists(paths.OwnedPluginFile(file.Path)));
            if (missing)
                return Inspection("Incomplete", "部分插件文件已缺失。请先卸载以恢复启动配置，再重新安装。", hash, uninstall: true, version: version, disabled: disabled);
            int comparison = version == null ? 0 : new Version(version).CompareTo(new Version(packageVersion));
            // 版本记录优先于内容相同：不能把未来仅修改工具的版本误标为当前版本。
            if (version != null && comparison > 0)
                return Inspection("NewerVersion", "已安装 v" + version + "，比本工具内置版本更新，保留现有插件，不执行降级。",
                    hash, install: disabled, uninstall: true, version: version, disabled: disabled);
            if (matches && comparison == 0)
                return Inspection(disabled ? "Disabled" : "Installed", disabled
                    ? "插件 v" + version + " 已安装但处于停用状态，可以重新启用。"
                    : "插件 v" + version + " 与内置载荷一致，无需升级；实际加载结果请查看启动日志。",
                    hash, install: disabled, uninstall: true, version: version, disabled: disabled);
            if (version != null && comparison == 0)
                return Inspection("DifferentBuild", "已安装相同版本的不同构建，无法确认是否应替换，已保留现有文件。",
                    hash, install: disabled, uninstall: true, version: version, disabled: disabled);
            if (version == null || !HasStandardPayload(state))
                return Inspection("UnknownVersion", "安装记录完整，但无法可靠识别这组插件载荷的版本，不能自动升级。请保留现有文件。",
                    hash, install: disabled, uninstall: true, version: version, disabled: disabled);
            return Inspection("UpgradeAvailable", "可将插件 v" + version + " 升级到 v" + packageVersion + "，无需先卸载。" +
                (disabled ? "升级后保留停用状态。" : "原配置备份、游戏和日志会保留。"),
                hash, uninstall: true, version: version, upgrade: true, disabled: disabled);
        }

        public OperationResult Upgrade(string launcherPath)
        {
            try
            {
                InstallationPaths paths = InstallationPaths.FromLauncher(launcherPath);
                using (var operationLock = new InstallationLock(paths.Root))
                {
                    paths.AssertStopped();
                    string hash = InstallationPaths.HashFile(paths.Launcher);
                    if (!InstallationPaths.Exists(paths.State)) throw InstallationPaths.Error("插件尚未安装，请先使用注入功能。");
                    InstallState state = ReadState(paths);
                    AssertUnchanged(paths, state, false);
                    InspectionResult current = InspectRegistered(paths, state, hash);
                    if (current.Status == "Installed" || current.Status == "Disabled")
                        return Success("已安装的插件与内置载荷一致，无需升级，现有文件和启用状态未改变。", hash);
                    if (!current.CanUpgrade) throw InstallationPaths.Error(current.Message);
                    return UpgradeRegistered(paths, state, hash, current.InstalledVersion);
                }
            }
            catch (InvalidOperationException) { throw; }
            catch (Exception ex) { throw InstallationPaths.Error("升级未完成。" + Explain(ex), ex); }
        }

        private OperationResult UpgradeRegistered(InstallationPaths paths, InstallState previous, string initialHash, string previousVersion)
        {
            byte[] installedConfig = File.ReadAllBytes(paths.Config);
            byte[] suspendedConfig = LauncherConfiguration.SuspendManager(installedConfig);
            byte[] previousReceipt = File.ReadAllBytes(paths.State);
            string previousReceiptHash = InstallationPaths.HashBytes(previousReceipt);
            string disabledHash = SnapshotDisabled(paths);
            string suspendedHash = InstallationPaths.HashBytes(suspendedConfig);
            var next = new InstallState {
                Schema = 2, PackageVersion = packageVersion, UpdatedUtc = DateTime.UtcNow.ToString("o"),
                LauncherPath = previous.LauncherPath, LauncherSha256AtInstall = initialHash,
                InstalledUtc = previous.InstalledUtc, OriginalConfigExisted = previous.OriginalConfigExisted,
                OriginalConfigSha256 = previous.OriginalConfigSha256, InstalledConfigSha256 = previous.InstalledConfigSha256,
                BootstrapSha256 = payloadHashes[BootstrapKey], PluginFiles = PayloadKeys.Where(key => key != BootstrapKey)
                    .Select(key => new PluginFileState { Path = key.Substring(PluginPrefix.Length), Sha256 = payloadHashes[key] }).ToList()
            };
            string staging = CreateStaging(paths, "upgrade");
            var replaced = new List<UpgradeReplacement>();
            bool configSuspended = false, configRestored = false, preserveStaging = false;
            try
            {
                // 保存恢复所需的原始字节；绝不改写最初安装时的 .original-exe.config。
                File.WriteAllBytes(Path.Combine(staging, "installed.config"), installedConfig);
                File.WriteAllBytes(Path.Combine(staging, "previous-receipt.json"), previousReceipt);
                AssertUnchanged(paths, previous, false);
                AssertHash(paths.Config, InstallationPaths.HashBytes(installedConfig), "升级前启动配置发生变化。");
                AssertHash(paths.State, previousReceiptHash, "升级前安装记录发生变化。");
                AssertHash(paths.Launcher, initialHash, "升级期间 PCL 文件发生变化。");
                AssertDisabled(paths, disabledHash);
                paths.AssertStopped();
                ReplaceConfig(paths, suspendedConfig, staging);
                configSuspended = true;

                foreach (string key in PayloadKeys)
                {
                    paths.AssertStopped();
                    AssertHash(paths.Config, suspendedHash, "升级期间启动配置被修改。");
                    string destination = key == BootstrapKey ? paths.Bootstrap : paths.OwnedPluginFile(key.Substring(PluginPrefix.Length));
                    string previousHash = key == BootstrapKey ? previous.BootstrapSha256 :
                        previous.PluginFiles.Single(file => Same(file.Path, key.Substring(PluginPrefix.Length))).Sha256;
                    ReplaceForUpgrade(destination, payload[key], previousHash, paths.Root, staging, replaced);
                }
                ReplaceForUpgrade(paths.State, Serialize(next), previousReceiptHash, paths.Root, staging, replaced);
                AssertUpgradeFiles(paths, next);
                AssertDisabled(paths, disabledHash);
                AssertHash(paths.Launcher, initialHash, "升级期间 PCL 文件发生变化。");
                AssertHash(paths.Config, suspendedHash, "升级期间启动配置被修改。");
                paths.AssertStopped();
                ReplaceConfig(paths, installedConfig, staging);
                configRestored = true;
                AssertUnchanged(paths, ReadState(paths), false);
                AssertDisabled(paths, disabledHash);
                AssertHash(paths.Launcher, initialHash, "升级期间 PCL 文件发生变化。");
                Log("插件版本：v" + previousVersion + " → v" + packageVersion);
                return Success("插件已直接升级到 v" + packageVersion + "，原配置备份、游戏及日志均保留。" +
                    (disabledHash == null ? "下次启动 PCL 时会尝试加载新版插件。" : "插件保持停用状态。"), initialHash);
            }
            catch (Exception failure)
            {
                try
                {
                    // 先暂停加载，再逆序恢复文件，全部校验通过后才恢复加载入口。
                    if (configRestored)
                    {
                        AssertHash(paths.Config, previous.InstalledConfigSha256, "升级后的配置被其他程序修改，已保留恢复目录。");
                        ReplaceConfig(paths, suspendedConfig, staging);
                        configRestored = false;
                    }
                    foreach (UpgradeReplacement item in replaced.AsEnumerable().Reverse())
                    {
                        InstallationPaths.SafePath(item.Path, paths.Root);
                        AssertHash(item.Backup, item.OldHash, "升级恢复文件已变化，请保留恢复目录。");
                        AssertHash(item.Path, item.NewHash, "升级后的文件被其他程序修改，已保留恢复目录，未覆盖该文件。");
                        File.Replace(item.Backup, item.Path, null);
                        AssertHash(item.Path, item.OldHash, "旧插件文件恢复校验失败。");
                    }
                    if (configSuspended)
                    {
                        AssertUpgradeFiles(paths, previous);
                        AssertHash(paths.State, previousReceiptHash, "旧安装记录恢复校验失败。");
                        AssertDisabled(paths, disabledHash);
                        AssertHash(paths.Config, suspendedHash, "升级期间配置被其他程序修改，已保留恢复目录。");
                        ReplaceConfig(paths, installedConfig, staging);
                        AssertUnchanged(paths, ReadState(paths), false);
                    }
                }
                catch (Exception rollback)
                {
                    preserveStaging = true;
                    throw InstallationPaths.Error("升级未完成，自动恢复也未能全部完成。请勿启动 PCL，保留插件备份和恢复目录：" + staging +
                        "\r\n原错误：" + failure.Message + "\r\n恢复错误：" + rollback.Message, failure);
                }
                throw InstallationPaths.Error("升级未完成，已恢复升级前的插件、安装记录和启动配置。" + Explain(failure), failure);
            }
            finally { if (!preserveStaging) TryCleanStaging(staging, paths.Root); }
        }

        private static string SnapshotDisabled(InstallationPaths paths)
        {
            InstallationPaths.SafePath(paths.Disabled, paths.Plugin);
            if (Directory.Exists(paths.Disabled)) throw InstallationPaths.Error("停用标记被同名文件夹占用，无法升级。");
            return File.Exists(paths.Disabled) ? InstallationPaths.HashFile(paths.Disabled) : null;
        }

        private static void AssertDisabled(InstallationPaths paths, string expected)
        {
            if (!Same(SnapshotDisabled(paths), expected)) throw InstallationPaths.Error("升级期间插件停用标记发生变化。");
        }

        private static void AssertUpgradeFiles(InstallationPaths paths, InstallState state)
        {
            CheckOwnedFile(paths.Bootstrap, state.BootstrapSha256, false);
            foreach (PluginFileState file in state.PluginFiles) CheckOwnedFile(paths.OwnedPluginFile(file.Path), file.Sha256, false);
            if (state.OriginalConfigExisted) AssertHash(paths.Backup, state.OriginalConfigSha256, "最初的配置备份已变化，无法完成升级。");
        }

        private static void ReplaceForUpgrade(string destination, byte[] contents, string previousHash, string root,
            string staging, List<UpgradeReplacement> replaced)
        {
            InstallationPaths.SafePath(destination, root);
            AssertHash(destination, previousHash, "升级前的文件发生变化，已停止替换：" + destination);
            string incoming = InstallationPaths.SafePath(Path.Combine(staging, "incoming-" + replaced.Count), staging);
            string backup = InstallationPaths.SafePath(Path.Combine(staging, "previous-" + replaced.Count), staging);
            using (var file = new FileStream(incoming, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { file.Write(contents, 0, contents.Length); file.Flush(true); }
            string newHash = InstallationPaths.HashBytes(contents);
            File.Replace(incoming, destination, backup);
            // 必须先登记成功替换，再执行可能失败的验证，确保当前文件也能回滚。
            replaced.Add(new UpgradeReplacement { Path = destination, Backup = backup, OldHash = previousHash, NewHash = newHash });
            AssertHash(destination, newHash, "升级后的文件校验失败：" + destination);
        }

        private sealed class UpgradeReplacement
        {
            internal string Path, Backup, OldHash, NewHash;
        }
    }
}
