# 灵犀截图 v0.3.0 Design QA

## Reference and implementation

- Source visual truth: `G:\GitHub\SnapTranslate\artifacts\ux-qa-v0.3.0\source-li-icon.png`
- Source dimensions: 1024 × 1024, transparent PNG
- Implementation: `G:\GitHub\SnapTranslate\src\SnapTranslate\MainWindow.xaml`
- Implementation screenshot: `G:\GitHub\SnapTranslate\artifacts\ux-qa-v0.3.0\implementation-lingxi-main.png`
- Combined comparison input: `G:\GitHub\SnapTranslate\artifacts\ux-qa-v0.3.0\combined-brand-comparison.png`

## Visual comparison

The supplied LI artwork is used directly, without redrawing or stylistic substitution. The blue rounded square, white LI letterform, speech-notch detail, and orange insight dot remain intact in the executable icon and the main-window brand lockup.

The combined comparison confirms:

- the source icon remains crisp and proportionally correct at the 56 × 56 header size;
- the same icon is present in the title bar, taskbar, system tray, executable metadata, and installer executable;
- the new name “灵犀截图” is the dominant product label;
- `LINGUAL INSIGHT · TRANSLATE WITH INSIGHT` and the Chinese supporting line are readable without collision or truncation;
- `#2A5CFF` is carried into the existing dark interface as the primary action and focus color;
- the existing screenshot, OCR, Indonesian translation, and advanced-editing settings remain visually subordinate to the brand header.

## Layout and accessibility

- Windows 11 desktop, 747 × 811 captured application viewport.
- The existing dark settings design was retained instead of replaced.
- Major settings remain grouped into high-contrast cards with a visible vertical scroll affordance.
- “开机自动启动灵犀截图” is exposed as an accessible checkbox.
- “退出程序” explicitly distinguishes full exit from the title-bar close action.
- The accessibility tree exposes the product name, brand tagline, screenshot action, startup state, save action, and exit semantics.

## Functional verification

- Release build and self-contained Windows x64 publish completed successfully.
- The Chinese Inno Setup installer compiled successfully.
- The installer displayed a selectable destination page and installed successfully to the non-default test path `C:\Users\w'k'r\Apps\LingxiCapture`.
- Installed executable metadata reports product name `灵犀截图` and file version `0.3.0.0`.
- Legacy settings migrated to `%APPDATA%\LingxiCapture\settings.json`.
- OCR remained `auto`, target language remained Indonesian, and the OpenAI-compatible API key remained DPAPI-encrypted rather than plaintext.
- The translation connection test completed successfully.
- The default HKCU startup entry points to the selected install path with `--background`.
- A background launch kept one process running without showing the main window.
- Closing the title bar removed the window from the taskbar while keeping the process active.
- Relaunching while hidden restored the existing window and did not create a second process.
- The pre-existing WeChat-style floating toolbar, circle, arrow, mosaic, OCR, translation, clipboard completion, and Advanced Editor behavior remain included in the same build.

## Findings

- P0: none
- P1: none
- P2: none
- P3: the installer uses Inno Setup’s standard wizard illustration on its content pages; the installer executable and installed software use the supplied LI icon correctly.

final result: passed
