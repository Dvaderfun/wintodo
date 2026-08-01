# Wintodo — Visual Design Specification

> Read this **before writing any XAML**. Every number here is deliberate.
> Where this file and a WinUI default disagree, this file wins — unless the
> `winui-design` skill forbids it, in which case the skill wins and you record
> the deviation in `docs/PROGRESS.md`.
>
> **The v1 target is a faithful Microsoft To Do clone.** Same layout, same
> hierarchy, same rhythm — rebuilt in current Fluent (Mica, layer fills, Segoe
> UI Variable, WinUI 3 type ramp) rather than the older UWP look To Do still
> ships. Do not invent new navigation patterns. Do not get creative in v1.

---

## 0. Non-negotiable rules

1. **No hardcoded colours.** `{ThemeResource <Name>Brush}` at every usage site.
   The only exception is user-chosen list theme colours (§9.3), which come from
   the database and are still applied through a brush resource.
2. **No hardcoded corner radii.** `{StaticResource ControlCornerRadius}` (4 px)
   for in-page controls, `{StaticResource OverlayCornerRadius}` (8 px) for
   cards, dialogs, flyouts.
3. **No raw `FontSize` / `FontWeight`.** Use the type ramp styles (§9.2).
   `SemiBold` is the heaviest weight in this app; `Bold` is never used.
4. **Every spacing and size value is a multiple of 4.**
5. **No `IValueConverter`.** `x:Bind` to static functions in code-behind.
6. **No floating centred cards on an empty background.** Content fills the window.
7. **No custom `ControlTemplate`** for a standard control when a `Style` will do.
   The completion circle (§3.4) is the one sanctioned template override.
8. Three theme dictionaries always: `x:Key="Light"`, `x:Key="Dark"`,
   `x:Key="HighContrast"`. Never `x:Key="Default"`.
9. **An icon column is 20 px, not 16.** `FontIcon`/`SymbolIcon` render at 20 px;
   a 16 px `ColumnDefinition` slices them on both sides.
10. **A 32 × 32 icon button needs `Padding="0"`.** `SubtleButtonStyle` inherits
   the 11 px horizontal control padding, which leaves 10 px of content box and
   slices the glyph. `TaskSubtleToggleButtonStyle` already zeroes it; every
   plain `Button`/`ToggleButton` sized 32 × 32 must set it explicitly.

---

## 1. Window

| Property | Value |
|---|---|
| Default size | **1180 × 820** DIPs |
| Minimum size | **640 × 480** DIPs |
| Backdrop | `MicaBackdrop` `Kind="Base"` on `MainWindow` |
| Title bar | `ExtendsContentIntoTitleBar = true` + `Microsoft.UI.Xaml.Controls.TitleBar` |
| Corner | System default (Windows 11 rounds it) |
| Persistence | Size, position, maximised state, and last-selected list restored on launch |

`AppWindow.Resize` takes **physical pixels**. There is no `SizeToContent` in
WinUI. Size in the `MainWindow` constructor:

```csharp
var hwnd  = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
var scale = GetDpiForWindow(hwnd) / 96.0;
AppWindow.Resize(new SizeInt32((int)(1180 * scale), (int)(820 * scale)));
```

Never set `Width`/`Height` on the root `Grid` to size the window — that clips
content instead.

### 1.1 Title bar

Height 48. Three regions inside the `TitleBar` control:

```
[ 16 ] icon 16px [ 12 ] "Wintodo" BodyStrong [ * ] AutoSuggestBox (max 460, min 240) [ * ] [ caption buttons ]
```

- Icon: `IconSource` on the `TitleBar`, 16 px.
- Title: `Title` property. Subtitle unused in v1.
- Search box: centred, `AutoSuggestBox`, `PlaceholderText="Search"`, query icon
  `Search`, corner radius `ControlCornerRadius`, background
  `ControlFillColorDefaultBrush`, no border until focus.
- Below 900 px window width the search box collapses to a 32 × 32 icon button
  that expands the box as a flyout (§6).

---

## 2. Shell layout

`ShellPage` is a single `Grid`, three columns, no nested scroll hosts.

