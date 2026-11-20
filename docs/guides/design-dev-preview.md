# Test the Wintodo DEV appearance preview

Open the signed `Wintodo-DEV-…-x64.msix` using App Installer and choose **Install** (or **Update** for a newer DEV build). Open **Wintodo DEV** from Start. Look for the purple **DEV** chevron on the teal Glide icon. Only this private preview gets the badge. Windows 11 24H2 or later, x64, is required.

The preview begins with its own empty task database. Create a list and a few sample tasks, select a row, and rename the list. The card boundaries stay in place; the selected marker and title editor are the changes to compare. No F8 switch is needed. The Store app remains separately available as **Wintodo**.

The package is named `Wintodo.Hybrid.Dev` and uses a local developer publisher, `CN=AppPublisher`. Its AUMID, package LocalState (tasks, attachments and settings), reminder activation class and startup registration are separate. Database launch/environment overrides are ignored in this build, and its vault resource names have a DEV prefix. To keep the comparison local, use sample tasks; connecting both apps to the same real CalDAV account would intentionally synchronize that remote calendar. Uninstalling **Wintodo DEV** removes only the preview and its local data.

The builder verifies the existing local test publisher. On that machine installation needs no new certificate trust. The public certificate beside the installer is for identifying the signer; it is not a private key. On a different machine Windows may reject an untrusted developer signature. The build script never changes certificate trust.

## Rebuild

From the preview worktree, commit the UI changes and run:

```powershell
.\scripts\Build-DevPreview.ps1
```

Use the project SDK and WinApp CLI, Windows SDK SignTool/MakePri, an existing code-signing certificate matching the DEV publisher, and Python with Pillow (`py -3.13` by default). The script snapshots the committed app, writes the DEV manifest, resources, build symbol and icon assets into that private snapshot, restores the locked AOT graph, and builds an x64 Release/AOT package. The Store manifest, assets and ordinary build are not rewritten. By default the existing local key must be unambiguous; use `-SigningThumbprint` to select one explicitly.

The output is under the main checkout's `artifacts/dev-preview/<version>-<commit>/`. The manifest version uses the Store source's first three components and the preview commit count as its revision; use a later commit on the same branch to make an update. The package, SHA-256, source revision, signer details, public CER and icon preview are provided there. Structural/privacy scans, exact comparison of packaged icon assets, and readback of the compiled DEV labels run before the installer is delivered. The script does not install or launch either app.
