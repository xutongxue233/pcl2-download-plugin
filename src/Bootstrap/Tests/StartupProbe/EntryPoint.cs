using System;
using System.Reflection;
using System.Net;
using System.IO;
using System.Threading;
using System.Runtime.InteropServices;
using System.Windows;

namespace PCL.DownloadRestore
{
    // 仅用于测试的插件，在宿主完成启动后、主窗口显示前退出。
    public static class EntryPoint
    {
        // 仅在测试中强制退出，防止 WPF 在探测结束后创建 StartupUri 指向的窗口。
        [DllImport("kernel32.dll", ExactSpelling = true)]
        private static extern void ExitProcess(uint exitCode);

        public static void Initialize(Assembly host, Action<string> log)
        {
            try
            {
                if (Application.Current == null) throw new Exception("WPF Application has not been constructed.");
                if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA) throw new Exception("Wrong initialization thread.");
                var core = Assembly.Load("MeloongCore");
                if ((string)core.GetType("MeloongCore.Main").GetField("AppName").GetValue(null) != "PCL") throw new Exception("Host core initialization has not run.");
                if (host.GetName().Name != "Plain Craft Launcher 2") throw new Exception("Incorrect host.");
                if (Application.Current.MainWindow != null) throw new Exception("Main window was constructed too early.");
                log("NETWORK_DEFAULTS: Framework=" + AppDomain.CurrentDomain.SetupInformation.TargetFrameworkName + "; OS=" + Environment.OSVersion + "; SecurityProtocol=" + ServicePointManager.SecurityProtocol);
                if (File.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "network-probe")))
                {
                    foreach (string url in new[] { "https://launchermeta.mojang.com/mc/game/version_manifest.json", "https://bmclapi2.bangbang93.com/mc/game/version_manifest.json" })
                    {
                        try
                        {
                            var request = WebRequest.CreateHttp(url);
                            request.Timeout = 10000;
                            using (var response = (HttpWebResponse)request.GetResponse())
                            using (var reader = new StreamReader(response.GetResponseStream()))
                                log("NETWORK_PROBE " + url + " => " + (int)response.StatusCode + ", chars=" + reader.ReadToEnd().Length);
                        }
                        catch (Exception exception) { log("NETWORK_PROBE " + url + " => " + exception.GetType().Name + ": " + exception.Message); }
                    }
                }
                log("STARTUP_PROBE_PASS: main STA thread; original embedded resolver active; host Startup complete; before main window.");
                ExitProcess(0);
            }
            catch (Exception exception)
            {
                log("STARTUP_PROBE_FAIL: " + exception);
                ExitProcess(2);
            }
        }
    }
}
