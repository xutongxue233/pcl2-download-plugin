using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
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
    private static string originalHost, runDirectory, legacyReleaseDirectory;
    private static int caseNumber;

    private static int Main(string[] args)
    {
        originalHost = Path.GetFullPath(args[1]);
        legacyReleaseDirectory = args.Length > 3 && !string.IsNullOrWhiteSpace(args[3]) ? Path.GetFullPath(args[3]) : null;
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
            Run("different executable checksum permits install and exact uninstall", DifferentExecutableChecksum);
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
            Run("historical schema 1 receipt remains compatible with native service", LegacySchemaOneReceipt);
            Run("fresh standalone EXE CLI without PowerShell on PATH", StandaloneCli);
            Run("schema 2 upgrade preserves first configuration and disabled state", UpgradeLifecycle);
            Run("same payload upgrade is a byte and timestamp no-op", UpgradeNoOp);
            Run("downgrade, different build and unknown legacy upgrades are blocked", UpgradeVersionProtection);
            Run("damaged registered installations cannot be upgraded", UpgradeIntegrityProtection);
            Run("upgrade failures restore DLLs receipt configuration and disabled marker", UpgradeRollback);
            Run("standalone upgrade CLI reports installed and available versions", UpgradeCli);
            if (legacyReleaseDirectory != null)
            {
                Run("published v1.1.2 schema 1 payload upgrades in place", () => PublishedLegacyUpgrade("1.1.2"));
                Run("published v1.1.3 schema 1 payload upgrades in place", () => PublishedLegacyUpgrade("1.1.3"));
            }
        }
        catch (Exception exception) { Cases.Add(new { Name = "harness setup", Passed = false, Error = exception.ToString() }); }

        bool allPassed = Cases.All(item => (bool)item.GetType().GetProperty("Passed").GetValue(item));
        string report = Json.Serialize(new {
            CheckedAt = DateTimeOffset.Now.ToString("o"), AllPassed = allPassed,
            Cases = Cases, Assertions = Assertions, AssertionCount = Assertions.Count,
            FixtureDirectory = runDirectory, OriginalHostSha256 = Hash(originalHost),
            LegacyReleaseDirectory = legacyReleaseDirectory,
            PublishedLegacyCasesRequested = legacyReleaseDirectory != null
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
        foreach (string ownAssembly in new[] { Bootstrap(exe), Path.Combine(Plugin(exe), "PCL.DownloadRestore.dll") })
            Check(AssemblyName.GetAssemblyName(ownAssembly).Version == new Version(1, 0, 0, 0) &&
                FileVersionInfo.GetVersionInfo(ownAssembly).FileVersion == EmbeddedPayload.PackageVersion + ".0",
                "Own DLL keeps stable CLR binding identity and reports the current file version: " + Path.GetFileName(ownAssembly));
        AssertConfig(exe + ".config");
        var state = ReadJson(Receipt(exe));
        Check(Convert.ToInt32(state["Schema"]) == 2 && (string)state["LauncherPath"] == exe, "Native receipt uses versioned schema 2 and exact launcher path");
        Check((string)state["PackageVersion"] == EmbeddedPayload.PackageVersion &&
            (!state.ContainsKey("UpdatedUtc") || state["UpdatedUtc"] == null), "Fresh receipt records the current package version without inventing an upgrade time");
        Check(installedState.InstalledVersion == EmbeddedPayload.PackageVersion && installedState.AvailableVersion == EmbeddedPayload.PackageVersion &&
            !installedState.CanUpgrade && !installedState.IsDisabled, "Fresh inspection reports exact installed and available versions");
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
    private static void DifferentExecutableChecksum()
    {
        string exe = NewFixture("different-checksum"); using (var stream = File.Open(exe, FileMode.Append)) stream.WriteByte(1);
        var before = Snapshot(Path.GetDirectoryName(exe)); var service = Service();
        string changedHash = Hash(exe);
        Check(changedHash != SupportedHash && service.Inspect(exe).CanInstall, "Different EXE checksum is installable without a fixed allowlist");
        Check(service.Install(exe).Success && Hash(exe) == changedHash, "Install accepts a different checksum without changing the executable");
        Check((string)ReadJson(Receipt(exe))["LauncherSha256AtInstall"] == changedHash, "Receipt records the actual selected executable checksum");
        Check(service.Inspect(exe).Status == "Installed" && !service.Inspect(exe).CanInstall, "Different checksum still supports installed-state detection");
        Check(service.Uninstall(exe).Success && SameSnapshot(before, Snapshot(Path.GetDirectoryName(exe))), "Uninstall exactly restores the different-checksum fixture");
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
        Check(status.Status == "Installed" && !status.CanInstall && status.CanUninstall, "Updated EXE keeps its existing load entry without requesting duplicate injection");
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
    private static void LegacySchemaOneReceipt()
    {
        string exe = NewFixture("legacy-schema-one");
        string folder = Path.GetDirectoryName(exe);
        byte[] original = ConfigBytes();

        // 独立重现已退役脚本的 schema 1 安装记录，不调用当前安装器生成测试样本。
        foreach (var item in payload)
        {
            string destination = Path.Combine(folder, item.Key);
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            File.WriteAllBytes(destination, item.Value);
        }
        File.WriteAllBytes(Backup(exe), original);
        string installedConfig = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
            "<configuration>\r\n" +
            "  <appSettings><add key=\"UserChoice\" value=\"保留\" /></appSettings>\r\n" +
            "  <startup><supportedRuntime version=\"v4.0\" sku=\".NETFramework,Version=v4.6.2\" /></startup>\r\n" +
            "  <runtime>\r\n" +
            "    <AppContextSwitchOverrides value=\"Custom.Switch=true;Switch.System.IO.UseLegacyPathHandling=false;Switch.System.IO.BlockLongPaths=false;Switch.System.Net.DontEnableSchUseStrongCrypto=false;Switch.System.Net.DontEnableSystemDefaultTlsVersions=false\" />\r\n" +
            "    <appDomainManagerAssembly value=\"PCL.PluginBootstrap, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null\" />\r\n" +
            "    <appDomainManagerType value=\"PCL.PluginBootstrap.PluginDomainManager\" />\r\n" +
            "  </runtime>\r\n</configuration>\r\n";
        File.WriteAllText(exe + ".config", installedConfig, new UTF8Encoding(false));
        var receipt = new
        {
            Schema = 1,
            LauncherPath = exe,
            LauncherSha256AtInstall = Hash(exe),
            InstalledUtc = "2026-10-20T00:00:00.0000000Z",
            OriginalConfigExisted = true,
            OriginalConfigSha256 = Hash(Backup(exe)),
            InstalledConfigSha256 = Hash(exe + ".config"),
            BootstrapSha256 = Hash(Bootstrap(exe)),
            PluginFiles = new[]
            {
                new { Path = "PCL.DownloadRestore.dll", Sha256 = Hash(Path.Combine(Plugin(exe), "PCL.DownloadRestore.dll")) },
                new { Path = "0Harmony.dll", Sha256 = Hash(Path.Combine(Plugin(exe), "0Harmony.dll")) }
            }
        };
        File.WriteAllText(Receipt(exe), Json.Serialize(receipt), new UTF8Encoding(false));
        string diagnostic = Path.Combine(Plugin(exe), "bootstrap.log");
        string userFile = Path.Combine(folder, "用户资料.txt");
        File.WriteAllText(diagnostic, "keep historical diagnostics");
        File.WriteAllText(userFile, "keep unrelated user data");
        var installed = Snapshot(folder);

        var service = Service();
        var status = service.Inspect(exe);
        Check(status.Status == "Installed" && !status.CanInstall && status.CanUninstall,
            "Native service recognizes independently constructed historical schema 1 receipt");
        Check(service.Install(exe).Success, "Historical schema 1 installation remains idempotent");
        Check(SameSnapshot(installed, Snapshot(folder)), "Inspecting and reinstalling historical schema 1 changes no file bytes");
        Check(service.Uninstall(exe).Success, "Native service uninstalls historical schema 1 installation");
        Check(BytesEqual(original, exe + ".config"), "Historical schema 1 backup restores exact original bytes including BOM and CRLF");
        Check(!File.Exists(Bootstrap(exe)) && !File.Exists(Receipt(exe)) && !File.Exists(Backup(exe)) &&
            !File.Exists(Path.Combine(Plugin(exe), "PCL.DownloadRestore.dll")) && !File.Exists(Path.Combine(Plugin(exe), "0Harmony.dll")),
            "Historical schema 1 uninstall removes registered binaries, receipt and backup");
        Check(Hash(exe) == SupportedHash, "Historical schema 1 uninstall leaves the official EXE byte-identical");
        Check(File.ReadAllText(diagnostic) == "keep historical diagnostics" && File.ReadAllText(userFile) == "keep unrelated user data",
            "Historical schema 1 uninstall preserves diagnostics and unrelated user files");
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
        string changedHash = Hash(exe);
        foreach (string action in new[] { "install", "inspect", "uninstall" })
        {
            string report = Path.Combine(isolated, "different-checksum-" + action + ".json");
            var result = Child(injector, "--" + action + " " + Quote(exe) + " --report " + Quote(report), isolated, emptyPath);
            Check(result.ExitCode == 0 && File.Exists(report) && Convert.ToBoolean(ReadJson(report)["Success"]) && Hash(exe) == changedHash,
                "Standalone CLI --" + action + " accepts an unknown checksum and preserves the executable");
        }
        string rejectedReport = Path.Combine(isolated, "missing-executable.json");
        var rejected = Child(injector, "--install " + Quote(Path.Combine(isolated, "missing.exe")) + " --report " + Quote(rejectedReport), isolated, emptyPath);
        Check(rejected.ExitCode != 0 && File.Exists(rejectedReport) && !Convert.ToBoolean(ReadJson(rejectedReport)["Success"]), "Standalone CLI still rejects a missing EXE with machine-readable error and no dialog");
    }
    private const string HistoricalInstalledUtc = "2026-10-20T00:00:00.0000000Z";
    private static readonly byte[] DisabledContents = Encoding.UTF8.GetBytes("保留用户的禁用标记\r\n");

    private static IDictionary<string, byte[]> SyntheticOlderPayload()
    {
        // 只在隔离安装事务样本内附加字节；这些 DLL 从不作为程序执行。
        byte[] marker = Encoding.ASCII.GetBytes("\r\nINDEPENDENT_OLDER_TRANSACTION_FIXTURE\r\n");
        return payload.ToDictionary(item => item.Key, item => item.Value.Concat(marker).ToArray(), StringComparer.OrdinalIgnoreCase);
    }

    private static string RegisteredFixture(string name, IDictionary<string, byte[]> files, int schema,
        string version, bool originalExisted = true, bool disabled = false)
    {
        // 独立构造旧安装记录，避免用被测 Install 实现生成升级前提。
        string exe = NewFixture(name);
        foreach (var item in files)
        {
            string destination = Path.Combine(Path.GetDirectoryName(exe), item.Key);
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            File.WriteAllBytes(destination, item.Value);
        }
        byte[] original = ConfigBytes();
        if (originalExisted) File.WriteAllBytes(Backup(exe), original);
        var document = new XmlDocument { PreserveWhitespace = true, XmlResolver = null };
        using (var input = new MemoryStream(original)) document.Load(input);
        XmlNode runtime = document.SelectSingleNode("/configuration/runtime");
        runtime.SelectSingleNode("AppContextSwitchOverrides").Attributes["value"].Value =
            "Custom.Switch=true;" + string.Join(";", Switches.Select(key => key + "=false"));
        foreach (var manager in new[] {
            new { Name = "appDomainManagerAssembly", Value = "PCL.PluginBootstrap, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null" },
            new { Name = "appDomainManagerType", Value = "PCL.PluginBootstrap.PluginDomainManager" }
        })
        {
            XmlElement element = document.CreateElement(manager.Name);
            element.SetAttribute("value", manager.Value);
            runtime.AppendChild(element);
        }
        document.Save(exe + ".config");
        var receipt = new Dictionary<string, object> {
            ["Schema"] = schema,
            ["LauncherPath"] = exe,
            ["LauncherSha256AtInstall"] = Hash(exe),
            ["InstalledUtc"] = HistoricalInstalledUtc,
            ["OriginalConfigExisted"] = originalExisted,
            ["OriginalConfigSha256"] = originalExisted ? Hash(Backup(exe)) : null,
            ["InstalledConfigSha256"] = Hash(exe + ".config"),
            ["BootstrapSha256"] = Hash(Bootstrap(exe)),
            ["PluginFiles"] = new[] {
                new { Path = "PCL.DownloadRestore.dll", Sha256 = Hash(Path.Combine(Plugin(exe), "PCL.DownloadRestore.dll")) },
                new { Path = "0Harmony.dll", Sha256 = Hash(Path.Combine(Plugin(exe), "0Harmony.dll")) }
            }
        };
        if (schema == 2) { receipt["PackageVersion"] = version; receipt["UpdatedUtc"] = null; }
        File.WriteAllText(Receipt(exe), Json.Serialize(receipt), new UTF8Encoding(false));
        if (disabled) File.WriteAllBytes(Path.Combine(Plugin(exe), "disabled"), DisabledContents);
        File.WriteAllText(Path.Combine(Plugin(exe), "bootstrap.log"), "保留旧运行日志\r\n", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(exe), "用户资料.txt"), "升级不应触碰", new UTF8Encoding(false));
        return exe;
    }

    private static void UpgradeLifecycle()
    {
        foreach (bool originalExisted in new[] { false, true })
        foreach (bool disabled in new[] { false, true })
        {
            string exe = RegisteredFixture("upgrade-" + originalExisted + "-" + disabled,
                SyntheticOlderPayload(), 2, "1.1.3", originalExisted, disabled);
            VerifyUpgradeLifecycle(exe, "1.1.3", originalExisted, disabled);
        }
    }

    private static void VerifyUpgradeLifecycle(string exe, string expectedVersion, bool originalExisted, bool disabled)
    {
        var service = Service();
        var beforeState = ReadJson(Receipt(exe));
        byte[] installedConfig = File.ReadAllBytes(exe + ".config");
        string marker = Path.Combine(Plugin(exe), "disabled");
        var before = Snapshot(Path.GetDirectoryName(exe));
        var status = service.Inspect(exe);
        Check(status.Status == "UpgradeAvailable" && status.CanUpgrade && !status.CanInstall && status.CanUninstall,
            "An independently registered older package exposes only upgrade and uninstall");
        Check(status.InstalledVersion == expectedVersion && status.AvailableVersion == EmbeddedPayload.PackageVersion && status.IsDisabled == disabled,
            "Upgrade inspection reports both versions and the existing disabled state");
        Check(SameSnapshot(before, Snapshot(Path.GetDirectoryName(exe))), "Version inspection does not rewrite historical files or receipt");
        Check(service.Upgrade(exe).Success, "Older package upgrades directly without uninstalling");
        Check(BytesEqual(installedConfig, exe + ".config"), "Upgrade restores the exact installed configuration including unrelated settings");
        Check(originalExisted ? BytesEqual(ConfigBytes(), Backup(exe)) : !File.Exists(Backup(exe)),
            "Upgrade preserves the first original configuration backup and its absence state");
        Check(disabled ? BytesEqual(DisabledContents, marker) : !File.Exists(marker), "Upgrade preserves disabled marker bytes and original existence");
        foreach (var item in payload)
            Check(BytesEqual(item.Value, Path.Combine(Path.GetDirectoryName(exe), item.Key)), "Upgrade replaces payload with this distribution: " + item.Key);
        var updated = ReadJson(Receipt(exe));
        Check(Convert.ToInt32(updated["Schema"]) == 2 && (string)updated["PackageVersion"] == EmbeddedPayload.PackageVersion,
            "Upgrade commits schema 2 with the current package version");
        DateTimeOffset updatedTime;
        Check((string)updated["InstalledUtc"] == HistoricalInstalledUtc && updated.ContainsKey("UpdatedUtc") &&
            DateTimeOffset.TryParse(updated["UpdatedUtc"] as string, out updatedTime), "Upgrade retains first installation time and records its own update time");
        Check(Equals(updated["OriginalConfigExisted"], beforeState["OriginalConfigExisted"]) &&
            Equals(updated["OriginalConfigSha256"], beforeState["OriginalConfigSha256"]), "Upgrade retains the first original configuration metadata");
        var current = service.Inspect(exe);
        Check(current.InstalledVersion == EmbeddedPayload.PackageVersion && !current.CanUpgrade && current.IsDisabled == disabled,
            "A completed upgrade is recognized as current without changing enablement");
        Check(Hash(exe) == SupportedHash && File.ReadAllText(Path.Combine(Plugin(exe), "bootstrap.log")) == "保留旧运行日志\r\n" &&
            File.ReadAllText(Path.Combine(Path.GetDirectoryName(exe), "用户资料.txt")) == "升级不应触碰", "Upgrade preserves the launcher, diagnostic log and user files");
        Check(!Directory.GetDirectories(Path.GetDirectoryName(exe), ".pcl-downloadrestore-*").Any(), "Successful upgrade removes temporary recovery data");
        Check(service.Uninstall(exe).Success, "An upgraded installation can be uninstalled normally");
        Check(originalExisted ? BytesEqual(ConfigBytes(), exe + ".config") : !File.Exists(exe + ".config"),
            "Uninstall after upgrade restores the first pre-installation configuration exactly");
        Check(Hash(exe) == SupportedHash && !File.Exists(Bootstrap(exe)) && !File.Exists(Receipt(exe)) && !File.Exists(Backup(exe)),
            "Uninstall after upgrade removes owned files while preserving the original launcher");
    }

    private static void UpgradeNoOp()
    {
        foreach (bool disabled in new[] { false, true })
        {
            string exe = RegisteredFixture("upgrade-current-" + disabled, payload, 2, EmbeddedPayload.PackageVersion, true, disabled);
            var before = Snapshot(Path.GetDirectoryName(exe));
            var dates = FileWriteTimes(Path.GetDirectoryName(exe));
            Check(Service().Upgrade(exe).Success, "Upgrading the same package succeeds idempotently");
            Check(SameSnapshot(before, Snapshot(Path.GetDirectoryName(exe))) && SameWriteTimes(dates, FileWriteTimes(Path.GetDirectoryName(exe))),
                "Same-package upgrade writes no file bytes or modification timestamps, including disabled state");
        }
        string legacy = RegisteredFixture("upgrade-current-schema-one", payload, 1, null);
        var legacyBefore = Snapshot(Path.GetDirectoryName(legacy));
        Check(Service().Inspect(legacy).Status == "Installed" && !Service().Inspect(legacy).CanUpgrade,
            "Schema 1 matching the exact current payload is already current");
        Check(Service().Upgrade(legacy).Success && SameSnapshot(legacyBefore, Snapshot(Path.GetDirectoryName(legacy))),
            "Current schema 1 payload remains a no-op without rewriting old metadata");
    }

    private static void UpgradeVersionProtection()
    {
        foreach (var item in new[] {
            new { Name = "future-version", Schema = 2, Version = "9.0.0", Status = "NewerVersion", Current = false },
            new { Name = "future-current-bytes", Schema = 2, Version = "9.0.0", Status = "NewerVersion", Current = true },
            new { Name = "different-build", Schema = 2, Version = EmbeddedPayload.PackageVersion, Status = "DifferentBuild", Current = false },
            new { Name = "unknown-legacy", Schema = 1, Version = (string)null, Status = "UnknownVersion", Current = false }
        })
        {
            string exe = RegisteredFixture(item.Name, item.Current ? payload : SyntheticOlderPayload(), item.Schema, item.Version);
            var before = Snapshot(Path.GetDirectoryName(exe));
            var status = Service().Inspect(exe);
            Check(status.Status == item.Status && !status.CanUpgrade && !status.CanInstall && status.CanUninstall,
                "Upgrade eligibility protects " + item.Name);
            ExpectFailure(() => Service().Upgrade(exe), "Automatic upgrade refuses " + item.Name);
            Check(SameSnapshot(before, Snapshot(Path.GetDirectoryName(exe))), "Rejected upgrade preserves every file for " + item.Name);
        }
        string old = RegisteredFixture("install-does-not-upgrade", SyntheticOlderPayload(), 2, "1.1.3");
        var oldBefore = Snapshot(Path.GetDirectoryName(old));
        try { Service().Install(old); } catch (InvalidOperationException) { }
        Check(SameSnapshot(oldBefore, Snapshot(Path.GetDirectoryName(old))) && Service().Inspect(old).CanUpgrade,
            "Install never silently performs a version upgrade");
    }

    private static void UpgradeIntegrityProtection()
    {
        foreach (string problem in new[] { "missing-plugin", "modified-plugin", "modified-config", "modified-backup", "malformed-version" })
        {
            string exe = RegisteredFixture("upgrade-damaged-" + problem, SyntheticOlderPayload(), 2, "1.1.3");
            string plugin = Path.Combine(Plugin(exe), "PCL.DownloadRestore.dll");
            if (problem == "missing-plugin") File.Delete(plugin);
            else if (problem == "malformed-version")
            {
                var receipt = ReadJson(Receipt(exe)); receipt["PackageVersion"] = "not-a-version";
                File.WriteAllText(Receipt(exe), Json.Serialize(receipt), new UTF8Encoding(false));
            }
            else
            {
                string target = problem == "modified-plugin" ? plugin : problem == "modified-config" ? exe + ".config" : Backup(exe);
                using (var stream = File.Open(target, FileMode.Append)) stream.WriteByte(1);
            }
            var before = Snapshot(Path.GetDirectoryName(exe));
            Check(!Service().Inspect(exe).CanUpgrade, "Damaged installation is not offered an upgrade: " + problem);
            ExpectFailure(() => Service().Upgrade(exe), "Upgrade refuses damaged installation: " + problem);
            Check(SameSnapshot(before, Snapshot(Path.GetDirectoryName(exe))), "Damaged installation remains untouched: " + problem);
        }
    }

    private static void UpgradeRollback()
    {
        foreach (string targetName in new[] { "bootstrap", "plugin", "harmony", "receipt", "config" })
        {
            string exe = RegisteredFixture("upgrade-rollback-" + targetName, SyntheticOlderPayload(), 2, "1.1.3", true, true);
            string target = targetName == "bootstrap" ? Bootstrap(exe) : targetName == "receipt" ? Receipt(exe) :
                targetName == "config" ? exe + ".config" : Path.Combine(Plugin(exe), targetName == "plugin" ? "PCL.DownloadRestore.dll" : "0Harmony.dll");
            var before = Snapshot(Path.GetDirectoryName(exe));
            using (var locked = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read))
                ExpectFailure(() => Service().Upgrade(exe), "Locked " + targetName + " forces an upgrade failure");
            Check(SameSnapshot(before, Snapshot(Path.GetDirectoryName(exe))),
                "Failed upgrade restores all old DLLs receipt config backup and disabled bytes: " + targetName);
            Check(!Directory.GetDirectories(Path.GetDirectoryName(exe), ".pcl-downloadrestore-*").Any(),
                "Successful rollback removes temporary data after failure at " + targetName);
            Check(Service().Inspect(exe).CanUpgrade && Service().Inspect(exe).IsDisabled && Hash(exe) == SupportedHash,
                "Rolled-back installation retains upgrade eligibility disabled state and unchanged EXE: " + targetName);
            Check(Service().Upgrade(exe).Success && Service().Uninstall(exe).Success && BytesEqual(ConfigBytes(), exe + ".config"),
                "Upgrade can be retried and uninstalled after releasing the locked " + targetName);
        }
    }

    private static void UpgradeCli()
    {
        string exe = RegisteredFixture("upgrade-cli", SyntheticOlderPayload(), 2, "1.1.3", true, true);
        string isolated = Path.Combine(runDirectory, "standalone-upgrade-tool"); Directory.CreateDirectory(isolated);
        string injector = Path.Combine(isolated, "PCL.DownloadRestore.Injector.exe");
        File.Copy(typeof(EmbeddedPayload).Assembly.Location, injector);
        string emptyPath = Path.Combine(isolated, "empty-path"); Directory.CreateDirectory(emptyPath);
        string beforeReport = Path.Combine(isolated, "before.json");
        var inspect = Child(injector, "--inspect " + Quote(exe) + " --report " + Quote(beforeReport), isolated, emptyPath);
        var before = ReadJson(beforeReport);
        Check(inspect.ExitCode == 0 && (string)before["InstalledVersion"] == "1.1.3" &&
            (string)before["AvailableVersion"] == EmbeddedPayload.PackageVersion && Convert.ToBoolean(before["CanUpgrade"]) && Convert.ToBoolean(before["IsDisabled"]),
            "Standalone inspect JSON exposes upgrade versions eligibility and disabled state");
        string report = Path.Combine(isolated, "upgrade.json");
        var result = Child(injector, "--upgrade " + Quote(exe) + " --report " + Quote(report), isolated, emptyPath);
        var after = ReadJson(report);
        Check(result.ExitCode == 0 && (string)after["Action"] == "--upgrade" && Convert.ToBoolean(after["Success"]),
            "Standalone --upgrade succeeds without PowerShell or companion DLLs");
        Check((string)after["InstalledVersion"] == EmbeddedPayload.PackageVersion &&
            (string)after["AvailableVersion"] == EmbeddedPayload.PackageVersion && !Convert.ToBoolean(after["CanUpgrade"]) && Convert.ToBoolean(after["IsDisabled"]),
            "Standalone upgrade JSON reports current installed version and preserves disabled state");
        Check((string)after["LauncherSha256"] == SupportedHash && BytesEqual(DisabledContents, Path.Combine(Plugin(exe), "disabled")),
            "Standalone upgrade preserves the official EXE and disabled marker bytes");
        var unchanged = Snapshot(Path.GetDirectoryName(exe));
        string repeatReport = Path.Combine(isolated, "repeat.json");
        var repeat = Child(injector, "--upgrade " + Quote(exe) + " --report " + Quote(repeatReport), isolated, emptyPath);
        Check(repeat.ExitCode == 0 && Convert.ToBoolean(ReadJson(repeatReport)["Success"]) && SameSnapshot(unchanged, Snapshot(Path.GetDirectoryName(exe))),
            "Repeated standalone --upgrade is a no-op");
        Check(Directory.GetFiles(isolated, "*.dll").Length == 0, "Standalone upgrade needs no companion DLLs");
        string future = RegisteredFixture("upgrade-cli-newer", SyntheticOlderPayload(), 2, "9.0.0");
        var futureBefore = Snapshot(Path.GetDirectoryName(future));
        string rejectedReport = Path.Combine(isolated, "rejected.json");
        var rejected = Child(injector, "--upgrade " + Quote(future) + " --report " + Quote(rejectedReport), isolated, emptyPath);
        Check(rejected.ExitCode != 0 && !Convert.ToBoolean(ReadJson(rejectedReport)["Success"]) &&
            SameSnapshot(futureBefore, Snapshot(Path.GetDirectoryName(future))), "Standalone --upgrade rejects downgrade with a machine-readable failure and no changes");
    }

    private static void PublishedLegacyUpgrade(string version)
    {
        // 只有显式提供本地发行目录才读取这些样本；固定校验值对应已发布的原附件。
        bool first = version == "1.1.2";
        string packageName = first ? "PCL.DownloadRestore.Injector-v1.1.2-PCL2.99.0.0.zip" : "PCL.DownloadRestore.Injector-v1.1.3.zip";
        string packageHash = first ? "211F7D7722F3086249EF6CE71042EAB6701AF1B270B59E0B23EAFB7C4B27E5A5" : "6C0E3480A9D43ED2ED4792A7930BA9925326866F0E502696A9E68D775EA5A4B4";
        string injectorHash = first ? "DB275799F330E157BB5F8B5F0758AC421A102C761F53D84A89BB6DAC9D3E3558" : "54152B97AAFAF02290890616A7B239052E7464AFAFD80ED87661551D0A7CAD1D";
        string archivePath = Path.Combine(legacyReleaseDirectory, "v" + version, packageName);
        Check(File.Exists(archivePath) && Hash(archivePath) == packageHash, "Published legacy ZIP has the exact pinned checksum: " + version);
        byte[] executable;
        using (var input = File.OpenRead(archivePath))
        using (var archive = new ZipArchive(input, ZipArchiveMode.Read))
        {
            var entry = archive.Entries.Single(item => item.FullName.Replace('\\', '/').Split('/').Last() == "PCL.DownloadRestore.Injector.exe");
            using (var stream = entry.Open()) using (var output = new MemoryStream()) { stream.CopyTo(output); executable = output.ToArray(); }
        }
        Check(HashBytes(executable) == injectorHash, "Published legacy injector has the exact pinned checksum: " + version);
        // 只读取验证过的程序集资源，不调用旧入口、安装器或插件代码。
        var assembly = Assembly.Load(executable);
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in new[] {
            new { File = "PCL.PluginBootstrap.dll", Resource = "payload.PCL.PluginBootstrap.dll" },
            new { File = @"PCL\Plugins\DownloadRestore\PCL.DownloadRestore.dll", Resource = "payload.PCL.DownloadRestore.dll" },
            new { File = @"PCL\Plugins\DownloadRestore\0Harmony.dll", Resource = "payload.0Harmony.dll" }
        })
        {
            using (var input = assembly.GetManifestResourceStream(item.Resource))
            using (var output = new MemoryStream())
            {
                if (input == null) throw new Exception("Published legacy resource is missing: " + item.Resource);
                input.CopyTo(output); files.Add(item.File, output.ToArray());
            }
        }
        string exe = RegisteredFixture("published-upgrade-" + version, files, 1, null, true, first);
        VerifyUpgradeLifecycle(exe, version, true, first);
    }

    private static string HashBytes(byte[] bytes)
    {
        using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
    }

    private static Dictionary<string, long> FileWriteTimes(string directory) =>
        Directory.GetFiles(directory, "*", SearchOption.AllDirectories).ToDictionary(path => path, path => File.GetLastWriteTimeUtc(path).Ticks, StringComparer.OrdinalIgnoreCase);
    private static bool SameWriteTimes(Dictionary<string, long> before, Dictionary<string, long> after) =>
        before.Count == after.Count && before.All(item => after.TryGetValue(item.Key, out var value) && value == item.Value);

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";
    private static ChildResult Child(string file, string arguments, string workingDirectory, string path = null)
    {
        var start = new ProcessStartInfo(file, arguments) { WorkingDirectory = workingDirectory, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        if (path != null) start.EnvironmentVariables["PATH"] = path;
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
