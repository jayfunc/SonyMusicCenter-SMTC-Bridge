# Sony Music Center SMTC Bridge
[简体中文版说明 (Chinese Version)](README_zh-CN.md)

A native Windows 10/11 System Media Transport Controls (SMTC) bridge for the desktop version of Sony Music Center for PC.

Sony Music Center for PC is an Electron-based application, but due to older Electron versions and missing flags, it does not natively interface with the Windows System Media Transport Controls (the volume OSD media popup). This project injects a lightweight background bridge to provide full native OS integration.

## Features
- **Media Keys:** Play, Pause, Next, Previous hardware keys work perfectly, even when the app is minimized to the system tray.
- **Track Information:** Extracts song title, artist, and album in real-time, fully supporting UTF-8 (CJK characters).
- **Album Art:** Automatically intercepts high-res cover art (including in-memory \lob:\ formats) and displays it on the Windows SMTC popup.
- **Timeline Syncing:** Extracts the exact track duration and dynamically creates a virtual audio stream to perfectly sync the SMTC progress bar without pop-up flickering or auto-sync glitches. 
- **Non-intrusive:** Operates completely out-of-process using a C# server. Does not block the main Electron UI thread.

## Installation

1. Close Sony Music Center if it is running.
2. Clone or download this repository.
3. Right-click on **\Install.bat\** and select **Run as administrator**.
   *(The script safely copies the bridge executable and patches the app's startup scripts).*
4. Open Sony Music Center and play a song!

## Architecture

This project consists of two components:
1. **\
enderer-hook.js\:** An injected Electron script that scrapes the DOM and intercepts media key events. It establishes a local HTTP POST connection to bypass strict Electron Content-Security-Policies (CSP).
2. **\SonyMusicCenterSMTC.cs\:** A native C# WinRT executable that instantiates a \MediaPlayer\ and a virtual \MediaStreamSource\ to perfectly control the Windows OS timeline without UI glitches.

## Uninstallation

If you wish to remove the patch:
1. Navigate to \C:\Program Files (x86)\Sony\Music Center\resources\app\\.
2. Delete the hooked \index.js\.
3. Rename the original \index.js.bak\ back to \index.js\.
4. Delete \SonyMusicCenterSMTC.exe\ from the main installation folder.