```
Column 0: NavigationView pane   Auto (OpenPaneLength 300)
Column 1: TaskListView          *          (min 400)
Column 2: TaskDetailView        360        (x:Load deferred, collapsed until a task is selected)
```

Surfaces, back to front:

| Surface | Fill | Notes |
|---|---|---|
| Window | Mica Base | Wallpaper shows through |
| Nav pane | **Transparent** | Mica reads through — this is the whole point of Mica |
| List pane | **Transparent** | Same |
| Detail pane | `LayerFillColorDefaultBrush` | Opaque-ish layer, 8 px radius, 1 px `CardStrokeColorDefaultBrush`, 8 px margin from window edges |
| Task rows | `CardBackgroundFillColorDefaultBrush` | See §3.3 |
| Flyouts / menus | `AcrylicBackgroundFillColorDefaultBrush` + `SurfaceStrokeColorFlyoutBrush` | Transient surfaces only |
| Dialogs | WinUI default `ContentDialog` + `SmokeFillColorDefaultBrush` scrim | |

The detail pane is a **layer**, not acrylic. Acrylic is for transient surfaces
(flyouts, menus); a persistent side pane on acrylic looks wrong and costs more
to composite.

No rule between list pane and detail pane — the detail pane's own card border
and 8 px margin do that job. A rule there reads as a second container wrapping
the card. The nav pane is separated by the `NavigationView` content frame, not
by an authored border, so `ShellDividerBrush` is currently unused.

---

## 3. List pane (centre)

Width `*`, horizontal padding **24**, top padding **16**, bottom padding **0**
(the list scrolls to the window bottom edge).

### 3.1 Header

```
┌──────────────────────────────────────────────────────────────┐
│  My Day                                    [Sort ▾] [⋯]      │   height 64
│  Thursday, 30 July                                            │   (subtitle only on My Day)
└──────────────────────────────────────────────────────────────┘
```

- Title: `TitleTextBlockStyle` (28 px SemiBold). Foreground is the list's theme
  colour brush (§9.3), or `TextFillColorPrimaryBrush` for smart lists without a
  theme.
- Editable in place on click for user lists (turns into a borderless `TextBox`,
  commits on `Enter` / focus loss, `Esc` cancels).
- Subtitle (`CaptionTextBlockStyle`, `TextFillColorSecondaryBrush`): only on
  My Day, showing today's date.
- Right side: `Sort` `DropDownButton` (text + chevron) and a `⋯` `Button`
  (32 × 32, `Subtle` style) opening the list menu: Rename, Change theme,
  Show/hide completed, Print, Duplicate, Export `.ics`, Delete list.
- Header spacing to composer: **16**.

### 3.2 Add-task composer

Pinned directly under the header. Never inside the scrolling `ListView`.

```
┌──────────────────────────────────────────────────────────────┐
│  ⊕   Add a task                                               │   height 48 collapsed
└──────────────────────────────────────────────────────────────┘
```

Focused state stays **one row of 48** — the chips appear inline on the right,
never on a second line:

```
┌──────────────────────────────────────────────────────────────┐
│  ○   Buy tickets      [☰ Tasks ▾] [📅] [🔔] [🔁] │ [Add]      │
└──────────────────────────────────────────────────────────────┘
```

- Container: `CardBackgroundFillColorDefaultBrush`, 1 px
  `CardStrokeColorDefaultBrush`, `ControlCornerRadius`, padding `8,0`,
  4 px column spacing.
- Leading slot is 32 × 32: the `Add` glyph (`E710`) in the list theme colour when
  collapsed, cross-fading to an empty completion circle (the same 20 px ring the
  task row uses) when the composer is focused. Driven by
  `VisualStateManager` states on the container plus `ScalarTransition` (120 ms).
- `TextBox`: `BorderThickness="0"`, transparent background in every state
  (`TextControlBorderThemeThicknessFocused` and the focused/pointer-over
  background brushes are overridden locally), `PlaceholderText="Add a task"`,
  `AcceptsReturn="False"`.
- Centring the composer text needs `MinHeight="0"`, symmetric `Padding="4"`, and
  `VerticalAlignment="Center"`. `VerticalContentAlignment` does **nothing** on a
  `TextBox`: the template's `ContentElement` and placeholder only bind `Padding`,
  and text is top-aligned inside the control, so any `MinHeight` slack pushes the
  glyphs above the row centre.
