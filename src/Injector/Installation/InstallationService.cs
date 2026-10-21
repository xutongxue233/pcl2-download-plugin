using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;

namespace PCL.DownloadRestore.Injector.Installation
{
    /// <summary>Pure managed, application-local installer. It never writes to the launcher EXE.</summary>
    public sealed class InstallationService
    {
        public const string SupportedLauncherSha256 = "6B6E1260CB5A1D6A303855F1C60CAB3D0BC1AEBBA7E40A964180188B271E7386";
        private const string BootstrapKey = "PCL.PluginBootstrap.dll";
        private const string PluginPrefix = @"PCL\Plugins\DownloadRestore\";
        private static readonly string[] PayloadKeys = { BootstrapKey, PluginPrefix + "PCL.DownloadRestore.dll", PluginPrefix + "0Harmony.dll" };
        private readonly Dictionary<string, byte[]> payload;
        private readonly Action<string> logger;

        public InstallationService(IDictionary<string, byte[]> payload, Action<string> log = null)
        {
            if (payload == null || payload.Count != PayloadKeys.Length ||
                PayloadKeys.Any(key => !payload.ContainsKey(key) || payload[key] == null || payload[key].Length == 0) ||
                payload.Keys.Any(key => !PayloadKeys.Contains(key, StringComparer.Ordinal)))
                throw new ArgumentException("安装器内嵌文件不完整或文件名无效，请重新获取完整安装器。", nameof(payload));
            // A caller changing its resource buffers must not change a running transaction.
            this.payload = PayloadKeys.ToDictionary(key => key, key => (byte[])payload[key].Clone(), StringComparer.Ordinal);
            logger = log;
        }

        public InspectionResult Inspect(string launcherPath)
        {
            string hash = null;
            try
            {
                InstallationPaths paths = InstallationPaths.FromLauncher(launcherPath);
                hash = InstallationPaths.HashFile(paths.Launcher);
                if (InstallationPaths.IsRunning(paths.Launcher))
                    return Inspection("Running", "这个 PCL 正在运行，请先退出后再安装或卸载。", hash);
                if (InstallationPaths.Exists(paths.State))
                {
                    InstallState state = ReadState(paths);
                    AssertUnchanged(paths, state, true);
                    bool missing = !File.Exists(paths.Bootstrap) || state.PluginFiles.Any(file => !File.Exists(paths.OwnedPluginFile(file.Path)));
                    if (!Same(hash, SupportedLauncherSha256))
                        return Inspection("Updated", "PCL 程序已更新或替换，插件会跳过这个未适配版本。可以卸载原插件，当前 EXE 会保留。", hash, false, true);
                    if (missing)
                        return Inspection("Incomplete", "部分插件文件已缺失。可以先卸载以恢复启动配置，再重新安装。", hash, false, true);
                    if (File.Exists(paths.Disabled))
                        return Inspection("Disabled", "插件已安装但处于禁用状态。点击安装可重新启用，也可以卸载。", hash, true, true);
                    return Inspection("Installed", "插件已安装，每次正常启动 PCL 时自动生效。", hash, false, true);
                }
                AssertNoInstallation(paths);
                if (!Same(hash, SupportedLauncherSha256))
                    return Inspection("Unsupported", "此 EXE 不是已验证的官方 PCL 2.99.0.0，不能安装该版本插件。请重新选择受支持的原版程序。", hash);
                // Validate XML and extension conflicts without writing any file.
                LauncherConfiguration.Patch(ReadOptionalConfig(paths));
                return Inspection("Ready", "已确认是受支持的官方 PCL 2.99.0.0，可以安装插件。", hash, true, false);
            }
            catch (Exception ex)
            {
                return Inspection("Conflict", Explain(ex), hash);
            }
        }

        public OperationResult Install(string launcherPath)
        {
            try
            {
                InstallationPaths paths = InstallationPaths.FromLauncher(launcherPath);
                using (var operationLock = new InstallationLock(paths.Root))
                {
                    paths.AssertStopped();
                    string hash = InstallationPaths.HashFile(paths.Launcher);
                    if (!Same(hash, SupportedLauncherSha256))
                        throw InstallationPaths.Error("仅支持已验证的官方 PCL 2.99.0.0，选中的 EXE 不匹配。未修改任何文件。SHA-256：" + hash);
                    if (InstallationPaths.Exists(paths.State))
                    {
                        InstallState state = ReadState(paths);
                        AssertUnchanged(paths, state, false);
                        if (File.Exists(paths.Disabled))
                        {
                            paths.AssertStopped();
                            InstallationPaths.SafePath(paths.Disabled, paths.Plugin);
                            File.Delete(paths.Disabled);
                            return Success("插件已重新启用，下次启动 PCL 时自动生效。", hash);
                        }
                        return Success("插件已经安装，无需重复注入。现有配置和文件未改变。", hash);
                    }
                    AssertNoInstallation(paths);
                    return InstallFresh(paths, hash);
                }
            }
            catch (InvalidOperationException) { throw; }
            catch (Exception ex) { throw InstallationPaths.Error("安装未完成。" + Explain(ex), ex); }
        }

