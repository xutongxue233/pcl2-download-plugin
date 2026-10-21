# PCL 下载恢复注入器

本文对应当前源码 **1.1.4 开发版（未发布）**，新增已安装 / 内置插件版本显示和直接升级。公开 Release 仍为 v1.1.3，其插件更新流程仍需先卸载再安装。1.1.4 沿用 v1.1.3 取消固定 PCL EXE 哈希和程序集版本限制的行为，用户自行确认宿主来源；实际绑定与补丁结构仍需匹配，不保证任意 PCL 版本兼容。

这是一个用 C# 和 WPF 编写的 Windows 图形工具。双击 `PCL.DownloadRestore.Injector.exe`，选择官方 PCL 程序，即可安装、检查、升级或卸载下载恢复插件。用户使用时**不需要 PowerShell、管理员权限或额外下载 DLL**；三个插件 DLL 已嵌入注入器内部。

界面直接移植并适配原 PCL 源码中 `Application.xaml`、`FormMain`、`MyButton` 和 `MyCard` 的样式，保留程序选择、版本、状态与注入／升级／卸载操作。界面来源及适配说明见源码包中的 `src/Injector/UI/SOURCE-NOTES.md`。

1.1.2 优化文字显示：界面统一使用微软雅黑 UI，启用灰度抗锯齿和自然字距，卡片阴影与文字分层；日志保留等宽字体，字号、布局及配色沿用上一版。

**所选 PCL EXE 保持原样。** 注入器只增加本地插件文件并合并启动配置，之后照常打开 PCL，每次启动都会自动尝试加载插件，实际结果请查看日志。关闭 PCL 后可随时用同一工具卸载，恢复安装前的配置。

