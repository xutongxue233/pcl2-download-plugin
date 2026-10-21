using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Windows;
using PCL.DownloadRestore.Injector.Installation;

namespace PCL.DownloadRestore.Injector
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] != "--launcher") return RunCommand(args);
            try
            {
                if (args.Length != 0 && args.Length != 2) throw new ArgumentException("用法：--launcher <官方 PCL.exe 路径>");
                var application = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
                application.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/PCL.DownloadRestore.Injector;component/UI/PclStyles.xaml", UriKind.Relative) });
                application.DispatcherUnhandledException += (sender, e) =>
                {
                    MessageBox.Show(e.Exception.Message, "PCL 下载恢复注入器", MessageBoxButton.OK, MessageBoxImage.Error);
                    e.Handled = true;
                };
                return application.Run(new MainWindow(args.Length == 2 ? args[1] : null));
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "无法启动注入器", MessageBoxButton.OK, MessageBoxImage.Error);
                return 1;
            }
        }

        private static int RunCommand(string[] args)
        {
            var report = new CommandReport { Action = args[0], Status = "Error" };
            FileStream reportStream = null;
            int exitCode = 1;
            try
            {
                if (args.Length != 2 && args.Length != 4) throw new ArgumentException("用法：--install|--upgrade|--uninstall|--inspect <PCL.exe> [--report <新建的 JSON 文件>]");
                if (args[0] != "--install" && args[0] != "--upgrade" && args[0] != "--uninstall" && args[0] != "--inspect") throw new ArgumentException("未知操作：" + args[0]);
                if (args.Length == 4)
                {
                    if (args[2] != "--report" || !string.Equals(Path.GetExtension(args[3]), ".json", StringComparison.OrdinalIgnoreCase))
                        throw new ArgumentException("报告必须使用 --report 指定新的 .json 文件。");
                    // 拒绝覆盖已有文件：报告不能覆盖启动器或安装状态文件。
                    string path = InstallationPaths.SafePath(args[3]);
                    string pluginRoot = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[1])), "PCL", "Plugins", "DownloadRestore") + Path.DirectorySeparatorChar;
                    if (path.StartsWith(pluginRoot, StringComparison.OrdinalIgnoreCase))
                        throw new ArgumentException("报告不能写入插件安装目录，请选择其他文件夹。");
                    reportStream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                }
                var service = new InstallationService(EmbeddedPayload.ReadFiles());
                if (args[0] != "--inspect")
                {
                    OperationResult operation = args[0] == "--install" ? service.Install(args[1]) :
                        args[0] == "--upgrade" ? service.Upgrade(args[1]) : service.Uninstall(args[1]);
                    report.Success = operation.Success;
                    report.Message = operation.Message;
                    report.LauncherSha256 = operation.LauncherSha256;
                }
                InspectionResult inspection = service.Inspect(args[1]);
                report.Status = inspection.Status;
                report.CanInstall = inspection.CanInstall;
                report.CanUninstall = inspection.CanUninstall;
                report.InstalledVersion = inspection.InstalledVersion;
                report.AvailableVersion = inspection.AvailableVersion;
                report.CanUpgrade = inspection.CanUpgrade;
                report.IsDisabled = inspection.IsDisabled;
                report.LauncherSha256 = inspection.LauncherSha256 ?? report.LauncherSha256;
                if (args[0] == "--inspect") { report.Success = true; report.Message = inspection.Message; }
                exitCode = report.Success ? 0 : 1;
            }
            catch (Exception ex)
            {
                report.Success = false;
                report.Message = ex.Message;
                exitCode = ex is ArgumentException ? 2 : 1;
            }
            try
            {
                var serializer = new DataContractJsonSerializer(typeof(CommandReport));
                using (var buffer = new MemoryStream())
                {
                    serializer.WriteObject(buffer, report);
                    byte[] bytes = buffer.ToArray();
                    if (reportStream != null) reportStream.Write(bytes, 0, bytes.Length);
                    else Console.WriteLine(Encoding.UTF8.GetString(bytes));
                }
            }
            catch { exitCode = 1; }
            finally
            {
                // Dispose 可能会刷新缓冲中的 JSON；操作完成后，报告写入失败
                // 也不应继续抛出未处理异常。
                try { reportStream?.Dispose(); }
                catch { exitCode = 1; }
            }
            return exitCode;
        }
    }

    [DataContract]
    public sealed class CommandReport
    {
        [DataMember] public string Action { get; set; }
        [DataMember] public bool Success { get; set; }
        [DataMember] public string Message { get; set; }
        [DataMember] public string LauncherSha256 { get; set; }
        [DataMember] public string Status { get; set; }
        [DataMember] public bool CanInstall { get; set; }
        [DataMember] public bool CanUninstall { get; set; }
        [DataMember] public string InstalledVersion { get; set; }
        [DataMember] public string AvailableVersion { get; set; }
        [DataMember] public bool CanUpgrade { get; set; }
        [DataMember] public bool IsDisabled { get; set; }
    }
}