        public OperationResult Uninstall(string launcherPath)
        {
            try
            {
                InstallationPaths paths = InstallationPaths.FromLauncher(launcherPath);
                using (var operationLock = new InstallationLock(paths.Root))
                {
                    paths.AssertStopped();
                    string hash = InstallationPaths.HashFile(paths.Launcher);
                    if (!InstallationPaths.Exists(paths.State))
                    {
                        AssertNoInstallation(paths);
                        return Success("插件未安装或已经卸载，没有修改任何文件。", hash);
                    }
                    InstallState state = ReadState(paths);
                    AssertUnchanged(paths, state, true);
                    return UninstallRegistered(paths, state, hash);
                }
            }
            catch (InvalidOperationException) { throw; }
            catch (Exception ex) { throw InstallationPaths.Error("卸载未完成。" + Explain(ex), ex); }
        }

        private OperationResult InstallFresh(InstallationPaths paths, string initialHash)
        {
            byte[] original = ReadOptionalConfig(paths);
            byte[] patched = LauncherConfiguration.Patch(original);
            var files = PayloadKeys.Where(key => key != BootstrapKey).Select(key => new PluginFileState
            {
                Path = key.Substring(PluginPrefix.Length), Sha256 = InstallationPaths.HashBytes(payload[key])
            }).ToList();
            var state = new InstallState
            {
                Schema = 1,
                LauncherPath = paths.Launcher,
                LauncherSha256AtInstall = initialHash,
                InstalledUtc = DateTime.UtcNow.ToString("o"),
                OriginalConfigExisted = original != null,
                OriginalConfigSha256 = original == null ? null : InstallationPaths.HashBytes(original),
                InstalledConfigSha256 = InstallationPaths.HashBytes(patched),
                BootstrapSha256 = InstallationPaths.HashBytes(payload[BootstrapKey]),
                PluginFiles = files
            };
            string staging = CreateStaging(paths, "install");
            var createdFiles = new List<string>();
            var createdDirectories = new List<string>();
            bool configChanged = false;
            bool preserveStaging = false;
            try
            {
                byte[] receipt = Serialize(state);
                File.WriteAllBytes(Path.Combine(staging, "state.json"), receipt);
                if (original != null) File.WriteAllBytes(Path.Combine(staging, "original.config"), original);
                paths.AssertStopped();
                AssertHash(paths.Launcher, initialHash, "安装期间启动器发生了变化，请重新检查后重试。");
                AssertConfigSnapshot(paths, original);
                paths.AssertOnlyDiagnosticsRemain();
                foreach (string directory in new[] { Path.Combine(paths.Root, "PCL"), Path.Combine(paths.Root, @"PCL\Plugins"), paths.Plugin })
                {
                    InstallationPaths.SafePath(directory, paths.Root);
                    if (!Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                        createdDirectories.Add(directory);
                    }
                }
                foreach (PluginFileState file in files)
                    CreateOwnedFile(paths.OwnedPluginFile(file.Path), payload[PluginPrefix + file.Path], paths.Root, createdFiles);
                if (original != null) CreateOwnedFile(paths.Backup, original, paths.Root, createdFiles);
                CreateOwnedFile(paths.Bootstrap, payload[BootstrapKey], paths.Root, createdFiles);
                CreateOwnedFile(paths.State, receipt, paths.Root, createdFiles);
                paths.AssertStopped();
                AssertConfigSnapshot(paths, original);
                ReplaceConfig(paths, patched, staging);
                configChanged = true;
                AssertHash(paths.Launcher, initialHash, "安装期间启动器发生了变化，请重新检查后重试。");
                AssertUnchanged(paths, ReadState(paths), false);
                if (File.Exists(paths.Disabled))
                {
                    InstallationPaths.SafePath(paths.Disabled, paths.Plugin);
                    File.Delete(paths.Disabled);
                }
                return Success("安装成功。官方 EXE 保持不变；以后照常启动这个 PCL，下载恢复插件会自动加载。", initialHash);
            }
            catch (Exception failure)
            {
                try
                {
                    if (configChanged) ReplaceConfig(paths, original, staging);
                    // Keep receipt/backup until payload cleanup succeeds, retaining recovery data.
                    foreach (string file in createdFiles.Where(file => file != paths.State && file != paths.Backup).Reverse())
                    {
                        InstallationPaths.SafePath(file, paths.Root);
                        if (File.Exists(file)) File.Delete(file);
                    }
                    foreach (string file in new[] { paths.State, paths.Backup })
                        if (createdFiles.Contains(file) && File.Exists(file)) File.Delete(InstallationPaths.SafePath(file, paths.Root));
                    paths.RemoveEmptyPluginDirectories(files.Select(file => file.Path));
                    foreach (string directory in createdDirectories.AsEnumerable().Reverse()) InstallationPaths.DeleteEmpty(directory, paths.Root);
                }
                catch (Exception rollback)
                {
                    preserveStaging = true;
                    throw InstallationPaths.Error("安装未完成，自动回滚也未能全部完成。请保留安装记录、原配置备份与恢复目录，关闭 PCL 后再处理。恢复目录：" + staging +
                        "\r\n原错误：" + failure.Message + "\r\n回滚错误：" + rollback.Message, failure);
                }
                throw InstallationPaths.Error("安装未完成，已撤销本次更改。" + Explain(failure), failure);
            }
            finally { if (!preserveStaging) TryCleanStaging(staging, paths.Root); }
        }

