# Install and test Wintodo DEV

Open the signed `Wintodo-DEV-…-x64.msix` using App Installer and choose **Install** (or **Update** for a newer DEV build). Open **Wintodo DEV** from Start. Look for the purple **DEV** chevron on the teal Glide icon. Only this private preview gets the badge. Windows 11 24H2 or later, x64, is required.

A first install starts with its own empty task database. Updating DEV keeps that database. Create a list and a few sample tasks, select a row, and rename the list. The card boundaries stay in place; the selected marker and title editor are the changes to compare. No F8 switch is needed. The Store app remains separately available as **Wintodo**.

For the My Day lightbulb, follow the [Suggestions layout and functional checklist](my-day-suggestions-preview.md).
For task opening, resizing, and note or step editing, use the [task details checklist](task-details-preview.md).

To try the [startup fix](../plans/2026-11-24-startup-and-minimized-work.md),
resize the window, close it normally, and reopen it from Start. It should
appear at that size immediately. Repeat after maximizing: it should open
maximized, and Restore Down should return to your earlier rectangle. Close
once in the desired state after updating; DEV .245 could save a maximized
window as normal.

Use **1.0.1.224** to try Repeat. On a task with no due date, choose **Repeat → Weekly**: it becomes due today. Mark it done and the same task stays open, due next week. If a task from **1.0.1.222** already repeats daily but says **Add due date**, marking it done makes it due tomorrow. You can also choose **Add due date → Today** to give it today's deadline without completing it.

Tasks due today appear automatically in My Day, and a row edit updates the open detail pane. [The appearance notes](../design/hybrid-preview.md) describe the DEV selection marker, list title editor and tested palettes.

The package is named `Wintodo.Hybrid.Dev` and uses a local developer publisher, `CN=AppPublisher`. Its AUMID, package LocalState (tasks, attachments and settings), reminder activation class and startup registration are separate. Database launch/environment overrides are ignored in this build, and its vault resource names have a DEV prefix. To keep the comparison local, use sample tasks; connecting both apps to the same real CalDAV account would intentionally synchronize that remote calendar. Uninstalling **Wintodo DEV** removes only the preview and its local data.

The builder verifies the existing local test publisher. On that machine installation needs no new certificate trust. The public certificate beside the installer is for identifying the signer; it is not a private key. On a different machine Windows may reject an untrusted developer signature. The build script never changes certificate trust.

## Rebuild

From the repository root with `dev` checked out, commit the UI changes and run:

```powershell
.\scripts\Build-DevPreview.ps1
```

Use the project SDK and WinApp CLI, Windows SDK SignTool/MakePri, an existing code-signing certificate matching the DEV publisher, and Python with Pillow (`py -3.13` by default). The script snapshots the committed app, writes the DEV manifest, resources, build symbol and icon assets into that private snapshot, restores the locked AOT graph, and builds an x64 Release/AOT package. The Store manifest, assets and ordinary build are not rewritten. By default the existing local key must be unambiguous; use `-SigningThumbprint` to select one explicitly.

The output is under the main checkout's `artifacts/dev-preview/<version>-<commit>/`. The manifest version uses the Store source's first three components and the preview commit count as its revision; use a later commit on the same branch to make an update. The package, SHA-256, source revision, signer details, public CER and icon preview are provided there. Structural/privacy scans, exact comparison of packaged icon assets, and readback of the compiled DEV labels run before the installer is delivered. The script does not install or launch either app.

## Verify an update

Run the installed-package gate in WintodoTest with both signed packages. Stage
the new installer's MSIX and `build.json` in `artifacts/hybrid-input`, and the
previous installer's two files under `artifacts/hybrid-input/previous`. Then run:

```powershell
dotnet build Wintodo.sln -p:Platform=x64 -warnaserror
.\scripts\Invoke-VmGate.ps1 -Gate run-dev-preview-gate.ps1 -NoWarmup `
    -Include 'artifacts/hybrid-input' -GateArguments '-UpgradeFromPrevious'
```

The guest writes a task and note in the old version, installs the update over
it, and checks that both remain. It also checks a note typed immediately
before closing the new version and exercises the chosen card and title design.
For a build containing the task-details changes, add `'-DetailProductivity'`
to `-GateArguments` to check per-task step drafts, note and footer geometry,
picker shortcuts, and restoration of a width above 720 DIPs. A dedicated
input folder can be passed with `'-InputDirectory', 'artifacts/<name>'`
and the matching VM `-Include`.

## DEV CI recommendation

Keep the same build and unit checks for both channels. If DEV packages become
routine, add a separate on-demand job for the `dev` branch and the
`Wintodo.Hybrid.Dev` identity. Build and validate an unsigned x64 package there
and finish signing locally with the existing developer key. Keep the revision
increasing so App Installer can update in place. No hosted DEV workflow or
signing credentials are configured for this build.
