# 后续 PCL 版本兼容性

本文描述 **v1.1.3**：安装器和引导器不再匹配固定官方 EXE 的 SHA-256，插件不再要求固定的 `2.99.0.0` 程序集版本。用户自行确认来源后，可以选择 `.exe` 尝试注入。插件仍检查 PCL 程序集名称及实际方法、字段和 IL 结构；允许安装不表示已经确认兼容。

GitHub Releases 中的 **v1.1.2 仍保留固定哈希与版本限制**，旧版标签和附件不被本版覆盖。安装过旧插件的用户，需要先卸载，再用 v1.1.3 安装；仅打开新注入器不会替换现有 DLL。

## 是否需要重复注入

不需要。工具依据安装记录、配置和插件文件的校验结果识别安装状态，不是仅检查 DLL 是否存在。完整安装且已启用时，检测结果为 `Installed`、`CanInstall=false`、`CanUninstall=true`；界面禁用注入按钮。直接通过命令行再次安装也会返回“无需重复注入”，不重写配置或文件。若存在 `disabled` 标记，允许重新启用；缺文件、文件被修改、宿主更新等情况会单独报告。

依据：[安装状态与记录检查](src/Injector/Installation/InstallationService.cs)，测试入口见[安装集成测试](tests/InjectorIntegration/Program.cs)。这些状态确认的是安装完整性；实际启动补丁是否成功应以当前这次 `bootstrap.log` 中的 `DownloadRestore ready: 5 runtime patches applied` 为准。

SHA-256 仍用于记录用户所选 EXE、确认安装或卸载操作期间文件没有变化，以及校验配置、插件 DLL 和备份。这里是对同一次安装的完整性检查，不再要求所有用户的 EXE 匹配预设官方样本。既有官方 PCL 2.99.0.0 的哈希保留在[测试样本记录](tests/official-build.json)，用于复现和来源核对。

## 为什么源码不变仍不足以保证兼容

| 依赖 | 当前实现与影响 |
| --- | --- |
| 宿主身份 | 安装器允许所选 `.exe` 尝试安装，不验证其官方来源。运行时插件仍要求程序集名称为 `Plain Craft Launcher 2`，但不限制固定程序集版本；文件名和版本号本身不能证明内部结构兼容。 |
| 反射绑定 | 使用 PCL 内部类型、方法、字段名及参数数量，包括 `PageDownloadInstall`、`ResourceVersion`、`ModModpack`。还直接引用 VB 编译器生成的 `_Lambda$__R18-5`、`_Closure$__0-0`，这些名称不是稳定的插件接口。 |
| IL 结构 | 四个补丁匹配具体指令形态；整合包补丁依赖固定的局部变量位置、类型及限制提示字符串，并要求每个匹配恰好出现一次。构建方式或编译器变化可能改变这些内容，即使相关业务源码文字没改。 |
| 语义与依赖库 | 整合包类型数值、资源类型值、下载器、安装器和 `MeloongCore` / `MeloongCore.Wpf` 的接口及行为需要保持一致。相同方法名与参数数量不等于相同语义。 |
| 启动过程 | 引导器钩住 `PCL.Application` 无参构造函数，再订阅 WPF `Startup`，依赖 PCL 先完成 `MeloongCore.Main.Init("PCL")`。启动顺序或运行时迁移也会影响加载。 |
| 安装位置与配置 | 使用对应的 `<实际 EXE 文件名>.config` 和该目录的插件文件；安装记录保存宿主绝对路径。改名、搬家、更新器覆盖配置或删除文件，不能视为安装仍然完整。 |

关键依据：[安装状态与完整性检查](src/Injector/Installation/InstallationService.cs)、[引导器与启动钩子](src/Bootstrap/PluginDomainManager.cs)、[插件入口](src/DownloadRestore/EntryPoint.cs)、[反射绑定](src/DownloadRestore/HostBindings.cs)、[IL 补丁](src/DownloadRestore/RuntimePatches.cs)、[配置路径](src/Injector/Installation/InstallationPaths.cs)、[CLR 配置](src/Injector/Installation/LauncherConfiguration.cs)。原版启动顺序见 [Application.xaml.vb](https://github.com/Meloong-Git/PCL/blob/2.99.0.0/Plain%20Craft%20Launcher%202/Application.xaml.vb#L7)。

“编译器变化可能影响生成名或 IL”是基于上述耦合的工程判断，不表示已经验证某个尚未取得的未来版本。

## 官方更新后目前会怎样

- 只替换原路径的 EXE，保留配置和 v1.1.3 插件文件：引导器可以继续尝试加载；插件检查程序集名称和实际结构。检查或补丁应用失败会记录日志，并回滚本次已应用的恢复补丁。安装器仍能依据安装记录发现宿主发生变化，应先卸载再安装以更新记录。
- 若保留的是 v1.1.2 的旧引导器与插件：固定哈希 / 版本限制仍然生效；必须先卸载旧插件，再安装 v1.1.3，才能采用新的尝试规则。
- 配置也被覆盖、删除或修改：加载入口可能失效；安装器报告配置冲突，不直接覆盖更新后的配置。当前实现不会自动合并或修复这类更新。
- EXE 改名或移动安装目录：加载依赖对应名称的配置和相对目录；安装记录的绝对路径也可能不匹配。当前没有自动迁移功能。

依据：[更新后状态与配置完整性检查](src/Injector/Installation/InstallationService.cs)、[引导器](src/Bootstrap/PluginDomainManager.cs)、[初始化与失败回滚](src/DownloadRestore/EntryPoint.cs)。失败记录并不等于整个宿主的所有行为都能恢复；未通过实际运行验证前，不应承诺该版本正常下载或安装。

## 最小后续适配方式

拿到新的 PCL EXE 后，可在独立目录尝试安装与启动，查看当次日志是否成功应用全部补丁；随后仍需核对完整反射绑定、四处 IL 匹配、启动顺序及依赖接口，并验证补丁回滚、Minecraft 下载和整合包安装。如果这些结构与行为保持兼容，可以直接复用恢复逻辑，无需为了哈希或版本号变化重新发布；如果结构已变，则需要更新相应绑定和补丁。

目前安装器没有原地升级旧插件的功能：完整的已安装状态会直接跳过重复安装，因此更新插件的现有途径是先卸载旧插件，再安装新版。用户可以继续使用自行获取的官方 PCL，无需重新编译主程序。

现有结构匹配只能发现部分变化，不能证明未知版本全部兼容。v1.1.3 取消的是固定样本准入，尚未建立跨版本兼容承诺；用户可自行尝试，但不能据此认为未来更新永久有效。[注入器验证](INJECTOR-VALIDATION.md)与[插件验证](VALIDATION.md)按各次实际测试的版本记录，不作为其他版本已验证的证据。
