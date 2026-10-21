using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;

namespace PCL.DownloadRestore.Injector.Installation
{
    internal static class LauncherConfiguration
    {
        internal const string BootstrapAssembly = "PCL.PluginBootstrap, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null";
        internal const string BootstrapType = "PCL.PluginBootstrap.PluginDomainManager";
        private static readonly string[] RequiredSwitches =
        {
            "Switch.System.IO.UseLegacyPathHandling",
            "Switch.System.IO.BlockLongPaths",
            "Switch.System.Net.DontEnableSchUseStrongCrypto",
            "Switch.System.Net.DontEnableSystemDefaultTlsVersions"
        };

        internal static byte[] Patch(byte[] original)
        {
            XmlDocument doc;
            if (original == null || original.Length == 0)
            {
                doc = new XmlDocument { PreserveWhitespace = true, XmlResolver = null };
                doc.AppendChild(doc.CreateXmlDeclaration("1.0", "utf-8", null));
                doc.AppendChild(doc.CreateElement("configuration"));
            }
            else doc = Parse(original);
            if (doc.DocumentElement == null || doc.DocumentElement.Name != "configuration" || doc.DocumentElement.NamespaceURI.Length != 0)
                throw InstallationPaths.Error("启动器配置格式不受支持。请保留原配置，确认它是标准 .NET configuration XML 后重试。");
            XmlElement startup = UniqueChild(doc.DocumentElement, "startup");
            if (startup == null)
            {
                startup = doc.CreateElement("startup");
                var runtimeVersion = doc.CreateElement("supportedRuntime");
                runtimeVersion.SetAttribute("version", "v4.0");
                runtimeVersion.SetAttribute("sku", ".NETFramework,Version=v4.8");
                startup.AppendChild(runtimeVersion);
                doc.DocumentElement.AppendChild(startup);
            }
            XmlElement runtime = UniqueChild(doc.DocumentElement, "runtime");
            if (runtime == null)
            {
                runtime = doc.CreateElement("runtime");
                doc.DocumentElement.AppendChild(runtime);
            }
            if (runtime.SelectSingleNode("appDomainManagerAssembly") != null || runtime.SelectSingleNode("appDomainManagerType") != null)
                throw InstallationPaths.Error("这个启动器已配置其他 AppDomain 启动扩展，不能直接覆盖。请先卸载原扩展或使用独立的 PCL 文件夹。");
            XmlElement switchesNode = UniqueChild(runtime, "AppContextSwitchOverrides");
            if (switchesNode == null)
            {
                switchesNode = doc.CreateElement("AppContextSwitchOverrides");
                runtime.AppendChild(switchesNode);
            }
            var switches = new List<KeyValuePair<string, string>>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string entry in switchesNode.GetAttribute("value").Split(';'))
            {
                if (string.IsNullOrWhiteSpace(entry)) continue;
                string[] parts = entry.Split('=');
                if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
                    throw InstallationPaths.Error("配置中的 AppContext 兼容开关格式无效。请修复配置后重试：" + entry);
                string key = parts[0].Trim();
                if (!seen.Add(key)) throw InstallationPaths.Error("配置含有重复的 AppContext 兼容开关，请先修复：" + key);
                switches.Add(new KeyValuePair<string, string>(key, parts[1].Trim()));
            }
            foreach (string key in RequiredSwitches)
            {
                switches.RemoveAll(pair => pair.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
                switches.Add(new KeyValuePair<string, string>(key, "false"));
            }
            switchesNode.SetAttribute("value", string.Join(";", switches.Select(pair => pair.Key + "=" + pair.Value)));
            var assembly = doc.CreateElement("appDomainManagerAssembly");
            assembly.SetAttribute("value", BootstrapAssembly);
            runtime.AppendChild(assembly);
            var type = doc.CreateElement("appDomainManagerType");
            type.SetAttribute("value", BootstrapType);
            runtime.AppendChild(type);
            using (var output = new MemoryStream()) { doc.Save(output); return output.ToArray(); }
        }

        internal static bool ContainsOwnedManager(string config)
        {
            if (!File.Exists(config)) return false;
            byte[] bytes = File.ReadAllBytes(config);
            if (bytes.Length == 0) return false;
            XmlDocument doc = Parse(bytes);
            foreach (XmlElement node in doc.SelectNodes("/configuration/runtime/appDomainManagerType"))
                if (node.GetAttribute("value").Split(',')[0].Trim().Equals(BootstrapType, StringComparison.OrdinalIgnoreCase)) return true;
            foreach (XmlElement node in doc.SelectNodes("/configuration/runtime/appDomainManagerAssembly"))
                if (node.GetAttribute("value").Split(',')[0].Trim().Equals("PCL.PluginBootstrap", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        internal static byte[] SuspendManager(byte[] installed)
        {
            XmlDocument doc = Parse(installed);
            var assemblies = doc.SelectNodes("/configuration/runtime/appDomainManagerAssembly");
            var types = doc.SelectNodes("/configuration/runtime/appDomainManagerType");
            if (assemblies.Count != 1 || types.Count != 1 ||
                ((XmlElement)assemblies[0]).GetAttribute("value") != BootstrapAssembly ||
                ((XmlElement)types[0]).GetAttribute("value") != BootstrapType)
                throw InstallationPaths.Error("启动配置中的加载入口与本插件不一致，无法安全升级。");
            // 升级期间只暂停自己的加载入口，其余配置保留；完成后恢复原始字节。
            assemblies[0].ParentNode.RemoveChild(assemblies[0]);
            types[0].ParentNode.RemoveChild(types[0]);
            using (var output = new MemoryStream()) { doc.Save(output); return output.ToArray(); }
        }

        private static XmlDocument Parse(byte[] bytes)
        {
            try
            {
                var document = new XmlDocument { PreserveWhitespace = true, XmlResolver = null };
                var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
                using (var input = new MemoryStream(bytes))
                using (var reader = XmlReader.Create(input, settings)) document.Load(reader);
                return document;
            }
            catch (XmlException ex) { throw InstallationPaths.Error("启动器配置 XML 无法读取，请先修复或恢复配置文件后重试。" + ex.Message, ex); }
        }

        private static XmlElement UniqueChild(XmlElement parent, string name)
        {
            XmlNodeList nodes = parent.SelectNodes(name);
            if (nodes.Count > 1) throw InstallationPaths.Error("启动器配置中有重复的 " + name + " 节点，请先修复配置后重试。");
            return nodes.Count == 0 ? null : (XmlElement)nodes[0];
        }
    }
}
