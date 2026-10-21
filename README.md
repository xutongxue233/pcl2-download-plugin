# PCL 下载恢复工具（第三方插件）

为官方 **PCL 2.99.0.0** 恢复 Minecraft 下载和整合包安装入口的 C# / WPF 工具。安装一次后，照常启动原来的 PCL 即可自动加载插件；支持状态检测和卸载。

**PCL 原作者：龙腾猫跃** · [原项目](https://github.com/Meloong-Git/PCL) · [支持原作者](https://meloong.com/afd/a/LTCat)

本项目是第三方基于 PCL 独立进行二次创作的插件工具，与 PCL 官方无隶属或授权背书关系。它复用原版页面与安装引擎，**不修改、替换、重新编译或分发官方 PCL EXE**。

[下载最新版本](https://github.com/xutongxue233/pcl2-download-plugin/releases/latest) · [使用说明](INJECTOR.md) · [更新记录](CHANGELOG.md) · [兼容性分析](COMPATIBILITY.md)

## 功能

- 恢复 Minecraft 版本列表、下载安装和已安装实例的 Minecraft 版本修改入口。
- 恢复在线整合包安装及本地整合包导入，保留 PCL 原有的 Mod、资源包和光影下载处理。
- 单 EXE 图形工具，内嵌引导器、恢复插件及 Harmony 依赖；使用时无需 PowerShell、.NET SDK 或额外下载 DLL。
- 自动识别已安装状态。完整安装后显示“已注入”，禁用重复注入；重新打开工具仍可识别，停用的插件可重新启用。
- “查看日志”展示当前操作和插件启动记录，展开期间自动刷新。
- 界面移植并适配 PCL 的 WPF 样式，保留文件选择、状态、注入与卸载；1.1.2 优化字体、抗锯齿及卡片阴影分层。

## 下载与使用

运行需要 Windows 和 **.NET Framework 4.8**。PCL 所在目录需要可写，通常无需管理员权限。

1. 从 [Releases](https://github.com/xutongxue233/pcl2-download-plugin/releases/latest) 下载 `PCL.DownloadRestore.Injector-v1.1.2-PCL2.99.0.0.zip`，解压到任意可写文件夹。`Source` 压缩包供查看与构建源码，不是安装包。
2. 退出目标 PCL，双击 `PCL.DownloadRestore.Injector.exe`。
3. 选择、拖入或输入官方 PCL `.exe` 的路径，工具会自动检查程序版本与安装状态。
4. 显示可以注入后，点击“注入插件”。完成后关闭工具，照常打开原 PCL；以后每次启动都会自动加载，无需重复注入。
5. 卸载时先退出 PCL，再打开工具选择同一个程序，点击“卸载插件”。原配置会还原，游戏文件和诊断日志保留。

Release 的 `SHA256SUMS.txt` 用于核对两个 ZIP；安装包内部也附有各文件的校验值和完整说明。工具目前未配置发行者代码签名；源码可供审查，插件会在 PCL 进程内执行第三方代码，请从可信来源获取。

### 支持的官方程序

当前仅支持已核验的 **PCL 2.99.0.0**，安装前可以更改 EXE 文件名，但文件内容必须符合以下 SHA-256：

```text
6B6E1260CB5A1D6A303855F1C60CAB3D0BC1AEBBA7E40A964180188B271E7386
```

PCL 官方下载入口：[meloong.com/PCL](https://meloong.com/PCL)。对应历史构建可在 [官方仓库的 2.99.0.0 标签](https://github.com/Meloong-Git/PCL/tree/2.99.0.0) 中查找；[核验记录](tests/official-build.json)记录了该标签下官方 ZIP 与 EXE 的校验信息。官网今后提供的新版本不一定适配，工具不会给未知 EXE 强行安装。

可选校验命令：

```powershell
Get-FileHash -LiteralPath 'D:\PCL\Plain Craft Launcher 2.exe' -Algorithm SHA256
```

## 安装原理与恢复

工具通过目标程序的 `.exe.config` 配置 .NET Framework `AppDomainManager`，在 PCL 启动时加载本地引导器，再由恢复插件应用 Harmony 运行时补丁。它不依赖注入器常驻，也不设置系统开机启动项或全局注册表项。

```text
PCL.exe                              官方程序，保持不变
PCL.exe.config                       本地加载入口及 .NET 路径 / TLS 兼容设置
PCL.PluginBootstrap.dll              插件引导器
PCL/Plugins/DownloadRestore/
  PCL.DownloadRestore.dll            下载恢复插件
  0Harmony.dll                       运行时补丁依赖
  .install-state.json                安装记录及校验值
  .original-exe.config               原配置逐字节备份（原配置存在时）
  bootstrap.log                      插件启动诊断日志（启动后生成）
```

安装前会检查 EXE、配置和现有文件，拒绝未知同名插件文件、其他加载器、路径链接或正在运行的目标程序。卸载按安装记录恢复原配置；原本没有配置时移除本工具创建的配置。若配置或插件文件被手动修改，工具会报告冲突，不覆盖这些修改。备份与安装记录应保留至卸载完成。

“已注入”表示安装记录、配置和插件文件通过检查。实际启动是否应用成功，可在“查看日志”中确认当前这次启动出现：

```text
DownloadRestore ready: 5 runtime patches applied
```

历史成功日志不代表当前仍然安装。需要移动、改名或更新 PCL 时，请先卸载，再对支持的新位置或版本安装；多个 PCL 程序应放在各自独立目录。

## 更新与兼容性

**一次安装持续生效，适用于同一安装目录中的受支持程序；不保证未来所有 PCL 版本兼容。**

当前安装器、引导器与插件会核对官方 EXE 的哈希，插件还核对程序集身份。即使未来下载相关源码不变，整个 EXE 的哈希、编译器生成名称、方法签名、IL 结构、依赖接口或启动流程仍可能变化。旧版本不会自动接受新的 EXE。

如果新版本经过检查和回归测试仍兼容，可以复用恢复逻辑并发布新的适配版本，用户继续使用官方 PCL，无需重编译主程序。现有插件升级方式为**先卸载旧插件，再安装新版**。更新覆盖配置时可能产生冲突，详情见[兼容性分析](COMPATIBILITY.md)。

## 开发与构建

在 Windows 上安装 **.NET SDK 10.0.301**（版本由 `global.json` 固定，允许同一功能版本内的补丁更新），并允许访问 NuGet。以下脚本通过 **Windows PowerShell 5.1** 执行；构建注入器不需要 PCL 源码或官方 EXE，SDK 也不是最终用户的运行依赖。

```powershell
git clone https://github.com/xutongxue233/pcl2-download-plugin.git
cd pcl2-download-plugin
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build-Injector.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Package-Injector.ps1
```

构建顺序为引导器、恢复插件、嵌入三份 DLL 的注入器。主要输出：

| 路径 | 内容 |
| --- | --- |
| `artifacts/Injector/` | 面向用户的单 EXE、说明、许可和校验文件 |
| `artifacts/PCL.DownloadRestore.Injector-2.99.0.0.zip` | 本地用户发行包；GitHub 上传时添加工具版本号 |
| `artifacts/PCL.DownloadRestore.Injector-Source.zip` | 源码包，排除官方程序、构建产物与运行日志 |

测试需要自己提供上面哈希对应的官方 EXE。以下检查在隔离副本上运行，不启动 PCL 或下载游戏：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\InjectorIntegration\Run.ps1 -HostExecutable 'D:\PCL\Plain Craft Launcher 2.exe'
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Verify-InjectorPackage.ps1 -HostExecutable 'D:\PCL\Plain Craft Launcher 2.exe'
```

运行时补丁、真实启动及网络验证的准备步骤见 [tests/README.md](tests/README.md)。自动化调用支持 `--inspect`、`--install`、`--uninstall` 和 JSON 报告，详见[命令行说明](INJECTOR.md#命令行)。

### 项目结构

```text
src/Injector/            C# / WPF 界面、安装事务、状态检测与内嵌载荷
src/Bootstrap/           AppDomainManager 引导与启动钩子
src/DownloadRestore/     PCL 反射绑定与五处运行时补丁
scripts/                构建、打包及开发兼容脚本
tests/                  安装、运行时与发行包验证
licenses/               PCL 与 Harmony 原始许可声明
docs/                   其他开发文档
```

旧版 `Install.cmd`、`Uninstall.cmd` 及 PowerShell 安装脚本仅用于开发和既有安装记录兼容性验证。新用户使用图形注入器；历史说明保存在 [docs/LEGACY-SCRIPT-INSTALL.md](docs/LEGACY-SCRIPT-INSTALL.md)。

## 验证范围

已在 Windows 11 x64 / .NET Framework 4.8 环境完成下列验证，详细记录见 [INJECTOR-VALIDATION.md](INJECTOR-VALIDATION.md) 和 [VALIDATION.md](VALIDATION.md)：

- 17 组安装集成测试、99 项断言：安装、重开识别、重复注入、卸载、配置恢复、冲突保护及失败回滚。
- 日志读取 18 项边界检查；最终发行包完整性与独立进程安装 / 卸载检查。
- 图形界面的注入、已注入状态、日志展开 / 收起、重开识别及卸载实测；1.1.2 另核对字体与布局。
- 相同插件核心的 122 项运行时断言，以及官方 PCL 启动后应用五处补丁。
- 相同插件核心实际下载 Minecraft 1.12.2，客户端、资源索引和 1,290 个唯一资源对象校验通过。

核心功能证据与界面更新证据分开记录，并非每次界面修改都重新下载游戏。尚未覆盖所有 Windows 环境、游戏版本、正版账户登录、游戏运行或大型整合包完整安装；下载源可用性取决于原服务。

## 来源与许可

- **PCL / 龙腾猫跃**：[原项目](https://github.com/Meloong-Git/PCL)、[赞助链接](https://meloong.com/afd/a/LTCat)。PCL 的许可与合理使用指南保存在 [LICENCE](LICENCE) 和 [licenses/PCL-LICENCE.txt](licenses/PCL-LICENCE.txt)，请在再分发时保留署名和声明。
- **WPF 视觉模板**：基于 PCL 2.99.0.0 的按钮、卡片、输入框和主题实现适配；具体来源与修改范围见 [SOURCE-NOTES.md](src/Injector/UI/SOURCE-NOTES.md)。
- **Harmony**：运行时补丁依赖，其 MIT 许可保存在 [licenses/Harmony-LICENSE.txt](licenses/Harmony-LICENSE.txt)。

本仓库公开注入器、引导器及恢复插件源码，不包含官方 PCL EXE。第三方内容遵循各自的许可与声明，详见 [NOTICE.md](NOTICE.md)。
