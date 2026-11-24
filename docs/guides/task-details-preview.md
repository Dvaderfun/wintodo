# Try task details in Wintodo DEV

Open the signed x64 MSIX and choose **Update** if Wintodo DEV is already installed. Launch **Wintodo DEV** from Start (purple DEV badge). The update keeps your existing DEV tasks. Create two disposable tasks named **Alpha** and **Bravo** in the same list.

| Try this | What to look for |
|---|---|
| Launch the app, click Alpha, close Details with **×**, and click Alpha again. Then leave Details open and alternate between Alpha and Bravo. | The pane acknowledges the selection immediately. The title, star and completion state should match the row you picked. The old task's note and steps should never appear beneath the new title. |
| Type a note on Alpha and immediately click Bravo. Go back to Alpha. | The note is on Alpha only. With the list focused, use the arrow keys to browse; focus should stay in the list. |
| On Alpha, type **Call back** in **Next step** without pressing Enter. Open Bravo, type a different step draft, and return to Alpha. Press Enter. | Alpha restores **Call back**. Enter adds it once, to Alpha. Bravo still has its own unsubmitted draft. Drafts are for the current session; submitting a step saves it. |
| Maximize the window. On Bravo, click **Add note** and type two lines. Click elsewhere, then make the window short and edit again. | The empty field starts compact. While writing, the note can use the spare space above **Created** and Delete. The footer stays visible and the short pane still scrolls. |
| In a wide window, drag the left edge of Details back and forth. Repeat in a narrow window and on a list with a photo background. | In wide mode the list and Details resize together. In narrow mode Details overlays the list. The contents should follow the divider and the background should remain visible through the drag. |
| On a wide display, drag the pane noticeably wider than 720 DIPs (about half a 1920-pixel screen at 100%). Close and reopen DEV at the same window size. | The width you chose returns. The divider is also keyboard accessible: focus it, then use Left/Right. |
| With Alpha open, press **Ctrl+Shift+D**; dismiss the date picker with Esc. Select Bravo and try **Ctrl+Shift+R**. | The correct date or reminder picker opens for the selected task. Pressing Esc dismisses it without changing either task. |

For feedback, the most useful distinction is **first open**, **reopen after ×**, **switch while open**, or **dragging**. If dragging still feels slow, include the window size, Windows display scale, and whether the list has a photo.