        private OperationResult UninstallRegistered(InstallationPaths paths, InstallState state, string initialHash)
        {
            byte[] installed = File.ReadAllBytes(paths.Config);
            byte[] original = state.OriginalConfigExisted ? File.ReadAllBytes(paths.Backup) : null;
            string staging = CreateStaging(paths, "uninstall");
            var moved = new List<KeyValuePair<string, string>>();
            bool configChanged = false;
            bool preserveStaging = false;
            try
            {
                File.WriteAllBytes(Path.Combine(staging, "installed.config"), installed);
                if (original != null) File.WriteAllBytes(Path.Combine(staging, "original.config"), original);
                paths.AssertStopped();
                AssertUnchanged(paths, state, true);
                AssertHash(paths.Launcher, initialHash, "卸载期间启动器发生了变化，请重新检查后重试。");
                // The CLR must stop requesting the manager before its DLL is removed.
                ReplaceConfig(paths, original, staging);
                configChanged = true;
                var owned = new List<string> { paths.Bootstrap };
                owned.AddRange(state.PluginFiles.Select(file => paths.OwnedPluginFile(file.Path)));
                if (state.OriginalConfigExisted) owned.Add(paths.Backup);
                owned.Add(paths.State);
                foreach (string source in owned)
                {
                    InstallationPaths.SafePath(source, paths.Root);
                    if (!File.Exists(source)) continue;
                    string destination = Path.Combine(staging, moved.Count.ToString() + ".backup");
                    File.Move(source, destination);
                    moved.Add(new KeyValuePair<string, string>(source, destination));
                }
                AssertHash(paths.Launcher, initialHash, "卸载期间启动器发生了变化，请重新检查后重试。");
            }
            catch (Exception failure)
            {
                try
                {
                    // Restore every moved DLL before re-enabling its startup configuration.
                    foreach (var file in moved.AsEnumerable().Reverse())
                    {
                        InstallationPaths.SafePath(file.Key, paths.Root);
                        File.Move(file.Value, file.Key);
                    }
                    if (configChanged) ReplaceConfig(paths, installed, staging);
                }
                catch (Exception rollback)
                {
                    preserveStaging = true;
                    throw InstallationPaths.Error("卸载未完成，自动恢复也未能全部完成。请保留恢复目录，关闭 PCL 后再处理：" + staging +
                        "\r\n原错误：" + failure.Message + "\r\n恢复错误：" + rollback.Message, failure);
                }
                throw InstallationPaths.Error("卸载未完成，已恢复卸载前的插件和配置。" + Explain(failure), failure);
            }
            finally { if (!preserveStaging) TryCleanStaging(staging, paths.Root); }
            try { paths.RemoveEmptyPluginDirectories(state.PluginFiles.Select(file => file.Path)); }
            catch (Exception ex) { Log("插件已卸载，部分空目录未删除：" + ex.Message); }
            return Success("卸载成功，已恢复安装前的启动配置。官方 EXE、游戏文件及诊断日志均已保留。", initialHash);
        }

