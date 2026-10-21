using System;
using System.IO;

internal static class Program
{
    private static int Main()
    {
        var manager = AppDomain.CurrentDomain.DomainManager;
        if (manager == null || manager.GetType().FullName != "PCL.PluginBootstrap.PluginDomainManager") return 1;
        File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "probe-result.txt"), "DOMAIN_MANAGER_AUTOSTART_PASS");
        return 0;
    }
}
