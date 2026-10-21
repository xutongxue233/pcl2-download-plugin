using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace PCL.PluginBootstrap
{
    /// <summary>仅作用于当前应用的 CLR 启动钩子，通过此启动器的 .exe.config 安装。</summary>
    public sealed class PluginDomainManager : AppDomainManager
    {
        private static readonly object LogLock = new object();
        private static string pluginDirectory;
        private static string hostExecutablePath;
        private static Assembly hostAssembly;
        private static int armed;
        private static int initialized;
        private static int diagnosticCount;
        [ThreadStatic] private static bool writingDiagnostic;

        public override void InitializeNewDomain(AppDomainSetup appDomainInfo)
        {
            base.InitializeNewDomain(appDomainInfo);
            if (!AppDomain.CurrentDomain.IsDefaultAppDomain()) return;
            try
            {
                var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
                pluginDirectory = Path.Combine(baseDirectory, "PCL", "Plugins", "DownloadRestore");
                var commandLine = Environment.GetCommandLineArgs();
                if (commandLine.Skip(1).Any(argument => argument == "--update" || argument == "--gpu" || argument == "--memory"))
                    return;
                // 插件缺失或存在禁用标记时，保持启动器的正常行为。
                if (!Directory.Exists(pluginDirectory)) return;
                if (File.Exists(Path.Combine(pluginDirectory, "disabled")))
                {
                    Log("DownloadRestore is disabled; normal startup continues.");
                    return;
                }
                if (!File.Exists(Path.Combine(pluginDirectory, "PCL.DownloadRestore.dll")) ||
                    !File.Exists(Path.Combine(pluginDirectory, "0Harmony.dll")))
                {
                    Log("Plugin payload is incomplete; normal startup continues.");
                    return;
                }

                // CLR 域管理器初始化期间，EntryAssembly 可能尚不存在。
                hostExecutablePath = Process.GetCurrentProcess().MainModule.FileName;
                if (File.Exists(Path.Combine(pluginDirectory, "diagnose-startup")))
                    AppDomain.CurrentDomain.FirstChanceException += TraceStartupException;
                AppDomain.CurrentDomain.AssemblyResolve += ResolvePluginDependency;
                AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
                Log("Detected executable " + Path.GetFileName(hostExecutablePath) + "; attempting PCL startup binding.");
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) TryArm(assembly);
            }
            catch (Exception exception)
            {
                Log("Bootstrap initialization failed; normal startup continues: " + exception);
            }
        }

        private static Assembly ResolvePluginDependency(object sender, ResolveEventArgs args)
        {
            // 仅解析名称精确匹配的本地插件程序集，不处理启动器自身的依赖。
            var name = new AssemblyName(args.Name).Name;
            if (name != "0Harmony" && name != "PCL.DownloadRestore") return null;
            var candidate = Path.Combine(pluginDirectory, name + ".dll");
            return File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
        }

        private static void OnAssemblyLoad(object sender, AssemblyLoadEventArgs args)
        {
            TryArm(args.LoadedAssembly);
        }

        private static void TryArm(Assembly assembly)
        {
            try
            {
                if (assembly.IsDynamic || !string.Equals(assembly.Location, hostExecutablePath, StringComparison.OrdinalIgnoreCase)) return;
                if (Interlocked.CompareExchange(ref armed, 1, 0) != 0) return;
                var identity = assembly.GetName();
                if (identity.Name != "Plain Craft Launcher 2")
                {
                    AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;
                    Log("Unsupported host assembly name " + identity.Name + "; normal startup continues without patches.");
                    return;
                }
                hostAssembly = assembly;
                Log("Detected PCL host assembly version " + identity.Version + "; inspecting Application constructor.");
                // 此事件同步执行，因此可在 CLR 执行入口点前应用补丁。
                var application = assembly.GetType("PCL.Application", true);
                var constructor = application.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null, Type.EmptyTypes, null);
                if (constructor == null) throw new MissingMethodException("PCL.Application", ".ctor()");
                var harmonyAssembly = Assembly.LoadFrom(Path.Combine(pluginDirectory, "0Harmony.dll"));
                var harmonyType = harmonyAssembly.GetType("HarmonyLib.Harmony", true);
                var harmonyMethodType = harmonyAssembly.GetType("HarmonyLib.HarmonyMethod", true);
                var harmony = Activator.CreateInstance(harmonyType, new object[] { "pcl.downloadrestore.bootstrap" });
                var callback = typeof(PluginDomainManager).GetMethod(nameof(AfterApplicationConstructor), BindingFlags.Static | BindingFlags.Public);
                var postfix = Activator.CreateInstance(harmonyMethodType, new object[] { callback });
                var patch = harmonyType.GetMethods().Single(method => method.Name == "Patch" && method.GetParameters().Length == 5);
                patch.Invoke(harmony, new object[] { constructor, null, postfix, null, null });
                AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;
                Log("Application constructor postfix installed.");
            }
            catch (Exception exception)
            {
                AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;
                Log("Unable to attach startup hook; normal startup continues: " + exception);
            }
        }

        // Harmony 必须能够从其生成的程序集中调用此方法。
        public static void AfterApplicationConstructor(object __instance)
        {
            try
            {
                // 宿主已在构造函数中注册 Startup 处理程序；在其后追加我们的处理程序，
                // 可确保它在核心与设置初始化完成后、WPF 打开 StartupUri 之前执行。
                // 过早修补 PCL 的静态方法可能提前触发其静态构造函数（.cctor）。
                var startup = __instance.GetType().GetEvent("Startup");
                var callback = typeof(PluginDomainManager).GetMethod(nameof(AfterApplicationStartup), BindingFlags.Public | BindingFlags.Static);
                startup.AddEventHandler(__instance, Delegate.CreateDelegate(startup.EventHandlerType, callback));
                Log("Plugin initialization scheduled after the host Startup handler.");
            }
            catch (Exception exception)
            {
                Log("Unable to register Startup callback; normal startup continues: " + exception);
            }
        }

        public static void AfterApplicationStartup(object sender, EventArgs args)
        {
            AppDomain.CurrentDomain.FirstChanceException -= TraceStartupException;
            if (Interlocked.CompareExchange(ref initialized, 1, 0) != 0) return;
            try
            {
                // 此时核心已完成初始化，启动器的程序集解析器也已可用。
                // WPF 会在所有 Startup 处理程序返回后才创建主窗口。
                var plugin = Assembly.LoadFrom(Path.Combine(pluginDirectory, "PCL.DownloadRestore.dll"));
                var entryType = plugin.GetType("PCL.DownloadRestore.EntryPoint", true);
                var initialize = entryType.GetMethod("Initialize", BindingFlags.Public | BindingFlags.Static,
                    null, new[] { typeof(Assembly), typeof(Action<string>) }, null);
                if (initialize == null) throw new MissingMethodException(entryType.FullName, "Initialize(Assembly, Action<string>)");
                initialize.Invoke(null, new object[] { hostAssembly, new Action<string>(Log) });
                Log("Plugin initialization completed after host Startup, before the main window.");
            }
            catch (Exception exception)
            {
                Log("Plugin initialization failed; normal startup continues: " + exception);
            }
        }

        private static void Log(string message)
        {
            try
            {
                if (pluginDirectory == null || !Directory.Exists(pluginDirectory)) return;
                lock (LogLock)
                {
                    var path = Path.Combine(pluginDirectory, "bootstrap.log");
                    if (File.Exists(path) && new FileInfo(path).Length > 1024 * 1024)
                        File.WriteAllText(path, "");
                    File.AppendAllText(path, DateTimeOffset.Now.ToString("o") + " " + message + Environment.NewLine);
                }
            }
            catch { /* 诊断功能不得阻止启动器启动。 */ }
        }

        private static void TraceStartupException(object sender, FirstChanceExceptionEventArgs args)
        {
            if (writingDiagnostic || Interlocked.Increment(ref diagnosticCount) > 100) return;
            try
            {
                writingDiagnostic = true;
                Log("STARTUP_DIAGNOSTIC " + args.Exception.GetType().FullName + ": " + args.Exception.Message + Environment.NewLine + new StackTrace(false));
            }
            catch { }
            finally { writingDiagnostic = false; }
        }
    }
}
