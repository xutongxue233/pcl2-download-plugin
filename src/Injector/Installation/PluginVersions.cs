using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace PCL.DownloadRestore.Injector.Installation
{
    internal static class PluginVersions
    {
        // 这里只识别本工具已发布的插件载荷，与 PCL EXE 的准入或哈希无关。
        // 旧 DLL 的文件版本都是 1.0.0.0，必须同时匹配三份文件，不能据单个 DLL 猜版本。
        private static readonly string[][] LegacyReleases = {
            new[] { "1.1.2", "BF2E0787EA61F7742E7E75615FE7321471C6F55F6840B775083D94C5984BF035",
                "44468FB85A7EA5D4BC685583D491817916367EF04E82E1F6312341A03EC8413A", "77E6901ECC606AEC66C2A972782A3779E4F50C037D2D165EB7ECECDD4D8F794D" },
            new[] { "1.1.3", "EC7E66237A9787D56858464D6FD26E51E37558375C02F3BC9198D5DB8E60DAC1",
                "82FD6854708DDF19057E69F200C8D60A4539CD20AD154B294FD66682E5A02910", "77E6901ECC606AEC66C2A972782A3779E4F50C037D2D165EB7ECECDD4D8F794D" }
        };

        internal static bool IsValid(string value) => value != null &&
            Regex.IsMatch(value, @"\A[0-9]+\.[0-9]+\.[0-9]+\z") && Version.TryParse(value, out _);

        internal static string IdentifyLegacy(InstallState state)
        {
            if (state.PluginFiles.Count != 2) return null;
            var plugin = state.PluginFiles.SingleOrDefault(file => file.Path == "PCL.DownloadRestore.dll");
            var harmony = state.PluginFiles.SingleOrDefault(file => file.Path == "0Harmony.dll");
            if (plugin == null || harmony == null) return null;
            foreach (string[] release in LegacyReleases)
                if (Same(release[1], state.BootstrapSha256) && Same(release[2], plugin.Sha256) && Same(release[3], harmony.Sha256))
                    return release[0];
            return null;
        }

        private static bool Same(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }
}
