using System;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using PCL.DownloadRestore.Injector.Installation;

namespace PCL.DownloadRestore.Injector
{
    public partial class MainWindow : Window
    {
        private readonly InstallationService service;
        private readonly DispatcherTimer pathTimer;
        private readonly DispatcherTimer runningTimer;
        private readonly StringBuilder log = new StringBuilder();
        private InspectionResult inspection;
        private bool busy, checking, closed, initialized, readingLogs;
        private string lastInspectionLog;
        private int generation;
        private enum OperationKind { Install, Upgrade, Uninstall }

        internal MainWindow(string launcherPath)
        {
            InitializeComponent();
            service = new InstallationService(EmbeddedPayload.ReadFiles(), AppendLog);
            pathTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
            pathTimer.Tick += async (sender, e) => { pathTimer.Stop(); await InspectAsync(); };
            runningTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            runningTimer.Tick += async (sender, e) =>
            {
                if (!busy && !checking && inspection?.Status == "Running") await InspectAsync(true);
                await UpdateLogsAsync();
            };
            initialized = true;
            runningTimer.Start();
            AppendLog("工具已启动。请选择 PCL 程序。");
            if (!string.IsNullOrWhiteSpace(launcherPath)) LauncherPath.Text = launcherPath;
        }

        private void FrameSizeChanged(object sender, SizeChangedEventArgs e) => FrameClip.Rect = new Rect(0, 0, e.NewSize.Width, e.NewSize.Height);
        private void MinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void CloseClick(object sender, RoutedEventArgs e) => Close();
        private void DragTitle(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left || e.OriginalSource is System.Windows.Controls.Button) return;
            try { DragMove(); } catch (InvalidOperationException) { }
        }
        private void WindowClosing(object sender, CancelEventArgs e) { if (busy) e.Cancel = true; }
        private void WindowClosed(object sender, EventArgs e)
        {
            closed = true;
            generation++;
            pathTimer?.Stop();
            runningTimer?.Stop();
        }
        private void WindowActivated(object sender, EventArgs e)
        {
            if (initialized && !busy && !checking && LauncherPath.Text.Length > 0) { pathTimer.Stop(); pathTimer.Start(); }
        }
        private void PathChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (!initialized) return;
            generation++;
            inspection = null;
            lastInspectionLog = null;
            checking = false;
            SetStatus("请选择 PCL 程序", "选择后会自动检查安装状态。", "ColorBrush1");
            UpdateButtons();
            pathTimer.Stop();
            pathTimer.Start();
        }
        private void PathDragOver(object sender, DragEventArgs e)
        {
            e.Effects = !busy && e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }
        private void PathDrop(object sender, DragEventArgs e)
        {
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (!busy && files?.Length == 1) LauncherPath.Text = files[0];
            e.Handled = true;
        }
        private void BrowseClick(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Title = "选择 PCL 程序", Filter = "PCL 程序 (*.exe)|*.exe", CheckFileExists = true, Multiselect = false, InitialDirectory = AppDomain.CurrentDomain.BaseDirectory };
            string current = LauncherPath.Text.Trim().Trim('"');
            if (File.Exists(current)) dialog.FileName = Path.GetFullPath(current);
            if (dialog.ShowDialog(this) == true) LauncherPath.Text = dialog.FileName;
        }
        private async Task InspectAsync(bool quiet = false)
        {
            if (busy || closed) return;
            pathTimer.Stop();
            int request = ++generation;
            string path = LauncherPath.Text.Trim().Trim('"');
            checking = true;
            if (!quiet) inspection = null;
            UpdateButtons();
            try
            {
                if (path.Length == 0) { SetStatus("请选择 PCL 程序", "安装一次，之后启动 PCL 自动尝试加载。", "ColorBrush1"); return; }
                if (!quiet) SetStatus("正在检查…", "", "ColorBrushGray3");
                var result = await Task.Run(() => service.Inspect(path));
                if (request != generation || closed) return;
                inspection = result;
                ShowInspection();
                string versions = DescribeVersions(result);
                string inspected = path + Environment.NewLine + result.Status + Environment.NewLine + versions + Environment.NewLine + result.Message;
                if (inspected != lastInspectionLog)
                {
                    lastInspectionLog = inspected;
                    AppendLog("检测程序：" + path + Environment.NewLine + versions + Environment.NewLine + result.Message);
                }
            }
            catch (Exception ex) { if (request == generation && !closed) { ShowError(ex.Message); AppendLog(ex.Message); } }
            finally { if (request == generation && !closed) { checking = false; UpdateButtons(); await UpdateLogsAsync(); } }
        }
        private void ShowInspection()
        {
            switch (inspection.Status)
            {
                case "Ready": SetStatus("可以尝试注入", "请确认 PCL 来源正确，安装后可在启动日志中查看兼容结果。", "ColorBrush2"); break;
                case "Installed": SetStatus("已注入，无需重复操作", "照常启动 PCL，在日志中查看插件加载结果。", "ColorBrush2"); break;
                case "Disabled": SetStatus("插件已停用", "点击“启用插件”可重新启用。", "ColorBrushGray3"); break;
                case "UpgradeAvailable": SetStatus("插件可以升级", inspection.Message, "ColorBrush2"); break;
                case "NewerVersion": SetStatus("已安装较新版本", inspection.Message, "ColorBrush1"); break;
                case "DifferentBuild": SetStatus("同版本的构建不同", inspection.Message, "ColorBrush1"); break;
                case "UnknownVersion": SetStatus("无法识别已安装版本", inspection.Message, "ColorBrush1"); break;
                case "Running": SetStatus("请先退出 PCL", "退出后会自动刷新状态。", "ColorBrush1"); break;
                case "Incomplete": SetStatus("插件文件不完整", "请先卸载插件，再重新注入。", "ColorBrushRedDark"); break;
                default: SetStatus("暂时无法操作", inspection.Message, "ColorBrushRedDark"); break;
            }
        }
        private async void InstallClick(object sender, RoutedEventArgs e) =>
            await OperateAsync(inspection?.CanUpgrade == true ? OperationKind.Upgrade : OperationKind.Install);
        private async void UninstallClick(object sender, RoutedEventArgs e) => await OperateAsync(OperationKind.Uninstall);
        private async Task OperateAsync(OperationKind operation)
        {
            if (busy || checking) return;
            string path = LauncherPath.Text.Trim().Trim('"');
            bool enabling = operation == OperationKind.Install && inspection?.IsDisabled == true;
            string action = operation == OperationKind.Upgrade ? "升级插件" : operation == OperationKind.Uninstall ? "卸载插件" : enabling ? "启用插件" : "注入插件";
            string progress = operation == OperationKind.Upgrade ? "正在升级…" : operation == OperationKind.Uninstall ? "正在卸载…" : enabling ? "正在启用…" : "正在注入…";
            busy = true;
            generation++;
            pathTimer.Stop();
            UpdateButtons();
            SetStatus(progress, "请稍候。", "ColorBrush2");
            AppendLog("开始" + action + "：" + DescribeVersions(inspection));
            try
            {
                var result = await Task.Run(() => operation == OperationKind.Upgrade ? service.Upgrade(path) :
                    operation == OperationKind.Uninstall ? service.Uninstall(path) : service.Install(path));
                inspection = await Task.Run(() => service.Inspect(path));
                ShowInspection();
                if (!result.Success) ShowError(result.Message);
                else if (operation == OperationKind.Uninstall && inspection.Status == "Ready") SetStatus("插件已卸载", "已恢复原配置，游戏文件保留。", "ColorBrush2");
                AppendLog(action + (result.Success ? "完成：" : "未完成：") + result.Message + Environment.NewLine + DescribeVersions(inspection));
            }
            catch (Exception ex)
            {
                try { inspection = await Task.Run(() => service.Inspect(path)); } catch { inspection = null; }
                ShowError(ex.Message);
                AppendLog(action + "未完成：" + ex.Message + Environment.NewLine + DescribeVersions(inspection));
            }
            finally { busy = false; UpdateButtons(); await UpdateLogsAsync(); }
        }
        private void UpdateButtons()
        {
            BrowseButton.IsEnabled = LauncherPath.IsEnabled = !busy;
            InstallButton.IsEnabled = !busy && !checking && (inspection?.CanInstall == true || inspection?.CanUpgrade == true);
            InstallButton.Content = inspection?.CanUpgrade == true ? "升级插件" :
                inspection?.IsDisabled == true && inspection.CanInstall ? "启用插件" :
                inspection?.Status == "Installed" || inspection?.Status == "NewerVersion" || inspection?.Status == "DifferentBuild" || inspection?.Status == "UnknownVersion" ? "已注入" : "注入插件";
            UninstallButton.IsEnabled = !busy && !checking && inspection?.CanUninstall == true;
            PluginVersions.Text = DescribeVersions(inspection);
            PluginVersions.Visibility = inspection == null ? Visibility.Collapsed : Visibility.Visible;
            Cursor = busy ? Cursors.Wait : null;
        }
        private static string DescribeVersions(InspectionResult result)
        {
            if (result == null) return "插件版本尚未确认";
            string installed = !string.IsNullOrWhiteSpace(result.InstalledVersion) ? "已安装 v" + result.InstalledVersion :
                result.Status == "Ready" ? "尚未安装" : result.Status == "UnknownVersion" ? "已安装版本未知" : "安装版本待确认";
            string available = !string.IsNullOrWhiteSpace(result.AvailableVersion) ? "内置 v" + result.AvailableVersion : "内置版本未知";
            return installed + " · " + available;
        }
        private void SetStatus(string title, string message, string color)
        {
            StatusTitle.Text = title;
            StatusTitle.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, color);
            StatusMessage.Text = message;
        }
        private void ShowError(string message) => SetStatus("操作未完成", message, "ColorBrushRedDark");
        private void AppendLog(string message)
        {
            lock (log)
            {
                if (log.Length > 40000) log.Clear();
                log.AppendLine(DateTime.Now.ToString("HH:mm:ss") + "  " + message);
            }
            if (!closed) Dispatcher.BeginInvoke(new Action(async () => await UpdateLogsAsync()));
        }
        private async void LogsClick(object sender, RoutedEventArgs e)
        {
            LogsPanel.Visibility = LogsPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
            LogsButton.Content = LogsPanel.Visibility == Visibility.Visible ? "收起日志" : "查看日志";
            await UpdateLogsAsync();
        }
        private async Task UpdateLogsAsync()
        {
            if (closed || readingLogs || LogsPanel.Visibility != Visibility.Visible) return;
            readingLogs = true;
            string path = LauncherPath.Text.Trim().Trim('"');
            try
            {
                string startupLog = await Task.Run(() => ReadStartupLog(path));
                if (closed || path != LauncherPath.Text.Trim().Trim('"')) return;
                string history;
                lock (log) history = log.ToString();
                string text = "本次操作日志" + Environment.NewLine + history + Environment.NewLine +
                    "所选 PCL 的插件启动日志（历史记录）" + Environment.NewLine + startupLog;
                if (LogsText.Text == text) return;
                // 阅读较早日志时保留当前滚动位置。
                double offset = LogsText.VerticalOffset;
                bool atEnd = offset + LogsText.ViewportHeight >= LogsText.ExtentHeight - 2;
                LogsText.Text = text;
                if (atEnd) LogsText.ScrollToEnd(); else LogsText.ScrollToVerticalOffset(offset);
            }
            finally { readingLogs = false; }
        }
        private static string ReadStartupLog(string launcherPath)
        {
            if (launcherPath.Length == 0) return "选择 PCL 程序后，可查看插件每次启动的加载记录。";
            try
            {
                var paths = InstallationPaths.FromLauncher(launcherPath);
                string logPath = InstallationPaths.SafePath(Path.Combine(paths.Plugin, "bootstrap.log"), paths.Root);
                if (!File.Exists(logPath)) return "暂无启动记录。安装后启动一次 PCL，即会生成日志。";
                using (var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    const int limit = 65536;
                    long offset = Math.Max(0, stream.Length - limit);
                    stream.Position = offset;
                    var bytes = new byte[limit];
                    int length = 0, read;
                    while (length < bytes.Length && (read = stream.Read(bytes, length, bytes.Length - length)) > 0) length += read;
                    int start = 0;
                    if (offset > 0)
                    {
                        int newline = Array.IndexOf(bytes, (byte)'\n', 0, length);
                        // 即使单条日志超出整个长度上限，也保留可读的末尾内容。
                        if (newline >= 0 && newline < length - 1) start = newline + 1;
                        while (start < length && (bytes[start] & 0xC0) == 0x80) start++;
                    }
                    string text = Encoding.UTF8.GetString(bytes, start, length - start).TrimStart('\uFEFF');
                    return (offset > 0 ? "（仅显示最近 64 KiB）" + Environment.NewLine : "") +
                        (string.IsNullOrWhiteSpace(text) ? "暂无启动记录。" : text);
                }
            }
            catch (Exception ex) { return "无法读取启动日志：" + ex.Message; }
        }
    }
}
