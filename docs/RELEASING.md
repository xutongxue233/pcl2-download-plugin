# 版本发布

在 Windows、.NET SDK 10.0.301 和 Windows PowerShell 5.1 环境执行。不要将官方 PCL EXE、`refs/`、本机日志或构建中间文件提交到仓库。

## 构建和验证

1. 确认 `src/Injector/PCL.DownloadRestore.Injector.csproj` 中的版本号，并更新 `CHANGELOG.md`、README 和验证记录。引导器与插件有独立的程序集版本，不能只为对齐界面版本而修改。
2. 执行 `scripts/Build-Injector.ps1`，构建并验证三份内嵌 DLL。
3. 执行 `scripts/Test-Package.ps1` 和 `scripts/Package-Injector.ps1`。
4. 使用已核验的官方 PCL EXE，执行 `tests/InjectorIntegration/Run.ps1 -HostExecutable '<官方 EXE 路径>'` 和 `tests/Verify-InjectorPackage.ps1 -HostExecutable '<官方 EXE 路径>'`。
5. 将源码 ZIP 解压到新的空目录，在没有 `refs/` 和预构建产物的情况下重新执行 `scripts/Build-Injector.ps1` 和 `scripts/Package-Injector.ps1`。
6. 修改运行时补丁后补测真实 PCL 启动和对应功能；修改界面后补测实际窗口。不要将历史下载验证描述为本次重新运行的结果。

## GitHub Release

先提交并推送源码，等待 GitHub Actions 的 Windows 构建通过，再为该提交创建版本标签。Release 附件使用包含工具版本号的名称：

| 本地输出 | v1.1.2 附件名 |
| --- | --- |
| `artifacts/PCL.DownloadRestore.Injector-2.99.0.0.zip` | `PCL.DownloadRestore.Injector-v1.1.2-PCL2.99.0.0.zip` |
| `artifacts/PCL.DownloadRestore.Injector-Source.zip` | `PCL.DownloadRestore.Source-v1.1.2.zip` |

对两个最终 ZIP 生成 `SHA256SUMS.txt`，格式为每行一个 `SHA256  文件名`，并随 Release 一起上传。不要在计算校验值后重新打包。

Release 说明应包含安装包选择、支持的 PCL 版本、变化和实际验证范围。发布后核对标签对应的提交、公开下载链接及远端附件哈希。GitHub 自动生成的源码归档之外，仍提供经过独立构建验证的源码 ZIP。
