using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace PCL.PluginBootstrap
{
    /// <summary>Application-local CLR startup hook; installed only via this launcher's .exe.config.</summary>
    public sealed class PluginDomainManager : AppDomainManager
    {
        public const string SupportedHostSha256 = "6B6E1260CB5A1D6A303855F1C60CAB3D0BC1AEBBA7E40A964180188B271E7386";
        private static readonly object LogLock = new object();
        private static string pluginDirectory;
        private static string verifiedHostPath;
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
                // An absent plugin or disabled marker keeps the launcher's normal behavior.
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

                // EntryAssembly may not exist yet during CLR domain-manager initialization.
                verifiedHostPath = Process.GetCurrentProcess().MainModule.FileName;
                using (var stream = File.OpenRead(verifiedHostPath))
                using (var sha = SHA256.Create())
                {
                    var hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
                    if (!string.Equals(hash, SupportedHostSha256, StringComparison.OrdinalIgnoreCase))
                    {
                        Log("Unsupported executable SHA-256 " + hash + "; no patches applied.");
                        return;
                    }
                }
                if (File.Exists(Path.Combine(pluginDirectory, "diagnose-startup")))
                    AppDomain.CurrentDomain.FirstChanceException += TraceStartupException;
                AppDomain.CurrentDomain.AssemblyResolve += ResolvePluginDependency;
                AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) TryArm(assembly);
                Log("Verified official PCL 2.99.0.0; waiting for Application constructor.");
            }
            catch (Exception exception)
            {
                Log("Bootstrap initialization failed; normal startup continues: " + exception);
            }
        }

        private static Assembly ResolvePluginDependency(object sender, ResolveEventArgs args)
        {
            // Only satisfy our exact local payload assemblies, never launcher dependencies.
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
                if (assembly.IsDynamic || !string.Equals(assembly.Location, verifiedHostPath, StringComparison.OrdinalIgnoreCase)) return;
                if (Interlocked.CompareExchange(ref armed, 1, 0) != 0) return;
                hostAssembly = assembly;
                // This event is synchronous: patch before the CLR executes the entry point.
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

        // Harmony must be able to call this method from its generated assembly.
        public static void AfterApplicationConstructor(object __instance)
        {
            try
            {
                // The host attached its Startup handler in the constructor. Appending ours
                // runs after core/settings initialization, before WPF opens StartupUri.
                // Patching PCL's static methods earlier can prematurely run their .cctors.
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
                // Core initialization and the launcher's resolver are now available.
                // WPF creates the main window only after all Startup handlers return.
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
            catch { /* Diagnostics must never prevent the launcher from starting. */ }
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
