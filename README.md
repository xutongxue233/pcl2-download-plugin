# PCL 下载恢复工具

为 PCL 恢复 Minecraft 下载和整合包安装入口的第三方 C# / WPF 工具。安装一次，随原 PCL 启动自动加载；支持检测、查看日志与卸载，**不修改或重新编译官方 PCL EXE**。

[![GitHub Release](https://img.shields.io/github/v/release/xutongxue233/pcl2-download-plugin)](https://github.com/xutongxue233/pcl2-download-plugin/releases/latest)
[![Build injector](https://github.com/xutongxue233/pcl2-download-plugin/actions/workflows/build.yml/badge.svg)](https://github.com/xutongxue233/pcl2-download-plugin/actions/workflows/build.yml)

[下载](https://github.com/xutongxue233/pcl2-download-plugin/releases/latest) · [使用说明](INJECTOR.md) · [编译打包](#编译与打包) · [Visual Studio](docs/VISUAL-STUDIO.md) · [更新记录](CHANGELOG.md)

PCL 原作者为 **龙腾猫跃**：[原项目](https://github.com/Meloong-Git/PCL) · [支持原作者](https://meloong.com/afd/a/LTCat)。本工具是第三方独立二次创作，与 PCL 官方无隶属或授权背书关系。

> **v1.1.3** 允许用户自行确认来源后选择 EXE 尝试注入，取消固定宿主 SHA-256 和程序集版本限制，仍检查实际反射绑定与补丁结构，不保证所有 PCL 版本兼容。已安装 **v1.1.2** 插件时，请先卸载，再安装 v1.1.3；旧版标签和附件保留原有行为。

## 特性

- 恢复 Minecraft 版本列表、下载安装、已安装实例的版本修改，以及在线 / 本地整合包安装入口。
- 单 EXE 图形工具，内嵌引导器、恢复插件与 Harmony，无需另外下载 DLL。
- 自动检测安装状态；已注入时禁用重复操作，重开工具仍能识别。
- 可展开操作日志和插件启动日志，卸载时恢复原配置、保留游戏文件。
- 适配 PCL 的 WPF 视觉样式，界面保留文件选择、状态和注入 / 卸载操作。

## 下载与使用

| 场景 | 环境 |
| --- | --- |
| 使用工具 | Windows、.NET Framework 4.8、可写的 PCL 目录 |
| 自行编译 | Windows、.NET SDK 10.0.301 或更高的 10.0 系列稳定版 |
| 生成 ZIP | 上述编译环境，以及 Windows 自带的 PowerShell 5.1 |

1. 从 [v1.1.3 Release](https://github.com/xutongxue233/pcl2-download-plugin/releases/tag/v1.1.3) 下载 `PCL.DownloadRestore.Injector-v1.1.3.zip` 并解压。`PCL.DownloadRestore.Source-v1.1.3.zip` 是供查看和编译的源码包，不是安装包。
2. 退出 PCL，双击 `PCL.DownloadRestore.Injector.exe`，选择、拖入或输入 PCL EXE 路径。
3. 点击“注入插件”。完成后照常启动原 PCL，以后无需重复运行注入器。
4. 卸载时先退出 PCL，再选择同一个程序，点击“卸载插件”。更新插件需先卸载旧版，再安装新版。

PCL 请从[官方入口](https://meloong.com/PCL)或自己确认可信的来源获取。工具未配置发行者代码签名；发行包提供校验文件，源码公开可查。普通用户无需 Visual Studio、SDK 或 PowerShell 安装脚本。

## 编译与打包

构建从源码自动生成引导器和恢复插件，并将它们与 NuGet 还原的 Harmony 嵌入注入器。**不需要官方 PCL EXE、PCL 源码或预编译 DLL，也不需要手动复制依赖。**

### 1. 获取源码

```powershell
git clone https://github.com/xutongxue233/pcl2-download-plugin.git
cd pcl2-download-plugin
```

下面所有命令均在仓库根目录执行。首次构建需要联网还原 NuGet 包；[global.json](global.json) 允许 .NET 10 同系列功能带更新，例如 10.0.402，不自动使用 .NET 11 或预览版。

### 2. 编译程序

```powershell
dotnet build .\PCL.DownloadRestore.sln -c Release
```

可执行文件位于 `src/Injector/bin/Release/net48/PCL.DownloadRestore.Injector.exe`。

使用 Visual Studio 时，安装 **Visual Studio 2026 的“.NET 桌面开发”工作负载**及 **.NET Framework 4.8 开发工具**，直接打开 [PCL.DownloadRestore.sln](PCL.DownloadRestore.sln)，将 `PCL.DownloadRestore.Injector` 设为启动项目。完整步骤见 [Visual Studio 开发说明](docs/VISUAL-STUDIO.md)。

### 3. 打包为可分享的 ZIP

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build-Injector.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Package-Injector.ps1
```

第一条重新构建 Release，检查三个内嵌 DLL，并准备程序、说明和许可；第二条生成用户包与源码包。

| 输出 | 用途 |
| --- | --- |
| `artifacts/Injector/PCL.DownloadRestore.Injector.exe` | 可独立运行的单 EXE |
| `artifacts/Injector/` | EXE、使用说明、许可与校验文件 |
| `artifacts/PCL.DownloadRestore.Injector.zip` | 分享给用户的完整安装包 |
| `artifacts/PCL.DownloadRestore.Injector-Source.zip` | 可重新编译的源码包 |

源码包排除 EXE、DLL、`bin/`、`obj/`、`refs/`、`artifacts/` 和运行日志。Harmony 是运行时补丁所需依赖，会在构建时还原并嵌入，不作为仓库内的预编译文件分发。

构建成功不等于已完成目标 PCL 的功能验证。[测试说明](tests/README.md)介绍安装、运行时和发行包检查；[发布说明](docs/RELEASING.md)介绍从干净源码复现、打包与发布前验证。GitHub Actions 检查 Windows 构建和打包，不自动运行官方 PCL 或发布 Release。

## 项目结构

```text
PCL.DownloadRestore.sln  Visual Studio 解决方案
global.json             .NET SDK 选择规则
src/Injector/           WPF 界面、安装 / 卸载和状态检测
src/Bootstrap/          随 PCL 启动的插件引导器
src/DownloadRestore/    PCL 反射绑定与运行时补丁
scripts/                构建、打包、测试参考提取和共享函数
tests/                  安装、运行时与发行包验证
docs/                   开发说明与发布流程
licenses/               第三方许可
```

## 常见问题

**会修改官方程序吗？** 安装器增加程序目录中的插件文件与启动配置，官方 EXE 保持不变。配置、插件和备份的完整性检查继续保留。具体文件见[安装说明](INJECTOR.md#安装内容与持久生效)。

**未来 PCL 更新能继续使用吗？** 1.1.3 允许直接尝试，但仍检查程序集名称、实际方法与 IL 结构。匹配或应用失败会记录日志，并回滚本次已应用的恢复补丁；不承诺所有版本兼容。详细条件见[兼容性分析](COMPATIBILITY.md)。

**为何新工具仍显示已注入？** 完整安装会被识别并阻止重复写入；它不会自动替换旧插件。升级、移动、改名或更新 PCL 前，先卸载旧插件，再安装。

**安装成功就代表下载恢复了吗？** “已注入”表示安装完整。请在本次启动日志中确认 `DownloadRestore ready: 5 runtime patches applied`，并实际验证需要的功能；历史日志不代表当前启动成功。

## 文档与验证

- [使用与命令行](INJECTOR.md)
- [Visual Studio 开发](docs/VISUAL-STUDIO.md) · [打包与发布](docs/RELEASING.md)
- [兼容性分析](COMPATIBILITY.md) · [测试复现](tests/README.md)
- [注入器验证记录](INJECTOR-VALIDATION.md) · [插件验证记录](VALIDATION.md)

验证记录保留各次测试的版本与范围，其中 v1.1.2 的界面、安装和 Minecraft 1.12.2 下载结果属于历史证据，不代表 1.1.3 已兼容其他版本。

## 来源与许可

PCL / 龙腾猫跃的署名与[赞助链接](https://meloong.com/afd/a/LTCat)随项目保留。PCL 许可与合理使用指南见 [LICENCE](LICENCE) 和 [licenses/PCL-LICENCE.txt](licenses/PCL-LICENCE.txt)，Harmony 许可见 [licenses/Harmony-LICENSE.txt](licenses/Harmony-LICENSE.txt)。

WPF 样式的具体来源与适配范围见 [SOURCE-NOTES.md](src/Injector/UI/SOURCE-NOTES.md)。本仓库不包含官方 PCL EXE；第三方内容遵循各自许可与声明，详见 [NOTICE.md](NOTICE.md)。
