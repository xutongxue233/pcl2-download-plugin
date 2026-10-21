using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace PCL.DownloadRestore
{
    internal static class RuntimePatches
    {
        internal static HostBindings Bindings;
        private const string RestrictionMessage = "为遵守相关法律法规，PCL 暂时不再提供 MC 下载功能，非常抱歉 qwq……";

        // 保留 ControlInit、PageLoaderInit、完成回调和宿主的界面生命周期。
        // 仅将已禁用加载器的构造替换为现有的实际加载器。
        internal static IEnumerable<CodeInstruction> RestoreMinecraftLoader(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            int matches = 0;
            for (int i = 0; i < code.Count; i++)
            {
                if (code[i].opcode != OpCodes.Newobj || !(code[i].operand is ConstructorInfo ctor) ||
                    ctor.DeclaringType != Bindings.ClientListLoader.FieldType) continue;
                var replacement = Enumerable.Range(0, ctor.GetParameters().Length)
                    .Select(_ => new CodeInstruction(OpCodes.Pop)).ToList();
                replacement.Add(new CodeInstruction(OpCodes.Ldsfld, Bindings.ClientListLoader));
                Replace(code, i, 1, replacement);
                i += replacement.Count - 1;
                matches++;
            }
            RequireExactlyOne(matches, "disabled Minecraft loader");
            return code;
        }

        internal static IEnumerable<CodeInstruction> RestoreMinecraftCard(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            int matches = 0;
            for (int i = 0; i + 2 < code.Count; i++)
            {
                if (!code[i].Calls(Bindings.MinecraftCardGetter) || !code[i + 1].LoadsConstant(2) ||
                    !(code[i + 2].operand is MethodInfo setter) || setter.Name != "set_Visibility") continue;
                code[i + 1].opcode = OpCodes.Ldc_I4_0; // 对应 Visibility.Visible
                code[i + 1].operand = null;
                matches++;
            }
            RequireExactlyOne(matches, "Minecraft version card visibility");
            return code;
        }

        // 仍由原方法对各个资源版本进行排序、分组和显示。
        // 仅为整合包设置 Install_Click 主操作和 Save_Click 按钮操作。
        internal static IEnumerable<CodeInstruction> RestoreResourceActions(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            int matches = 0;
            for (int i = 0; i < code.Count; i++)
            {
                if (!code[i].Calls(Bindings.ResourceToListItem)) continue;
                var replacement = new List<CodeInstruction>
                {
                    new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(RuntimePatches), nameof(CreateResourceItem))),
                    new CodeInstruction(OpCodes.Castclass, Bindings.ResourceToListItem.ReturnType)
                };
                Replace(code, i, 1, replacement);
                i += replacement.Count - 1;
                matches++;
            }
            RequireExactlyOne(matches, "resource list item factory");
            return code;
        }

        internal static object CreateResourceItem(object version, Delegate click, Delegate save, bool isBadDisplay, object page)
        {
            if (Convert.ToInt32(Bindings.TargetResourceType.GetValue(page)) == 2) // 对应 ResourceTypes.ModPack
            {
                ParameterInfo[] parameters = Bindings.ResourceToListItem.GetParameters();
                click = Delegate.CreateDelegate(parameters[0].ParameterType, page, Bindings.ResourceInstallAdapter);
                save = Delegate.CreateDelegate(parameters[1].ParameterType, page, Bindings.ResourceSaveClick);
            }
            return HostBindings.Invoke(Bindings.ResourceToListItem, version, click, save, isBadDisplay);
        }

        internal static bool ChooseModpack()
        {
            object filter = HostBindings.Invoke(Bindings.FilterFromString, null, "整合包文件(*.rar;*.zip;*.mrpack)|*.rar;*.zip;*.mrpack");
            var files = (IEnumerable)HostBindings.Invoke(Bindings.SelectFile, null, "选择整合包压缩文件", false, null, filter);
            string file = files.Cast<string>().FirstOrDefault();
            if (string.IsNullOrEmpty(file)) return false;

            // 沿用此 PCL 闭包，保留原有的取消操作和错误提示处理。
            object closure = Activator.CreateInstance(Bindings.ModpackClosure, true);
            Bindings.ModpackClosureFile.SetValue(closure, file);
            var action = (Action)Delegate.CreateDelegate(typeof(Action), closure, Bindings.ModpackClosureAction);
            HostBindings.Invoke(Bindings.RunInThread, null, action);
            return false;
        }

        internal static IEnumerable<CodeInstruction> RestoreModpackDispatch(IEnumerable<CodeInstruction> instructions,
            ILGenerator generator, MethodBase original)
        {
            var code = instructions.ToList();
            var locals = original.GetMethodBody().LocalVariables;
            Type returnType = ((MethodInfo)original).ReturnType;
            if (locals.Count < 5 || locals[0].LocalType != returnType ||
                locals[1].LocalType.FullName != "System.IO.Compression.ZipArchive" ||
                locals[2].LocalType != typeof(string) || locals[4].LocalType != typeof(int))
                throw new NotSupportedException("ModpackInstall locals no longer match the supported host.");
            Label done = generator.DefineLabel();
            int matches = 0;
            for (int i = 0; i + 2 < code.Count; i++)
            {
                if (code[i].opcode != OpCodes.Ldstr || !Equals(code[i].operand, RestrictionMessage) ||
                    code[i + 1].opcode != OpCodes.Newobj || code[i + 2].opcode != OpCodes.Throw) continue;
                var replacement = new List<CodeInstruction>
                {
                    new CodeInstruction(OpCodes.Ldloc_S, (byte)4), // PCL 选定的整合包类型
                    new CodeInstruction(OpCodes.Ldarg_0),
                    new CodeInstruction(OpCodes.Ldloc_1), // PCL 打开的压缩包
                    new CodeInstruction(OpCodes.Ldloc_2), // PCL 选定的压缩包基础目录
                    new CodeInstruction(OpCodes.Ldarg_1),
                    new CodeInstruction(OpCodes.Ldarg_2),
                    new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(RuntimePatches), nameof(DispatchModpack))),
                    new CodeInstruction(OpCodes.Castclass, returnType),
                    new CodeInstruction(OpCodes.Stloc_0),
                    new CodeInstruction(OpCodes.Leave, done)
                };
                Replace(code, i, 3, replacement);
                i += replacement.Count - 1;
                matches++;
            }
            RequireExactlyOne(matches, "modpack restriction throw");
            // Leave 会执行原有的 finally，无论成功还是失败都会释放压缩包资源。
            var result = new CodeInstruction(OpCodes.Ldloc_0);
            result.labels.Add(done);
            code.Add(result);
            code.Add(new CodeInstruction(OpCodes.Ret));
            return code;
        }

        internal static object DispatchModpack(int packType, string file, object archive, string archiveBaseFolder, string instanceName, string logo)
        {
            if (!Bindings.Installers.TryGetValue(packType, out var installer)) installer = Bindings.Installers[-1];
            EntryPoint.Log("Dispatching modpack to original PCL method " + installer.Name + ".");
            object[] all = { file, archive, archiveBaseFolder, instanceName, logo };
            return HostBindings.Invoke(installer, null, all.Take(installer.GetParameters().Length).ToArray());
        }

        private static void Replace(List<CodeInstruction> code, int index, int count, List<CodeInstruction> replacement)
        {
            replacement[0].labels.AddRange(code[index].labels);
            replacement[0].blocks.AddRange(code[index].blocks);
            // 不得静默丢弃匹配区间内的分支目标或异常处理边界。
            for (int offset = 1; offset < count; offset++)
                if (code[index + offset].labels.Count != 0 || code[index + offset].blocks.Count != 0)
                    throw new NotSupportedException("Unexpected control-flow boundary inside a patch match.");
            code.RemoveRange(index, count);
            code.InsertRange(index, replacement);
        }

        private static void RequireExactlyOne(int matches, string description)
        {
            if (matches != 1) throw new NotSupportedException("Expected exactly one " + description + "; found " + matches + ".");
        }
    }
}