PCL 原作者为 **龙腾猫跃**，原项目：[Meloong-Git/PCL](https://github.com/Meloong-Git/PCL)，[支持原作者](https://meloong.com/afd/a/LTCat)。本工具是独立第三方适配，不是 PCL 官方发布或背书。恢复内容复用原版的页面和安装引擎，包括 Minecraft 版本列表与下载安装、Minecraft 版本修改、在线整合包安装和本地整合包导入。

## 使用

1. 退出目标 PCL，将注入器 ZIP 解压到可写文件夹。
2. 双击 `PCL.DownloadRestore.Injector.exe`，选择需要恢复功能的官方 PCL `.exe`。
3. 自行确认所选 PCL 的来源；选择程序后自动检查文件、配置和安装状态，显示已安装版本 `InstalledVersion` 和本工具内置版本 `AvailableVersion`（1.1.4），不匹配固定官方 EXE 哈希。新安装点击“注入插件”；与内置载荷相同的完整安装显示“已注入”，无需重复操作。可识别且可更新的旧安装显示“升级插件”；升级方式见下文。
4. 卸载时，先退出 PCL，再用注入器选择同一个程序，点击“卸载插件”。

标题栏左侧为“PCL下载恢复工具”，不再显示顶部导航项。主界面保留程序选择、版本、当前状态和注入／升级／卸载操作。“查看日志”展开本次检测、安装、升级和卸载记录，以及所选 PCL 的插件启动日志；展开期间每 2 秒刷新，可选中文字复制，点击“收起日志”折叠。启动日志只读取最近 64 KiB，历史成功记录不代表当前仍安装，当前状态以重新检测结果为准。切回注入器窗口时会自动刷新状态；检测到 PCL 正在运行时，每 2 秒刷新一次，退出 PCL 后即可继续操作。

运行环境为 Windows 和 .NET Framework 4.8，与目标 PCL 的运行时一致；使用注入器不需要 .NET SDK。安装目录必须可写。程序未配置发行者代码签名；请通过来源、校验值和随附源码判断是否信任该工具，不应将文件名或界面当作官方认证。

安装器允许所选 `.exe` 进入安装尝试，不核验它是否来自官方。以下 SHA-256 仅用于标识已有测试使用的官方 **PCL 2.99.0.0** 样本，供复现与来源核对，不是当前安装器的准入名单：

```text
6B6E1260CB5A1D6A303855F1C60CAB3D0BC1AEBBA7E40A964180188B271E7386
```

测试样本的官方构建核对记录见 `original-build.json`（源码中为 `tests/official-build.json`）；随附历史验证记录中的固定哈希要求属于旧版行为。包内文件校验值见 `SHA256SUMS.txt`。SHA-256 仍用于安装记录、事务期间宿主文件未变化检查，以及配置、DLL 和备份的完整性检查。允许安装不等于实际运行时兼容。

## 安装内容与持久生效

```text
官方 PCL.exe                         不修改
官方 PCL.exe.config                  本地启动入口与 .NET 兼容设置
PCL.PluginBootstrap.dll             每次启动加载插件
PCL/Plugins/DownloadRestore/
  PCL.DownloadRestore.dll           下载恢复逻辑
  0Harmony.dll                      方法补丁依赖
  .install-state.json               文件清单、校验值和插件包版本
  .original-exe.config              原配置存在时的精确备份
```

这是程序目录内的持久加载，不修改全局注册表或开机启动项，也不依赖注入器一直运行。安装后请保留这些文件；需要移动、改名或更新 PCL 时，建议先卸载插件，完成后再安装适配版本。“安装一次、每次启动生效”不代表未来所有 PCL 版本都兼容。

自 v1.1.3 起，安装器和引导器不再匹配固定官方 EXE 哈希，插件也不再要求固定的 `2.99.0.0` 程序集版本；仍保留 PCL 程序集名称、反射绑定和补丁结构检查。运行时检查或应用失败会记录日志，并回滚本次已应用的恢复补丁。下载相关源码不变有利于复用补丁，但编译器生成名称、方法签名、IL 指令、依赖接口和启动流程仍需核验。更新覆盖启动配置时，加载入口也可能失效。插件升级与 PCL 宿主更新是不同操作；详细技术分析见源码包 `COMPATIBILITY.md`。

启动配置保留原有无关内容，并启用原版所需的现代路径处理与操作系统默认 TLS 协议。原配置会按原始字节备份，卸载时还原；如果原来没有配置文件，就移除插件创建的配置。检测到安装后手动修改配置或替换插件文件时，工具会停止并说明冲突，避免覆盖用户修改。未知文件和诊断日志不应被误删。

插件在 PCL 进程内执行代码。保持官方 EXE 不变，并不表示插件无需信任。注入器会将内嵌 DLL 写入程序目录；它们是用户明确安装的第三方代码，而不是官方组件。

## 直接升级插件

此功能属于 **1.1.4 开发版**。退出 PCL，选择已有安装后，界面会展示已安装版本和内置版本。识别出完整、可升级的旧插件时，点击“升级插件”即可更新，无需先卸载；这里的内置版本来自当前注入器，不是在线查询到的最新版本。

- 已发布 v1.1.2 / v1.1.3 的 schema 1 安装按三份插件 DLL 的精确哈希识别版本。它只识别本工具的历史载荷，不恢复 PCL EXE 的哈希白名单，也不根据文件名猜测版本。
- 升级失败时回滚三份 DLL、原始安装记录和升级前的当前配置。最初的 `.original-exe.config` 备份与 `InstalledUtc` 保留，之后卸载仍恢复首次安装前的配置。
- 升级后使用 schema 2 记录，增加 `PackageVersion` 和 `UpdatedUtc`。已禁用的插件升级后仍禁用，需要时单独启用。旧工具不认识 schema 2，会拒绝处理；后续管理和卸载应使用 1.1.4 或支持该格式的新工具。
- 已安装更高版本时不降级；同版本但构建不同、无法识别的旧自编译载荷不会自动覆盖。缺失文件、篡改或配置冲突也不会借升级自动修复，工具会说明原因。

`--install` 保持幂等，不自动升级；升级必须点击“升级插件”或使用 `--upgrade`。公开 Release v1.1.3 没有此入口，仍需卸载后重装。

## 命令行

以下入口对应 **1.1.4 开发版**。将路径替换为自己的路径，结果可写入 JSON 报告：

```text
PCL.DownloadRestore.Injector.exe --inspect "D:\PCL\Plain Craft Launcher 2.exe" --report "D:\Reports\inspect.json"
PCL.DownloadRestore.Injector.exe --install "D:\PCL\Plain Craft Launcher 2.exe" --report "D:\Reports\install.json"
PCL.DownloadRestore.Injector.exe --upgrade "D:\PCL\Plain Craft Launcher 2.exe" --report "D:\Reports\upgrade.json"
PCL.DownloadRestore.Injector.exe --uninstall "D:\PCL\Plain Craft Launcher 2.exe" --report "D:\Reports\uninstall.json"
```

以上调用直接运行 C# 注入器，不调用 PowerShell 安装脚本。报告目录需已存在，报告文件名必须尚未使用，工具不会覆盖已有报告。自动化时请等待进程退出，再读取报告与退出状态；检查操作是否可用时读取 `Status`、`CanInstall`、`CanUpgrade`、`CanUninstall`，版本与禁用状态分别见 `InstalledVersion`、`AvailableVersion`、`IsDisabled`。`--install` 不会代替 `--upgrade`。

## 源码构建

源码包包含注入器、插件、构建脚本和测试代码。构建需要 Windows、.NET SDK 及 NuGet 网络访问；最终用户无需这些开发工具。构建与打包脚本使用 Windows PowerShell 5.1，脚本路径的默认值在参数解析完成后计算。

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build-Injector.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Package-Injector.ps1
```

构建顺序为引导 DLL、插件 DLL、嵌入三份 DLL 的 C# 注入器。构建不需要官方 PCL 源码、EXE 或宿主路径参数。输出目录为 `artifacts/Injector`，本地 ZIP 为 `artifacts/PCL.DownloadRestore.Injector.zip`，源码 ZIP 为 `artifacts/PCL.DownloadRestore.Injector-Source.zip`。当前源码构建的是尚未发布的 1.1.4；公开 v1.1.3 附件仍为 `PCL.DownloadRestore.Injector-v1.1.3.zip` 和 `PCL.DownloadRestore.Source-v1.1.3.zip`，不包含直接升级功能，也不会被本地构建覆盖。

用户发行包只有一个可执行程序及说明、校验和与许可文件，不包含官方 PCL EXE、外置 DLL 或 PowerShell 脚本。PCL 原许可见 `licenses/PCL-LICENCE.txt`，Harmony 许可见 `licenses/Harmony-LICENSE.txt`。
