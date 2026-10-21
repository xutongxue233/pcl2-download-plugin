# 测试复现

在 Windows、.NET Framework 4.8 环境中运行。此次使用 .NET SDK 10.0.301；不需要重编译 PCL，也不需要补齐 MeloongCore 源码。

先在本项目目录执行：

```powershell
.\scripts\Build.ps1
.\scripts\Prepare-References.ps1 -HostExecutable 'D:\你的启动器目录\Plain Craft Launcher 2.exe'
```

参考文件提取器验证官方 EXE 的 SHA-256，通过 PE 元数据和原始资源字节读取 13 个嵌入库；不会执行 EXE。`refs` 仅供测试，发布包不包含这些文件。支持的官方 EXE SHA-256 为：

```text
6B6E1260CB5A1D6A303855F1C60CAB3D0BC1AEBBA7E40A964180188B271E7386
```

## 运行时功能集成

```powershell
.\tests\RuntimeIntegration\Run.ps1
```

此脚本自动编译测试程序，在 `artifacts/runtime-integration` 的独立副本中加载真实官方程序集和真实插件。测试专用 Harmony 替身截住底层安装器、文件选择器和提示框，因此不会下载游戏、安装整合包或显示对话框。

122 项断言覆盖五处补丁、初始化时机、重复初始化、第三处补丁失败后的完整回滚及重试、七种整合包分发方法、根目录和嵌套清单、参数传递、成功及失败时 ZIP 释放、错误和取消处理、手动选择导入、在线整合包点击委托及其他资源行为。结果写入 `artifacts/runtime-integration/results.txt`。

## 自动加载与实际 HTTPS

先退出全部 PCL 窗口，避免官方单实例锁干扰：

```powershell
.\src\Bootstrap\Tests\Test-Bootstrap.ps1 -Network
```

此脚本自动编译引导器与测试程序，验证配置自动加载、缺失/禁用插件、未知 EXE 拒绝、维护命令跳过，以及真实官方 EXE 在原始 Startup 处理完毕后、主窗口创建前同步加载插件。网络探针请求官方与 BMCL 的版本清单，使用正常证书校验。测试专用载荷完成断言后用 `ExitProcess` 结束自己的隔离进程，以免运行主界面；生产插件不包含此操作。

结果写入 `artifacts/bootstrap-tests/results.json`，实际宿主日志位于其 `official-host-startup/PCL/Plugins/DownloadRestore/bootstrap.log`。去掉 `-Network` 可跳过 HTTPS 请求。

安装配置显式保留 .NET Framework 4.8 的四项行为：现代路径处理、长路径、强加密、操作系统默认 TLS。真实 EXE 的早期 AppDomainManager 加载会使框架目标信息为空；未指定这四项时，实测出现路径非法及 `Ssl3, Tls` 旧协议默认值。指定后，真实宿主 Startup 成功，两个 HTTPS 清单均返回 200。配置修改仅作用于此 EXE，卸载时恢复原配置。

独立小型网络对照探针可另行运行：

```powershell
.\tests\RuntimeIntegration\NetworkProbe\Run.ps1
```

其结果位于 `artifacts/tls-probes`。该小程序自身的框架目标信息完整，因此不代表官方 EXE 的早期加载行为；真实宿主的网络证据以 `Test-Bootstrap.ps1 -Network` 为准。

## 独立 EXE 注入器

先准备 `artifacts/payload`，再执行：

```powershell
.\tests\InjectorIntegration\Run.ps1
```

脚本编译并引用真实的 WPF 注入器，读取 EXE 内嵌的三个载荷文件，在独立的官方 EXE 副本上运行 17 组、99 项断言。测试不会启动 PCL 或游戏下载。

覆盖正常安装/重装/禁用后启用/卸载、重开注入器后识别已安装并禁止重复注入、重复安装不修改文件字节、原配置字节级恢复、四项兼容开关合并、未知 EXE 拒绝、配置及 DLL 修改保护、损坏或缺失安装记录、缺失 DLL 卸载、更新后清理、目录联接拒绝，以及锁定配置/载荷文件造成的安装和卸载事务回滚。还验证了旧 PowerShell schema 1 安装记录与原生安装器双向兼容。

独立命令行测试将注入器 EXE 单独复制到新目录，进程 PATH 指向空目录，再执行安装、检查和卸载，确认不需要旁边的 DLL、载荷目录或 PATH 中的 PowerShell。现有报告和插件安装元数据路径也经过拒绝写入测试。旧脚本互操作测试单独启动 Windows PowerShell 5.1；这是兼容性验证工具，不是原生注入器的运行依赖。

结果位于 `artifacts/injector-integration/results.json` 和 `results.txt`。各次运行的隔离目录会保留以便排查，原始 EXE 和用户游戏目录不受影响。

## 最终 ZIP 核验

生成注入器二进制与源码 ZIP 后执行：

```powershell
.\tests\Verify-InjectorPackage.ps1
```

脚本核验二进制 ZIP 内全部 SHA256SUMS、单 EXE 分发约束及源码包不含宿主和生成产物。随后仅提取注入器 EXE，使用空 PATH 的五个独立进程完成检查、安装、检查、卸载、检查，验证官方 EXE 哈希始终不变、原配置逐字节恢复。不会启动 PCL 主程序或桌面操作。报告与各步 JSON 保存在 `artifacts/injector-package-verification`。
