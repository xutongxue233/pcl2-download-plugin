using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace PCL.DownloadRestore.Injector
{
    /// <summary>The only distributable runtime files, embedded without loading or executing them.</summary>
    public static class EmbeddedPayload
    {
        public static IDictionary<string, byte[]> ReadFiles()
        {
            var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            Read(files, "PCL.PluginBootstrap.dll", "payload.PCL.PluginBootstrap.dll");
            Read(files, @"PCL\Plugins\DownloadRestore\PCL.DownloadRestore.dll", "payload.PCL.DownloadRestore.dll");
            Read(files, @"PCL\Plugins\DownloadRestore\0Harmony.dll", "payload.0Harmony.dll");
            return files;
        }

        private static void Read(IDictionary<string, byte[]> files, string path, string resource)
        {
            using (var input = typeof(EmbeddedPayload).Assembly.GetManifestResourceStream(resource))
            using (var output = new MemoryStream())
            {
                if (input == null) throw new InvalidOperationException("注入器缺少内置插件，请重新获取完整发行文件。");
                input.CopyTo(output);
                files.Add(path, output.ToArray());
            }
        }
    }
}
