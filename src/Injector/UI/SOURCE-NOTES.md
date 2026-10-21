# PCL WPF 样式移植说明

本目录将工作区携带的 PCL 2.99.0.0 WPF 视觉模板适配为标准 WPF 控件，供独立的第三方下载恢复注入器使用。安装核心及官方 PCL 可执行文件不在此处改动。

## 原始文件

下面路径均相对于工作区根目录 `pcl2-plugins`：

- `PCL-2.99.0.0/Plain Craft Launcher 2/Application.xaml`：`ColorBrush1` 至 `ColorBrush8` 的命名、灰度、半透明白色及输入框样式。正文为 13 WPF 设备无关像素。
- `PCL-2.99.0.0/Plain Craft Launcher 2/FormMain.xaml`：主窗口复用 48 像素顶栏、6 像素窗口圆角及最小化、关闭的原始矢量路径；按工具需求移除顶部导航，将左侧标志替换为“PCL下载恢复工具”文字。
- `PCL-2.99.0.0/Plain Craft Launcher 2/Modules/ModSecret.vb`：ThemeRefresh 与 ThemeRefreshMain 的实际默认运行时配色，Hue=210、Sat=85、LightAdjust=0。它们会覆盖 Application.xaml 的初始主题色。本字典使用这些运行时结果，保留顶栏三段与页面三段渐变的位置及方向。
- `PCL-2.99.0.0/Plain Craft Launcher 2/Modules/Base/ModBase.vb`：FromHSL2 的亮度补偿曲线及 FromHSL、ClampToByte。字典的主题 RGB 值按这套转换计算，并按原实现取整；没有用普通 HSL 转换替代。
- `PCL-2.99.0.0/Plain Craft Launcher 2/Resources/Font.ttf`：原 PCL English 字体保留在 `UI/Resources/Font.ttf`，作为来源对照资源。1.1.2 起主界面统一采用系统 `Microsoft YaHei UI, Segoe UI`，不再使用该嵌入字体绘制界面；日志保留 `Consolas, Microsoft YaHei UI`。
- `PCL-2.99.0.0/Plain Craft Launcher 2/Controls/MyButton.xaml`：双 Border 结构、圆角 3、描边 1、居中文字。
- `PCL-2.99.0.0/Plain Craft Launcher 2/Controls/MyButton.xaml.vb`：普通按钮使用 ColorBrush1，高亮按钮使用 ColorBrush2；悬停文字和描边使用 ColorBrush3，底色使用 ColorBrush7；按下缩放到 0.955。
- `PCL-2.99.0.0/Plain Craft Launcher 2/Controls/MyTextBox.vb`：圆角 3；悬停描边 ColorBrush4、焦点描边 ColorBrush3，底色均为 ColorBrush7；禁用状态使用灰色。
- `PCL-2.99.0.0/Plain Craft Launcher 2/Controls/MyCard.vb`：卡片白色 Alpha 245、圆角 5、阴影半径 3、阴影不透明度 0.07；标题左边距 15、上边距 12、13 像素粗体。
- `PCL-2.99.0.0/Plain Craft Launcher 2/Controls/MyIconButton.xaml` 和 `.xaml.vb`：圆形标题栏按钮及 White 主题，悬停背景白色 Alpha 50。
- `PCL-2.99.0.0/Plain Craft Launcher 2/Controls/MyTextButton.vb`：普通、悬停、按下文字分别使用 ColorBrush1、ColorBrush3、ColorBrush4。

发行包中的 `licenses/PCL-LICENCE.txt` 随这些移植样式保留。

## 适配边界

原控件的 VB 日志、自定义事件、动画调度器、主题解锁及启动器业务未引入。Button 和 TextBox 使用标准 WPF 类型及 ControlTemplate，保留键盘导航、回车/空格点击和 UI Automation 语义。添加可见的键盘焦点反馈，按下缩放通过 WPF 模板触发器实现；不依赖原项目的动画服务。

输入框保留 `PART_ContentHost`，空文本时显示主窗口通过 Tag 提供的占位提示。卡片的自定义 MyDropShadow 改用 WPF DropShadowEffect，以同样的圆角、阴影半径和透明度表达；内容布局由主窗口定义。阴影由同级 `PclCardShadow` 背景承担，前景内容层不设置 Effect，避免文字参与阴影合成；这与原 MyCard 的分层方式一致。

1.1.2 将文字格式化统一为 `Ideal`，根窗口显式设置 `Grayscale` 抗锯齿，移除输入框中强制 ClearType 的提示。保留布局取整和边框像素对齐，只调整字体与文字渲染，不修改系统设置。类型定义参考微软文档：[TextRenderingMode](https://learn.microsoft.com/en-us/dotnet/api/system.windows.media.textrenderingmode)、[TextFormattingMode](https://learn.microsoft.com/en-us/dotnet/api/system.windows.media.textformattingmode)。

## 使用

在 Window.Resources 的 ResourceDictionary.MergedDictionaries 中加载 `UI/PclStyles.xaml`。若主窗口本身位于 UI 目录，使用相对路径 `PclStyles.xaml`。

```xml
<Button Style="{StaticResource PclPrimaryButton}" Content="安装插件" />
<Button Style="{StaticResource PclButton}" Content="卸载插件" />
<TextBox Style="{StaticResource PclTextBox}" Tag="选择或拖入 PCL.exe" />
<Border Style="{StaticResource PclCard}"><Grid /></Border>
<Button Style="{StaticResource PclTitleButton}" Content="×" />
<Button Style="{StaticResource PclLinkButton}" Content="重新检查" />
```

另外提供 `PclText`、`PclCardTitle`、`PclFont`、`PclTitleBackground`、`PclPageBackground`，原始 `ColorBrush*` 资源名，以及 `HalfWhite` 和 `Gray` 简写。标题栏图标若使用 Path，其 Stroke 或 Fill 应绑定父 Button.Foreground，以响应悬停状态。