- `Enter` commits and **keeps focus** in the box for rapid entry. `Esc` collapses
  and clears.
- Chips are `DropDownButton`s with `Subtle` style, 8 px padding, 4 px spacing.
  A chip shows its glyph only; when its value is set it also shows a
  `CaptionTextBlockStyle` label ("Today", "Daily", the reminder time). Each
  flyout carries a `Clear` item.
- The first chip is the **target list picker**, shown only on smart lists. It
  names the list the new task will be filed under and lets the user change it;
  the choice sticks for the session. On a user list the picker is hidden — the
  target is that list.
- A 1 px `DividerStrokeColorDefaultBrush` rule separates the chips from `Add`.
- The `Add` button is `AccentButtonStyle`, right-aligned, enabled only when the
  text is non-empty.
- Chips are `x:Load`-deferred and slide in 12 px from the right on focus.
- Composer to first row spacing: **12**.

Smart lists prefill the new task so it lands where it was typed:

| List | Prefill |
|---|---|
| My Day | `MyDayDate = today` |
| Important | `Priority = High` |
| Planned | Due chip preset to **Today** |
| All / user list | Nothing |
| Completed | Composer hidden — a completed task cannot be composed |

The task itself is always created in a real user list (the picker target), never
in a smart list. A first-run database is seeded with one list, `Tasks`, and the
last list can never be deleted away — deleting it recreates `Tasks`.

### 3.3 Task row

The single most important control in the app. Get it exactly right.

The row card comes from a custom `ListViewItem` `ControlTemplate`, not from the
default `ListViewItemPresenter`: the presenter draws its background, border, and
selection fill square whatever `CornerRadius` or `ListViewItemCornerRadius` say,
which left rows visibly squarer than the composer directly above them. The
template is a `Border` (card chrome) + overlay `Border` (hover/pressed/selected
fill) + `ContentPresenter`, with `CommonStates`, `DisabledStates`, and
`DragStates`. This is the second sanctioned template override, alongside §3.4.

```
┌────────────────────────────────────────────────────────────────┐
│  ○   Buy tickets for the show                            ☆     │  height 52
│      Work · Due Tomorrow · 2 of 3 · 🔁                          │  (metadata line only when non-empty)
└────────────────────────────────────────────────────────────────┘
```

| Property | Value |
|---|---|
| Container | `ListViewItem`, `CardBackgroundFillColorDefaultBrush`, `ControlCornerRadius`, 1 px `CardStrokeColorDefaultBrush` |
| Height | **52** with metadata, **44** without. `MinHeight`, never fixed `Height` — the title wraps to 2 lines max |
| Vertical gap between rows | **4** (`ListView` item margin `0,0,0,4`) |
| Inner padding | `12,8,12,8` |
| Grid columns | `[32 completion] [12 gap] [* content] [8 gap] [32 star]` |
| Title | `BodyTextBlockStyle`, `TextFillColorPrimaryBrush`, `TextTrimming="CharacterEllipsis"`, `MaxLines="2"` |
| Metadata | `CaptionTextBlockStyle`, `TextFillColorSecondaryBrush`, single line, ellipsised, 2 px above baseline gap |
| Hover | Background → `CardBackgroundFillColorSecondaryBrush` |
| Pressed | Background → `SubtleFillColorTertiaryBrush` |
| Selected | Background → `SubtleFillColorSecondaryBrush` + a 3 px × 20 px rounded accent bar at the left edge in the list theme colour |
| Drag-over | 2 px accent insertion line, 4 px inset |

**Element budget: 12 or fewer visual elements.** Every element costs working set
× visible rows × recycling headroom. Do not add decorative borders.

Metadata segments, joined with ` · `, in this order, omitting empties:
1. List name — only in smart lists (My Day, Important, Planned, All, Search).
2. Due date — "Today", "Tomorrow", "Yesterday", weekday name within 7 days,
   otherwise `d MMM`. Overdue renders in `SystemFillColorCriticalBrush`;
   due today renders in the list theme colour.
