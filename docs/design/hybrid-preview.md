# What to compare in Wintodo DEV

The preview keeps Wintodo’s card outline, four-DIP gap, completion circle, star and click target. Selecting a card opens the same detail pane. The change to look for is the short rounded system-accent marker inside the card; the longer list-coloured bar is gone. The marker is decorative and does not intercept input. In Light and Dark it follows WinUI’s normal, hover, pressed and disabled brushes. The card still owns its background and pointer states.

To see the title editor, create a personal list and click **its name above the task list**. Enter saves, Escape discards, and clicking elsewhere saves. The editor now uses the displayed header’s effective WinUI font size and weight. On this SDK the original 28 DIP / SemiBold already matched the title, so a visible difference is not expected.

## What the signed build actually rendered

| Light | Desert contrast theme |
|---|---|
| ![Light selected task: the short blue pill sits inside a separate outlined card.](hybrid-preview/selected-light.png) | ![The same task in Desert: dark pill on the pale system button surface.](hybrid-preview/selected-desert.png) |

Both images are unscaled 439 × 50 px views of the same selected task in the installed x64 MSIX `1.0.1.180`, source `7fa57e14`, SHA-256 `61fb3d464956a9d8e9c40c820e0ef5896602f48674a791788f34ae2499f35d8b`. The light crop is from the guest window capture; the Desert crop came directly from UI Automation’s selected-row capture.

The signed-package run passed all six assertion groups in WintodoTest. Five rows retained the original 54 px pitch (216 px first to fifth); the unselected rows had no marker, selecting one produced a single 3 × 16 px pill, and Ctrl+A selected all five before Escape cleared selection. The list-title editor measured 250 × 47 px; Escape discarded and both Enter and moving focus saved. The guest’s Desert run read the actual system colours, found a continuous 14 px solid foreground inside the rounded marker on its expected card surface at 96 DPI, and restored the original palette. Other contrast palettes below were calculated, not driven through the live app. Dark appearance and owner preference are still personal review items.

## Why the marker needs a High Contrast exception

The native presenter pairs `HighlightText` with a `Highlight` backplate. The custom card instead keeps its pointer and selected overlays, which resolve to `ButtonFace` in Windows contrast themes. The first private build combined `HighlightText` with `ButtonFace` and the marker almost disappeared. The corrected build uses `ButtonText` on that surface; disabled retains the SDK’s `GrayText`.

These ratios use [WCAG relative luminance](https://www.w3.org/WAI/WCAG22/Understanding/non-text-contrast.html) on the exact sRGB values in `%WINDIR%\Resources\Ease of Access Themes\`. They measure the solid center of an **enabled** indicator against its card, not antialiased edge pixels or other parts of the page. They are palette calculations, not four rendered screenshots.

| Windows palette (file) | Card background | Previous marker | Corrected marker | Contrast before → after |
|---|---|---|---|---|
| Aquatic (`hc1.theme`) | `#2D3236` | `#212D3B` | `#B6F6F0` | 1.08 → 10.75:1 |
| Desert (`hcwhite.theme`) | `#FFFAEF` | `#FFF5E3` | `#202020` | 1.04 → 15.65:1 |
| Dusk (`hc2.theme`) | `#000000` | `#2B2B2B` | `#FFEE32` | 1.48 → 17.53:1 |
| Night sky (`hcblack.theme`) | `#202020` | `#263B50` | `#FFFFFF` | 1.42 → 16.29:1 |

## Capture the signed package in WintodoTest

Use the [DEV installer and build instructions](../guides/design-dev-preview.md). From the preview worktree, place the specific build’s installer and generated metadata in the ignored input directory:

~~~powershell
$build = '..\dev-preview\1.0.1.180-7fa57e14' # use the actual directory printed by Build-DevPreview.ps1
$metadata = Join-Path $build 'build.json'
$msix = Join-Path $build (Get-Content -LiteralPath $metadata -Raw | ConvertFrom-Json).file
$inputDirectory = New-Item -Path artifacts\hybrid-input -ItemType Directory -Force
Copy-Item -LiteralPath $metadata,$msix -Destination $inputDirectory.FullName
dotnet build Wintodo.sln -p:Platform=x64 -warnaserror  # prerequisite of the common VM wrapper
.\scripts\Invoke-VmGate.ps1 -Gate run-dev-preview-gate.ps1 -NoWarmup -Include 'artifacts\hybrid-input' -TimeoutMinutes 18
~~~

The wrapper restores the clean WintodoTest checkpoint. Inside the guest, the gate checks the package hash and already-trusted signature; it installs the MSIX, activates its registered Start entry, and enters five synthetic tasks on a fresh list. It never registers the Debug layout and does not import a certificate. No production tasks, accounts or credentials are needed. Results and original PNGs return to this checkout’s `tests/ui/artifacts/dev-preview/<run-id>/`; the console log is under `tests/ui/artifacts/vm/`.
