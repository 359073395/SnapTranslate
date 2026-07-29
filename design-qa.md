# SnapTranslate v0.2.3 Design QA

## Reference and implementation

- Selected reference: `G:\Codex\UserData\.codex\generated_images\019fa791-d06d-7392-8489-525378604f83\call_8KkAPJjh2YgY9sv7u95ndDAv.png`
- Reference dimensions: 1487 × 1058
- Implementation: `G:\GitHub\SnapTranslate\src\SnapTranslate\Views\CaptureOverlayWindow.xaml`
- Quick-toolbar evidence: `G:\GitHub\SnapTranslate\artifacts\ux-qa-v0.2.3\01-quick-toolbar.png`
- Translation evidence: `G:\GitHub\SnapTranslate\artifacts\ux-qa-v0.2.3\02-translation-popover.png`
- Advanced-editor evidence: `G:\GitHub\SnapTranslate\artifacts\ux-qa-v0.2.3\03-advanced-editor.png`
- Full comparison: `G:\GitHub\SnapTranslate\artifacts\ux-qa-v0.2.3\comparison-full.png`
- Focused toolbar comparison: `G:\GitHub\SnapTranslate\artifacts\ux-qa-v0.2.3\comparison-toolbar.png`

## Test viewport

- Windows 11 desktop, 1536 × 864 logical viewport
- Display density: 125% (1920 × 1080 physical display)
- Tested states: initial selection, OCR result, Indonesian translation result, completion by Enter, completion by double-click, and Advanced Editor handoff

## Full-view comparison

The reference and implementation use the same visual hierarchy: a dimmed desktop, a blue dashed selection frame with handles and a size badge, a compact dark toolbar attached to the selection, a blue primary completion action, and a separated Advanced Editor action. The implementation repositions floating UI above or below the selection to avoid viewport clipping.

## Focused-region comparison

The focused comparison normalizes the selection-and-toolbar region to equal-size panels. Tool order, icon rhythm, separators, copy/save/cancel grouping, completion emphasis, and the Advanced Editor separation match the selected direction. The copied-content controls appear inside their corresponding source-text and translated-text sections.

## Fidelity review

- Typography: Segoe UI with compact white labels remains legible at 125% scaling.
- Spacing and layout: consistent 64–92 px tool widths, 8 px toolbar padding, grouped separators, and safe viewport margins.
- Colors and tokens: dark navy surface, subtle cool-gray border, white iconography, blue selection/primary action, and red annotation color align with the reference.
- Image quality and icons: real vector Material icons are used; there are no emoji or raster placeholder icons.
- Copy and content: “复制图片” stays in the image-action group; “复制原文” and “复制译文” stay beside their corresponding result sections.
- Accessibility and states: toolbar controls expose automation names and tooltips; selected tools have a blue state; busy, success, and error status feedback are provided.

## Interaction verification

- Global shortcut opened the quick-capture overlay.
- Dragging created and preserved a selection while toolbar actions were used.
- OCR produced recognized text and exposed “复制原文”.
- Translation used automatic OCR language detection and produced Indonesian text, on-image overlays, and “复制译文”.
- Enter copied the final rendered image to the clipboard and closed the overlay.
- Double-click inside the selection copied the final rendered image to the clipboard and closed the overlay.
- Advanced Editor opened the existing full editor with the current annotated/translated composite preserved.

## Comparison history

1. Initial behavior check found that the first click of a double-click restarted the selection, preventing completion. The selection hit-test was changed so a click inside the ready selection is preserved; the retest closed the overlay and placed a 749 × 425 image on the clipboard.
2. Enter completion was tested separately and placed a 937 × 575 image on the clipboard.
3. OCR, relay-backed Indonesian translation, on-image translation, and Advanced Editor handoff were exercised in the running Windows app.

## Findings

- P0: none
- P1: none after the double-click fix
- P2: none
- P3: the generated reference and real monitor use different aspect ratios and background content; the implementation intentionally changes popover placement when needed to remain visible.

final result: passed