3. Reminder — bell glyph + time.
4. Steps progress — "2 of 3".
5. Repeat glyph.
6. Note glyph — only if a note exists.
7. Attachment glyph + count.
8. "My Day" sun glyph — only when viewing a list other than My Day.

Precompute this string in the item ViewModel. **Never** compose it in the
`DataTemplate` with multiple bindings and converters.

### 3.4 Completion circle

The one place a custom `ControlTemplate` is sanctioned. Base it on `CheckBox`.

| State | Rendering |
|---|---|
| Unchecked | 20 px circle, 1.5 px `ControlStrokeColorDefaultBrush` ring, transparent fill |
| Unchecked + row hover | Ring → `TextFillColorSecondaryBrush`, a 12 px `CheckMark` (`E73E`) glyph at 40% opacity appears inside |
| Unchecked + pressed | Ring → `TextFillColorTertiaryBrush`, circle scales to 0.9 |
| Checked | Circle filled with the list theme colour, 12 px `CheckMark` in `TextOnAccentFillColorPrimaryBrush` |
| Disabled | `ControlStrokeColorDefaultBrush` at disabled opacity |
| High contrast | `SystemColorWindowTextColorBrush` ring; checked = `SystemColorHighlightColorBrush` fill + `SystemColorHighlightTextColorBrush` glyph |

Hit target is **32 × 32** even though the circle is 20 px.

### 3.5 Importance star

- 16 px `FontIcon`. Off: `FavoriteStar` (`E734`), `TextFillColorSecondaryBrush`.
  On: `FavoriteStarFill` (`E735`), list theme colour.
- Hit target 32 × 32. `ToggleButton` with `Subtle` style.
- `AutomationProperties.Name` = "Mark as important" / "Remove importance".
- Off-state star is visible only on row hover, focus, or when the row is
  selected — matching To Do. Always visible when on.

### 3.6 Completed section

Below the incomplete rows, separated by **16**.

```
  ▾  Completed   4
```

- A `ToggleButton` header: chevron (`ChevronDown` `E70D` / `ChevronRight` `E76B`,
  rotating 90° over 150 ms), label `BodyStrongTextBlockStyle`, count in
  `TextFillColorSecondaryBrush`.
- Completed rows: opacity **0.6**, title `TextDecorations="Strikethrough"`,
  metadata hidden except the completion date.
- Section collapsed state persists per list.
- The section itself is `x:Load`-deferred until first expansion.

### 3.7 Grouping (Planned smart list)

Planned groups by due bucket with sticky headers:
Overdue · Today · Tomorrow · This week · Later · No date.
Header: `BodyStrongTextBlockStyle`, 32 px tall, 8 px top margin, count in
`TextFillColorSecondaryBrush`. "Overdue" header text uses
`SystemFillColorCriticalBrush`.

---

## 4. Navigation pane (left)

`NavigationView`, `PaneDisplayMode="Left"`, `OpenPaneLength="300"`,
`IsBackButtonVisible="Collapsed"`, `IsSettingsVisible="True"`.
Pane background transparent (Mica shows through). Item corner radius
`ControlCornerRadius`; selection indicator is the WinUI default 3 px pill.

```
┌───────────────────────────────┐
│                               │
│  ☀  My Day                 3  │   ← smart lists, fixed order
│  ★  Important              7  │
│  📅 Planned                5  │
│  ≡  All                   42  │
│  ✓  Completed             18  │
│  ───────────────────────────  │   1px DividerStrokeColorDefaultBrush, 8px margin
│  ▾ Work                       │   ← list group, collapsible
│    ▪ Sprint 12             4  │
│    ▪ Backlog              12  │
│  ▪ Personal                6  │
│  ▪ Shopping                2  │
│                               │
│  ─────────────────────────── │
│  ⊕  New list        [⊞ group] │   ← PaneFooter
└───────────────────────────────┘
```

| Element | Spec |
|---|---|
| Item height | **40** |
| Item content | 16 px `FontIcon` + 12 px gap + `BodyTextBlockStyle` label + `*` + count in `CaptionTextBlockStyle` / `TextFillColorSecondaryBrush` |
| User list icon | 16 px filled circle (`PathIcon` or a `Rectangle` with `RadiusX/Y`) in that list's theme colour — **not** a generic list glyph |
| Count | Incomplete tasks only. Hidden when 0. Never shown for Completed |
| Groups | `NavigationViewItem` with children, expander chevron, drag lists in/out |
| Reorder | Drag within the user-lists section only; smart lists are fixed |
| Pane footer | "New list" `Button` with `Add` glyph, full width, `Subtle` style, height 40; plus a 32 × 32 "New group" icon button |

