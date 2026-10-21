> 历史开发文档：以下内容保留最初的 PowerShell 脚本安装方式，仅供开发和旧安装记录兼容性验证。新用户请使用 [README 中的 C# 图形注入器](../README.md)。下述命令均在仓库根目录运行；旧脚本发行包不作为当前用户发行入口。

# PCL 2.99 下载恢复插件

本包为原版 PCL 2.99.0.0 增加本地插件启动入口，并在每次启动时加载下载恢复插件。**不修改、不替换、不包含官方 EXE，也不发行重新编译的 PCL 主程序。** 官方程序仍然从你选择的位置启动。

PCL 原作者为 **龙腾猫跃**，原项目：[Meloong-Git/PCL](https://github.com/Meloong-Git/PCL)，[支持原作者](https://meloong.com/afd/a/LTCat)。本插件是独立的第三方适配，不是官方发布或授权背书；恢复逻辑复用 PCL 原有页面和安装引擎。

恢复范围包括 Minecraft 版本列表与下载安装、已安装实例的 Minecraft 版本修改、在线整合包安装、本地整合包导入安装。Mod、资源包、光影等原有下载功能继续由 PCL 处理。

目前只支持已验证的官方 PCL 2.99.0.0 程序，文件可以改名，SHA256 必须为：

```text
6B6E1260CB5A1D6A303855F1C60CAB3D0BC1AEBBA7E40A964180188B271E7386
```

插件是非官方扩展，不代表 PCL 作者提供支持。保持官方 EXE 不变，并不等于插件不执行代码：启动时仍会在 PCL 进程内执行本包 DLL。请只安装自己信任、可核对来源和 SHA256 的插件包。

## 安装与卸载

安装器使用 Windows 自带的 Windows PowerShell 5.1；双击入口会以 STA 模式打开文件选择框。请保持 PCL 所在目录可写。

1. 退出需要安装插件的 PCL。将整个插件 ZIP 解压到任意可写文件夹，保留目录结构。
2. 双击 `Install.cmd`，在弹出的文件选择框里选择原来的 PCL `.exe`。安装不需要管理员权限，不修改注册表、系统启动项或其他程序。
3. 以后照常打开该 PCL 程序；插件每次启动自动加载。保留完整安装目录即可，不需要重复注入。
4. 卸载时先退出 PCL，再双击 `Uninstall.cmd`，选择同一个 PCL `.exe`。卸载恢复安装前的 `.exe.config` 原始字节；如果原来没有配置文件，就删除插件创建的配置文件。

也可以在 Windows PowerShell 5.1 中运行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -STA -File .\scripts\Install.ps1 -LauncherPath "D:\PCL\Plain Craft Launcher 2.exe"
powershell.exe -NoProfile -ExecutionPolicy Bypass -STA -File .\scripts\Uninstall.ps1 -LauncherPath "D:\PCL\Plain Craft Launcher 2.exe"
```

重复安装会检查现有安装并提示已安装，不覆盖配置或生成重复副本；若插件目录中存在 `disabled` 标记，重新安装会清除该标记，以便下次启动重新启用插件。安装后请不要移动或改名 EXE；需要移动时先卸载，移动后再安装。多个 PCL EXE 共用同一目录的情况，请先分开到各自文件夹。

## 安装会增加什么

```text
原版 PCL.exe                         保持不变
原版 PCL.exe.config                  合并启动入口与 .NET 路径兼容配置
PCL.PluginBootstrap.dll             负责每次启动加载插件
PCL/Plugins/DownloadRestore/
  PCL.DownloadRestore.dll           下载恢复逻辑
  0Harmony.dll                      方法补丁依赖
  .install-state.json               安装清单与哈希记录
  .original-exe.config              原配置的逐字节备份（原配置存在时）
```

安装器会拒绝其他 AppDomain manager、未登记的同名 DLL、未登记的插件目录、不支持的 EXE、正在运行的 PCL，以及路径中的符号链接或目录联接，避免误覆盖其他扩展。备份和安装清单请保留到卸载完成。

启动入口早于 PCL 自身初始化，配置因此将 `Switch.System.IO.UseLegacyPathHandling`、`Switch.System.IO.BlockLongPaths`、`Switch.System.Net.DontEnableSchUseStrongCrypto`、`Switch.System.Net.DontEnableSystemDefaultTlsVersions` 设为 `false`，保留原版需要的现代路径处理和操作系统默认 TLS 协议，避免初始化时退回旧路径规则或旧 HTTPS 协议。其他兼容开关继续保留，没有 `startup` 节时会明确声明 .NET Framework 4.8。卸载时这些配置一并还原为安装前的原始内容。

为使 .NET Framework 能加载从 ZIP 解压的插件，安装器会对复制后的插件 DLL 清除 Windows 下载来源标记；操作仅作用于插件文件，不改变其 SHA256，也不处理官方 EXE。

卸载只移除本次安装登记且内容未变的 DLL、依赖和安装记录，保留其他文件及 `bootstrap.log` 诊断日志。日志与 `disabled` 标记不会阻止重新安装；未知用户文件会保留，但重新安装前需要移出插件专用目录。若部分 DLL 已被删除，卸载仍可恢复原配置。若安装后手动编辑过 `.exe.config` 或替换过插件文件，卸载会停止并说明冲突，不覆盖这些修改。请先备份自己的修改，再恢复安装后的配置或插件文件，然后重试卸载。

“一次安装、每次启动生效”限于这一安装目录和已支持的程序版本。PCL 更新后，插件可能失效或需要适配；安装器不会给未验证版本强行安装。即使 EXE 已更新，仍可运行卸载脚本，只要原配置与插件记录没有冲突。更新前先卸载插件更方便。

## 源码构建与打包

构建环境需要 Windows、可运行 `dotnet` 命令的 .NET SDK，以及网络访问以还原 NuGet 依赖。插件面向 .NET Framework 4.8，运行时与原版 PCL 一致；能正常运行该原版 PCL 的电脑无需为本插件另装 SDK。

运行项目提供的 `scripts/Build.ps1`，将编译产物放入 `artifacts/payload`。构建插件不需要 PCL 源码或主程序；可选的 `-LauncherPath` 只用于检查官方 EXE 哈希。完成测试后运行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Package.ps1
```

输出为 `artifacts/PCL.DownloadRestore-2.99.0.0.zip` 和 `artifacts/PCL.DownloadRestore-Source.zip`。插件压缩包包含插件、安装与卸载工具、说明、许可声明及 `SHA256SUMS.txt`，排除官方 EXE、引用程序集、安装记录和日志。源码包排除构建产物、下载工具、测试临时文件和官方 EXE，方便检查实现和自行构建。

本地官方 ZIP 已与 PCL 官方仓库 `2.99.0.0` 标签的 Git blob 校验值核对；核对记录见包内 `official-build.json`（源码目录下为 `tests/official-build.json`）。

PCL 源码许可声明见 `licenses/PCL-LICENCE.txt`，Harmony 许可见 `licenses/Harmony-LICENSE.txt`。请保留这些声明。
