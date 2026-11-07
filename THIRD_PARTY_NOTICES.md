# Third-party notices

## ShareX

The OCR language discovery and recognition flow in:

```text
src/SnapTranslate/Services/OcrService.cs
```

was adapted and rewritten from `ShareX.Tools/Tools/OCR/OCRHelper.cs` in [ShareX 21.0.0](https://github.com/ShareX/ShareX/blob/v21.0.0/ShareX.Tools/Tools/OCR/OCRHelper.cs).

ShareX is copyright © ShareX Team and contributors and is distributed under the GNU General Public License version 3.

Lingxi Capture is an independent project and is not affiliated with or endorsed by the ShareX project.

## Material Design Icons

The 22 vector paths in `src/SnapTranslate/Controls/ToolbarIcon.cs` are an unchanged
subset of Material Design Icons by Pictogrammers and contributors, previously
distributed through MahApps.Metro.IconPacks.Material 6.2.1. The full icon-pack
runtime dependency is no longer included. The control implementation is new;
the underlying icon artwork is unchanged.

- Project: https://github.com/Templarian/MaterialDesign
- Icons license: Apache License, Version 2.0 (see `LICENSE.material-icons.txt`).
- Pictogrammers license statement: https://github.com/Templarian/MaterialDesign/blob/master/LICENSE

## Platform APIs

Lingxi Capture uses Windows APIs supplied by the operating system, including Windows Media OCR, Windows Graphics Imaging, clipboard, global hotkeys, and screen capture APIs.