Smart-list glyphs (verify each code against the Segoe Fluent Icons reference
before use — names are authoritative, codes are the starting point):

| List | Glyph name | Code |
|---|---|---|
| My Day | Brightness | `E706` |
| Important | FavoriteStarFill | `E735` |
| Planned | Calendar | `E787` |
| All | List | `E8FD` |
| Completed | Completed | `E930` |

---

## 5. Detail pane (right)

User-resizable, **360 px by default**, `x:Load`-deferred,
`LayerFillColorDefaultBrush`, 8 px radius, 1 px `CardStrokeColorDefaultBrush`,
8 px margin from the window edges, internal padding 16.

A 12 px `ContentSizer` (`CommunityToolkit.WinUI.Controls.Sizers`) sits on the
pane's left edge: drag it, or focus it and use the arrow keys. The width is
clamped to a **320 px minimum** and an upper limit of
`min(content width - 320, 960)` recomputed on every `ContentGrid` resize, so the
drag always leaves the list usable and grows with the window. The width persists
in `appearance.detail_pane_width` with a 500 ms debounce. The responsive states must **not** set
`DetailHost.Width` — that value is a two-way `x:Bind` to the shell view model
and a `Setter` would fight the binding.

```
┌──────────────────────────────────────┐
│  ○  Buy tickets for the show      ✕  │  title block
│  ────────────────────────────────── │
│  ○  Check seat map                   │  steps
│  ○  Ask about the discount           │
│  ⊕  Next step                        │
│  ────────────────────────────────── │
│  ☀  Added to My Day               ✕  │  action rows
│  🔔 Remind me                     ▸  │
│  📅 Due Tomorrow                  ✕  │
│  🔁 Repeats weekly                ✕  │
│  ────────────────────────────────── │
│  📎 Add file                         │
│  ┌────────────────────────────────┐  │
│  │ Add note                       │  │  note field
│  └────────────────────────────────┘  │
│                                       │
│  Created Tue, 28 Jul            🗑    │  footer, pinned to bottom
└──────────────────────────────────────┘
```

| Region | Spec |
|---|---|
| Title block | Completion circle (24 px here, larger than in rows) + multi-line `TextBox`, `BodyLargeTextBlockStyle`, borderless, transparent, `AcceptsReturn="True"`, `TextWrapping="Wrap"`, **`UpdateSourceTrigger=PropertyChanged`**; importance star at the right of the title row; close `✕` (`Cancel` `E711`) 32 × 32 top-right |
| Steps | Each: 20 px circle + borderless `TextBox` + `✕` on hover. Height 36. Drag-reorder. Completed steps: 0.6 opacity + strikethrough. "Next step" row has an `Add` glyph and creates a step on `Enter`, keeping focus for the next one |
| Action rows | Height 40, `Subtle` `Button`, full width, `[20px glyph][12 gap][label *][trailing]`, **4 px apart** so each hover fill reads as its own button. Unset: label is the action ("Remind me"), glyph **and** label `TextFillColorSecondaryBrush`. Set: label is the value ("Due Tomorrow"), glyph and label `ListAccentBrush`, trailing `✕` clears it. Driven by one `VisualStateGroup` per row (`StateTrigger` on `IsMyDay` / `HasReminder` / `HasDue` / `HasRepeat`) so the brushes re-resolve on theme change |
| Separators | 1 px `DividerStrokeColorDefaultBrush`, 12 px vertical margins |
| Note | `TextBox`, `MinHeight="88"`, `AcceptsReturn`, `TextWrapping="Wrap"`, `ControlFillColorDefaultBrush`, autosaves 500 ms after the last keystroke |
| Attachments | Row per file: 16 px glyph + name (ellipsised) + size in caption + remove. Click opens with the shell |
| Footer | Pinned to the bottom, `CaptionTextBlockStyle` / `TextFillColorSecondaryBrush` created-date on the left, `Delete` (`E74D`) 32 × 32 icon button on the right |

