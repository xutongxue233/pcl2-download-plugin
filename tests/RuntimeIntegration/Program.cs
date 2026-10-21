using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using System.Runtime.Serialization;
using HarmonyLib;
using PCL.DownloadRestore;

internal static class Program
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    private const string TestPatchId = "pcl.downloadrestore.tests.runtime";
    private static readonly List<string> Passed = new List<string>();
    private static readonly List<string> Log = new List<string>();
    private static Assembly host;
    private static MethodInfo install, choose;
    private static string capturedMethod, hint, fixtures, gameFolder;
    private static object[] capturedArgs;
    private static Exception installerException;
    private static List<string> chosenFiles = new List<string>();
    private static int dispatchedThreads;
    private static object[] resourceArguments;
    private static object resourceInstallSender;

    [STAThread]
    private static int Main(string[] args)
    {
        string report = args.Length > 1 ? Path.GetFullPath(args[1]) : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "results.txt");
        try
        {
            string hostPath = Path.GetFullPath(args[0]);
            string hashBefore = Hash(hostPath);
            fixtures = Path.Combine(Path.GetDirectoryName(report), "fixtures");
            gameFolder = Path.Combine(fixtures, "minecraft") + "\\";
            Directory.CreateDirectory(fixtures);
            host = Assembly.LoadFrom(hostPath);
            host.GetType("PCL.Application", true).GetConstructor(All, null, Type.EmptyTypes, null).Invoke(new object[0]);
            bool timingRejected = false;
            try { EntryPoint.Initialize(host, message => Log.Add(message)); }
            catch (InvalidOperationException exception) { timingRejected = exception.Message.Contains("Application_Startup"); }
            Assert(timingRejected && !EntryPoint.IsInitialized, "Premature initialization rejected before host static types are patched");
            Assembly.Load("MeloongCore").GetType("MeloongCore.Main", true).GetMethod("Init").Invoke(null, new object[] { "PCL" });
            Assembly.Load("MeloongCore.Wpf").GetType("MeloongCore.Wpf.Main", true).GetMethod("Init").Invoke(null, new object[0]);
            var sabotage = new Harmony(TestPatchId + ".atomic");
            var thirdTranspiler = typeof(EntryPoint).Assembly.GetType("PCL.DownloadRestore.RuntimePatches", true).GetMethod("RestoreResourceActions", All);
            sabotage.Patch(thirdTranspiler, prefix: new HarmonyMethod(typeof(Program), nameof(FailThirdPatch)));
            bool patchFailed = false;
            try { EntryPoint.Initialize(host, message => Log.Add(message)); }
            catch { patchFailed = true; }
            Assert(patchFailed && !EntryPoint.IsInitialized && EntryPoint.PatchedMethods.Count == 0, "Simulated third-patch failure leaves plugin uninitialized");
            Assert(!Harmony.GetAllPatchedMethods().Any(method => Harmony.GetPatchInfo(method).Owners.Contains(EntryPoint.PatchId)), "All earlier plugin patches roll back after mid-initialization failure");
            sabotage.UnpatchAll(TestPatchId + ".atomic");
            EntryPoint.Initialize(host, message => Log.Add(message));
            Assert(EntryPoint.IsInitialized && EntryPoint.PatchedMethods.Count == 5, "Exactly five runtime patches initialized");
            var targets = Harmony.GetAllPatchedMethods().Where(method => Harmony.GetPatchInfo(method).Owners.Contains(EntryPoint.PatchId)).ToArray();
            Assert(targets.Length == 5, "Exactly five host methods owned by production plugin");
            EntryPoint.Initialize(host, message => Log.Add(message));
            Assert(targets.All(method => Harmony.GetPatchInfo(method).Prefixes.Count(p => p.owner == EntryPoint.PatchId) + Harmony.GetPatchInfo(method).Transpilers.Count(p => p.owner == EntryPoint.PatchId) == 1), "Repeated initialization does not duplicate patches");

            var modpack = host.GetType("PCL.ModModpack", true);
            install = modpack.GetMethods(All).Single(method => method.Name == "ModpackInstall" && method.GetParameters().Length == 3);
            choose = modpack.GetMethods(All).Single(method => method.Name == "ModpackInstall" && method.GetParameters().Length == 0);
            Assert(!PatchProcessor.GetCurrentInstructions(install).Any(instruction => instruction.opcode == OpCodes.Ldstr && instruction.operand is string text && text.Contains("暂时不再提供 MC 下载功能")), "Archive dispatch has no remaining restriction throw");
            var loader = targets.Single(method => method.Name == "LoaderInit");
            Assert(PatchProcessor.GetCurrentInstructions(loader).Any(instruction => instruction.opcode == OpCodes.Ldsfld && instruction.operand is FieldInfo field && field.Name == "DlClientListLoader"), "Install page uses the original online version loader");
            var card = targets.Single(method => method.Name == "LoadInstanceFinished");
            var cardCode = PatchProcessor.GetCurrentInstructions(card);
            int cardIndex = cardCode.FindIndex(instruction => instruction.operand is MethodInfo method && method.Name == "get_CardMinecraft");
            Assert(cardIndex >= 0 && cardCode[cardIndex + 1].LoadsConstant(0), "Minecraft modification card is visible");
            var resource = targets.Single(method => method.Name == "InitializeStackResourceVersions");
            Assert(PatchProcessor.GetCurrentInstructions(resource).Any(instruction => instruction.operand is MethodInfo method && method.Name == "CreateResourceItem" && method.DeclaringType.Assembly == typeof(EntryPoint).Assembly), "Resource list retains grouping and calls restored action factory");

            var mocks = new Harmony(TestPatchId);
            foreach (var method in modpack.GetMethods(All).Where(method => method.Name.StartsWith("InstallPack", StringComparison.Ordinal)))
                mocks.Patch(method, prefix: new HarmonyMethod(typeof(Program), nameof(CaptureInstaller)));
            var minecraft = host.GetType("PCL.ModMinecraft", true);
            mocks.Patch(minecraft.GetProperty("McFolderSelected", All).GetGetMethod(true), prefix: new HarmonyMethod(typeof(Program), nameof(GetGameFolder)));
            mocks.Patch(host.GetType("PCL.ModMain", true).GetMethods(All).Single(method => method.Name == "Hint"), prefix: new HarmonyMethod(typeof(Program), nameof(CaptureHint)));
            var dialogs = Assembly.Load("MeloongCore.Wpf").GetType("MeloongCore.Wpf.Dialogs", true);
            mocks.Patch(dialogs.GetMethods(All).Single(method => method.Name == "SelectFile" && method.GetParameters().Length == 4), prefix: new HarmonyMethod(typeof(Program), nameof(ChooseFile)));
            mocks.Patch(host.GetType("PCL.ModBase", true).GetMethods(All).Single(method => method.Name == "RunInThread" && method.GetParameters().Length == 1), prefix: new HarmonyMethod(typeof(Program), nameof(RunThread)));

            TestResourceActions(mocks);

            TestPack("curseforge", "manifest.json", "{}", "InstallPackCurseForge", 5);
            TestPack("mcbbs", "mcbbs.packmeta", "{}", "InstallPackMCBBS", 4);
            TestPack("mcbbs-manifest", "manifest.json", "{\"addons\":[]}", "InstallPackMCBBS", 4);
            TestPack("modrinth", "modrinth.index.json", "{}", "InstallPackModrinth", 5);
            TestPack("multimc", "mmc-pack.json", "{}", "InstallPackMMC", 3);
            TestPack("hmcl", "modpack.json", "{}", "InstallPackHMCL", 3);
            TestPack("wrapper-zip", "modpack.zip", "embedded fixture", "InstallPackLauncherPack", 3);
            TestPack("wrapper-mrpack", "modpack.mrpack", "embedded fixture", "InstallPackLauncherPack", 3);
            TestPack("generic", "readme.txt", "ordinary archive", "InstallPackCompress", 2);
            foreach (var fixture in new[] {
                new[] { "nested-cf", "manifest.json", "{}", "InstallPackCurseForge", "5" },
                new[] { "nested-mcbbs", "mcbbs.packmeta", "{}", "InstallPackMCBBS", "4" },
                new[] { "nested-mmc", "mmc-pack.json", "{}", "InstallPackMMC", "3" },
                new[] { "nested-modrinth", "modrinth.index.json", "{}", "InstallPackModrinth", "5" },
                new[] { "nested-hmcl", "modpack.json", "{}", "InstallPackHMCL", "3" },
                new[] { "nested-wrapper", "modpack.zip", "embedded", "InstallPackLauncherPack", "3" }
            }) TestPack(fixture[0], "folder/" + fixture[1], fixture[2], fixture[3], int.Parse(fixture[4]), "folder/");
            TestPack("nested-mcbbs-addons", "folder/manifest.json", "{\"addons\":[]}", "InstallPackMCBBS", 4, "overrides/");

            string valid = MakeZip("cancelled", "manifest.json", "{}");
            installerException = new OperationCanceledException("test cancellation");
            Assert(ReferenceEquals(CatchInstall(valid), installerException), "Installer cancellation propagates without wrapping");
            AssertDisposed(capturedArgs[1] as ZipArchive, "Archive disposed after cancellation");
            installerException = new InvalidOperationException("test installer failure");
            Assert(ReferenceEquals(CatchInstall(valid), installerException), "Installer error propagates without wrapping");
            AssertDisposed(capturedArgs[1] as ZipArchive, "Archive disposed after installer failure");
            installerException = null;

            string malformed = Path.Combine(fixtures, "malformed.zip");
            File.WriteAllText(malformed, "not an archive");
            ResetCapture();
            var malformedError = CatchInstall(malformed);
            Assert(malformedError.Message.Contains("打开整合包文件失败") && malformedError.ToString().Contains("文件已损坏"), "Malformed ZIP retains original useful error");
            Assert(capturedMethod == null, "Malformed ZIP never invokes an installer");
            string rar = Path.Combine(fixtures, "unsupported.rar");
            File.WriteAllText(rar, new string('x', 512));
            Assert(CatchInstall(rar).Message.Contains("rar 格式"), "RAR format retains original conversion guidance");
            string brokenManifest = MakeZip("broken-manifest", "manifest.json", "{");
            Assert(CatchInstall(brokenManifest).Message.Contains("文件可能损坏"), "Malformed manifest retains original archive error");
            using (File.Open(brokenManifest, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            Passed.Add("Invalid manifest releases archive file handle");

            foreach (string marker in new[] { "!", ";" })
            {
                string oldFolder = gameFolder;
                gameFolder = oldFolder + marker + "\\";
                ResetCapture();
                Assert(CatchInstall(valid) is OperationCanceledException && capturedMethod == null && hint != null && hint.Contains("不能含有感叹号或分号"), "Original unsafe path cancellation preserved: " + marker);
                gameFolder = oldFolder;
            }

            ResetCapture(); chosenFiles.Clear(); dispatchedThreads = 0;
            Invoke(choose);
            Assert(capturedMethod == null && dispatchedThreads == 0, "Canceled file chooser does not schedule an installer");
            chosenFiles.Add(valid);
            Invoke(choose);
            Assert(capturedMethod == "InstallPackCurseForge" && dispatchedThreads == 1 && (string)capturedArgs[0] == valid, "Manual file chooser schedules preserved host closure and dispatches selected file");
            AssertDisposed(capturedArgs[1] as ZipArchive, "Manual import disposes archive");
            Assert(Hash(hostPath) == hashBefore && hashBefore == EntryPoint.SupportedHostSha256, "Official EXE remains byte-identical after all runtime tests");
            mocks.UnpatchAll(TestPatchId);
            File.WriteAllLines(report, Passed.Select(item => "PASS " + item).Concat(new[] { "", "Passed: " + Passed.Count, "", "Plugin log:" }).Concat(Log));
            Console.WriteLine("Runtime integration passed: " + Passed.Count + " assertions. " + report);
            return 0;
        }
        catch (Exception exception)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(report));
            File.WriteAllLines(report, Passed.Select(item => "PASS " + item).Concat(new[] { "FAIL " + exception, "", "Plugin log:" }).Concat(Log));
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void TestPack(string name, string entry, string contents, string expected, int argumentCount, string baseFolder = "")
    {
        string file = MakeZip(name, entry, contents);
        ResetCapture();
        Assert(Invoke(install, file, "named-instance", "fixture-logo.png") == null, name + ": installer result returned");
        Assert(capturedMethod == expected && capturedArgs.Length == argumentCount, name + ": correct original installer selected");
        Assert((string)capturedArgs[0] == file && capturedArgs[1] is ZipArchive, name + ": file and archive passed intact");
        if (argumentCount >= 3) Assert((string)capturedArgs[2] == baseFolder, name + ": correct archive base folder");
        if (argumentCount >= 4) Assert((string)capturedArgs[3] == "named-instance", name + ": instance name preserved");
        if (argumentCount >= 5) Assert((string)capturedArgs[4] == "fixture-logo.png", name + ": logo preserved");
        AssertDisposed(capturedArgs[1] as ZipArchive, name + ": archive disposed");
        using (File.Open(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
    }

    private static string MakeZip(string name, string entry, string content)
    {
        string file = Path.Combine(fixtures, name + ".zip");
        using (var output = File.Create(file))
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create))
        using (var writer = new StreamWriter(archive.CreateEntry(entry).Open())) writer.Write(content);
        return file;
    }
    private static void ResetCapture() { capturedMethod = null; capturedArgs = null; hint = null; }
    private static void TestResourceActions(Harmony mocks)
    {
        var pageType = host.GetType("PCL.PageDownloadResourceDetail", true);
        var page = FormatterServices.GetUninitializedObject(pageType);
        var versionType = host.GetType("PCL.ResourceVersion", true);
        var version = FormatterServices.GetUninitializedObject(versionType);
        var itemFactory = versionType.GetMethods(All).Single(method => method.Name == "ToListItem" && method.GetParameters().Length == 3);
        mocks.Patch(itemFactory, prefix: new HarmonyMethod(typeof(Program), nameof(CaptureResourceItem)));
        var callback = typeof(Program).GetMethod(nameof(UnusedResourceClick), All);
        var click = Delegate.CreateDelegate(itemFactory.GetParameters()[0].ParameterType, callback);
        var save = Delegate.CreateDelegate(itemFactory.GetParameters()[1].ParameterType, callback);
        var targetType = pageType.GetField("TargetResourceType", All);
        var wrapper = typeof(EntryPoint).Assembly.GetType("PCL.DownloadRestore.RuntimePatches", true).GetMethod("CreateResourceItem", All);
        targetType.SetValue(page, Enum.ToObject(targetType.FieldType, 2));
        Invoke(wrapper, version, click, save, true, page);
        var primaryClick = (Delegate)resourceArguments[0];
        Assert(primaryClick.Method.Name == "_Lambda$__R18-5" && primaryClick.Target == page, "Online modpack primary click binds to original VB installation adapter");
        var installClick = pageType.GetMethods(All).Single(method => method.Name == "Install_Click");
        mocks.Patch(installClick, prefix: new HarmonyMethod(typeof(Program), nameof(CaptureResourceInstall)));
        var clickedItem = FormatterServices.GetUninitializedObject(host.GetType("PCL.MyListItem", true));
        primaryClick.DynamicInvoke(clickedItem, null);
        Assert(ReferenceEquals(clickedItem, resourceInstallSender), "Online modpack click adapter reaches original Install_Click with exact item");
        Assert(((Delegate)resourceArguments[1]).Method.Name == "Save_Click" && ((Delegate)resourceArguments[1]).Target == page, "Online modpack secondary click binds to original save handler");
        Assert((bool)resourceArguments[2], "Resource display flags preserved");
        targetType.SetValue(page, Enum.ToObject(targetType.FieldType, 0));
        Invoke(wrapper, version, click, save, false, page);
        Assert(ReferenceEquals(resourceArguments[0], click) && ReferenceEquals(resourceArguments[1], save), "Non-modpack resource handlers remain unchanged");
        Assert(!(bool)resourceArguments[2], "Non-modpack display flags preserved");
    }
    public static bool CaptureResourceItem(object[] __args) { resourceArguments = __args.ToArray(); return false; }
    public static bool CaptureResourceInstall(object[] __args) { resourceInstallSender = __args[0]; return false; }
    public static bool FailThirdPatch() { throw new InvalidOperationException("Simulated third patch failure for atomic rollback verification."); }
    public static void UnusedResourceClick(object sender, EventArgs args) { }
    public static bool CaptureInstaller(MethodBase __originalMethod, object[] __args)
    {
        capturedMethod = __originalMethod.Name;
        capturedArgs = __args.ToArray();
        if (installerException != null) throw installerException;
        return false;
    }
    public static bool GetGameFolder(ref string __result) { __result = gameFolder; return false; }
    public static bool CaptureHint(object[] __args) { hint = (string)__args[0]; return false; }
    public static bool ChooseFile(ref List<string> __result) { __result = chosenFiles.ToList(); return false; }
    public static bool RunThread(Action __0) { dispatchedThreads++; __0(); return false; }
    private static object Invoke(MethodInfo method, params object[] args)
    {
        try { return method.Invoke(null, args); }
        catch (TargetInvocationException exception) { throw exception.InnerException; }
    }
    private static Exception CatchInstall(string path)
    {
        try { Invoke(install, path, "named-instance", "fixture-logo.png"); }
        catch (Exception exception) { return exception; }
        throw new Exception("Expected installation to fail: " + path);
    }
    private static void AssertDisposed(ZipArchive archive, string label)
    {
        bool disposed = false;
        try { var ignored = archive.Entries.Count; }
        catch (ObjectDisposedException) { disposed = true; }
        Assert(disposed, label);
    }
    private static void Assert(bool passed, string label) { if (!passed) throw new Exception(label); Passed.Add(label); }
    private static string Hash(string file)
    {
        using (var stream = File.OpenRead(file)) using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "");
    }
}