        private static void AssertNoInstallation(InstallationPaths paths)
        {
            paths.AssertOnlyDiagnosticsRemain();
            if (Directory.Exists(paths.Config)) throw InstallationPaths.Error("启动配置路径被同名文件夹占用，请先移出该文件夹：" + paths.Config);
            if (LauncherConfiguration.ContainsOwnedManager(paths.Config))
                throw InstallationPaths.Error("配置仍要求加载本插件，但安装记录已缺失。请保留当前配置和原配置备份；无法验证来源时不会自动覆盖。");
        }

        private static byte[] ReadOptionalConfig(InstallationPaths paths)
        {
            InstallationPaths.SafePath(paths.Config, paths.Root);
            if (Directory.Exists(paths.Config)) throw InstallationPaths.Error("启动配置路径被同名文件夹占用：" + paths.Config);
            return File.Exists(paths.Config) ? File.ReadAllBytes(paths.Config) : null;
        }

        private static void AssertConfigSnapshot(InstallationPaths paths, byte[] original)
        {
            InstallationPaths.SafePath(paths.Config, paths.Root);
            if (original == null)
            {
                if (InstallationPaths.Exists(paths.Config)) throw InstallationPaths.Error("安装期间出现了新的配置文件，已停止以免覆盖。请重新检查后重试。");
            }
            else AssertHash(paths.Config, InstallationPaths.HashBytes(original), "安装期间配置文件发生了变化，已停止以免覆盖。请重新检查后重试。");
        }

        private static InstallState ReadState(InstallationPaths paths)
        {
            InstallState state;
            try
            {
                if (!File.Exists(paths.State)) throw new IOException("安装记录不是普通文件。");
                if (new FileInfo(paths.State).Length > 1024 * 1024) throw new IOException("安装记录过大。");
                using (var input = new MemoryStream(Encoding.UTF8.GetBytes(File.ReadAllText(paths.State, Encoding.UTF8))))
                    state = (InstallState)new DataContractJsonSerializer(typeof(InstallState)).ReadObject(input);
            }
            catch (Exception ex) { throw InstallationPaths.Error("无法读取插件安装记录，请保留记录和原配置备份，不要直接删除启动 DLL。记录：" + paths.State, ex); }
            if (state == null || state.Schema != 1 || !Same(state.LauncherPath, paths.Launcher) ||
                !IsHash(state.LauncherSha256AtInstall) || !IsHash(state.BootstrapSha256) || !IsHash(state.InstalledConfigSha256) ||
                (state.OriginalConfigExisted && !IsHash(state.OriginalConfigSha256)) || state.PluginFiles == null)
                throw InstallationPaths.Error("安装记录与选中的启动器不匹配或已损坏。请使用原安装位置，保留备份后再处理。");
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (PluginFileState file in state.PluginFiles)
            {
                if (file == null) throw InstallationPaths.Error("安装记录含有空文件项，已停止操作。");
                string full = paths.OwnedPluginFile(file.Path);
                if (!seen.Add(full) || Same(full, paths.State) || Same(full, paths.Backup) || !IsHash(file.Sha256))
                    throw InstallationPaths.Error("安装记录含有重复或非法文件项，已停止操作：" + file.Path);
            }
            if (!seen.Contains(paths.OwnedPluginFile("PCL.DownloadRestore.dll")) || !seen.Contains(paths.OwnedPluginFile("0Harmony.dll")))
                throw InstallationPaths.Error("安装记录缺少必要插件文件，无法安全卸载。请保留记录和原配置备份。");
            return state;
        }

        private static void AssertUnchanged(InstallationPaths paths, InstallState state, bool allowMissing)
        {
            AssertHash(paths.Config, state.InstalledConfigSha256, "启动器配置在安装后被修改或删除。请先备份自己的修改并恢复安装时的配置，再重试；本次不会覆盖它。");
            CheckOwnedFile(paths.Bootstrap, state.BootstrapSha256, allowMissing);
            foreach (PluginFileState file in state.PluginFiles) CheckOwnedFile(paths.OwnedPluginFile(file.Path), file.Sha256, allowMissing);
            if (state.OriginalConfigExisted)
                AssertHash(paths.Backup, state.OriginalConfigSha256, "安装前的配置备份已丢失或修改，无法确认恢复内容。请先找回原备份再卸载。");
        }

