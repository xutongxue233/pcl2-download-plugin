# PCL 2.99 下载恢复注入器

这是一个用 C# 和 WPF 编写的 Windows 图形工具。双击 `PCL.DownloadRestore.Injector.exe`，选择官方 PCL 程序，即可安装、检查或卸载下载恢复插件。用户使用时**不需要 PowerShell、管理员权限或额外下载 DLL**；三个插件 DLL 已嵌入注入器内部。

界面直接移植并适配原 PCL 源码中 `Application.xaml`、`FormMain`、`MyButton` 和 `MyCard` 的样式，保留程序选择、状态与注入／卸载操作。界面来源及适配说明见源码包中的 `src/Injector/UI/SOURCE-NOTES.md`；安装与卸载功能保持不变。

1.1.2 优化文字显示：界面统一使用微软雅黑 UI，启用灰度抗锯齿和自然字距，卡片阴影与文字分层；日志保留等宽字体，字号、布局及配色沿用上一版。

**官方 PCL EXE 保持原样。** 注入器只增加本地插件文件并合并启动配置，之后照常打开 PCL，每次启动都会自动加载插件。关闭 PCL 后可随时用同一工具卸载，恢复安装前的配置。

PCL 原作者为 **龙腾猫跃**，原项目：[Meloong-Git/PCL](https://github.com/Meloong-Git/PCL)，[支持原作者](https://meloong.com/afd/a/LTCat)。本工具是独立第三方适配，不是 PCL 官方发布或背书。恢复内容复用原版的页面和安装引擎，包括 Minecraft 版本列表与下载安装、Minecraft 版本修改、在线整合包安装和本地整合包导入。

## 使用

1. 退出目标 PCL，将注入器 ZIP 解压到可写文件夹。
2. 双击 `PCL.DownloadRestore.Injector.exe`，选择需要恢复功能的官方 PCL `.exe`。
3. 选择程序后会自动校验，并显示当前状态。点击“注入插件”，完成后照常打开原来的官方 PCL，无需再次运行注入器。已完整安装时显示“已注入，无需重复操作”，注入按钮变为禁用的“已注入”；重新打开工具也能识别。已停用的插件则显示“启用插件”。
4. 卸载时，先退出 PCL，再用注入器选择同一个程序，点击“卸载插件”。

标题栏左侧为“PCL下载恢复工具”，不再显示顶部导航项。主界面保留程序选择、当前状态和注入／卸载操作，底部显示适配版本与第三方工具说明。“查看日志”展开本次检测、安装和卸载记录，以及所选 PCL 的插件启动日志；展开期间每 2 秒刷新，可选中文字复制，点击“收起日志”折叠。启动日志只读取最近 64 KiB，历史成功记录不代表当前仍安装，当前状态以重新检测结果为准。切回注入器窗口时会自动刷新状态；检测到 PCL 正在运行时，每 2 秒刷新一次，退出 PCL 后即可继续操作。

运行环境为 Windows 和 .NET Framework 4.8，与目标 PCL 的运行时一致；使用注入器不需要 .NET SDK。安装目录必须可写。程序未配置发行者代码签名；请通过来源、校验值和随附源码判断是否信任该工具，不应将文件名或界面当作官方认证。

目前只支持已核验的官方 **PCL 2.99.0.0**。文件可以在安装前改名，但 SHA256 必须是：

```text
6B6E1260CB5A1D6A303855F1C60CAB3D0BC1AEBBA7E40A964180188B271E7386
```

官方构建核对记录见 `original-build.json`，本次验证结果见 `VALIDATION.md`，包内文件校验值见 `SHA256SUMS.txt`。不支持其他 EXE 哈希时会拒绝安装；不会通过替换或重新编译 PCL 来绕过此检查。

## 安装内容与持久生效

```text
官方 PCL.exe                         不修改
官方 PCL.exe.config                  本地启动入口与 .NET 兼容设置
PCL.PluginBootstrap.dll             每次启动加载插件
PCL/Plugins/DownloadRestore/
  PCL.DownloadRestore.dll           下载恢复逻辑
  0Harmony.dll                      方法补丁依赖
  .install-state.json               文件清单与校验值
  .original-exe.config              原配置存在时的精确备份
```

这是程序目录内的持久加载，不修改全局注册表或开机启动项，也不依赖注入器一直运行。安装后请保留这些文件；需要移动、改名或更新 PCL 时，建议先卸载插件，完成后再安装适配版本。“安装一次、每次启动生效”不代表未来所有 PCL 版本都兼容。

当前版本严格核对官方 EXE 的 SHA256 和程序集版本，因此未来更新即使没有修改下载源码，也不会自动放行。若只在原路径替换 EXE，旧插件会跳过未适配的程序；如果更新还覆盖启动配置，加载入口也可能失效。下载相关源码不变有利于复用补丁，但编译器生成名称、方法签名、IL 指令、依赖接口和启动流程仍需核验。新版本通过回归验证后，可发布适配的插件／引导器，无需重新编译官方 PCL。当前插件更新流程为先卸载旧插件，再安装新版。详细技术分析见源码包 `COMPATIBILITY.md`。

启动配置保留原有无关内容，并启用原版所需的现代路径处理与操作系统默认 TLS 协议。原配置会按原始字节备份，卸载时还原；如果原来没有配置文件，就移除插件创建的配置。检测到安装后手动修改配置或替换插件文件时，工具会停止并说明冲突，避免覆盖用户修改。未知文件和诊断日志不应被误删。

插件在 PCL 进程内执行代码。保持官方 EXE 不变，并不表示插件无需信任。注入器会将内嵌 DLL 写入程序目录；它们是用户明确安装的第三方代码，而不是官方组件。

## 命令行

图形界面之外，也支持以下入口。将路径替换为自己的路径，结果可写入 JSON 报告：

```text
PCL.DownloadRestore.Injector.exe --inspect "D:\PCL\Plain Craft Launcher 2.exe" --report "D:\Reports\inspect.json"
PCL.DownloadRestore.Injector.exe --install "D:\PCL\Plain Craft Launcher 2.exe" --report "D:\Reports\install.json"
PCL.DownloadRestore.Injector.exe --uninstall "D:\PCL\Plain Craft Launcher 2.exe" --report "D:\Reports\uninstall.json"
```

以上调用直接运行 C# 注入器，不调用 PowerShell 安装脚本。报告目录需已存在，报告文件名必须尚未使用，工具不会覆盖已有报告。自动化时请等待进程退出，再读取报告与退出状态；检查是否可安装或卸载时，请读取 JSON 中的 `Status`、`CanInstall`、`CanUninstall` 字段。

## 源码构建

源码包包含注入器、插件、构建脚本和测试代码。构建需要 Windows、.NET SDK 及 NuGet 网络访问；最终用户无需这些开发工具。构建与打包脚本使用 Windows PowerShell 5.1，脚本路径的默认值在参数解析完成后计算。

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build-Injector.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Package-Injector.ps1
```

构建顺序为插件 DLL、引导 DLL、嵌入三份 DLL 的 C# 注入器。构建不需要官方 PCL 源码或 EXE；可选的 `-LauncherPath` 仅用于校验已支持的官方程序。输出目录为 `artifacts/Injector`，发行 ZIP 为 `artifacts/PCL.DownloadRestore.Injector-2.99.0.0.zip`，源码 ZIP 为 `artifacts/PCL.DownloadRestore.Injector-Source.zip`。

用户发行包只有一个可执行程序及说明、校验和与许可文件，不包含官方 PCL EXE、外置 DLL 或 PowerShell 脚本。PCL 原许可见 `licenses/PCL-LICENCE.txt`，Harmony 许可见 `licenses/Harmony-LICENSE.txt`。