Deleting a task always confirms first — from the row context menu, the `Delete`
key, or the detail footer. One `ContentDialog` (`Todo.Dialog.DeleteTask`) names
the task, defaults to **Cancel**, and only then runs the delete plus its undo
snackbar. `Todo.Dialog.DeleteList` defaults to Cancel for the same reason.

Date/time picking uses a `DropDownButton` → `MenuFlyout` with quick options
(Today · Tomorrow · Next week · **Pick a date**), where "Pick a date" swaps in a
`CalendarDatePicker` + `TimePicker` inside the same flyout. Never open a modal
dialog for a date.

Repeat editor flyout: `RadioButtons` (Daily · Weekdays · Weekly · Monthly ·
Yearly · Custom). "Custom" reveals a `NumberBox` ("every N") + a period
`ComboBox` + a weekday `ToggleButton` row for weekly.

---

## 6. Responsive behaviour

`VisualStateManager` with `AdaptiveTrigger` on the `ShellPage` root. Never set
these properties from code-behind.

| Window width | Behaviour |
|---|---|
| ≥ 1100 | All three panes side by side. Detail pane inline at 360 |
| 900 – 1099 | Detail pane becomes an **overlay** on the right (still 360 wide, `ThemeShadow`, dims nothing), list pane keeps full width beneath |
| 640 – 899 | Nav pane → `PaneDisplayMode="LeftCompact"` (48 px icon rail); search box in the title bar collapses to an icon; detail pane stays an overlay |
| < 640 | Nav pane → `LeftMinimal` (hamburger, pane overlays); detail pane overlays full width minus 48 |

The window minimum is 640 × 480, so no state below that is needed.

---

## 7. Motion

Prefer built-in theme transitions — they respect the user's reduce-motion
setting automatically. For explicit animations, check
`UISettings.AnimationsEnabled` and skip when false.

| Interaction | Animation |
|---|---|
| Switch list | Content fade 0 → 1 over **150 ms** + 8 px upward translate, `CubicBezier(0.1, 0.9, 0.2, 1)`. **Not** `NavigationThemeTransition` — no `Frame` involved |
| Detail pane open | Translate X +32 → 0 and opacity 0 → 1 over **200 ms**, same easing |
| Detail pane close | Reverse, **150 ms** |
| Row added | `AddDeleteThemeTransition` on the `ListView` |
| Row removed | Same, plus a 5 s undo snackbar |
| Complete a task | Circle scales 0.8 → 1.0 with a spring (damping 0.6, period 40 ms); check glyph fades in over 100 ms; row opacity 1 → 0.6 and strikethrough over 200 ms; **250 ms later** the row animates into the Completed section |
| Uncomplete | Reverse, no delay |
| Star toggle | Glyph scales 1 → 1.25 → 1 over 200 ms |
| Composer focus | Leading glyph cross-fades to the completion ring over 120 ms; chips enter 12 px from the right |
| Nav pane selection | WinUI's built-in indicator animation — do not override |
| Hover on rows | `PointerOverThemeAnimation` defaults; no custom timing |
| Undo snackbar | Slides up 24 px + fades in over 200 ms; auto-dismisses at 5 s with a 200 ms fade |

Connected animations are **not** used — there is no page navigation to connect.

---

## 8. Keyboard

Every one of these is a `KeyboardAccelerator` and appears in a shortcuts dialog
reachable from the list menu and from `Ctrl+?`.

| Shortcut | Action |
|---|---|
| `Ctrl+N` | Focus the add-task composer |
| `Ctrl+L` | New list |
| `Ctrl+Shift+L` | New list group |
| `Ctrl+F` / `Ctrl+E` | Focus search |
| `Ctrl+1` … `Ctrl+5` | My Day / Important / Planned / All / Completed |
| `Ctrl+,` | Settings |
| `Ctrl+?` | Keyboard shortcuts |
| `↑` / `↓` | Move row selection |
| `Space` | Toggle completion on the selected row |
| `Enter` | Open the detail pane for the selected row |
| `Esc` | Close detail pane → clear search → collapse composer (in that order) |
| `Ctrl+I` | Toggle importance |
| `Ctrl+T` | Toggle "Add to My Day" |
| `Ctrl+Shift+D` | Open the due-date picker |
| `Ctrl+Shift+R` | Open the reminder picker |
| `F2` | Rename the selected list, or edit the selected task title inline |
| `Delete` | Delete the selected task (confirmation dialog, then undo snackbar) |
| `Ctrl+Z` | Undo the last destructive action |
| `Alt+↑` / `Alt+↓` | Move the selected task up/down in manual sort order |
| `Tab` / `Shift+Tab` | *(M11)* Demote / promote a task in the tree |