        private static void CheckOwnedFile(string path, string hash, bool allowMissing)
        {
            InstallationPaths.SafePath(path);
            if (Directory.Exists(path)) throw InstallationPaths.Error("已登记的插件文件被同名文件夹替代，已停止操作：" + path);
            if (!File.Exists(path) && allowMissing) return;
            AssertHash(path, hash, "已安装的插件文件缺失或被修改，无法安全覆盖或移除。请先恢复原文件：" + path);
        }

        private static bool IsHash(string value) => value != null && Regex.IsMatch(value, "\\A[0-9A-Fa-f]{64}\\z");
        private static bool Same(string first, string second) => string.Equals(first, second, StringComparison.OrdinalIgnoreCase);
        private static void AssertHash(string path, string hash, string message)
        {
            InstallationPaths.SafePath(path);
            if (!File.Exists(path) || !Same(InstallationPaths.HashFile(path), hash)) throw InstallationPaths.Error(message);
        }

        private static byte[] Serialize(InstallState state)
        {
            using (var output = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(InstallState)).WriteObject(output, state);
                return output.ToArray();
            }
        }

        private static string CreateStaging(InstallationPaths paths, string operation)
        {
            string directory = InstallationPaths.SafePath(Path.Combine(paths.Root, ".pcl-downloadrestore-" + operation + "-" + Guid.NewGuid().ToString("N")), paths.Root);
            Directory.CreateDirectory(directory);
            return directory;
        }

        private static void CreateOwnedFile(string path, byte[] bytes, string root, List<string> createdFiles)
        {
            InstallationPaths.SafePath(path, root);
            using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                createdFiles.Add(path); // Only after this transaction successfully created the file.
                output.Write(bytes, 0, bytes.Length);
                output.Flush(true);
            }
            AssertHash(path, InstallationPaths.HashBytes(bytes), "写入后的插件文件校验失败，请检查磁盘后重试：" + path);
        }

        private static void ReplaceConfig(InstallationPaths paths, byte[] bytes, string staging)
        {
            InstallationPaths.SafePath(paths.Config, paths.Root);
            if (bytes == null) { if (File.Exists(paths.Config)) File.Delete(paths.Config); return; }
            string temporary = InstallationPaths.SafePath(Path.Combine(staging, "config-" + Guid.NewGuid().ToString("N") + ".tmp"), staging);
            File.WriteAllBytes(temporary, bytes);
            if (File.Exists(paths.Config)) File.Replace(temporary, paths.Config, null);
            else File.Move(temporary, paths.Config);
        }

        private void TryCleanStaging(string staging, string root)
        {
            try
            {
                InstallationPaths.SafePath(staging, root);
                if (!Directory.Exists(staging)) return;
                // Our staging folders are flat. Never recurse into an unexpected directory/link.
                string[] entries = Directory.GetFileSystemEntries(staging);
                foreach (string entry in entries)
                {
                    InstallationPaths.SafePath(entry, staging);
                    if (Directory.Exists(entry)) throw InstallationPaths.Error("恢复目录内出现了意外的子文件夹，已保留：" + entry);
                }
                foreach (string entry in entries) File.Delete(entry);
                Directory.Delete(staging);
            }
            catch (Exception ex) { Log("临时恢复目录未清理，请保留以便检查：" + staging + "；" + ex.Message); }
        }

        private OperationResult Success(string message, string hash)
        {
            Log(message);
            return new OperationResult { Success = true, Message = message, LauncherSha256 = hash };
        }

        private static InspectionResult Inspection(string status, string message, string hash, bool install = false, bool uninstall = false) =>
            new InspectionResult { Status = status, Message = message, LauncherSha256 = hash, CanInstall = install, CanUninstall = uninstall };

        private static string Explain(Exception ex)
        {
            if (ex is InvalidOperationException) return ex.Message;
            if (ex is UnauthorizedAccessException) return "无法访问目标文件夹。请关闭 PCL，并将它放在自己可写的文件夹后重试。" + ex.Message;
            if (ex is IOException) return "文件正在使用、磁盘空间不足或文件读写失败。请关闭 PCL 和占用文件的程序，检查磁盘后重试。" + ex.Message;
            return "请检查选中的文件与安装目录后重试。" + ex.Message;
        }

        private void Log(string message)
        {
            try { logger?.Invoke(message); } catch { /* UI logging cannot invalidate an installation. */ }
        }
    }
}
