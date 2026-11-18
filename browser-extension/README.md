# NetDownloader Chromium Extension (Manifest V3)

A fast, lightweight browser extension for **NetDownloader** compatible with **Brave**, **Google Chrome**, **Microsoft Edge**, **Opera**, and any Chromium-based browser.

---

## Why Use the Extension Instead of Userscripts?

* **No Tampermonkey Required**: Native Manifest V3 extension running directly in your browser.
* **Never Freezes Pastebin**: Resolves links in background service workers without touching or interfering with PrivateBin's client-side AES decryption.
* **Automatic Filename Preservation**: Preserves original repack archive filenames (`.part1.rar`, `.part2.rar`) during link resolution.
* **Popup & Context Menu Integration**:
  * Toolbar popup with connection status to NetDownloader (`● Online (9666)`).
  * Right-click any link or highlighted text to send directly to NetDownloader.
  * In-page 1-Click floating widget.

---

## Installation (30 Seconds)

1. Open your Chromium browser (e.g., **Brave**, **Chrome**, **Edge**).
2. Navigate to the extensions page:
   * **Brave**: `brave://extensions`
   * **Chrome**: `chrome://extensions`
   * **Edge**: `edge://extensions`
3. Toggle on **Developer mode** in the top-right corner.
4. Click the **Load unpacked** button in the top-left corner.
5. Select this folder:
   ```
   C:\Users\amerm\source\repos\FIDM\browser-extension
   ```
   *(or `publish\browser-extension`)*

The NetDownloader icon will appear in your browser toolbar!

---

## Features

1. **Toolbar Popup**:
   * **Current Page Tab**: Detects links on active FitGirl/file host pages and queues all parts in 1 click.
   * **Batch Paste Tab**: Paste multiple links (from pastebin, Discord, or text files) to resolve all parts and send to NetDownloader.
   * **Connection Status**: Real-time heartbeat checking whether NetDownloader desktop app is running.

2. **In-Page Floating Widget**:
   * Appears non-intrusively in the bottom-right corner on supported download pages.
   * Fully isolated using **Shadow DOM** so it never breaks website styling or script execution.

3. **Right-Click Context Menu**:
   * Right-click any link &rarr; **Download with NetDownloader**.
   * Highlight text containing links &rarr; **Send Selected Links to NetDownloader**.
