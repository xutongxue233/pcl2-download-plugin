# 用 Visual Studio 打开和调试

打开仓库根目录的 **[PCL.DownloadRestore.sln](../PCL.DownloadRestore.sln)**。解决方案包含注入器、启动加载器和下载插件三个项目；编译时自动生成并嵌入插件，无需先运行脚本、下载预编译 DLL，也不需要官方 PCL 源码或 EXE。

当前版本为 **v1.1.3**，允许用户自行确认来源后选择 `.exe` 尝试注入，不再限制固定宿主 SHA-256 和程序集版本。运行时仍检查 PCL 程序集名称与实际方法 / IL 结构，不保证未知版本兼容。旧版 v1.1.2 标签与附件保留原有固定限制。

## 安装开发环境

- Windows 上的 **Visual Studio 2026，18.0 或更高版本**，建议使用当前稳定版本。微软兼容表列出的 .NET SDK `10.0.3xx` 最低 Visual Studio 版本为 18.0。[SDK 与 Visual Studio 兼容说明](https://learn.microsoft.com/en-us/dotnet/core/porting/versioning-sdk-msbuild-vs)
- 在 **Visual Studio Installer → 修改 → 工作负载**中勾选 **“.NET 桌面开发”**，并在安装详细信息中保留 **“.NET Framework 4.8 开发工具”**，包括 SDK 和目标包。这个工作负载支持 WPF 和 .NET Framework。[微软工作负载说明](https://github.com/MicrosoftDocs/visualstudio-docs/blob/main/docs/install/includes/vs-2026/workload-component-id-vs-community.md)
- 安装 **.NET SDK 10.0.301 或更高的 10.0 系列稳定版本**。仓库中的 `global.json` 使用 `latestFeature`，允许 Visual Studio 更新到同一 10.0 系列的新功能带，例如 `10.0.402`；不自动改用 .NET 11 或预览版。

这里的 .NET 10 SDK 是编译工具，程序的目标框架仍是 **.NET Framework 4.8（`net48`）**。使用注入器的普通用户不需要安装 Visual Studio 或 .NET 10 SDK。

首次还原 NuGet 包需要网络连接，Harmony 和编译用的 .NET Framework 引用程序集由项目自动还原。

## 打开、编译、启动

1. 克隆当前仓库，或解压 `PCL.DownloadRestore.Source-v1.1.3.zip`。首次发布的 v1.1.2 源码附件不包含解决方案，请使用 v1.1.3 或更新的源码。
2. 在 Visual Studio 启动页选择 **“打开项目或解决方案”**，打开 `PCL.DownloadRestore.sln`。也可以使用 **文件 → 打开 → 项目/解决方案**。
3. 等待项目加载和 NuGet 还原完成。
4. 在解决方案资源管理器中，右键 **`PCL.DownloadRestore.Injector` → 设为启动项目**。
5. 顶部配置选择 **Debug / Any CPU**。
6. 按 **F5** 编译并调试注入器；不需要断点调试时按 **Ctrl+F5**。

启动后应看到“PCL下载恢复工具”窗口。查看界面不需要选择 PCL；测试安装、卸载功能时，再选择自己确认来源的 PCL EXE。若目录中已安装 v1.1.2 插件，先卸载再安装 v1.1.3，重复注入不会自动升级旧 DLL。

正常编译会自动完成以下步骤：

1. 编译 `PCL.PluginBootstrap`。
2. 编译 `PCL.DownloadRestore`，还原并输出 Harmony。
3. 将对应 Debug/Release 配置的三个 DLL 嵌入 `PCL.DownloadRestore.Injector.exe`。

注入器通过 `ProjectReference` 声明构建顺序，但不会把这些 DLL 当作自己的运行时外部依赖。输出目录不需要旁置插件 DLL，也无需手动准备载荷目录。Harmony 是实际使用的补丁依赖，由 NuGet 自动还原后嵌入 EXE。

Release 输出为 `src/Injector/bin/Release/net48/PCL.DownloadRestore.Injector.exe`；Debug 输出位于对应的 `Debug/net48/` 目录。

## 常用修改位置

| 内容 | 文件或目录 |
| --- | --- |
| 界面布局 | `src/Injector/MainWindow.xaml` |
| 界面事件与状态 | `src/Injector/MainWindow.xaml.cs` |
| 配色、按钮、输入框样式 | `src/Injector/UI/PclStyles.xaml` |
| 检测、安装和卸载 | `src/Injector/Installation/` |
| PCL 启动时加载插件 | `src/Bootstrap/` |
| 恢复下载功能的运行时补丁 | `src/DownloadRestore/` |

## 生成分享用的 ZIP

Visual Studio 的“生成”负责编译程序。制作包含说明、许可证和校验文件的完整用户包时，在仓库根目录运行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build-Injector.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Package-Injector.ps1
```

第一条命令直接构建注入器项目及项目引用，核对内嵌 DLL，并准备 `artifacts/Injector/`；第二条生成 `artifacts/PCL.DownloadRestore.Injector.zip` 和 `artifacts/PCL.DownloadRestore.Injector-Source.zip`。这些命令不需要指定 PCL 路径。

源码包排除 EXE、DLL、`bin/`、`obj/`、`refs/`、`artifacts/` 和运行日志，解压后可重新构建。完整流程见[构建、打包与发布](RELEASING.md)。本地打包不会自动创建 GitHub Release，已发布 v1.1.2 附件名称与行为保持不变。

## 排查加载或构建问题

- **找不到兼容的 .NET SDK**：在终端运行 `dotnet --list-sdks`，确认有 `10.0.301` 或更高的 `10.0.x` 稳定 SDK；安装后重启 Visual Studio。只安装 .NET Runtime 不够。
- **项目无法加载、缺少 WPF 设计器或 .NET Framework 目标包**：重新打开 Visual Studio Installer，核对“.NET 桌面开发”和“.NET Framework 4.8 开发工具”。本仓库使用 SDK 风格的 `net48` 项目，不要把它改建成新的 .NET 10 WPF 项目。
- **提示类库无法直接启动**：将 `PCL.DownloadRestore.Injector` 设为启动项目。
- **缺少插件资源**：使用“生成 → 重新生成解决方案”，并确认三个项目均已加载。不要单独复制 Injector 文件夹作为完整源码，也不要在配置管理器中禁用另外两个项目的构建。
- **NuGet 还原失败**：先恢复对 `nuget.org` 的网络访问，然后在解决方案上执行“还原 NuGet 程序包”。

## 验证范围

本页的 F5 / Ctrl+F5 步骤是开发操作说明，不代表已对每个 Visual Studio 版本完成交互调试验证。解决方案构建、程序集检查与实际界面运行是不同的检查。

安装、运行时补丁和下载功能的复现方法见[测试说明](../tests/README.md)，历史结果见[验证记录](../INJECTOR-VALIDATION.md)。1.1.3 允许尝试其他宿主版本，不等于这些版本已通过功能测试。
