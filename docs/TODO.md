- [ ] Typage: Allow pasting images. Typage should render images inline like how Obsidian does, and be pasted into the same folder as where the current file being edited is with "image-<TIMESTAMP>.<extension>".
- [ ] Typage: When pasting text content that starts with "- [ ]" onto a line that already begins with a checkbox, it should paste the content without the leading "- [ ]" characters.
- [ ] Typage: The inline code block syntax cuts off text content due to the padding , it must be padding around the text and not cover or obscure the text.
- [ ] Typage: When switching view modes, keep the scroll position and cursor in the same location as it was originally
- [ ] Typage: Fix erroneous behavior when adding new lines. "Enter" should only add two new lines if it's after a contiguous paragraph that is not prefixed by a list indicator like "-", "1.", "- [ ]", etc.
- [ ] Puffinote: click and drag files and folders in sidebar to actually move them. draw a ghost on the file being moved, show the drop indicator, expand folders if hovered over for a second.

- [ ] Plaintain: macOS dictation is not working. I speak but nothing enters immediately, I need to press "esc" first then all dictated content enters at the same time. I would like the content to be entered word by word as I dictate it as the default specification has. i see this error in console `2026-08-09 22:25:41.929 Plaintain[85941:5106259] error messaging the mach port for IMKCFRunLoopWakeUpReliable` ![[Pasted image 20260809222422.png, check artifacts folder]]
- [ ] Cellestra: With whole-sheet ⌘A, only highlight the range of what the copy/cut would be selecting in this case, since that's what the action will be performed on. It signals to the user about what actually will occur. and is a better user experience.
- [ ] All apps: When reloading from an external change on disk, and selecting "Reload", please apply that to the undo/redo history so that if what they reloaded from disk isn't something they want they can cmd+z to go back.![[Pasted image 20260809215649.png check artifacts folder]]
- [ ] Cellestra: external-change detection
figure out why libreoffice shows this while the .ods file is being edited in cellestra. I first opened it in cellestra, then clicked the ods file to open in libreoffice, then it  showed this. It doesn't do it the other way around, when libreoffice opens the file first and then I open the file in cellestra![[ Pasted image 20260809220024.png check artifacts folder]]

- [ ] Pivotask: Change the supported file extension from ".pivotask.json" to ".pivotask" for simplicity and to allow the operating system to associate it with a file handler specific to this program and not for other JSON files. The file format will still be JSON, but the extension will be ".pivotask". This change will require updating the file handling logic in the application to recognize and work with the new extension.

- [ ] Add an Outlook-like sidebar at the very left of the screen that allows toggling between:
    - [ ] Todos (Placeholder, will be a UI that manages list of .pivotask files recursively in the notebook directory)
    - [ ] Calendar (Placeholder, will be a future calendar app)
    - [ ] Notes (Puffinote's current UI).
    - [ ] Data (Placeholder, will be a UI that manages list of .ods files recursively in the notebook directory)
