using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Xml;
using PCL.DownloadRestore.Injector;
using PCL.DownloadRestore.Injector.Installation;

internal static class Program
{
    private const string SupportedHash = "6B6E1260CB5A1D6A303855F1C60CAB3D0BC1AEBBA7E40A964180188B271E7386";
    private static readonly string[] Switches = {
        "Switch.System.IO.UseLegacyPathHandling", "Switch.System.IO.BlockLongPaths",
        "Switch.System.Net.DontEnableSchUseStrongCrypto", "Switch.System.Net.DontEnableSystemDefaultTlsVersions"
    };
    private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
    private static readonly List<object> Cases = new List<object>();
    private static readonly List<string> Assertions = new List<string>();
    private static IDictionary<string, byte[]> payload;
    private static string root, originalHost, runDirectory;
    private static int caseNumber;

    private static int Main(string[] args)
    {
        root = Path.GetFullPath(args[0]); originalHost = Path.GetFullPath(args[1]);
        string reportDirectory = Path.GetFullPath(args[2]);
        Directory.CreateDirectory(reportDirectory);
        runDirectory = Path.Combine(reportDirectory, "run-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6));
        Directory.CreateDirectory(runDirectory);
        try
        {
            if (Hash(originalHost) != SupportedHash) throw new Exception("Test reference is not the supported official EXE.");
            payload = EmbeddedPayload.ReadFiles();
            Check(payload.Count == 3 && payload.All(item => item.Value.Length > 0), "Real executable embeds exactly three nonempty payload files");
            Run("install/reinstall/disable/uninstall", BasicLifecycle);
            Run("original config exact restore and switch merge", ExistingConfiguration);
            Run("unsupported executable rejected without mutation", UnsupportedExecutable);
            Run("conflicting bootstrap preserved", ExistingBootstrap);
            Run("invalid XML and duplicate/conflicting configuration rejected", InvalidConfigurations);
            Run("missing managed DLLs can be uninstalled", MissingPayloadUninstall);
            Run("changed installed config preserved", ChangedConfig);
            Run("changed payload and original backup preserved", ChangedPayloadAndBackup);
            Run("updated launcher can remove old plugin without EXE mutation", UpdatedLauncherCleanup);
            Run("unregistered config and missing state protected", MissingReceipt);
            Run("malicious and duplicate receipt paths rejected", InvalidReceiptPaths);
            Run("directory junction rejected without touching target", JunctionRejected);
            Run("installation failure rolls back files and original config", InstallRollback);
            Run("uninstallation failure restores installed state", UninstallRollback);
            Run("PowerShell schema 1 installs uninstall through native service", PowerShellToNative);
            Run("native schema 1 installs uninstall through PowerShell", NativeToPowerShell);
            Run("fresh standalone EXE CLI without PowerShell on PATH", StandaloneCli);
        }
        catch (Exception exception) { Cases.Add(new { Name = "harness setup", Passed = false, Error = exception.ToString() }); }

        bool allPassed = Cases.All(item => (bool)item.GetType().GetProperty("Passed").GetValue(item));
        string report = Json.Serialize(new {
            CheckedAt = DateTimeOffset.Now.ToString("o"), AllPassed = allPassed,
            Cases = Cases, Assertions = Assertions, AssertionCount = Assertions.Count,
            FixtureDirectory = runDirectory, OriginalHostSha256 = Hash(originalHost)
        });
        File.WriteAllText(Path.Combine(reportDirectory, "results.json"), report, new UTF8Encoding(false));
        File.WriteAllLines(Path.Combine(reportDirectory, "results.txt"), Cases.Select(item =>
            ((bool)item.GetType().GetProperty("Passed").GetValue(item) ? "PASS " : "FAIL ") + item.GetType().GetProperty("Name").GetValue(item))
            .Concat(new[] { "Assertions: " + Assertions.Count, "Fixture directory: " + runDirectory }));
        Console.WriteLine("Injector integration: " + Cases.Count + " cases, " + Assertions.Count + " assertions, all passed=" + allPassed);
        return allPassed ? 0 : 1;
    }

    private static InstallationService Service() => new InstallationService(payload);
    private static void Run(string name, Action action)
    {
        try { action(); Cases.Add(new { Name = name, Passed = true, Error = (string)null }); Console.WriteLine("PASS " + name); }
        catch (Exception exception) { Cases.Add(new { Name = name, Passed = false, Error = exception.ToString() }); Console.WriteLine("FAIL " + name + ": " + exception.Message); }
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Assertions.Add(message);
    }
    private static string NewFixture(string name)
    {
        string folder = Path.Combine(runDirectory, (++caseNumber).ToString("D2") + "-" + name + " 中文 空格");
        Directory.CreateDirectory(folder);
        string exe = Path.Combine(folder, "Plain Craft Launcher 2.exe");
        File.Copy(originalHost, exe);
        return exe;
    }
    private static string Plugin(string exe) => Path.Combine(Path.GetDirectoryName(exe), "PCL", "Plugins", "DownloadRestore");
    private static string Bootstrap(string exe) => Path.Combine(Path.GetDirectoryName(exe), "PCL.PluginBootstrap.dll");
    private static string Receipt(string exe) => Path.Combine(Plugin(exe), ".install-state.json");
    private static string Backup(string exe) => Path.Combine(Plugin(exe), ".original-exe.config");
    private static void ExpectFailure(Action action, string description)
    {
        bool failed = false;
        try { action(); } catch (Exception) { failed = true; }
        Check(failed, description);
    }
    private static string Hash(string path)
    {
        using (var stream = File.OpenRead(path)) using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
    }
    private static bool BytesEqual(byte[] expected, string path) => File.Exists(path) && expected.SequenceEqual(File.ReadAllBytes(path));

    private static void BasicLifecycle()
    {
        string exe = NewFixture("basic"); var service = Service();
        var before = service.Inspect(exe);
        Check(before.CanInstall && !before.CanUninstall && before.LauncherSha256 == SupportedHash, "Fresh supported EXE is installable");
        Check(service.Install(exe).Success, "Native install succeeds");
        Check(Hash(exe) == SupportedHash, "Install leaves official EXE byte-identical");
        var installedState = service.Inspect(exe);
        Check(installedState.Status == "Installed" && !installedState.CanInstall && installedState.CanUninstall,
            "Installed plugin is detected and repeat injection is disabled while uninstall stays available");
        foreach (var item in payload) Check(BytesEqual(item.Value, Path.Combine(Path.GetDirectoryName(exe), item.Key)), "Installed payload matches embedded resource: " + item.Key);
        AssertConfig(exe + ".config");
        var state = ReadJson(Receipt(exe));
        Check(Convert.ToInt32(state["Schema"]) == 1 && (string)state["LauncherPath"] == exe, "Native receipt uses interoperable schema 1 and exact launcher path");
        var installed = Snapshot(Path.GetDirectoryName(exe));
        var reopenedService = Service(); var reopenedState = reopenedService.Inspect(exe);
        Check(reopenedState.Status == "Installed" && !reopenedState.CanInstall && reopenedState.CanUninstall,
            "A reopened injector recognizes the persisted installation and needs no repeat injection");
        Check(reopenedService.Install(exe).Success, "Repeated install through a fresh service succeeds idempotently");
        Check(SameSnapshot(installed, Snapshot(Path.GetDirectoryName(exe))), "Repeated install changes no file bytes");
        string marker = Path.Combine(Plugin(exe), "disabled"); File.WriteAllText(marker, "");
        var disabledState = reopenedService.Inspect(exe);
        Check(disabledState.Status == "Disabled" && disabledState.CanInstall && disabledState.CanUninstall,
            "Disabled plugin can be re-enabled or uninstalled");
        Check(reopenedService.Install(exe).Success && !File.Exists(marker), "Install re-enables existing disabled plugin");
        var reenabledState = Service().Inspect(exe);
        Check(reenabledState.Status == "Installed" && !reenabledState.CanInstall && reenabledState.CanUninstall,
            "Re-enabled plugin is recognized after reopening and repeat injection is disabled again");
        Check(SameSnapshot(installed, Snapshot(Path.GetDirectoryName(exe))), "Re-enabling changes only the disabled marker and preserves all installed bytes");
        string diagnostic = Path.Combine(Plugin(exe), "bootstrap.log"); File.WriteAllText(diagnostic, "keep diagnostic");
        Check(service.Uninstall(exe).Success, "Native uninstall succeeds");
        Check(!File.Exists(exe + ".config") && !File.Exists(Bootstrap(exe)) && !File.Exists(Receipt(exe)), "Uninstall removes only generated hook config and owned binaries/state");
        Check(File.ReadAllText(diagnostic) == "keep diagnostic", "Uninstall preserves diagnostic log");
        var removed = Snapshot(Path.GetDirectoryName(exe));
        Check(service.Uninstall(exe).Success && SameSnapshot(removed, Snapshot(Path.GetDirectoryName(exe))), "Repeated uninstall is harmless");
        Check(Hash(exe) == SupportedHash, "Uninstall leaves official EXE byte-identical");
    }

    private static byte[] ConfigBytes()
    {
        string xml = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<!-- 原有配置，必须按字节恢复 -->\r\n<configuration>\r\n  <appSettings><add key=\"UserChoice\" value=\"保留\" /></appSettings>\r\n  <startup><supportedRuntime version=\"v4.0\" sku=\".NETFramework,Version=v4.6.2\"/></startup>\r\n  <runtime><AppContextSwitchOverrides value=\"Custom.Switch=true;Switch.System.IO.UseLegacyPathHandling=true;Switch.System.IO.BlockLongPaths=true;Switch.System.Net.DontEnableSchUseStrongCrypto=true;Switch.System.Net.DontEnableSystemDefaultTlsVersions=true\"/></runtime>\r\n</configuration>\r\n";
        return new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(xml)).ToArray();
    }
    private static void ExistingConfiguration()
    {
        string exe = NewFixture("original-config"); byte[] original = ConfigBytes(); File.WriteAllBytes(exe + ".config", original);
        var service = Service(); service.Install(exe); AssertConfig(exe + ".config");
        Check(BytesEqual(original, Backup(exe)), "Original config backup is byte-exact including BOM and CRLF");
        string merged = File.ReadAllText(exe + ".config");
        Check(merged.Contains("Custom.Switch=true") && merged.Contains("UserChoice") && merged.Contains("保留"), "Install preserves unrelated settings and switch");
        service.Uninstall(exe);
        Check(BytesEqual(original, exe + ".config"), "Native uninstall restores original config byte-for-byte");
    }
    private static void AssertConfig(string path)
    {
        var document = new XmlDocument { XmlResolver = null }; document.Load(path);
        Check(document.SelectSingleNode("/configuration/runtime/appDomainManagerType")?.Attributes["value"]?.Value == "PCL.PluginBootstrap.PluginDomainManager", "Config selects the application-local domain manager");
        string switches = document.SelectSingleNode("/configuration/runtime/AppContextSwitchOverrides")?.Attributes["value"]?.Value;
        Check(Switches.All(key => switches.Split(';').Contains(key + "=false")), "All four path and TLS compatibility switches are explicit secure values");
    }
    private static void UnsupportedExecutable()
    {
        string exe = NewFixture("unsupported"); using (var stream = File.Open(exe, FileMode.Append)) stream.WriteByte(1);
        var before = Snapshot(Path.GetDirectoryName(exe)); var service = Service();
        Check(!service.Inspect(exe).CanInstall, "Changed EXE is not installable");
        ExpectFailure(() => service.Install(exe), "Install rejects unsupported SHA-256");
        Check(SameSnapshot(before, Snapshot(Path.GetDirectoryName(exe))), "Unsupported EXE rejection writes nothing");
    }
    private static void ExistingBootstrap()
    {
        string exe = NewFixture("bootstrap-conflict"); File.WriteAllText(Bootstrap(exe), "unregistered user file");
        var before = Snapshot(Path.GetDirectoryName(exe));
        ExpectFailure(() => Service().Install(exe), "Unregistered bootstrap is not overwritten");
        Check(SameSnapshot(before, Snapshot(Path.GetDirectoryName(exe))), "Conflicting bootstrap remains intact");
    }
    private static void InvalidConfigurations()
    {
        var invalid = new Dictionary<string, string> {
            ["duplicate-runtime"] = "<configuration><runtime/><runtime/></configuration>",
            ["duplicate-switch-node"] = "<configuration><runtime><AppContextSwitchOverrides/><AppContextSwitchOverrides/></runtime></configuration>",
            ["duplicate-switch-key"] = "<configuration><runtime><AppContextSwitchOverrides value=\"Custom.Switch=true;custom.switch=false\"/></runtime></configuration>",
            ["invalid-switch"] = "<configuration><runtime><AppContextSwitchOverrides value=\"broken-entry\"/></runtime></configuration>",
            ["other-manager"] = "<configuration><runtime><appDomainManagerType value=\"Other.Manager\"/></runtime></configuration>",
            ["duplicate-startup"] = "<configuration><startup/><startup/></configuration>",
            ["dtd"] = "<!DOCTYPE configuration [<!ENTITY test 'unsafe'>]><configuration><appSettings>&test;</appSettings></configuration>",
            ["malformed"] = "<configuration><runtime>"
        };
        foreach (var fixture in invalid)
        {
            string exe = NewFixture(fixture.Key); File.WriteAllText(exe + ".config", fixture.Value);
            var before = Snapshot(Path.GetDirectoryName(exe));
            ExpectFailure(() => Service().Install(exe), "Rejects invalid/conflicting configuration: " + fixture.Key);
            Check(SameSnapshot(before, Snapshot(Path.GetDirectoryName(exe))), "Invalid configuration rejection preserves all bytes: " + fixture.Key);
        }
    }
    private static void MissingPayloadUninstall()
    {
        string exe = NewFixture("missing-payload"); var service = Service(); service.Install(exe);
        File.Delete(Bootstrap(exe)); File.Delete(Path.Combine(Plugin(exe), "PCL.DownloadRestore.dll"));
        Check(service.Inspect(exe).CanUninstall, "Missing DLLs still allow verified cleanup");
        Check(service.Uninstall(exe).Success && !File.Exists(exe + ".config") && !File.Exists(Receipt(exe)), "Missing bootstrap/plugin uninstall removes activation config safely");
    }
    private static void ChangedConfig()
    {
        string exe = NewFixture("changed-config"); var service = Service(); service.Install(exe);
        byte[] installed = File.ReadAllBytes(exe + ".config"); File.AppendAllText(exe + ".config", "\r\n<!-- user edit -->");
        var before = Snapshot(Path.GetDirectoryName(exe));
        Check(!service.Inspect(exe).CanUninstall, "Changed config disables automatic uninstall");
        ExpectFailure(() => service.Uninstall(exe), "Changed config prevents uninstall");
        Check(SameSnapshot(before, Snapshot(Path.GetDirectoryName(exe))), "Changed config and payload all preserved");
        File.WriteAllBytes(exe + ".config", installed); service.Uninstall(exe);
    }
    private static void ChangedPayloadAndBackup()
    {
        foreach (string target in new[] { "bootstrap", "plugin", "backup" })
        {
            string exe = NewFixture("changed-" + target); File.WriteAllBytes(exe + ".config", ConfigBytes()); var service = Service(); service.Install(exe);
            string path = target == "bootstrap" ? Bootstrap(exe) : target == "plugin" ? Path.Combine(Plugin(exe), "PCL.DownloadRestore.dll") : Backup(exe);
            byte[] original = File.ReadAllBytes(path); using (var stream = File.Open(path, FileMode.Append)) stream.WriteByte(1);
            var before = Snapshot(Path.GetDirectoryName(exe));
            ExpectFailure(() => service.Uninstall(exe), "Changed " + target + " prevents destructive cleanup");
            Check(SameSnapshot(before, Snapshot(Path.GetDirectoryName(exe))), "Changed " + target + " and all surrounding files preserved");
            File.WriteAllBytes(path, original); service.Uninstall(exe);
        }
    }
    private static void UpdatedLauncherCleanup()
    {
        string exe = NewFixture("updated-launcher"); var service = Service(); service.Install(exe);
        using (var stream = File.Open(exe, FileMode.Append)) stream.WriteByte(2);
        string changedHash = Hash(exe); var status = service.Inspect(exe);
        Check(!status.CanInstall && status.CanUninstall, "Updated EXE refuses injection but permits old plugin cleanup");
        Check(service.Uninstall(exe).Success && Hash(exe) == changedHash, "Uninstall does not overwrite updated executable");
    }
    private static void MissingReceipt()
    {
        string exe = NewFixture("missing-state"); var service = Service(); service.Install(exe); File.Delete(Receipt(exe));
        var before = Snapshot(Path.GetDirectoryName(exe));
        ExpectFailure(() => service.Uninstall(exe), "Missing receipt blocks unverified DLL removal");
        Check(SameSnapshot(before, Snapshot(Path.GetDirectoryName(exe))), "Missing receipt cleanup preserves files");
    }
    private static void InvalidReceiptPaths()
    {
        foreach (string badPath in new[] { "..\\..\\..\\user-data.txt", "0harmony.dll" })
        {
            string exe = NewFixture("invalid-state"); var service = Service(); service.Install(exe);
            string victim = Path.Combine(Path.GetDirectoryName(exe), "user-data.txt"); File.WriteAllText(victim, "user data stays");
            var receipt = ReadJson(Receipt(exe)); var files = ((object[])receipt["PluginFiles"]).ToList();
            files.Add(new Dictionary<string, object> { ["Path"] = badPath, ["Sha256"] = Hash(victim) }); receipt["PluginFiles"] = files.ToArray();
            File.WriteAllText(Receipt(exe), Json.Serialize(receipt), new UTF8Encoding(false));
            var before = Snapshot(Path.GetDirectoryName(exe));
            ExpectFailure(() => service.Uninstall(exe), "Invalid manifest path rejected: " + badPath);
            Check(SameSnapshot(before, Snapshot(Path.GetDirectoryName(exe))), "Invalid manifest never deletes outside or duplicate files: " + badPath);
        }
    }
    private static void JunctionRejected()
    {
        string exe = NewFixture("junction"); string target = Path.Combine(runDirectory, "junction-target"); Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "keep.txt"), "must remain"); string link = Path.Combine(Path.GetDirectoryName(exe), "PCL");
        var command = Child(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"), "/d /c mklink /J " + Quote(link) + " " + Quote(target), Path.GetDirectoryName(exe));
        Check(command.ExitCode == 0 && (File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0, "Junction test fixture created");
        var before = Snapshot(target);
        ExpectFailure(() => Service().Install(exe), "Install refuses directory junction ancestor");
        Check(SameSnapshot(before, Snapshot(target)) && !File.Exists(Bootstrap(exe)), "Rejected junction target remains untouched");
    }
    private static void InstallRollback()
    {
        string exe = NewFixture("install-rollback"); File.WriteAllBytes(exe + ".config", ConfigBytes()); var before = Snapshot(Path.GetDirectoryName(exe));
        using (var locked = new FileStream(exe + ".config", FileMode.Open, FileAccess.Read, FileShare.Read))
            ExpectFailure(() => Service().Install(exe), "Locked config forces installation failure");
        Check(SameSnapshot(before, Snapshot(Path.GetDirectoryName(exe))), "Installation failure rolls back payload, receipt, backup and config");
        Check(!Directory.GetDirectories(Path.GetDirectoryName(exe), ".pcl-downloadrestore-*").Any(), "Installation rollback leaves no staging backups behind");
    }
    private static void UninstallRollback()
    {
        string exe = NewFixture("uninstall-rollback"); File.WriteAllBytes(exe + ".config", ConfigBytes()); var service = Service(); service.Install(exe);
        var before = Snapshot(Path.GetDirectoryName(exe));
        using (var locked = new FileStream(Path.Combine(Plugin(exe), "0Harmony.dll"), FileMode.Open, FileAccess.Read, FileShare.Read))
            ExpectFailure(() => service.Uninstall(exe), "Locked payload forces late uninstallation failure");
        Check(SameSnapshot(before, Snapshot(Path.GetDirectoryName(exe))), "Uninstall failure restores activation config and all moved files");
        Check(service.Inspect(exe).CanUninstall, "Rolled-back installation remains valid for retry");
        service.Uninstall(exe);
    }
    private static void PowerShellToNative()
    {
        string exe = NewFixture("ps-to-native"); byte[] original = ConfigBytes(); File.WriteAllBytes(exe + ".config", original);
        string arguments = "-NoProfile -ExecutionPolicy Bypass -File " + Quote(Path.Combine(root, "scripts", "Install.ps1")) + " -LauncherPath " + Quote(exe) + " -PayloadPath " + Quote(Path.Combine(root, "artifacts", "payload"));
        var result = Child(PowerShell(), arguments, root);
        Check(result.ExitCode == 0, "PowerShell schema 1 installation succeeds: " + result.Output);
        var service = Service(); Check(service.Inspect(exe).CanUninstall, "Native service recognizes PowerShell receipt");
        Check(service.Uninstall(exe).Success && BytesEqual(original, exe + ".config"), "Native service exactly restores PowerShell installation backup");
    }
    private static void NativeToPowerShell()
    {
        string exe = NewFixture("native-to-ps"); byte[] original = ConfigBytes(); File.WriteAllBytes(exe + ".config", original); Service().Install(exe);
        var result = Child(PowerShell(), "-NoProfile -ExecutionPolicy Bypass -File " + Quote(Path.Combine(root, "scripts", "Uninstall.ps1")) + " -LauncherPath " + Quote(exe), root);
        Check(result.ExitCode == 0, "PowerShell understands native schema 1 receipt: " + result.Output);
        Check(BytesEqual(original, exe + ".config") && !File.Exists(Bootstrap(exe)), "PowerShell exactly restores native installation backup");
    }
    private static void StandaloneCli()
    {
        string exe = NewFixture("standalone-cli"); string isolated = Path.Combine(runDirectory, "standalone-tool"); Directory.CreateDirectory(isolated);
        string injector = Path.Combine(isolated, "PCL.DownloadRestore.Injector.exe");
        File.Copy(typeof(EmbeddedPayload).Assembly.Location, injector);
        string emptyPath = Path.Combine(isolated, "empty-path"); Directory.CreateDirectory(emptyPath);
        foreach (string action in new[] { "inspect", "install", "inspect", "uninstall", "uninstall" })
        {
            string report = Path.Combine(isolated, action + "-" + Guid.NewGuid().ToString("N") + ".json");
            var result = Child(injector, "--" + action + " " + Quote(exe) + " --report " + Quote(report), isolated, emptyPath);
            Check(result.ExitCode == 0 && File.Exists(report), "Standalone fresh-process --" + action + " succeeds without PowerShell on PATH: " + result.Output);
            var json = ReadJson(report);
            Check(Convert.ToBoolean(json["Success"]) && (string)json["LauncherSha256"] == SupportedHash, "CLI --" + action + " reports success and unchanged official SHA-256");
        }
        Check(Directory.GetFiles(isolated, "*.dll").Length == 0, "Standalone CLI requires no companion DLLs or payload directory");
        Check(!File.Exists(exe + ".config") && Hash(exe) == SupportedHash, "Standalone CLI final uninstall restores original host state");
        string existingReport = Path.Combine(isolated, "existing-report.json"); File.WriteAllText(existingReport, "preserve existing report");
        var existingSnapshot = Snapshot(Path.GetDirectoryName(exe));
        var existingResult = Child(injector, "--install " + Quote(exe) + " --report " + Quote(existingReport), isolated, emptyPath);
        Check(existingResult.ExitCode != 0 && File.ReadAllText(existingReport) == "preserve existing report", "CLI refuses to overwrite an existing report");
        Check(SameSnapshot(existingSnapshot, Snapshot(Path.GetDirectoryName(exe))), "Existing report rejection happens before installation mutation");
        Directory.CreateDirectory(Plugin(exe));
        var reservedSnapshot = Snapshot(Path.GetDirectoryName(exe));
        var reservedResult = Child(injector, "--install " + Quote(exe) + " --report " + Quote(Receipt(exe)), isolated, emptyPath);
        Check(reservedResult.ExitCode != 0 && !File.Exists(Receipt(exe)), "CLI rejects reserved receipt report path before creating it");
        Check(SameSnapshot(reservedSnapshot, Snapshot(Path.GetDirectoryName(exe))), "Reserved report path cannot poison installation metadata");
        using (var stream = File.Open(exe, FileMode.Append)) stream.WriteByte(1);
        string rejectedReport = Path.Combine(isolated, "unsupported.json");
        var rejected = Child(injector, "--install " + Quote(exe) + " --report " + Quote(rejectedReport), isolated, emptyPath);
        Check(rejected.ExitCode != 0 && File.Exists(rejectedReport) && !Convert.ToBoolean(ReadJson(rejectedReport)["Success"]), "Standalone CLI rejects unsupported EXE with machine-readable error and no dialog");
    }
    private static string PowerShell() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";
    private static ChildResult Child(string file, string arguments, string workingDirectory, string path = null)
    {
        var start = new ProcessStartInfo(file, arguments) { WorkingDirectory = workingDirectory, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        if (path != null) start.EnvironmentVariables["PATH"] = path;
        if (Path.GetFileName(file).Equals("powershell.exe", StringComparison.OrdinalIgnoreCase))
        {
            // A parent PowerShell 7 session exports its own incompatible module paths.
            // These interoperability checks run the real, unmodified Windows PS 5.1 scripts.
            start.EnvironmentVariables["PSModulePath"] = Path.Combine(Path.GetDirectoryName(file), "Modules") + ";" +
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsPowerShell", "Modules");
        }
        using (var process = Process.Start(start))
        {
            Task<string> output = process.StandardOutput.ReadToEndAsync(); Task<string> error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(120000)) { process.Kill(); throw new TimeoutException("Test child process timed out: " + file); }
            Task.WaitAll(output, error);
            return new ChildResult { ExitCode = process.ExitCode, Output = output.Result + error.Result };
        }
    }
    private sealed class ChildResult { public int ExitCode; public string Output; }
    private static Dictionary<string, object> ReadJson(string path) => (Dictionary<string, object>)Json.DeserializeObject(File.ReadAllText(path));
    private static Dictionary<string, string> Snapshot(string directory)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); var pending = new Stack<string>(); pending.Push(directory);
        while (pending.Count > 0)
        {
            foreach (string path in Directory.EnumerateFileSystemEntries(pending.Pop()))
            {
                string relative = path.Substring(directory.Length + 1); var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0) { result[relative] = "REPARSE_POINT"; continue; }
                if ((attributes & FileAttributes.Directory) != 0) pending.Push(path); else result[relative] = Hash(path);
            }
        }
        return result;
    }
    private static bool SameSnapshot(Dictionary<string, string> left, Dictionary<string, string> right) => left.Count == right.Count && left.All(item => right.TryGetValue(item.Key, out var value) && value == item.Value);
}
