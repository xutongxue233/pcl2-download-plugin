// 第三方 PCL 下载功能恢复插件，始终不改写宿主可执行文件。
// 原版 PCL 的行为与方法名称来源：龙腾猫跃 / Meloong-Git/PCL。
// 引用的 PCL 源码资料适用仓库原有的 LICENCE。
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace PCL.DownloadRestore
{
    /// <summary>在 PCL 完成 Application_Startup 后、创建主窗口前调用。</summary>
    public static class EntryPoint
    {
        public const string PatchId = "thirdparty.pcl.downloadrestore.2.99.0.0";
        private static readonly object Sync = new object();
        private static readonly string[] NoMethods = new string[0];
        private static IReadOnlyList<string> patchedMethods = Array.AsReadOnly(NoMethods);
        private static Assembly initializedHost;
        private static Action<string> logger;

        public static bool IsInitialized { get { lock (Sync) return initializedHost != null; } }
        public static IReadOnlyList<string> PatchedMethods { get { lock (Sync) return patchedMethods; } }

        /// <summary>
        /// 校验宿主身份及实际绑定后，尝试应用五个运行时补丁。
        /// 失败时移除此插件的补丁，始终不更改磁盘上的 EXE。
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
                    Log("DownloadRestore ready: 5 runtime patches applied; launcher EXE unchanged.");
                }
                catch
                {
                    // 仅移除此插件的补丁，保留启动加载器和其他无关扩展。
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
            var identity = host.GetName();
            if (identity.Name != "Plain Craft Launcher 2")
                throw new NotSupportedException("DownloadRestore requires the PCL host assembly named Plain Craft Launcher 2.");
            // 兼容性由后续的实际类型、方法和 IL 结构检查决定。
            Log("Detected PCL host assembly version " + identity.Version + "; validating startup timing and runtime patch bindings.");
        }

        private static void ValidateStartupTiming()
        {
            // Harmony 可能触发被检查宿主类型的静态初始化器。如果早于 Main.Init 执行，
            // 它们会缓存默认的 Meloong 路径，而非 PCL 路径。
            // 读取这个简单的核心字段是安全的，也能避免访问任何宿主类。
            Type core = Assembly.Load("MeloongCore").GetType("MeloongCore.Main", true);
            object name = core.GetField("AppName", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            if (!Equals(name, "PCL"))
                throw new InvalidOperationException("DownloadRestore must initialize after PCL Application_Startup has initialized MeloongCore.");
        }

        internal static void Log(string message)
        {
            try { logger?.Invoke(message); }
            catch { /* 诊断回调不得中断启动，也不得导致仅应用部分补丁。 */ }
        }
    }
}
