# Store artwork sources

The six PNGs in `captures/` are whole Wintodo windows, in English, from
the x64 Debug app in the WintodoTest VM. Their contents are unchanged.
Where WinApp returned a labelled multi-window sheet, only its Wintodo
rectangle was kept. No UI, demo text, task row or date was painted in.
The two My Day captures use the same data, date and window geometry.

The gallery is set on Thursday, 19 November 2026. The three fictional lists
were ingested through Wintodo's `IcalReader` and `SqliteTaskStore.ImportIcalAsync`
into fresh disposable databases, one per appearance setting. The Microsoft To Do
chooser uses the Debug Graph fixture with the display name “Demo account” and
no email address. List background photos were disabled for these captures.

Local capture evidence under `tests/ui/artifacts/store-gallery/`:

| Master | Guest run and file | Region of the WinApp image |
|---|---|---|
| `day-light`, `day-dark`, `bag-light` | `20261014-000456/<name>.png` | Main window: left 0, top 28, width 1426, height 873; excludes the harness labels and shortcut tooltip panel. |
| `keys` | `20261014-000456/keys.png` | Full window, including the shortcut dialog. |
| `choose` | `20261119-194944/choose.png` | Full window, including the sample account label. |
| `kitchen-export` | `20261119-195533/exported.png` | Full window, including Wintodo's export confirmation. The same run returned `Sunday kitchen.ics` with five tasks and their weekly repeat rules. |

The first directory name reflects the restored guest's clock. Its host
capture log is `tests/ui/artifacts/vm/run-store-gallery-capture-20261119-194346.log`.

From the repository root, with Pillow and resvg-py available to a Windows
Python interpreter and the standard Segoe UI fonts installed:

```powershell
py -3.13 docs/releases/store/artwork/render.py
```

The renderer reads `captures/`, [the harbour illustration](hero.svg) and
[the two icon masters](../../../plans/assets/). It writes the English
gallery, the icon comparison, and the three review ZIPs under
`artifacts/store-1.0.0-rc/`. It does not write to the project's
`Assets` directory or rebuild packages. Fonts, databases, fixture responses
and raw guest captures are not included in the handoff ZIPs.