`Esc` ordering matters — implement it as an explicit fall-through chain, not
three competing handlers.

---

## 9. Tokens

### 9.1 Brushes

Use only these. Anything not on this list needs a justification in
`docs/PROGRESS.md`.

| Purpose | Brush |
|---|---|
| Primary text | `TextFillColorPrimaryBrush` |
| Secondary text, metadata | `TextFillColorSecondaryBrush` |
| Disabled text | `TextFillColorDisabledBrush` |
| Text on accent | `TextOnAccentFillColorPrimaryBrush` |
| Hyperlink / accent text | `AccentTextFillColorPrimaryBrush` |
| Row / card background | `CardBackgroundFillColorDefaultBrush` |
| Row hover | `CardBackgroundFillColorSecondaryBrush` |
| Row selected | `SubtleFillColorSecondaryBrush` |
| Row pressed | `SubtleFillColorTertiaryBrush` |
| Detail pane surface | `LayerFillColorDefaultBrush` |
| Input field fill | `ControlFillColorDefaultBrush` |
| Focused input fill | `ControlFillColorInputActiveBrush` |
| Card / pane border | `CardStrokeColorDefaultBrush` |
| Control border | `ControlStrokeColorDefaultBrush` |
| Divider | `DividerStrokeColorDefaultBrush` |
| Flyout background | `AcrylicBackgroundFillColorDefaultBrush` |
| Flyout border | `SurfaceStrokeColorFlyoutBrush` |
| Dialog scrim | `SmokeFillColorDefaultBrush` |
| Primary button | `AccentFillColorDefaultBrush` / `Secondary` / `Tertiary` / `Disabled` |
| Overdue / error | `SystemFillColorCriticalBrush` |
| Success | `SystemFillColorSuccessBrush` |

### 9.2 Type

| Style | Used for |
|---|---|
| `TitleTextBlockStyle` | List header title |
| `SubtitleTextBlockStyle` | Settings section headers, dialog titles |
| `BodyLargeTextBlockStyle` | Detail-pane task title |
| `BodyStrongTextBlockStyle` | Completed-section header, group headers, empty-state title |
| `BodyTextBlockStyle` | Task titles, nav labels, step text, notes |
| `CaptionTextBlockStyle` | Metadata line, counts, dates, chip labels, footers |

Nothing smaller than `CaptionTextBlockStyle` (12 px). Never re-declare an
inherited property in a `BasedOn` style.

### 9.3 List theme colours

Twelve options, matching To Do's palette. Stored per list in the database as an
enum, **not** as a hex string — the actual colour resolves per theme so contrast
holds in both Light and Dark.

`Blue` (default) · `Red` · `Orange` · `Yellow` · `Green` · `Teal` · `Lime` ·
`Purple` · `Pink` · `Olive` · `Graphite` · `Navy`

Rules:
- Each enum value maps to a Light hex and a Dark hex, both ≥ **4.5:1** contrast
  against their surface. Do not reuse one hex across themes.
- Applied by overriding a single named brush (`ListAccentBrush`) in the
  `TaskListView`'s local `Resources` when the list changes. Everything that
  should tint — header title, completion circle fill, star, add glyph,
  selection bar, nav-pane list dot — binds to `ListAccentBrush`.
- **High Contrast ignores list colours entirely.** `ListAccentBrush` redirects to
  `SystemColorHighlightColorBrush`. This is a hard requirement, not a nicety.
- The colours live in `Themes/Colors.xaml` inside the Light/Dark/HighContrast
  dictionaries — never inline in a view.

### 9.4 List background themes

