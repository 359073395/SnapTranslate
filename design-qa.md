# SnapTranslate v0.2.4 Design QA

## Reference and implementation

- Source visual truth: `G:\GitHub\SnapTranslate\artifacts\ux-qa-v0.2.4\source-wechat-toolbar.png`
- Source pixel dimensions: 1048 × 350
- Implementation: `G:\GitHub\SnapTranslate\src\SnapTranslate\Views\CaptureOverlayWindow.xaml`
- Implementation screenshot: `G:\GitHub\SnapTranslate\artifacts\ux-qa-v0.2.4\01-wechat-toolbar.png`
- Implementation pixel dimensions: 1536 × 864
- Full-view comparison: `G:\GitHub\SnapTranslate\artifacts\ux-qa-v0.2.4\comparison-full.png`
- Focused toolbar comparison: `G:\GitHub\SnapTranslate\artifacts\ux-qa-v0.2.4\comparison-toolbar.png`

## Viewport and state

- Windows 11 desktop, 1536 × 864 logical viewport
- Display density: 125% (1920 × 1080 physical display)
- State: completed selection with the compact toolbar visible below the selection
- Density normalization: the focused comparison normalizes both toolbar crops to the same 96 px height while preserving aspect ratio; the full comparison preserves each screenshot's original aspect ratio inside equal-width panels

## Full-view comparison

Both screens use a dimmed desktop, a visible selection frame with resize handles, and a dark floating toolbar attached below the selection. The implementation preserves SnapTranslate's blue dashed selection frame and size badge while adopting the reference's compact icon-only action row.

## Focused-region comparison

The focused toolbar comparison confirms the same workflow rhythm:

1. drawing and annotation tools
2. OCR and translation
3. undo, save, and follow-up editing
4. red cancel and green completion at the far right

SnapTranslate intentionally exposes only its existing capabilities, so it does not add WeChat-only ellipse, emoji, arrow, mosaic, long-screenshot, pin, or share tools. It keeps “重新选择” at the far left and “高级编辑” before cancel, preserving the previously approved product scope.

## Fidelity review

- Fonts and typography: the toolbar is icon-only like the source; Segoe UI remains limited to the size badge, hint, results, and tooltips.
- Spacing and layout rhythm: 52 × 48 px actions, compact separators, 10 × 6 px toolbar padding, 10 px radius, and consistent center alignment match the source's density.
- Colors and visual tokens: dark near-black surface, cool-gray border, white utility icons, red cancellation, and green completion match the reference hierarchy.
- Image quality and icons: all actions use real vector Material icons; there are no emoji, handcrafted SVGs, text glyph substitutes, or raster placeholders.
- Copy and content: the redundant image-copy action is removed. Completion, Enter, and double-click copy the final composited screenshot; source-text and translated-text copy controls remain in their corresponding result sections.
- Accessibility and states: every icon action exposes an automation name and tooltip; selected tools retain a visible blue background; busy and error states still disable completion-sensitive actions.

## Interaction verification

- `Esc` closed the overlay without completing the selection.
- Green completion closed the overlay and wrote a 775 × 475 image to the clipboard.
- Enter closed the overlay and wrote a 749 × 437 image to the clipboard.
- Double-click inside the selection closed the overlay and wrote a 750 × 475 image to the clipboard.
- Clipboard writes now retry and flush before the overlay closes.
- OCR, translation, save PNG, undo, and Advanced Editor remain present in the accessibility tree in the intended order.

## Comparison history

1. The previous v0.2.3 toolbar had a separate “复制图片” action, placed undo before OCR/translation, placed Advanced Editor after completion, and used a blue filled completion button. User feedback classified this as a P1 workflow mismatch with the supplied WeChat reference.
2. The toolbar was reordered into four usage groups, the redundant copy action was removed, Advanced Editor moved before the terminal actions, and cancel/complete changed to red and green icon actions.
3. Post-fix evidence is recorded in `01-wechat-toolbar.png` and `comparison-toolbar.png`. Real-app checks verified that every completion path writes the final image to the clipboard before closing.

## Findings

- P0: none
- P1: none after the toolbar-order and completion-semantics fix
- P2: none
- P3: SnapTranslate has fewer total toolbar actions than WeChat because unsupported WeChat-only tools were intentionally not introduced in this iteration.

final result: passed
