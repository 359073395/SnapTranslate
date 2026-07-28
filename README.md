# SnapTranslate

一个面向 Windows 的轻量截图翻译工具：像微信截图一样框选屏幕区域，然后标注、识别文字、翻译、复制或保存。

> 当前版本：`v0.1.1`（可用预览版）

## 功能

- 可自定义全局截图快捷键，默认 `Ctrl + Shift + A`
- 鼠标拖动框选当前屏幕区域
- 矩形、自由画笔、文字标注，可调整颜色和粗细
- 撤销上一条标注
- 使用 Windows 本地 OCR 识别文字
- 一键复制原文或译文
- 一键复制带标注图片，或保存为 PNG
- Google Web 翻译（实验性、无需密钥）
- OpenAI 兼容翻译接口（通过环境变量读取 API Key）

## 下载和使用

从 GitHub 的 [Releases](../../releases) 下载 `SnapTranslate-v0.1.1-win-x64.zip`，解压后运行 `SnapTranslate.exe`。发布包自带 .NET 运行时，不需要单独安装。

系统要求：

- Windows 10 2004（Build 19041）或更高版本
- 64 位 Windows
- 使用中文、日语等 OCR 前，需要在 Windows 设置中安装相应语言包

首次启动后，按 `Ctrl + Shift + A`，拖动鼠标选择区域。松开鼠标后会进入编辑器。若默认快捷键被占用，点击主界面的快捷键输入框，直接按下新的组合键，再点击“保存设置”。

## OCR 语言包

本项目调用 Windows 自带的 `Windows.Media.Ocr`，不会把截图上传到 OCR 服务器。若语言列表中没有你需要的语言：

1. 打开 Windows“设置”。
2. 进入“时间和语言”→“语言和区域”。
3. 添加对应语言，并安装其 OCR/基本输入组件。
4. 重启 SnapTranslate。

## 翻译配置

### Google Web（默认）

无需 API Key，适合快速体验。它使用非正式的网页翻译端点，可能受网络、频率限制或服务变更影响，不建议用于关键业务。

### OpenAI 兼容接口

在主界面填写兼容的 Chat Completions 地址和模型，并在启动程序前设置环境变量：

```powershell
$env:SNAPTRANSLATE_API_KEY = "你的 API Key"
.\SnapTranslate.exe
```

API Key 不写入设置文件。配置默认保存到：

```text
%APPDATA%\SnapTranslate\settings.json
```

## 隐私说明

- 截图和 OCR 默认仅在本机处理。
- 只有点击“翻译”时，识别出的文字才会发送给所选翻译服务。
- OpenAI 兼容接口的密钥只从 `SNAPTRANSLATE_API_KEY` 环境变量读取。

## 从源码构建

需要 Windows 和 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)：

```powershell
dotnet restore SnapTranslate.slnx
dotnet build SnapTranslate.slnx -c Release
dotnet publish src\SnapTranslate\SnapTranslate.csproj `
  -c Release -r win-x64 --self-contained true `
  -o artifacts\publish
```

## 当前限制

- 首版只截取鼠标所在显示器，暂不支持跨屏框选。
- 尚未提供自动更新、窗口吸附、箭头/马赛克和托盘常驻。
- Google Web 翻译是实验性功能，稳定性不作保证。

欢迎通过 Issue 提交问题和功能建议。

## 与 ShareX 的关系

SnapTranslate 不是 ShareX 官方产品。项目的 Windows OCR 处理流程参考并改写自 [ShareX 21.0.0](https://github.com/ShareX/ShareX/tree/v21.0.0)，因此采用同样的 GNU GPL v3 许可证。详情见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。

## 许可证

[GNU General Public License v3.0](LICENSE)

---

## English

SnapTranslate is a lightweight Windows screenshot translator. Press `Ctrl + Shift + A`, select a region, annotate it, run local Windows OCR, translate text, and copy or save the result.

The `v0.1.1` preview supports customizable global hotkeys, rectangle/freehand/text annotations, local OCR, Google Web translation, OpenAI-compatible translation, clipboard output, and PNG export. Windows 10 build 19041 or later is required. See the Chinese documentation above for setup details.