Optional per-list background image (the To Do "themes" feature), rendered behind
the list pane at 12% opacity with the Mica still visible at the edges.

- Ship 8 built-in images, each ≤ 200 KB, as `ms-appx` assets.
- Decode at display size: set `BitmapImage.DecodePixelWidth` to the actual pane
  width. Never decode at source resolution.
- Exactly one decoded image is held at a time; switching lists disposes the
  previous.
- Off by default. If the memory budget in `PLAN.md` §7 is threatened, this is the
  first feature to cut.

---

## 10. Empty states

Centred in the list pane, vertically at 40% height (not 50% — it reads better).
48 px `FontIcon` in `TextFillColorTertiaryBrush`, 16 px gap, title in
`BodyStrongTextBlockStyle`, 4 px gap, subtitle in `CaptionTextBlockStyle` /
`TextFillColorSecondaryBrush`, max width 320, centre-aligned.

Each empty state is `x:Load`-deferred.

| View | Glyph | Title | Subtitle |
|---|---|---|---|
| My Day (empty) | Brightness | "Focus on your day" | "Add tasks you want to get done today." |
| Important | FavoriteStar | "Nothing important yet" | "Star a task to see it here." |
| Planned | Calendar | "Nothing planned" | "Tasks with a due date show up here." |
| All | List | "No tasks yet" | "Add your first task to get started." |
| Completed | Completed | "Nothing completed yet" | "Finished tasks land here." |
| User list (empty) | Add | "This list is empty" | "Add a task above." |
| Search (no results) | Search | "No results for \"{query}\"" | "Try a different word." |
| No lists at all (first run) | Add | "Welcome" | "Create your first list to get started." + an `AccentButton` "New list" |

---

## 11. Accessibility

- `AutomationProperties.AutomationId` on **every** interactive element, named
  `Todo.<Area>.<Element>` — e.g. `Todo.Nav.NewListButton`,
  `Todo.List.AddTaskBox`, `Todo.Row.CompleteToggle`, `Todo.Detail.TitleBox`.
  The `winapp ui` test suite depends on these; treat them as API.
- `AutomationProperties.Name` on every icon-only control.
- A task row's accessible name is the full sentence:
  *"Buy tickets for the show, due tomorrow, important, 2 of 3 steps complete,
  in Work"* — not just the title.
- Completing a task raises
  `AutomationPeer.RaiseNotificationEvent(..., "Buy tickets completed")`.
- Semantic controls only. A clickable `Border` or `TextBlock` is a bug.
- `HighContrastAdjustment = None` at app level; the theme dictionaries do the work.
- Focus visuals must be visible on every surface in all three themes —
  check the completion circle and the star specifically, they are the easiest to
  get wrong on a card background.
- Minimum hit target 32 × 32 for every interactive element, including the
  completion circle and star.

---

## 12. Settings page

Single scrolling page, `Frame`-navigated, `SettingsCard` /
`SettingsExpander` from `CommunityToolkit.WinUI.Controls.SettingsControls`.
Page padding 36, section spacing 24.

Sections: **Appearance** (theme: System/Light/Dark; density: Comfortable/Compact;
show list backgrounds) · **Tasks** (default reminder time; week starts on;
confirm before delete; move completed to bottom) · **My Day** (reset time —
default midnight) · **Notifications** (enable reminders; default snooze;
launch at login) · **Data** (import `.ics`, export all, database location,
database size) · **About** (version, GitHub link, licences).

---

## 13. Do not

- Do not use `ScrollViewer` around a `ListView` — it kills virtualisation.
- Do not build a custom pill/segment tab switcher; use `SelectorBar` or
  `NavigationView`.
- Do not split the window 50/50; the nav pane is fixed-width, the list flexes.
- Do not put the add-task box inside the scrolling list.
- Do not animate anything without checking `UISettings.AnimationsEnabled`.
- Do not use emoji as icons — `FontIcon` with Segoe Fluent Icons only.
- Do not add a hamburger button at ≥ 640 px; the pane is already visible.
- Do not show a splash screen or a loading spinner on launch. The app must paint
  the shell immediately and fill the list asynchronously — a spinner on startup
  is an admission that the perf budget was missed.
