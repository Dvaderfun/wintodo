# Wintodo for Windows — English Store listing

**Short description:** A native Windows task manager with daily lists, reminders and optional CalDAV sync.

**Description:** Plan your day with My Day, star what matters, and see upcoming work in Planned. Wintodo is built with WinUI 3 for Windows 11. Use it locally without an account, or connect a CalDAV server when you want to sync.

Give a task a due date, a reminder or a repeat schedule. Add steps, notes and files so the details stay with the work. Keyboard shortcuts let you add, find and complete tasks without leaving the list.

Already using Microsoft To Do? Choose which lists to copy in a one-time import. Your originals stay where they are. You can also import and export tasks as iCalendar files.

Requires Windows 11 24H2 or later.

**Publisher:** Univera LLC · **Support:** support@univera.app · **Store ID:** [9PF4DX2BSF0X](https://apps.microsoft.com/detail/9PF4DX2BSF0X)

## Desktop screenshots and captions

Upload the **Screenshot** column in order to the English (United States) desktop listing. Paste each caption into Partner Center's caption field. Each PNG is 1920 × 1080, RGB, and shows a complete Wintodo window on a solid backdrop. All lettering inside the window comes from the app. The separate **Promo** files have English headlines for the website, press or social posts.

| # | Screenshot | English caption | Promo |
|---|---|---|---|
| 1 | [My Day on Windows](screenshots/01-windows.png) | Bring today's tasks from different lists together in My Day, a native WinUI 3 view for Windows 11. | [Your day, on Windows.](promos/01-windows.png) |
| 2 | [A weekend packing checklist](screenshots/02-task-details.png) | Pack for a weekend away: check off steps and keep the reminder, due date and packing note in the same pane. | [One bag. Three small steps.](promos/02-task-details.png) |
| 3 | [Keyboard shortcuts in dark mode](screenshots/03-keyboard.png) | Press Ctrl+N to add a task, Ctrl+F to search or Space to mark the selected task complete. Open the shortcut list for more. | [Stay on the keys.](promos/03-keyboard.png) |
| 4 | [Choose lists to import](screenshots/04-import.png) | Choose which Microsoft To Do lists to copy and see how many tasks each contains. Your originals stay in Microsoft To Do. | [Bring your lists along.](promos/04-import.png) |
| 5 | [An iCalendar list after export](screenshots/05-open-formats.png) | Sunday kitchen came from an iCalendar file with due dates, stars and weekly repeats. The confirmation shows the list was exported again. | [Keep your Sunday routine.](promos/05-open-formats.png) |

[Preview all five promo designs and the hero](preview.jpg). The first promo combines two real captures of the same My Day list, one in light mode and one in dark mode.

## Store hero art

Upload [store-hero.png](store-hero.png) to **Trailers and additional assets → 16:9 Super hero art**. *Out of harbour* is an original illustration of a sailboat, drawn in solid colours. Its sails sit in the upper two-thirds, with open water below for Store overlays. The 1920 × 1080 PNG contains no text, logo, app UI or device frame. The [editable artwork](../artwork/hero.svg) is included.

## Notes for certification

On Windows 11 24H2 or later, choose **Start fresh** on the welcome screen.
Create a task in **Tasks**, open its details, add a step, and add the task to
**My Day**. Reopen the app to see the saved task. This path works without an
account. Microsoft To Do import and CalDAV sync are optional.

Wintodo is a packaged WinUI 3 desktop application. Its manifest declares
`runFullTrust` for the desktop process and its Windows integrations, including
toast activation. File import and export use Windows file pickers.

## Image sources

The gallery uses real Wintodo windows from the Windows test VM. Three fictional
lists were read by Wintodo's iCalendar importer: *Weekend in Copenhagen*,
*Sunday kitchen* and *In the studio*. The optional Microsoft To Do wizard
uses a sample account. No customer data, live sign-in, stock photography,
app background JPEGs or third-party marketing images are used. No font
files are distributed. Source captures and rerender instructions live under
`docs/releases/store/artwork/` in the repository.

Before submission, compare these views with the final Store build. They
document the interface, not a live account, certified installation or
real-server interoperability test. [Microsoft's desktop screenshot guidance](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/screenshots-and-images)
sets a 1366 × 768 minimum, a 50 MB maximum and a separate caption of up to 200 characters.
