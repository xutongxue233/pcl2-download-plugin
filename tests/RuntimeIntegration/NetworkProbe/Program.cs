using System;
using System.IO;
using System.Net;

internal static class Program
{
    private static int Main()
    {
        Console.WriteLine("TargetFramework=" + AppDomain.CurrentDomain.SetupInformation.TargetFrameworkName);
        Console.WriteLine("DomainManager=" + (AppDomain.CurrentDomain.DomainManager?.GetType().FullName ?? "none"));
        Console.WriteLine("SecurityProtocol=" + ServicePointManager.SecurityProtocol);
        foreach (string key in new[] { "Switch.System.Net.DontEnableSchUseStrongCrypto", "Switch.System.Net.DontEnableSystemDefaultTlsVersions" })
        {
            bool value;
            Console.WriteLine(key + "=" + (AppContext.TryGetSwitch(key, out value) ? value.ToString() : "unset"));
        }
        foreach (string url in new[] { "https://launchermeta.mojang.com/mc/game/version_manifest.json", "https://bmclapi2.bangbang93.com/mc/game/version_manifest.json" })
        {
            try
            {
                var request = WebRequest.CreateHttp(url);
                request.Timeout = 10000;
                using (var response = (HttpWebResponse)request.GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream()))
                    Console.WriteLine(url + " => " + (int)response.StatusCode + ", chars=" + reader.ReadToEnd().Length);
            }
            catch (Exception exception) { Console.WriteLine(url + " => " + exception.GetType().Name + ": " + exception.Message); }
        }
        return 0;
    }
}
