# SnapTranslate

一个面向 Windows 的轻量截图翻译工具：像微信截图一样框选屏幕区域，然后标注、识别文字、翻译、复制或保存。

> 当前版本：`v0.2.2`（可用预览版）

## 功能

- 可自定义全局截图快捷键，默认 `Ctrl + Shift + A`
- 鼠标拖动框选当前屏幕区域
- 矩形、自由画笔、文字标注，可调整颜色和粗细
- 撤销上一条标注
- 使用 Windows 本地 OCR 识别文字，默认自动选择更合适的已安装识别引擎
- 根据 OCR 文字坐标，把译文直接覆盖在图片对应位置
- 支持印尼语 / Bahasa Indonesia，适合 TikTok 图片文案
- 一键复制原文或译文
- 一键复制带标注图片，或保存为 PNG
- Google Web 翻译（实验性、无需密钥）
- OpenAI 兼容翻译接口（支持基础 URL、加密保存 API Key 和连接测试）

## 下载和使用

从 GitHub 的 [Releases](../../releases) 下载 `SnapTranslate-v0.2.2-win-x64.zip`，解压后运行 `SnapTranslate.exe`。发布包自带 .NET 运行时，不需要单独安装。

系统要求：

- Windows 10 2004（Build 19041）或更高版本
- 64 位 Windows
- 使用中文、日语等 OCR 前，需要在 Windows 设置中安装相应语言包

首次启动后，OCR 语言保持“自动识别（推荐）”，按 `Ctrl + Shift + A`，拖动鼠标选择区域。松开鼠标后会进入编辑器。若默认快捷键被占用，点击主界面的快捷键输入框，直接按下新的组合键，再点击“保存设置”。

需要制作印尼 TikTok 图片时，在主界面把“目标语言”设为“印尼语 / Bahasa Indonesia”并保存。截图后点击“翻译到图片”，译文会自动覆盖到识别出的原文字块位置；可继续标注、复制图片或保存 PNG。“清除译文”可以恢复原图。

## OCR 语言包

本项目调用 Windows 自带的 `Windows.Media.Ocr`，不会把截图上传到 OCR 服务器。“自动识别（推荐）”会用本机已安装的多个 OCR 引擎尝试截图，并根据文字数量、覆盖范围和文字体系选择更合适的结果；中文、英文/印尼语和中英混排无需每次手动切换。印尼语使用拉丁文字识别引擎。

自动识别仍然依赖 Windows 已安装的 OCR 引擎。例如，要准确识别中文，电脑上仍需安装中文 OCR 组件。若语言列表中没有你需要的语言：

1. 打开 Windows“设置”。
2. 进入“时间和语言”→“语言和区域”。
3. 添加对应语言，并安装其 OCR/基本输入组件。
4. 重启 SnapTranslate。

## 翻译配置

### Google Web（默认）

无需 API Key，适合快速体验。它使用非正式的网页翻译端点，可能受网络、频率限制或服务变更影响，不建议用于关键业务。

### OpenAI 兼容接口

在主界面选择“OpenAI 兼容接口”，然后填写：

- 接口地址：可以填写 `https://example.com/v1` 基础地址，也可以填写完整的 `/v1/chat/completions` 地址。
- API Key：输入框会遮罩显示，保存时使用 Windows DPAPI 按当前用户加密。
- 模型：填写中转站支持的模型名称。

点击“测试连接”可以在截图前验证地址、Key 和模型是否可用。为兼容旧的自动化配置，程序仍会在界面未填写 Key 时尝试读取 `SNAPTRANSLATE_API_KEY` 环境变量。

配置默认保存到：

```text
%APPDATA%\SnapTranslate\settings.json
```

## 隐私说明

- 截图和 OCR 默认仅在本机处理。
- 只有点击“翻译到图片”时，识别出的文字才会发送给所选翻译服务。
- OpenAI 兼容接口的 API Key 不以明文写入设置文件，而是使用 Windows 当前用户的数据保护机制加密；只有同一个 Windows 用户可以解密。

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
- 图片译文使用深色半透明文字卡覆盖原文，不包含生成式图片修复或背景重绘。
- 自动 OCR 只能在 Windows 已安装的识别引擎之间选择，未安装对应文字语言包时准确率会受限。
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

The `v0.2.2` preview adds automatic selection among installed Windows OCR engines. It also supports customizable global hotkeys, rectangle/freehand/text annotations, position-aware translation overlays, Indonesian/Bahasa Indonesia, clipboard output, PNG export, and OpenAI-compatible relay settings with Windows-encrypted API key storage. Windows 10 build 19041 or later is required. See the Chinese documentation above for setup details.
