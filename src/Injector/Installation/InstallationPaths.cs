using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace PCL.DownloadRestore.Injector.Installation
{
    internal sealed class InstallationPaths
    {
        internal string Launcher, Root, Plugin, Bootstrap, Config, State, Backup, Disabled;

        internal static InstallationPaths FromLauncher(string launcherPath)
        {
            if (string.IsNullOrWhiteSpace(launcherPath)) throw Error("请先选择需要安装插件的 PCL 程序（.exe）。");
            string launcher = SafePath(launcherPath);
            if (!File.Exists(launcher) || !string.Equals(Path.GetExtension(launcher), ".exe", StringComparison.OrdinalIgnoreCase))
                throw Error("请选择一个已经存在的 PCL .exe 文件。");
            string root = Path.GetDirectoryName(launcher);
            string plugin = SafePath(Path.Combine(root, @"PCL\Plugins\DownloadRestore"), root);
            return new InstallationPaths
            {
                Launcher = launcher,
                Root = root,
                Plugin = plugin,
                Bootstrap = SafePath(Path.Combine(root, "PCL.PluginBootstrap.dll"), root),
                Config = SafePath(launcher + ".config", root),
                State = SafePath(Path.Combine(plugin, ".install-state.json"), root),
                Backup = SafePath(Path.Combine(plugin, ".original-exe.config"), root),
                Disabled = SafePath(Path.Combine(plugin, "disabled"), root)
            };
        }

        internal static string SafePath(string path, string within = null)
        {
            string full;
            try { full = Path.GetFullPath(path); }
            catch (Exception ex) { throw Error("路径无效，请重新选择启动器：" + path, ex); }
            if (within != null)
            {
                string root = Path.GetFullPath(within).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    throw Error("安装记录中的路径越出了目标文件夹，已停止操作：" + full);
            }
            string cursor = full;
            while (!string.IsNullOrEmpty(cursor))
            {
                try
                {
                    FileAttributes attributes = File.GetAttributes(cursor);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                        throw Error("不支持符号链接或目录联接。请直接选择真实文件夹中的启动器：" + cursor);
                }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }
                string parent = Path.GetDirectoryName(cursor);
                if (string.Equals(cursor, parent, StringComparison.OrdinalIgnoreCase)) break;
                cursor = parent;
            }
            return full;
        }

        internal string OwnedPluginFile(string relative)
        {
            if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.IndexOf(':') >= 0 ||
                relative.Split('\\', '/').Any(part => part == "." || part == ".." || string.IsNullOrEmpty(part)))
                throw Error("安装记录中的文件路径无效：" + relative);
            return SafePath(Path.Combine(Plugin, relative), Plugin);
        }

        internal static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

        internal static string HashFile(string path)
        {
            using (var input = File.OpenRead(path))
            using (var sha = SHA256.Create())
                return Hex(sha.ComputeHash(input));
        }

        internal static string HashBytes(byte[] bytes)
        {
            using (var sha = SHA256.Create()) return Hex(sha.ComputeHash(bytes));
        }

        private static string Hex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", "");

        internal static bool IsRunning(string launcher)
        {
            foreach (Process process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(launcher)))
            {
                using (process)
                {
                    try
                    {
                        if (process.HasExited) continue;
                        string actual = process.MainModule?.FileName;
                        if (string.IsNullOrEmpty(actual) || string.Equals(actual, launcher, StringComparison.OrdinalIgnoreCase)) return true;
                    }
                    catch (InvalidOperationException) { /* The process may have exited meanwhile. */ }
                    catch { return true; } // An inaccessible matching process must not be modified.
                }
            }
            return false;
        }

        internal void AssertStopped()
        {
            if (IsRunning(Launcher)) throw Error("请先关闭选中的 PCL，再安装或卸载插件。如果它以管理员身份运行，也请先退出该进程。");
        }

        internal void AssertOnlyDiagnosticsRemain()
        {
            if (Exists(Bootstrap)) throw Error("发现未登记的 PCL.PluginBootstrap.dll，无法确认它的来源。请保留并移出该文件后重试。");
            if (Exists(Plugin) && !Directory.Exists(Plugin)) throw Error("插件目录被同名文件占用，请先移出该文件：" + Plugin);
            if (!Directory.Exists(Plugin)) return;
            foreach (string entry in Directory.EnumerateFileSystemEntries(Plugin))
            {
                SafePath(entry, Plugin);
                string name = Path.GetFileName(entry);
                if (Directory.Exists(entry) || !(name.Equals("bootstrap.log", StringComparison.OrdinalIgnoreCase) || name.Equals("disabled", StringComparison.OrdinalIgnoreCase)))
                    throw Error("插件目录中有未登记的文件，已保留所有内容。请先将这些文件移出专用目录后重试：" + entry);
            }
        }

        internal void RemoveEmptyPluginDirectories(IEnumerable<string> relativeFiles)
        {
            var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string relative in relativeFiles)
            {
                string cursor = Path.GetDirectoryName(OwnedPluginFile(relative));
                while (cursor != null && cursor.StartsWith(Plugin + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    directories.Add(cursor);
                    cursor = Path.GetDirectoryName(cursor);
                }
            }
            foreach (string directory in directories.OrderByDescending(x => x.Length)) DeleteEmpty(directory, Plugin);
            DeleteEmpty(Plugin, Root);
        }

        internal static void DeleteEmpty(string directory, string within)
        {
            SafePath(directory, within);
            if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
        }

        internal static InvalidOperationException Error(string message, Exception inner = null) => new InvalidOperationException(message, inner);
    }

    internal sealed class InstallationLock : IDisposable
    {
        private readonly Mutex mutex;
        private readonly bool acquired;

        internal InstallationLock(string root)
        {
            string name = @"Local\PCL.DownloadRestore.Install." + InstallationPaths.HashBytes(Encoding.UTF8.GetBytes(root.ToUpperInvariant()));
            mutex = new Mutex(false, name);
            try
            {
                try { acquired = mutex.WaitOne(0); }
                catch (AbandonedMutexException) { acquired = true; }
                if (!acquired) throw InstallationPaths.Error("另一个安装器正在处理这个 PCL 文件夹，请等待它完成后重试。");
            }
            catch { mutex.Dispose(); throw; }
        }

        public void Dispose()
        {
            if (acquired) mutex.ReleaseMutex();
            mutex.Dispose();
        }
    }
}
