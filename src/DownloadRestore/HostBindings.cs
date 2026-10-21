using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace PCL.DownloadRestore
{
    internal sealed class HostBindings
    {
        private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        internal MethodInfo LoaderInit, LoadInstanceFinished, InitializeResourceVersions, ModpackChoose, ModpackInstall;
        internal FieldInfo ClientListLoader, TargetResourceType, ModpackClosureFile;
        internal MethodInfo MinecraftCardGetter, ResourceToListItem, ResourceInstallClick, ResourceInstallAdapter, ResourceSaveClick;
        internal MethodInfo RunInThread, ModpackClosureAction, SelectFile, FilterFromString;
        internal Type ModpackClosure;
        internal Dictionary<int, MethodInfo> Installers;

        internal MethodInfo[] Targets => new[] { LoaderInit, LoadInstanceFinished, InitializeResourceVersions, ModpackChoose, ModpackInstall };

        internal static HostBindings Create(Assembly host)
        {
            var result = new HostBindings();
            Type installPage = RequireType(host, "PCL.PageDownloadInstall");
            Type modifyPage = RequireType(host, "PCL.PageDownloadModify");
            Type resourcePage = RequireType(host, "PCL.PageDownloadResourceDetail");
            Type modpack = RequireType(host, "PCL.ModModpack");
            result.LoaderInit = Method(installPage, "LoaderInit", 0, false);
            result.LoadInstanceFinished = Method(modifyPage, "LoadInstanceFinished", 1, false);
            result.InitializeResourceVersions = Method(resourcePage, "InitializeStackResourceVersions", 1, false);
            result.ModpackChoose = Method(modpack, "ModpackInstall", 0, true);
            result.ModpackInstall = Method(modpack, "ModpackInstall", 3, true);
            if (result.ModpackInstall.GetParameters().Any(p => p.ParameterType != typeof(string)))
                throw new MissingMethodException("Unexpected ModpackInstall parameter types.");
            result.ClientListLoader = RequireField(RequireType(host, "PCL.ModDownload"), "DlClientListLoader");
            result.MinecraftCardGetter = Method(installPage, "get_CardMinecraft", 0, false);
            result.TargetResourceType = RequireField(resourcePage, "TargetResourceType");
            Type resourceVersion = RequireType(host, "PCL.ResourceVersion");
            result.ResourceToListItem = Method(resourceVersion, "ToListItem", 3, false);
            result.ResourceInstallClick = Method(resourcePage, "Install_Click", 2, false);
            // VB's AddressOf adapter narrows the sender from Object to MyListItem.
            // The delegate cannot bind directly to Install_Click's narrower first argument.
            result.ResourceInstallAdapter = Method(resourcePage, "_Lambda$__R18-5", 2, false);
            result.ResourceSaveClick = Method(resourcePage, "Save_Click", 2, false);
            result.RunInThread = Method(RequireType(host, "PCL.ModBase"), "RunInThread", 1, true);
            result.ModpackClosure = RequireType(host, "PCL.ModModpack+_Closure$__0-0");
            result.ModpackClosureFile = RequireField(result.ModpackClosure, "$VB$Local_File");
            result.ModpackClosureAction = Method(result.ModpackClosure, "_Lambda$__0", 0, false);

            result.Installers = new Dictionary<int, MethodInfo>
            {
                [0] = Method(modpack, "InstallPackCurseForge", 5, true),
                [1] = Method(modpack, "InstallPackHMCL", 3, true),
                [2] = Method(modpack, "InstallPackMMC", 3, true),
                [3] = Method(modpack, "InstallPackMCBBS", 4, true),
                [4] = Method(modpack, "InstallPackModrinth", 5, true),
                [9] = Method(modpack, "InstallPackLauncherPack", 3, true),
                [-1] = Method(modpack, "InstallPackCompress", 2, true)
            };
            foreach (var installer in result.Installers.Values)
                if (installer.ReturnType != result.ModpackInstall.ReturnType)
                    throw new MissingMethodException("Unexpected installer result: " + installer.Name);

            // Use the host's file chooser, including its normal filtering, logging and path handling.
            var dialogs = RequireType(Assembly.Load("MeloongCore.Wpf"), "MeloongCore.Wpf.Dialogs");
            result.SelectFile = Method(dialogs, "SelectFile", 4, true);
            Type filter = Nullable.GetUnderlyingType(result.SelectFile.GetParameters()[3].ParameterType);
            if (filter == null) throw new MissingMethodException("Unsupported file chooser filter.");
            result.FilterFromString = filter.GetMethods(All).Single(m => m.Name == "op_Implicit" &&
                m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(string));
            return result;
        }

        internal static object Invoke(MethodInfo method, object instance, params object[] arguments)
        {
            try { return method.Invoke(instance, arguments); }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }
        }

        private static Type RequireType(Assembly assembly, string name) => assembly.GetType(name, true);
        private static FieldInfo RequireField(Type type, string name) =>
            type.GetField(name, All) ?? throw new MissingFieldException(type.FullName, name);
        private static MethodInfo Method(Type type, string name, int count, bool isStatic) =>
            type.GetMethods(All).Single(m => m.Name == name && m.IsStatic == isStatic && m.GetParameters().Length == count);
    }
}
