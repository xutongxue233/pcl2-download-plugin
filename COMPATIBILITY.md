# 后续 PCL 版本兼容性

结论：当前发行版只支持已验证的官方 PCL 2.99.0.0。安装一次后，同一受支持程序每次启动会自动加载插件；这不等于今后的官方更新也会自动兼容。即使下载相关源码没有修改，只要 EXE 文件发生变化，当前版本也会拒绝安装或跳过加载。本文只分析现有实现，没有放宽任何检查。

## 是否需要重复注入

不需要。工具依据安装记录、配置和插件文件的校验结果识别安装状态，不是仅检查 DLL 是否存在。完整安装且已启用时，检测结果为 `Installed`、`CanInstall=false`、`CanUninstall=true`；界面禁用注入按钮。直接通过命令行再次安装也会返回“无需重复注入”，不重写配置或文件。若存在 `disabled` 标记，允许重新启用；缺文件、文件被修改、宿主更新等情况会单独报告。

依据：[安装状态检测](src/Injector/Installation/InstallationService.cs#L32)、[重复安装直接返回](src/Injector/Installation/InstallationService.cs#L78)、[记录与文件校验](src/Injector/Installation/InstallationService.cs#L293)。已有[重复安装测试](tests/InjectorIntegration/Program.cs#L122)。这些状态确认的是安装完整性；实际启动补丁是否成功应以 `bootstrap.log` 中的 `DownloadRestore ready: 5 runtime patches applied` 为准。

## 为什么源码不变仍不足以保证兼容

| 依赖 | 当前实现与影响 |
| --- | --- |
| 宿主身份 | 安装器、引导器、插件入口分别校验整个 EXE 的 SHA-256。插件入口还要求程序集名称为 `Plain Craft Launcher 2`、版本为 `2.99.0.0`。只改版本号或无关功能也会使当前版本拒绝处理。 |
| 反射绑定 | 使用 PCL 内部类型、方法、字段名及参数数量，包括 `PageDownloadInstall`、`ResourceVersion`、`ModModpack`。还直接引用 VB 编译器生成的 `_Lambda$__R18-5`、`_Closure$__0-0`，这些名称不是稳定的插件接口。 |
| IL 结构 | 四个补丁匹配具体指令形态；整合包补丁依赖固定的局部变量位置、类型及限制提示字符串，并要求每个匹配恰好出现一次。构建方式或编译器变化可能改变这些内容，即使相关业务源码文字没改。 |
| 语义与依赖库 | 整合包类型数值、资源类型值、下载器、安装器和 `MeloongCore` / `MeloongCore.Wpf` 的接口及行为需要保持一致。相同方法名与参数数量不等于相同语义。 |
| 启动过程 | 引导器钩住 `PCL.Application` 无参构造函数，再订阅 WPF `Startup`，依赖 PCL 先完成 `MeloongCore.Main.Init("PCL")`。启动顺序或运行时迁移也会影响加载。 |
| 安装位置与配置 | 使用对应的 `<实际 EXE 文件名>.config` 和该目录的插件文件；安装记录保存宿主绝对路径。改名、搬家、更新器覆盖配置或删除文件，不能视为安装仍然完整。 |

关键依据：[安装器哈希检查](src/Injector/Installation/InstallationService.cs#L14)、[引导器哈希与启动钩子](src/Bootstrap/PluginDomainManager.cs#L50)、[程序集版本检查](src/DownloadRestore/EntryPoint.cs#L87)、[反射绑定](src/DownloadRestore/HostBindings.cs#L21)、[IL 补丁](src/DownloadRestore/RuntimePatches.cs#L102)、[配置路径](src/Injector/Installation/InstallationPaths.cs#L16)、[CLR 配置](src/Injector/Installation/LauncherConfiguration.cs#L33)。原版启动顺序见 [Application.xaml.vb](https://github.com/Meloong-Git/PCL/blob/2.99.0.0/Plain%20Craft%20Launcher%202/Application.xaml.vb#L7)。

“编译器变化可能影响生成名或 IL”是基于上述耦合的工程判断，不表示已经验证某个尚未取得的未来版本。

## 官方更新后目前会怎样

- 只替换原路径的 EXE，保留配置和插件文件：旧引导器发现哈希不支持，记录日志后不应用补丁。安装器显示程序已更新，允许卸载旧插件；卸载保留新的官方 EXE。
- 配置也被覆盖、删除或修改：加载入口可能失效；安装器报告配置冲突，不直接覆盖更新后的配置。当前实现不会自动合并或修复这类更新。
- EXE 改名或移动安装目录：加载依赖对应名称的配置和相对目录；安装记录的绝对路径也可能不匹配。当前没有自动迁移功能。

依据：[更新后状态](src/Injector/Installation/InstallationService.cs#L41)、[配置完整性检查](src/Injector/Installation/InstallationService.cs#L321)、[引导器跳过未知版本](src/Bootstrap/PluginDomainManager.cs#L55)。

## 最小后续适配方式

拿到新的官方 EXE 后，先检查完整反射绑定、四处 IL 匹配、启动顺序及依赖接口，再在独立目录运行补丁回滚、原版启动、Minecraft 下载和整合包安装测试。若全部保持兼容，可以复用补丁逻辑，仅为新版本增加经过验证的哈希与版本记录，并同步更新安装器、引导器和插件入口；用户仍使用官网下载的 PCL，只更新本工具和插件。

为了后续维护，可将三处重复的宿主身份判断改为共享的版本适配表，并增加明确的插件升级流程。目前安装器没有原地升级旧插件的功能：完整的已安装状态会直接跳过重复安装，因此更新插件的现有途径是先卸载旧插件，再安装经过新版本验证的插件。

若未来希望自动接受未知官方版本，需要额外实现结构与语义预检、版本适配及更新策略。现有匹配检查只能发现部分变化，不能证明未知版本全部兼容；直接删除哈希和版本检查不能构成可靠的兼容方案。对于尚未发布或未取得的版本，现在无法承诺永久兼容。
