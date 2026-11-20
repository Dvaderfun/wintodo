# Store artwork sources

The six English PNGs in `captures/` show complete Wintodo windows from the
WintodoTest VM. They use the x64 Debug build of `aa59ff4`. The guest layout
came from that build's MSBuild package recipe; all nine icon files matched
the approved Glide set before launch. The title-bar mark is part of the
capture. The two My Day windows use the same tasks, date and geometry.

The gallery is set on Thursday, 19 November 2026. The three fictional lists
were imported through Wintodo's `IcalReader` and `SqliteTaskStore.ImportIcalAsync`
into disposable databases, one per appearance setting. The Microsoft To Do
chooser uses the Debug Graph fixture with the display name “Demo account” and
no email address. List background photos were disabled.

Private capture evidence: `tests/ui/artifacts/store-glide-gallery/`.
The run log is `tests/ui/artifacts/vm/run-store-glide-capture-20261119-202215.log`.

| Master | Guest file | Region retained |
|---|---|---|
| `day-light`, `day-dark`, `bag-light` | `gallery/<name>.png` | Main window at (0, 28), 1426 × 873. This excludes the harness labels and shortcut tooltip panel. |
| `keys` | `gallery/keys.png` | Full window, including the shortcut dialog. |
| `choose` | `import/choose.png` | Full window, including the demo account label. |
| `kitchen-export` | `export/exported.png` | Full window, including the export confirmation. The returned iCalendar file contained five tasks and two weekly repeat rules. |

The captures preserve those pixels. When framing a window, the renderer
replaces only the captured square one-pixel OS border with a single
antialiased hairline. Desktop screenshots have 48 px above and below the
window and no added lettering. Each promo aligns the left edges of its two
lines and the right edge of its wordmark to the window below. The original
[paper checklist illustration](hero.svg) leaves the bottom third clear for
Store overlays.

From the repository root on Windows, with Pillow 12.3.0, resvg-py 0.5.0
and the standard Segoe UI fonts:

```powershell
py -3.13 docs/releases/store/artwork/render.py
```

This regenerates `en-US/` artwork and
`artifacts/store-1.0.0-rc/Wintodo-Store-en-US.zip`. The ZIP contains listing
copy, the preview, five screenshots, five promos and the hero PNG/SVG.
Raw guest captures, fonts, databases and fixture responses stay out of the ZIP.

To reproduce the package PNGs and seven-frame ICO from the approved
[Glide SVG](../../../plans/assets/store-icon-glide.svg) in a scratch folder:

```powershell
py -3.13 docs/releases/store/artwork/render.py --export-icon artifacts/icon-repro
```

The unplated taskbar assets in both themes and the small ICO frames use
[a tighter crop](../../../adr/ADR-024-glide-taskbar-sizing.md). The Store
logo, larger ICO frames, tiles, splash and lock screen retain the original crop.
