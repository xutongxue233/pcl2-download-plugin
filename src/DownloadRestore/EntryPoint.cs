// Third-party PCL download restoration. The host executable is never rewritten.
// Original PCL behavior and method names: 龙腾猫跃 / Meloong-Git/PCL.
// The repository's original LICENCE applies to referenced PCL source material.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using HarmonyLib;

namespace PCL.DownloadRestore
{
    /// <summary>Invoked after PCL completes Application_Startup, before its main window is created.</summary>
    public static class EntryPoint
    {
        public const string SupportedHostSha256 = "6B6E1260CB5A1D6A303855F1C60CAB3D0BC1AEBBA7E40A964180188B271E7386";
        public const string PatchId = "thirdparty.pcl.downloadrestore.2.99.0.0";
        private static readonly object Sync = new object();
        private static readonly string[] NoMethods = new string[0];
        private static IReadOnlyList<string> patchedMethods = Array.AsReadOnly(NoMethods);
        private static Assembly initializedHost;
        private static Action<string> logger;

        public static bool IsInitialized { get { lock (Sync) return initializedHost != null; } }
        public static IReadOnlyList<string> PatchedMethods { get { lock (Sync) return patchedMethods; } }

        /// <summary>
        /// Validates the original host and every binding, then applies five runtime patches.
        /// A failure removes this plugin's patches; it never changes the EXE on disk.
        /// </summary>
        public static void Initialize(Assembly host, Action<string> log)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            lock (Sync)
            {
                if (initializedHost != null)
                {
                    if (initializedHost != host) throw new InvalidOperationException("The plugin is already bound to another host assembly.");
                    return;
                }
                logger = log;
                ValidateHost(host);
                ValidateStartupTiming();
                HostBindings bindings = HostBindings.Create(host);
                var harmony = new Harmony(PatchId);
                RuntimePatches.Bindings = bindings;
                try
                {
                    Patch(harmony, bindings.LoaderInit, transpiler: nameof(RuntimePatches.RestoreMinecraftLoader));
                    Patch(harmony, bindings.LoadInstanceFinished, transpiler: nameof(RuntimePatches.RestoreMinecraftCard));
                    Patch(harmony, bindings.InitializeResourceVersions, transpiler: nameof(RuntimePatches.RestoreResourceActions));
                    Patch(harmony, bindings.ModpackChoose, prefix: nameof(RuntimePatches.ChooseModpack));
                    Patch(harmony, bindings.ModpackInstall, transpiler: nameof(RuntimePatches.RestoreModpackDispatch));

                    MethodInfo[] targets = bindings.Targets;
                    foreach (MethodInfo method in targets)
                    {
                        var info = Harmony.GetPatchInfo(method);
                        if (info == null || !info.Owners.Contains(PatchId))
                            throw new InvalidOperationException("Patch verification failed: " + method.DeclaringType.FullName + "." + method.Name);
                    }
                    patchedMethods = Array.AsReadOnly(targets.Select(m => m.DeclaringType.FullName + "." + m).ToArray());
                    initializedHost = host;
                    Log("DownloadRestore ready: 5 runtime patches applied; official EXE unchanged.");
                }
                catch
                {
                    // Unpatch only this plugin, leaving the bootstrap and unrelated extensions alone.
                    harmony.UnpatchAll(PatchId);
                    RuntimePatches.Bindings = null;
                    patchedMethods = Array.AsReadOnly(NoMethods);
                    Log("DownloadRestore failed; all of its patches were rolled back.");
                    throw;
                }
            }
        }

        private static void Patch(Harmony harmony, MethodInfo target, string prefix = null, string transpiler = null)
        {
            harmony.Patch(target,
                prefix: prefix == null ? null : new HarmonyMethod(typeof(RuntimePatches), prefix),
                transpiler: transpiler == null ? null : new HarmonyMethod(typeof(RuntimePatches), transpiler));
            Log("Patched " + target.DeclaringType.FullName + "." + target.Name + " (" + target.GetParameters().Length + " arguments).");
        }

        private static void ValidateHost(Assembly host)
        {
            if (host.GetName().Name != "Plain Craft Launcher 2" || host.GetName().Version != new Version(2, 99, 0, 0))
                throw new NotSupportedException("DownloadRestore supports the original PCL 2.99.0.0 executable only.");
            string hash;
            using (var stream = File.OpenRead(host.Location))
            using (var sha = SHA256.Create())
                hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
            if (!string.Equals(hash, SupportedHostSha256, StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException("PCL executable SHA-256 is not supported: " + hash);
        }

        private static void ValidateStartupTiming()
        {
            // Harmony may trigger the static initializers of inspected host types. Before
            // Main.Init these would cache the default Meloong paths instead of PCL paths.
            // Reading this simple core field is safe and avoids touching any host class.
            Type core = Assembly.Load("MeloongCore").GetType("MeloongCore.Main", true);
            object name = core.GetField("AppName", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            if (!Equals(name, "PCL"))
                throw new InvalidOperationException("DownloadRestore must initialize after PCL Application_Startup has initialized MeloongCore.");
        }

        internal static void Log(string message)
        {
            try { logger?.Invoke(message); }
            catch { /* A diagnostic callback cannot break startup or leave a partial patch set. */ }
        }
    }
}
