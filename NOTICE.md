# 来源与许可说明

本工具是第三方基于 PCL 独立制作的下载恢复插件与安装工具，不是 PCL 官方发布或背书。

## PCL

原作者：**龙腾猫跃**。原项目：[Meloong-Git/PCL](https://github.com/Meloong-Git/PCL)，[支持原作者](https://meloong.com/afd/a/LTCat)。

界面样式、资源及所参考的下载逻辑来自 PCL。仓库根目录 [LICENCE](LICENCE) 与 [licenses/PCL-LICENCE.txt](licenses/PCL-LICENCE.txt) 保留原《PCL 分发有限许可》说明和《PCL 存储库合理使用指南》。本仓库未将这些内容改授 MIT 等其他许可；具体范围以原声明及其引用的许可文本为准。

界面资源出处见 [SOURCE-NOTES.md](src/Injector/UI/SOURCE-NOTES.md)。官方 PCL EXE、其提取的依赖和游戏文件不包含在本仓库或发行包中。

## Harmony

运行时补丁使用 [Harmony](https://github.com/pardeike/Harmony) 2.4.2，通过 NuGet `Lib.Harmony` 获取。其许可原文保留在 [licenses/Harmony-LICENSE.txt](licenses/Harmony-LICENSE.txt)。注入器将运行所需的 Harmony DLL 内嵌到单 EXE 中。

## 构建依赖

.NET SDK 与 `Microsoft.NETFramework.ReferenceAssemblies` 仅用于构建。界面使用 Windows 系统提供的字体，仓库不再携带未使用的 PCL English 字体，也不分发微软雅黑、Segoe UI 或 Consolas 字体文件。
