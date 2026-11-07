# v0.3.2 本机轻量化验证

日期：2026-11-07，Windows x64。以下数据来自本机验证；发布前补充验收记录见文末。

## 图标问题的原因

实际发现两个同时运行的进程：旧 v0.2.5 便携程序位于 Downloads，v0.3.1 安装版位于用户 Apps 目录。
用户截图中的旧标题和默认图标来自 v0.2.5，不是新版 LI 图标丢失。排查时保留旧文件，仅退出重复进程；用户后续授权在验证新版后清理被替代的旧程序副本。

v0.3.2 进一步统一窗口资源 URI、托盘图标、安装快捷方式图标和 AppUserModelID，并在标题中显示版本。
真实安装版的标题栏已人工观察到 LI 图标和 v0.3.2 标题。开始菜单快捷方式目标和图标均指向已安装 EXE。

## 体积与内存的取舍

- 移除整套 MahApps.Material 依赖，只保留实际使用的 22 个相同矢量图标；与原库逐个比对路径完全一致。
- 不删 .NET 运行组件，不强制裁剪 WPF，不移除任何截图工具。
- 不发布调试 PDB，但构建目录仍可保留开发符号。
- 否决 `.NET EnableCompressionInSingleFile`：虽然可显著减小逻辑文件大小，本机启动测试却出现私有内存约 180 MiB、工作集约 341 MiB；未将该设置保留在最终代码中。
- 最终安装器使用可取消、默认勾选的 Windows 应用二进制透明压缩，只处理本应用 EXE/DLL。在不支持的文件系统上跳过，不能依赖该优化减少非 NTFS 磁盘占用。
- 便携 ZIP 不保存透明压缩状态；解压后的逻辑目录仍约 189 MiB，不应把安装版的物理占用宣传成便携版文件大小。

## 最终实测

| 对象 | 实测值 |
|---|---:|
| v0.3.1 发布目录（未透明压缩） | 202,347,069 字节，约 193 MiB |
| v0.3.2 发布目录逻辑大小 | 198,654,249 字节，约 189 MiB |
| v0.3.2 本机安装目录物理占用，含卸载器等 | 77,257,847 字节，73.68 MiB |
| v0.3.2 安装包 | 54,379,249 字节，约 51.9 MiB |
| v0.3.2 便携 ZIP | 78,203,666 字节，约 74.6 MiB |

物理占用通过 Windows `GetCompressedFileSizeW` 对应用文件求和读取；不是仅把 ZIP 大小作为安装大小。
比较基线是上版发布目录，安装目录还含卸载器，故该表用于量级判断而不是严格的同目录统计实验。

三次 Windows 透明压缩版启动到输入空闲测试为 912、1282、886 ms；短时私有内存约 99 MiB，工作集约 183 MiB。
上版短时私有内存约 71–99 MiB；未声称透明压缩本身能降低 RAM，也未声称这些本机热启动数据适用于所有机器。
最终安装版一次观察为工作集 178.1 MiB、私有内存 94.5 MiB。长期后台和连续大量截图未作本轮耐久压测。

## 验证范围

- Release 构建：0 警告、0 错误。
- 冒烟测试：22 图标在 16/24/32 px 均非空白；主窗口/高级编辑/文字对话框图标资源解析；五类绘制和撤销；合成图、PNG 编码；原生自动 OCR；无 MahApps 程序集依赖。
- 合成 OCR 样本可识别 `Lingxi Capture`；数字尾字符存在 OCR 误识别，不能宣称本地 OCR 总能逐字准确。
- 原路径静默升级返回 0，个人设置文件哈希保持，开机启动仍指向原安装路径及 `--background`。
- 默认透明压缩选项在实际升级中执行，日志返回 0。
- 已真实观察新版标题栏 LI 图标。任务栏像素结果未独立截图取证；相同 WPF 窗口图标和快捷方式应用身份均已设置。
- 已实际点击关闭按钮并确认：主窗口消失，仍保留一个后台进程，没有旧版 SnapTranslate 进程。
- 当前 Computer Use 工具不枚举隐藏于任务栏的截图浮层，因此本轮没有完成浮层上回车复制/双击/ESC 的完整真实鼠标键盘验收；截图合成和工具创建已用本机代码级冒烟测试覆盖，测试后已重启应用清理状态。
- 翻译请求逻辑未在本轮改动，未重新发送真实截图文字到远端。
- 当前保存的 Ctrl+Shift+A 显示已被其他软件占用；未擅自替换用户快捷键，等待用户选择。界面“开始截图”入口可用。

## 发布前补充验收

- 新增可选本机回归 `--clipboard`：直接调用截图窗口的实际回车/ESC 处理逻辑，确认回车后原生剪贴板图片为所选尺寸、取消不覆盖剪贴板。测试前保存剪贴板数据到内存，结束后恢复，不输出或写入数据内容。
- 原生回车复制、ESC 取消、本地 OCR 与其余冒烟测试全部通过。该检查是本机集成测试，不代表已完成真实双击手势的 UI 自动化。
- 新增可选 `--live-translation`：使用当前已保存的本机配置，仅发送三行合成文字，验证行数对齐、非空输出与缓存一致。首次请求 4541 ms，未记录接口地址、模型或凭据。
- 云端构建新增 WPF 冒烟测试，跳过依赖本机语言包的 OCR，也不运行需要个人配置的远端翻译或剪贴板检查；这些检查在本机单独完成。
- 旧程序清理仅针对已核实的 Downloads 旧便携程序和项目 artifacts 内被替代的发布目录/安装包；保留个人配置、开发工具依赖和独有验收证据。使用回收站，可恢复，不删除 GitHub 历史版本或源码。

## 复验

```powershell
& 'G:\GitHub\.tools\dotnet\dotnet.exe' build SnapTranslate.slnx -c Release
& 'G:\GitHub\.tools\dotnet\dotnet.exe' run --project tests/SnapTranslate.SmokeTests -c Release
# 仅在本机需要额外验收时启用（默认不读取个人翻译配置、不触碰剪贴板）：
& 'G:\GitHub\.tools\dotnet\dotnet.exe' run --project tests/SnapTranslate.SmokeTests -c Release -- --clipboard --live-translation
```

官方参考：

- https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview
- https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/incompatibilities
- https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/compact
