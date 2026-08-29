# Sony Music Center SMTC Bridge
[简体中文版说明 (Chinese Version)](README_zh-CN.md)

A native Windows 10/11 System Media Transport Controls (SMTC) bridge for the desktop version of Sony Music Center for PC.

Sony Music Center for PC is an Electron-based application, but due to older Electron versions and missing flags, it does not natively interface with the Windows System Media Transport Controls (the volume OSD media popup). This project injects a lightweight background bridge to provide full native OS integration.

## Features
- **Media Keys:** Play, Pause, Next, Previous hardware keys work perfectly, even when the app is minimized to the system tray.
- **Track Information:** Extracts song title, artist, and album in real-time, fully supporting UTF-8 (CJK characters).
- **Album Art:** Automatically intercepts high-res cover art (including in-memory `blob:` formats) and displays it on the Windows SMTC popup.
- **Timeline Syncing:** Extracts the exact track duration and dynamically creates a virtual audio stream to perfectly sync the SMTC progress bar without pop-up flickering or auto-sync glitches. 
- **Non-intrusive:** Operates completely out-of-process using a C# server. Does not block the main Electron UI thread.

## Installation

1. Close Sony Music Center if it is running.
2. Clone or download this repository.
3. Right-click on **`Install.bat`** and select **Run as administrator**.
   *(The script safely copies the bridge executable and patches the app's startup scripts).*
4. Open Sony Music Center and play a song!


### Manual Installation (If the batch script fails)
If `Install.bat` flashes and closes immediately (e.g., due to antivirus restrictions or UAC issues), you can install the bridge manually:

1. Close **Sony Music Center**.
2. Open Task Manager and ensure `SonyMusicCenterSMTC.exe` is completely closed (if it was running).
3. Copy **`SonyMusicCenterSMTC.exe`** to your Sony Music Center installation directory (usually `C:\Program Files (x86)\Sony\Music Center\`).
4. Navigate to `C:\Program Files (x86)\Sony\Music Center\resources\app\`.
5. Rename the existing `index.js` to `index.js.bak` (this is your backup).
6. Copy the **`renderer-hook.js`** file from this project into that `app` folder and rename it to **`index.js`**.
7. Open the newly renamed `index.js` in a text editor (like Notepad), scroll to the very bottom, add a new line, and append exactly this text: `require('@z-app/core');`
8. Save the file and start Sony Music Center!

## Architecture

This project consists of two components:
1. **`renderer-hook.js`:** An injected Electron script that scrapes the DOM and intercepts media key events. It establishes a local HTTP POST connection to bypass strict Electron Content-Security-Policies (CSP).
2. **`SonyMusicCenterSMTC.cs`:** A native C# WinRT executable that instantiates a `MediaPlayer` and a virtual `MediaStreamSource` to perfectly control the Windows OS timeline without UI glitches.

## Uninstallation

If you wish to remove the patch:
1. Navigate to `C:\Program Files (x86)\Sony\Music Center\resources\app\`.
2. Delete the hooked `index.js`.
3. Rename the original `index.js.bak` back to `index.js`.
4. Delete `SonyMusicCenterSMTC.exe` from the main installation folder.

