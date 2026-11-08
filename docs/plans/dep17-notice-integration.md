# DEP-17 inherited notice integration

Integrated 2026-11-08. Status: **SOURCE NOTICE INTEGRITY AND PRIVACY PASS;
fresh x64/ARM64 package inclusion PASS; final legal-distribution acceptance remains pending.**

[The shipped-notice review](dep17-third-party-notices-review.md) established
missing supplier legal texts in both unsigned packages from clean `965d86b`.
[ThirdPartyNotices.txt](../../src/Wintodo.App/Assets/ThirdPartyNotices.txt)
now retains those nine whole texts, with their exact package versions and
package-relative source filenames. The existing 38,748-byte notice file is
preserved as an unchanged prefix; no licence, copyright or attribution was
removed. Explicit associations cover transitive Toolkit and SQLite inputs,
the Windows App SDK native closure including RestartAgent, Windows ML,
WebView2 loader/interop and the linked .NET runtime.

The integrated file is **898,198 bytes**, SHA-256
`ba042235866020798b50fcc24483abaea5a61c827340ca8db943a6819cac98fa`.
Each original legal body matches its source inside the exact original NuGet
archive and occurs intact in the resulting UTF-8 notice file. Seven source
texts use CRLF and the two WebView2 texts use LF; their original bytes are
preserved. The narrow `-text -whitespace` entry in [.gitattributes](../../.gitattributes)
preserves these line endings and inherited trailing whitespace. Supplier legal
formatting is excluded from Git whitespace diagnostics; compiler and analyzer
warnings remain errors.

## Intact inherited sources

The following are whole legal files from the exact package versions in the
review. Headers added around each body identify its source and public NuGet
record; they do not edit or replace supplier wording.

| Exact package | Legal file | Bytes | SHA-256 |
|---|---|---:|---|
| CommunityToolkit.Common 8.2.1; identical Mvvm 8.4.2 notice | `ThirdPartyNotices.txt` | 8,600 | `7774f8b0ab66bfb47ec154ce24a2d6d574febc44a74d940b8a037d1c56756163` |
| Microsoft.NETCore.App.Runtime.NativeAOT.win-x64 10.0.14; identical ARM64 text | `LICENSE.TXT` | 1,139 | `d7a68596ab69b06f51ca278a6545148e4269a9381c26d597c13df5d88e08cf5b` |
| Microsoft.NETCore.App.Runtime.NativeAOT.win-x64 10.0.14; identical ARM64 text | `THIRD-PARTY-NOTICES.TXT` | 78,041 | `6d15e10a101c6bfff2ab4429ed061bf76c456fc4b23ad6b03e0d0f8377148a21` |
| Microsoft.WindowsAppSDK.Runtime 2.5.1 | `NOTICE.txt` | 340,893 | `572b43d41dea717dae7dc5de69acb20a74df025e8f5a3c0aa6f7bca02615e23c` |
| Microsoft.Windows.AI.MachineLearning 2.1.74 | `license.txt` | 13,996 | `66395f8cb219087fae2bd025010bd9076b736c14f03b48f20295471c0c376814` |
| Microsoft.Windows.AI.MachineLearning 2.1.74 | `ThirdPartyNotices.txt` | 331,175 | `fb0af774b4d7cffc5b9d046f2aaeade2f37df2f80abf8033c95dfffcc77a8866` |
| Microsoft.Web.WebView2 1.0.3719.77 | `LICENSE.txt` | 1,487 | `0af8f1b807512aae39c2ac1aa4d0cae65cabecb6fd554b8439a5162a0d6eca55` |
| Microsoft.Web.WebView2 1.0.3719.77 | `NOTICE.txt` | 3,894 | `106423785c5b7eba0a8e61d1837f2132e9c828e20ad530f565d981c1df60dd90` |
| System.Numerics.Tensors 9.0.0 | `THIRD-PARTY-NOTICES.TXT` | 75,640 | `40686c6447a7d5b5d3693068e4571b5f483d7ed335aeee773ef662440de4c5d5` |

Byte-identical Toolkit, per-architecture .NET and shared SDK notices are
included once with explicit component associations. The Runtime NOTICE
contains the whole WinUI root NOTICE. The distinct WebView2 redistribution
licence and Windows ML terms remain separate from the generic MIT and Windows
App SDK terms retained above them. Supplier instructions, including “Do Not
Translate or Localize”, and required public attribution remain intact.

## Verification and remaining acceptance

Private `artifacts/dep-notice-integration/integrity.json` records nine original
NuGet-archive legal file matches and nine intact whole-body inclusions.
`privacy.json` records one source file scanned with zero findings:

```powershell
python -B scripts/Test-ReleasePrivacy.py src/Wintodo.App/Assets/ThirdPartyNotices.txt `
  --output artifacts/dep-notice-integration/privacy.json --private-root $PWD
```

This static legal-payload integration introduces no app logic or new
dependency. No maintenance framework or implementation-mirroring tests were
added; direct source/body hashes establish the required preservation property.
Builds and package validation belong to the integrating release batch. No app
launch, host UI automation, installation, signing or publication was performed
by this stream.

[Fresh clean-source `9106040` packages](dep17-notice-package-validation.md)
now pass actual notice-byte inclusion, structural/privacy and layout-equivalence
checks for both architectures. Final production candidates retain their own
signed-byte and legal/distribution gates.

New x64 and ARM64 candidates must include these actual notice bytes and pass
the exact-package/privacy gates again. The older `965d86b` packages still
contain the original notice file and do not inherit this source change. Final
candidate SBOM and retained-code review must distinguish resolved managed
inputs, native files, linked code and vendor/tool/test entries removed by
trimming. Tensors and project-wide supplier notices are retained conservatively;
their inclusion does not assert every named upstream component ships.

This change does not choose a first-party licence, establish image rights,
resolve public owner attribution/contact, approve production identity/signing,
or establish compliance with every vendor distribution and end-user condition.
Those owner and final-release gates remain open in
[the deployment plan](deployment-readiness.md).
