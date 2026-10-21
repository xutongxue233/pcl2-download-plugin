# 构建、打包与发布

本流程面向维护者。普通用户从 [GitHub Releases](https://github.com/xutongxue233/pcl2-download-plugin/releases/latest) 下载用户包即可。

当前版本为 **v1.1.3（2026-10-21）**；v1.1.2 标签和附件保留旧版行为与名称，不被本版覆盖。构建或生成 ZIP 不会自动发布到 GitHub。

## 构建环境

- Windows 与 .NET SDK **10.0.301 或更高的 10.0 系列稳定版本**。[global.json](../global.json) 的 `latestFeature` 允许同系列功能带更新。
- Windows 自带的 **PowerShell 5.1**；构建验证需要加载 .NET Framework 程序集，使用下面的 `powershell.exe` 命令。
- 首次构建可访问 NuGet。项目目标为 .NET Framework 4.8，所需引用程序集和 Harmony 自动还原。

编译不需要官方 PCL EXE、原 PCL 源码或预编译 DLL。三个核心项目通过项目引用确定构建顺序，并将引导器、恢复插件和 Harmony 嵌入注入器。

## 编译与生成用户包

以下命令均从仓库根目录执行。

仅编译解决方案：

```powershell
dotnet build .\PCL.DownloadRestore.sln -c Release
```

输出程序：`src/Injector/bin/Release/net48/PCL.DownloadRestore.Injector.exe`。

准备完整的用户包和源码包：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build-Injector.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Package-Injector.ps1
```

`Build-Injector.ps1` 直接构建注入器项目及其项目引用，核对三个内嵌 DLL，检查单 EXE 输出，再将说明、许可和校验文件整理到 `artifacts/Injector/`。它不接受宿主路径参数，也不依赖事先准备的载荷目录。

`Package-Injector.ps1` 生成：

| 文件 | 内容 |
| --- | --- |
| `artifacts/PCL.DownloadRestore.Injector.zip` | 单 EXE、使用说明、验证说明、许可和校验文件 |
| `artifacts/PCL.DownloadRestore.Injector-Source.zip` | 解决方案、源码、构建 / 测试脚本及文档 |

源码 ZIP 不应包含 EXE、DLL、`bin/`、`obj/`、`refs/`、`artifacts/` 或运行日志。必要的 Harmony 依赖在编译时通过 NuGet 还原，不作为预编译 DLL 提交。测试代码与第三方许可应保留。

## 发布前验证

1. 核对 `src/Injector/PCL.DownloadRestore.Injector.csproj` 的工具版本号和 `CHANGELOG.md`。引导器与插件有独立的程序集版本，不应只为对齐界面版本而修改。
2. 完成上述构建与打包，检查输出包和校验文件。
3. 将源码 ZIP 解压到新的空目录，在没有 `refs/` 或预构建产物的情况下，再执行相同的构建与打包命令，确认完整源码可以独立复现。
4. 自行提供 [official-build.json](../tests/official-build.json) 对应的官方 PCL 回归样本，在隔离副本中运行安装与发行包检查。这些测试的固定哈希只用于确保测试数据一致，不是生产安装器的准入限制：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\InjectorIntegration\Run.ps1 -HostExecutable 'D:\PCL\Plain Craft Launcher 2.exe'
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tests\Verify-InjectorPackage.ps1 -HostExecutable 'D:\PCL\Plain Craft Launcher 2.exe'
```

5. 修改运行时补丁时，补测真实 PCL 启动与相关功能；修改界面时，补测实际窗口。运行时测试准备见 [tests/README.md](../tests/README.md)。只有实际运行过的检查才能写入本次验证记录，历史下载证据应保留其原版本与日期。

GitHub Actions 的 [Windows 构建](../.github/workflows/build.yml)检查源码构建、内嵌 DLL 与打包，并上传工作流产物。它不包含官方 PCL EXE，因此不代表安装、运行时功能或图形界面测试通过。

## GitHub Release

1. 更新说明文档，提交并推送源码，确认目标提交的 GitHub Actions 构建通过。
2. 对同一个目标提交建立版本标签，创建对应 Release。不应改写旧版标签，或将已有 v1.1.2 附件替换为新的代码。
3. 为最终 ZIP 添加实际工具版本号。v1.1.3 的附件名称如下：

| 本地文件 | v1.1.3 附件名 |
| --- | --- |
| `PCL.DownloadRestore.Injector.zip` | `PCL.DownloadRestore.Injector-v1.1.3.zip` |
| `PCL.DownloadRestore.Injector-Source.zip` | `PCL.DownloadRestore.Source-v1.1.3.zip` |

4. 对最终命名的两个 ZIP 生成 `SHA256SUMS.txt`，每行格式为 `SHA256  文件名`，与两个 ZIP 一起上传。计算后不要重新打包。
5. 发布说明列出用户应下载的附件、行为变化、实际验证的 PCL 样本和已知限制。取消固定宿主哈希限制不等于已经验证所有 PCL 版本。
6. 发布后核对标签提交、公开下载链接及远端附件哈希。除 GitHub 自动生成的源码归档外，保留经过独立构建验证的源码 ZIP。

现有 v1.1.2 用户附件仍为 `PCL.DownloadRestore.Injector-v1.1.2-PCL2.99.0.0.zip`，源码附件为 `PCL.DownloadRestore.Source-v1.1.2.zip`。这些旧版附件不受当前构建目录的命名变更影响。
