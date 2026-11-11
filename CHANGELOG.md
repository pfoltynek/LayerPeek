# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html). Until 1.0.0, minor versions may contain breaking changes
(settings format, behaviour).

## [Unreleased]

### Added

- **User manual** in all eight languages of the app (English, Czech, Slovak, German, Spanish, French, Italian,
  Polish) in `manuals/`, for Windows and macOS in one document, with screenshots rendered by the app in each language.
  The README links to it.
- **User manual** item in the tray / menu bar menu, above **About**: opens the manual in the language of the app's
  texts (English for any other language).
- **Printer compatibility report:** a GitHub issue form with a short checklist for printer models other than the
  CORE One, linked from the README and the manuals.
- **Securing access to PrusaLink:** a new manual chapter (all eight languages) on the risks of PrusaLink's plain HTTP,
  separate networks and VPNs, HTTPS through a reverse proxy (Caddy and nginx examples) and trusting your own
  certificate authority on Windows and macOS.
- **Camera reconnect status:** the camera window shows roughly when the next connection attempt is due ("Next attempt
  in about 20 s"); the time stays current while the window is visible.

### Changed

- **Camera window performance:** the mini preview and camera windows clearly smaller than the camera image (up to 70 % of
  its width) convert the image at the size it is shown instead of the camera's full resolution, and each frame is copied
  once, off the UI thread. A window only a little smaller keeps the full resolution and lets the display scale it, which
  measured cheaper. Snapshots, recordings, timelapses and print failure detection keep the full resolution.
- **Mini preview frame rate:** the mini preview shows at most 10 frames per second by default (the frames in between are
  decoded but not converted, copied or drawn), which saves CPU; the normal window and full screen keep the camera's full
  frame rate. A new **Camera** tab of the settings sets it for all cameras: Economy (5 fps), Balanced (10 fps), Smooth
  (every frame) or a custom limit of 1–30 fps. Print failure detection, recordings and timelapses are not affected.
- **Shorter settings tab names** in French ("Démarrage, MàJ"), Italian ("Riprese"), Polish ("Start i aktualizacje") and
  Slovak ("Štart a aktualizácie"), so that the six tabs fit on one row.
- **Print failure detection performance:** its samples are scaled straight from the brightness (luma) of the camera
  image instead of being converted to full-resolution colour first: about a tenth of the work per sample and no
  full-size colour buffer while only detection watches the camera. The detection thresholds are unchanged.
- **Camera reconnects back off:** a camera that keeps failing is retried after 5 s, then 10, 20, 40 s and at most every
  minute, instead of every 5 s; a successful stream starts over at 5 s. Opening or restoring the camera window, and
  **Test camera** in the settings, try again at once.
- **Built with the stable .NET 11** release instead of a release candidate.

### Fixed

- Settings, the printer's **Camera** sub-tab: in Italian, Spanish, French and Polish the **Keep the last minutes
  (dashcam)** check box was cut off; the labels are shorter now.

### Security

- **Updates are verified with a signature** (installations from `Setup.exe` on Windows and the app in Applications on
  macOS): the update feed of every release is signed with an offline key that is never on GitHub, and LayerPeek refuses
  an update whose feed is unsigned, changed or signed with another key, so that a taken-over GitHub account cannot ship
  a malicious update. Versions before this one still accept any update, including this one; the protection starts once
  this version runs. The first download of an installer is not covered: check it against `SHA256SUMS-*.txt`.
- **Warning for unencrypted connections outside the local network:** the printer and camera settings show an
  informative warning when a PrusaLink `http://` address or an RTSP camera address is not in a local network (private
  or link-local ranges, `.local`, `.lan`, `.home.arpa` or a name without a dot), with a link to the new manual chapter.
  Saving is not blocked.

## [0.5.0] - 2026-11-05

### Added

- **Camera recording** (Windows and macOS): the submenu of a printer with a camera has **Start recording** / **Stop
  recording**, **Play last recording** (opens the newest recording in the system's video player) and **Open recordings
  folder** (the printer's own folder once it has recordings); the camera window has a record button next to the pin
  that shows a red dot with **REC** while the camera is recorded (also in the mini preview and in full screen), and
  the tray tooltip says **Recording**. Recordings are MP4 files without re-encoding, by default in the Videos
  (Windows) / Movies (macOS) folder under `LayerPeek`, one subfolder per printer, named after the time and the print.
  **Record prints automatically** on the printer's Camera sub-tab records every print from its start to a minute after
  its end, also without an open camera window (off by default); every new print job gets a file of its own. A new
  **Recording** tab in the settings has the folder (**Change…**) and a total size limit (50 GB by default; the oldest
  recordings are deleted above it, recording stops below 5 GB of free space). A recording that fails shows a
  notification with the reason; clicking it opens the Recording tab.
- **Dashcam: keep the last minutes of a print** (Windows and macOS): **Keep the last minutes (dashcam)** on the
  printer's Camera sub-tab (off by default) keeps the last 2 minutes of the camera in memory during a print (1–10 min
  on the Recording tab, about 8 MB per minute and printer; nothing is written to the disk until it is saved). They are
  saved together with 30 s after the event as `…_dashcam-<reason>.mp4` in the printer's recordings folder on a print
  error, when the printer needs attention, when the print is stopped and when the camera detects a print failure (each
  can be switched off), or by hand with **Save the last 2 min** in the printer's menu or in the context menu of the
  camera window. The dashcam is idle while the whole print is being recorded. Only a failed save shows a notification.
- **Print failure detection (experimental)** (Windows and macOS, off by default): LayerPeek can watch a print through
  the camera and warn you when something goes wrong on the bed. Turn on **Look for print failures in the camera image** on the
  Recording tab and **Watch the prints of this printer with the camera** on the printer's new **Detection** sub-tab.
  The first detector, **Change on the bed**, notices a part that vanished or moved and new material on the bed
  (spaghetti, a part torn off), with a sensitivity slider (0–100, default 50). The sub-tab has an editor for the **bed
  region** that is watched (drag with the mouse or use the arrow keys; **Whole frame**, **Load frame** for a new
  picture from the camera and, for a Core One, **Default for Core One**, which is also its preset). An alarm shows a
  notification "Possible print failure – Change on the bed" with a snapshot of the camera, with the same sound as a
  print error; clicking it opens the camera window, and its **False alarm** button keeps a local record for tuning
  (nothing leaves the computer, the sensitivity does not change). By default there is one alarm per print and kind of
  failure (or again every 10 min while it lasts), a JPEG snapshot of every alarm is saved in the printer's recordings
  folder (can be switched off) and a running dashcam saves the last minutes. Detection runs only while printing and
  with the chamber light on, checks the camera every 2 s (2–5 s on the Recording tab), decodes only key frames and
  works without an open camera window. **Limitations:** it does not notice the quality of the first layer, problems at
  the nozzle or defects inside the part, it is less reliable with dark filament on a black sheet, and with the whole
  frame as the region the moving parts of the printer can cause false alarms. It is experimental: tuned on synthetic
  failures and one real successful print of a Core One with a Buddy3D camera, not yet on real failed prints, so it may
  miss a failure or raise a false alarm. The settings mark it with an **Experimental** badge.
- **Print failure detector "Printing into the air" (experimental, off by default)** (Windows and macOS): a second
  detector on the printer's **Detection** sub-tab, offered only for printers whose bed moves down while printing
  (Core One, Core One L, XL, XL+; hidden for other models). It notices that the printer goes on printing (the Z axis
  rises) but nothing grows on the bed any more, e.g. filament that ran out unnoticed, a slipping extruder or a clogged
  nozzle: the top edges of the parts stand at the nozzle in a good print and move down with the bed when nothing is
  extruded. It uses the printer's reported Z height and looks at the bed region extended up to the nozzle. It is off
  after the update and marked **Experimental**; switch it on per printer. **Limitations:** the alarm comes ~3 mm of Z
  after the failure (a few minutes on a fast print, about half an hour or more on a slow one), the first ~6 mm of a
  print (and of a resumed print or after the camera reconnects) are not judged, small parts mostly hidden under the
  print head are not judged, a wipe tower that keeps growing can hide a failure, and a part whose wider lower step ends
  (or several parts of which most end earlier) can cause a false alarm. It is tuned on synthetic failures and two real
  successful Core One prints; there is no real footage of printing into the air yet.
- **Dark filament mode (experimental)** for print failure detection (Windows and macOS, off by default): **Dark
  filament** with an **Experimental** badge on the printer's Detection sub-tab. For black or very dark filament, the
  "Change on the bed" detector then counts a smaller darkening of the bed as a change (10 instead of 14 gray levels;
  brightening is unchanged). On synthetic black-filament failures on dark sheets it finds a little more (about 2–3
  percentage points), at the price of less headroom against false alarms (the real Core One test print stays about
  4× below the alarm threshold instead of 9×). The known limitation (less reliable with dark filament on a black
  sheet) stays. Saved as `printers[*].detection.darkFilament`.
- **Timelapse (experimental)** (Windows and macOS): **Start timelapse (experimental)** / **Stop timelapse
  (experimental)** in the printer's menu and a stopwatch button in the camera window, which shows an amber **TL**
  while a timelapse is taken (dimmed while the print is paused); **Play last timelapse** in the printer's menu. **Take
  a timelapse of prints** on the printer's Camera sub-tab (off by default) takes one of every print, from its start to
  a minute after its end, also without an open camera window. One frame every 30 s by default (**Time between
  frames**, 5–600 s, on the Recording tab), none while the print is paused; the video plays at 30 frames per second.
  The frames are key frames of the camera stored without re-encoding (so a frame can be up to about 2 s off the
  interval with a Buddy3D), saved as `…_timelapse.mp4` in the printer's recordings folder and counted in the size
  limit of the recordings. A failed timelapse shows a notification.
- **Timelapse frames per layer (experimental):** on the Recording tab, **Every N seconds** next to the timelapse heading
  can be switched to **Every N-th layer (estimate)** (N = 1–100, 1 by default): a frame when a new layer starts, plus
  one at the start and one of the finished part. PrusaLink reports no layers, so LayerPeek estimates them from the
  height of the Z axis at every poll (z-hops are filtered out); layers shorter than about two polls merge, and rarely a
  z-hop or a tool change adds a frame. Tested on a real Core One print: every layer found at 2–5 s polling. The polling
  interval does not change.
- **Timelapse from a recording (experimental):** **Create timelapse from recording… (experimental)** in the printer's
  menu asks for a LayerPeek recording and makes a timelapse of it with the same time between frames, saved next to it
  as `…_recording_timelapse.mp4`; a recording split by a camera dropout (`…-2.mp4`) is taken as a whole. The menu item
  shows the progress and cancels it; a notification tells when it is done.
- **Export a timelapse to JPEG (experimental):** **Export timelapse to JPEG… (experimental)** in the printer's menu
  asks for a LayerPeek timelapse and saves its frames as numbered JPEG files (`0001.jpg`, …) in a folder next to it,
  e.g. for your own video editing. Progress and cancelling are in the menu item, a notification tells when it is done;
  the folder counts in the size limit of the recordings.
- **Camera window closes when the printer turns off** (Windows and macOS, on by default): an open camera window closes
  after the printer has been offline for 30 s and opens again at its last place 10 s after the printer is back,
  without taking the focus from the app you work in (also after LayerPeek restarts in between). A window you close
  yourself stays closed. While you edit the layout or the context menu is open, the window waits. No notification.
  Switch it off with **Close when the printer turns off, reopen when it is back on** on the Camera window sub-tab of a
  printer (it applies to all printers).
- **Settings:** new `recording` and `detection` sections, `printers[*].camera.recordPrints`, `.dashcam` and
  `.timelapsePrints`, `printers[*].detection` and `cameraWindowAutoClose` (only values that differ from the defaults
  are written); older settings files load unchanged.

### Changed

- Camera: every printer now has **one camera connection** shared by the camera window, recording, dashcam, timelapse
  and detection, so recording or detection does not open a second stream. After the last user is gone (e.g. the camera
  window is closed or minimized), the connection stays open for 10 s, so a window restored within that time shows the
  picture at once. **Test camera** in the settings uses the open window's connection for the saved address and its own
  connection for a changed address.
- Settings: on the printer's Camera sub-tab the camera address label is now left of the field, to make room for the
  new recording options.
- Windows: the bundled FFmpeg libraries are a newer LGPL build of FFmpeg 9.0.2 (BtbN `autobuild-2026-10-31-12-59`),
  because the previously used build is no longer available for download. macOS is unchanged.
- Windows: the whole tray menu closes after **Hide camera** in a printer's submenu; since the recording items were added,
  the main menu stayed open next to the tray icon.

### Removed

- **Prusa Connect**: the app supports PrusaLink only. The disabled "Prusa Connect (not available yet)" choice and the
  **Connection** field are gone from the printer settings, and the printer menu offers only **Open PrusaLink**. A Prusa
  Connect printer in an older settings file (it could only be added by editing the file) is skipped when the settings
  are loaded, with a warning in the log, and disappears from the file with the next save.

## [0.4.0] - 2026-11-02

### Added

- **Always on top pin** in the camera window (Windows and macOS): a pin button at the right end of the window buttons,
  in the normal window and in the mini preview (not in full screen), shows and toggles "Always on top". When it is on,
  the filled pin stays visible, dimmed, even without the mouse over the window; when it is off, the outlined pin
  appears only with the other buttons. The picture's context menu has the same choice as **Always on top**, now also
  in the mini preview (there it is its only item; the layout is still edited only in the normal window). A click
  switches the window at once and saves the choice of that printer for the current mode (normal window or mini
  preview), the same option as in the settings; an open settings window shows the new value and keeps your other
  unsaved changes. If saving fails, the window goes back to the saved choice. The taskbar button (Windows) and the
  Dock icon (macOS) follow the change as they do for the option in the settings. Screen readers announce the pin as a
  toggle with its state, and it works with the keyboard.
- Mini preview: a thin light grey **outline** around the borderless window (picture and strip), so that a dark preview
  no longer blends into a dark desktop or window behind it. With the mouse over the preview it gets brighter, to show
  that its edges can be dragged. Windows and macOS; the normal window and full screen are unchanged.

### Changed

- The printer menu item that brings an open camera window to the front (added in 0.3.1 as *Show camera*) is now
  called **Bring camera to front** in all languages, so that it no longer looks like the opposite of *Hide camera*
  next to it. It works as before on Windows and macOS.
- Windows: in the context menu of the camera window (right click, the Menu key or Shift+F10) the underlined letter
  alone runs an item and closes the menu, as in the notification area menu and other Windows menus; Alt+letter still
  works. macOS is unchanged.

### Fixed

- Windows: choosing **Bring camera to front** in the notification area menu, or choosing Settings, Diagnostics or
  About again while that window is open, now really brings the window to the front; before, Windows often refused and
  the window stayed behind other apps (at most its taskbar button flashed). In the rare case that Windows still keeps
  another app in the foreground, the window ends up right below that app, above all other windows. "Always on top"
  of the window does not change, and a minimized window is restored as before.
- Windows: after switching back to a camera window (e.g. with Alt+Tab), a window button pressed with Enter or Space
  is treated as pressed from the keyboard again, so the keyboard focus stays in the row of window buttons instead of
  being left on a button that has just hidden itself.
- macOS: turning on "Always on top" of a camera window (with the pin, Space or Return) no longer leaves LayerPeek
  inactive with no keyboard focus; the window keeps the focus, so Return on the pin turns it off again.
- macOS: Control-click (left button with Control held, e.g. on a trackpad) in a camera window opens its context menu,
  like a right click, instead of starting a window drag.
- macOS: Command-period (⌘.) now works like Esc in all windows: it leaves full screen of a camera window, cancels
  Settings and closes Diagnostics, About and Licenses.

### Removed

- Windows ARM64 (`win-arm64`): no longer planned. The release scripts refuse it and Windows is built and released as
  `win-x64` only. No release ever shipped a `win-arm64` build.

## [0.3.1] - 2026-10-30

> **macOS 0.3.0:** that version cannot update itself: drag the new LayerPeek.app from the 0.3.1 disk image over the old
> one once (settings and saved passwords stay). From 0.3.1 on, LayerPeek updates itself.

### Added

- **Update check** (Windows and macOS): shortly after the start and then once a day LayerPeek asks GitHub whether a new
  version was released (anonymously, one request a day). A newer version appears as **New version X.Y.Z available** in
  the tray / menu bar menu (above *About*) and once per version as a notification. A click does the update: the
  installed Windows version and LayerPeek.app on macOS download it, quit normally and start the new version; the ZIP
  version and development builds open the release page. The *Startup and updates* tab has **Check for updates
  automatically** (on by default), **Offer pre-release versions** (now also in the ZIP version and on macOS) and **Check
  now** with its result next to it. A LayerPeek.app that runs straight from the disk image or Downloads is told to move
  to Applications first. Failures (no network, the GitHub limit) are only logged. **Settings:** `checkForUpdates`
  (older files load with the check on); what the check remembers is in `update-state.json` next to the settings.

- macOS: the printer items of the menu bar menu show the printer's picture, like on Windows (the model illustration or
  the custom picture, in colour, 18 × 18 points as macOS menus draw item images, made for Retina).

- macOS: keyboard shortcuts as on other Mac apps. Camera window: **⌃⌘F** toggles full screen (F11, which macOS
  reserves for Show Desktop and Mac keyboards send only with fn, stays as a fallback), and the full screen button and
  the image description name ⌃⌘F. Settings: **⌘⇧]** / **⌘⇧[** switch the tabs, **⌘⌥→** / **⌘⌥←** the tabs of a
  printer (Ctrl+Tab and Ctrl+PageDown still work). Every auxiliary window (Settings, Diagnostics, About, licences,
  camera) closes with **⌘W**, and **⌘.** works like Esc (in Settings both are Cancel; with a drop-down list open in
  Settings or Diagnostics, ⌘. first closes only the list, as Esc does). Windows is unchanged.
- **Send test notification** on the *General* tab of the settings (Windows and macOS): shows a sample notification
  the way printer events are shown, with the sound unless *Sound* is set to Off (the choice shown in the window counts,
  even unsaved), also while *Show notifications* is unchecked. A new test notification replaces the previous one, and a
  click on it brings the settings window to the front on the *General* tab. The result is shown next to the button:
  sent (with a hint to check Focus / Do not disturb if nothing appears) or failed. On macOS, when LayerPeek has not
  asked yet, the button asks for permission first (macOS asks only once; after a denial the tab offers System Settings
  → Notifications).
- Windows: when LayerPeek's notifications are turned off in Settings → System → Notifications, the *General* tab says
  so and offers to open that page (checked every time the settings window opens or comes back to the front, as on
  macOS); when a policy of the organization turned them off, the tab says that instead.
- macOS: while a window is open (Settings, Diagnostics, About, licences, a camera window that is not always on top),
  LayerPeek has a Dock icon and is in the ⌘Tab switcher, so a window that fell behind other apps can be brought back
  (clicking the Dock icon, or the window list of its menu); with only always-on-top windows (the mini preview by
  default) or no window it is back in the menu bar only.
- The menu of a printer with an open camera window (menu bar on macOS, notification area on Windows) has **Show
  camera**, which brings the window to the front, restoring it when minimized; also a window that is always on top and
  so has no Dock icon or taskbar button.
- Mini preview: the strip below the image shows when the print finishes, after a flag next to the remaining time:
  "Done at 7:50 PM" when there is room, otherwise only "7:50 PM" (on another day of the week "Fri 7:50 PM"), and
  in the narrowest window only in the tooltip. The time is never cut off. Only while printing (not while paused).
- Mini preview: after a print the bar on the top edge of the strip shows how far the nozzle has cooled down (blue),
  and once it has cooled it is full and green and the strip says "Cooled down – safe to turn off" like the Cool-down
  widget (in a narrow window "Cooled down · 48 °C"; the tooltip always has both). While cooling, a narrow strip drops
  the word "Cooling down…" (the snowflake icon stands for it) before the estimate.
- Mini preview: choose what its info strip shows, per printer, under **Mini preview** at the end of the list on the
  *Widgets* tab of the settings: the progress bar at the top, the percentage, the time remaining, the finish time, the
  cool-down after printing, and the print name and temperature chart rows (all on by default, as before). The printer
  state is always shown. A row that is switched off has no button in the strip, which leaves more room for the rest
  (e.g. without the remaining time the finish time fits even the narrowest window); the tooltip names only what is on.
  An open mini preview changes as soon as the settings are saved. *Restore defaults* and *Use layout for all printers*
  include these choices. **Settings:** stored as `printers[*].camera.miniStrip` (only the items that are off); older
  settings files load unchanged.

### Changed

- Settings: a field with an invalid value now has a red outline (2 px, also in the dark theme), so it is clear which
  field "Fix the highlighted fields" means, e.g. the nozzle or the bed threshold of the *Cool-down* widget, which share
  one message. After **Save** (and **Apply** of the camera window, and **Test connection** / **Test camera** refused
  because of a field) the keyboard focus moves to the first invalid field, and screen readers (Narrator, NVDA,
  VoiceOver) read its error as the help text of the field; a valid field gives its hint. The fields keep their size,
  so the window still fits without scrolling.
- Windows: **Start automatically when I sign in to Windows** is on by default: the installer switches it on. Updates
  keep your choice (an existing installation does not change).
- macOS: LayerPeek.app is packed and signed by Velopack (still ad hoc, with the hardened runtime and the entitlements of
  0.3.0), so that it can update itself; the release also carries the Velopack package and update feed of `osx-arm64`.
  The `.dmg` and `.tar.gz` stay; there is no `.pkg`.
- macOS: the help text of **Open System Settings** under *Open at login* (shown when LayerPeek is turned off in System
  Settings > General > Login Items & Extensions) now only says to allow it there; saving again is not needed, because
  an unchanged *Open at login* option takes over the state of macOS when the settings window comes back to the front.
- Cool-down: a printer now counts as cooled down only when **both the nozzle and the bed** are at or below their
  thresholds (before, only the nozzle decided). Each printer has a nozzle and a bed threshold, 40 °C each by default,
  set in the *Cool-down* widget on the *Widgets* tab of the settings for the printer selected there, with **Use
  thresholds for all printers**. The estimate ("about 6 min") and the progress bar follow the slower of the two. The
  *Cool-down* widget names both thresholds, the "Printer cooled down" notification shows both temperatures, and the mini
  preview strip shows the temperature that is still holding it up (both in its tooltip). A heater switched on again,
  also the bed's, ends the cool-down. **Settings:** the former nozzle threshold is not taken over; every printer starts
  with 40 °C / 40 °C (the log notes it once; older settings files still load).
- Cool-down widget, small (S) size: while cooling, the temperature has the icon of its sensor in front of it (the
  nozzle cone or a heated bed with heat waves, in their colours), so it is clear whether the nozzle or the bed is
  holding the cool-down up.
- Windows: a camera window has a taskbar button exactly when it is not always on top. With the default options nothing
  changes (the mini preview is on top and has none); a normal window with "Always on top" now has no taskbar button, a
  mini preview without it now has one. Changing "Always on top" of an open window may make it disappear and reappear
  once (how Avalonia moves a window off the taskbar).
- macOS: the camera mini preview with *Always on top* is now shown on every desktop (Space) and above apps in full
  screen, like a picture-in-picture window. The normal camera window and full screen are unchanged; the preview without
  *Always on top* stays on its desktop.
- The small temperature charts in the camera window (the *Temperatures* widget and the chart row of the mini preview)
  no longer look like flat lines: the nozzle and the bed each get their own band (nozzle on top, bed below), scaled to
  their own values over the last 60 minutes with at least 6 °C, so a fluctuation of 1–2 °C is visible while sensor
  noise is not. A target line is drawn only where it falls within its band. In the large (L) *Temperatures* widget
  each band shows its range next to the chart (e.g. "212–218 °C"). The chart in the Diagnostics window keeps its axis
  from 0 °C. Windows and macOS.
- Update check of the ZIP version and of LayerPeek.app outside Applications: a release is offered only when it already
  has the files of the operating system LayerPeek runs on (its `SHA256SUMS-windows.txt` / `SHA256SUMS-macos.txt`), so
  the Windows ZIP version is no longer told about a version that so far has only macOS files, and the other way round.
  The installed versions (Velopack) already skipped such a release.
- Releases: the Windows and macOS files of one version can be published at different times. The OS that goes first
  publishes with `scripts/release-publish.ps1 -AllowIncomplete`; the other OS later adds its files to the published
  release with `scripts/release-upload.ps1 -AddToPublished`, which never replaces a file, uploads the checksum file
  and the update feed last and then checks the whole release. `scripts/release.ps1` runs on macOS as well as on
  Windows. `release-publish.ps1 -AllowIncomplete` no longer fails with "Cannot bind argument to parameter
  'ExpectedOs'".

### Security

- The PrusaLink web proxy (the printer's web opened logged in from the menu) no longer lets the printer's content
  register a Service Worker, which could have outlived the proxy on its `127.0.0.1` port: such requests are refused
  and the proxy's policy forbids workers. Only the response headers the PrusaLink web needs are passed from the printer
  to the browser (content type and encoding, download name, caching, file information); everything else, e.g.
  `Service-Worker-Allowed`, `Access-Control-*`, `Link` or `Refresh`, is dropped. Other pages can no longer frame the
  proxied web or keep a handle on its window (`X-Frame-Options`, `frame-ancestors`, `Cross-Origin-Opener-Policy`).

### Security

- macOS: LayerPeek.app is signed with the hardened runtime, so macOS ignores `DYLD_INSERT_LIBRARIES` and the other
  `DYLD_*` variables when it starts LayerPeek. Before, another program running as the same user could start LayerPeek with
  its own library injected and read the printer passwords from the Keychain without a prompt. The only entitlement is
  `disable-library-validation`, which the ad-hoc signed app needs to load its own bundled libraries. After updating to
  this version, macOS may ask once more to allow LayerPeek access to its Keychain items.

## [0.3.0] - 2026-10-29

### Added

- macOS (Apple Silicon): `LayerPeek.app` without a Dock icon (menu bar only), ad-hoc signed, distributed as a `.dmg`
  (drag the app to Applications) and a `.tar.gz`. The builds are not notarized: the first start has to be allowed in
  **System Settings → Privacy & Security → Open Anyway** (see the README).
- macOS: **Open at Login** in the settings (tab *Startup and updates*), using the system Login Items. Off by default.
  When LayerPeek was turned off in System Settings → General → Login Items, the tab says so under the option (checked
  every time the settings window opens or comes back to the front) and offers to open System Settings; saving then
  reports that Open at login has to be allowed there.
- macOS: windows opened from the menu bar (Settings, Diagnostics, About, camera) come to the front with the keyboard focus.
- macOS: the app icon follows Apple's app icon grid (a margin around the rounded square with Apple's continuous corners
  and a soft drop shadow), so in Finder, Launchpad and System Settings it is as large as the other apps' icons; the 16
  and 32 px sizes use the simplified design, like on Windows. The Windows icon is unchanged.
- macOS: system notifications (User Notifications) with the same texts, sound setting and click target as on Windows; a
  click opens the printer's web interface. macOS asks for permission with the first notification. When notifications
  are turned off in System Settings, the *General* tab of the settings says so and offers to open System Settings →
  Notifications.
- macOS: the live camera stream (RTSP/H.264, e.g. Buddy3D). The app bundles its own LGPL build of FFmpeg 9.0.2,
  compiled from the official source (software decoding), and needs no FFmpeg from Homebrew. The third-party licences
  window links the source release and the build script used, at the release tag of the app version. Building on a Mac
  for the first time needs the Xcode Command Line Tools (a clear build error says so when they are missing).
- macOS 15+: the Local Network permission dialog explains why LayerPeek connects to printers and cameras on the local
  network, in all eight app languages. When a connection to a printer or camera on the local network fails with
  "no route to host" (what a denied permission looks like), the diagnostics window, the connection and camera tests
  and the camera window say that Local Network access may be turned off in **System Settings → Privacy & Security →
  Local Network**, and the log says so once per printer or camera. The diagnostics window, the connection test and
  the camera test offer an **Open System Settings** button next to that message.
- macOS: when LayerPeek starts while no display is active (display asleep, lid closed without a monitor, Open at Login
  while the display sleeps), it waits and appears in the menu bar once a display wakes, instead of quitting at start.
  Printer monitoring and notifications already run while it waits (a click on a notification opens the printer's web
  interface); the menu bar item then shows the current state at once. Opening the app again while it waits does what
  it always does (the last window or the settings) once the menu bar item is there.
- Maintainers: `build/update-ffmpeg.ps1` verifies a new FFmpeg version for both platforms before it is pinned (macOS:
  official source tarball against the Homebrew formula and the GPG signature; Windows: the BtbN zip against the
  release's `checksums.sha256`, its folder, LGPL licence and libraries) and with `-Update` writes the pinned values.
- macOS: the monitor chosen for a camera window is recognised by the display's persistent identifier, so the window
  still opens on the right one of two identical monitors after macOS numbers them differently ("(1)", "(2)") at the
  next start or reconnect, and on the built-in display after the system language changed.

### Fixed

- macOS: the borderless mini camera preview can be resized with its edges and corners (the window follows the
  pointer itself; macOS offers no resize for borderless windows there). The height follows the width when the edge is
  released, as on Windows.

## [0.2.0] - 2026-10-26

### Added

- Camera overlay widgets instead of a single text block: Status (printer name, state, progress), Times (remaining,
  finish time, print time), Temperatures (nozzle and bed with a small chart), Print name (title and material chips
  read from the PrusaSlicer file name, the G-code thumbnail and the slicer estimate) and Cool-down. Each widget can be
  shown or hidden, has a size S/M/L and can be emphasized.
- Layout mode in the camera window: drag the widgets to one of six anchors, change their size, reset to the default
  layout. Works with the keyboard too.
- New Widgets tab in the settings: visibility and settings of each widget, card background opacity, "Edit layout in
  the camera window…", reset, and applying the layout to all printers.
- Cool-down after a finished or stopped print: the Cool-down widget shows the nozzle and bed temperatures, the
  progress to the threshold and an estimate, then "Cooled down – you can switch it off". An optional "Printer cooled
  down" notification (off by default). The nozzle threshold is set per printer (default 50 °C) and can be applied to
  all printers.
- Mini preview info strip below the image (progress bar, percentage and remaining time) instead of text over the
  image. The print name row and a temperature chart row can be expanded or collapsed right in the mini window and stay
  as you left them.
- The Settings and Diagnostics windows can be maximized and remember their size and maximized state.

### Changed

- The camera overlay settings moved from the printer's Overlay sub-tab to the new Widgets tab. Existing overlay
  settings (items and corner) are converted to the widget layout automatically. The settings file format is now
  version 2. If you go back to LayerPeek 0.1.0, it only logs a warning and drops the widget layout when it saves the
  settings.
- Settings and Diagnostics windows are a little taller by default (780 × 532).
- Numbers and their units (`50 °C`, `2 h 35 min`, `960 × 540`) are never split across lines.

### Fixed

- The mini preview size box offers its own presets (320 × 180, 480 × 270, 640 × 360, 960 × 540). Before, the default
  320 × 180 disappeared from the list once another size was picked.

## [0.1.0] - 2026-10-24

First public preview. Windows x64 only, PrusaLink only, builds are not code-signed.

The project was called *Prusa Tray Diagnostic* (`PrusaTrayDiagnostic`) during development and was renamed to
LayerPeek before this first release, so that the name does not start with the Prusa trademark. No earlier version was
released, so the app does not migrate anything; developers who ran the old builds can use the one-time script
`tools/migrate-from-prusatraydiagnostic.ps1`.

### Added

- Tray-only app for Windows: the tray icon shows the printer state and print progress, and the tooltip shows the
  details.
- Monitoring of multiple printers over PrusaLink, with one combined icon and a submenu for each printer.
- Windows toast notifications for print events (started, finished, paused, resumed, stopped, error, attention,
  offline, minutes remaining), with configurable sounds.
- Diagnostics window with printer and firmware information, temperatures, fans, speed, flow and Z height, and a
  temperature chart for the last 30 minutes, 2 hours or 24 hours.
- Printer picture chosen by the printer model reported by PrusaLink, or a custom image.
- Camera window for RTSP/H.264 cameras (e.g. Buddy3D) through FFmpeg, with automatic reconnection and full-screen,
  normal and mini (always on top) modes.
- Camera overlay with configurable items (name, state, progress, temperatures, remaining time, finish time, print
  time, file name) and corner.
- Camera window settings for each printer: start mode, size, position, monitor and always on top.
- PrusaLink web opened already signed in through a local proxy. The password never reaches the browser.
- Settings window: printers, connection test, camera with a live preview, notifications, polling interval and
  language.
- Passwords stored in the Windows Credential Manager.
- Localization: English, Czech, Slovak, German, Spanish, French, Italian and Polish.
- App icon for the executable, the windows and the tray. On Windows 11 the tray icon is shown in the visible part of
  the taskbar instead of the hidden-icons area.
- File logging with a "Detailed logging (Debug)" setting. Unhandled exceptions are written to the log before the app
  ends.
- About window with the third-party licences and a "Report a problem" group: save a diagnostic bundle (ZIP without
  passwords), copy a short report to the clipboard, open the log folder.
- Single instance: starting the app again doesn't open a second tray icon.
- Native AOT publishing (single executable, no .NET runtime required).
- Windows installer (Velopack): installs for the current user without administrator rights, adds a Start menu
  shortcut, and its uninstall removes the app's notifications, notification settings and system registrations (settings
  and passwords stay). A ZIP is still available.
- "Start automatically when I sign in to Windows" option (installed version only, off by default) and "Offer
  pre-release versions" option in the new Startup and updates tab of the settings.

### Fixed

- Settings, camera window positions and printer images are saved even when another program (antivirus, backup)
  briefly holds the file. A detected printer model that still can't be saved is tried again every minute, up to five
  times.
- The monitor chosen for the camera window is found again after it is plugged into another connector, and its new
  connector is remembered. A switched-off monitor is still not confused with an identical one next to it.

### Security

- FFmpeg accepts only RTSP over TCP with H.264, nothing else.
- The PrusaLink web proxy rejects write requests from other origins.
- Native libraries are loaded only from the application folder and the system folder.

[Unreleased]: https://github.com/pfoltynek/LayerPeek/compare/v0.5.0...HEAD
[0.5.0]: https://github.com/pfoltynek/LayerPeek/compare/v0.4.0...v0.5.0
[0.4.0]: https://github.com/pfoltynek/LayerPeek/compare/v0.3.1...v0.4.0
[0.3.1]: https://github.com/pfoltynek/LayerPeek/compare/v0.3.0...v0.3.1
[0.3.0]: https://github.com/pfoltynek/LayerPeek/compare/v0.2.0...v0.3.0
[0.2.0]: https://github.com/pfoltynek/LayerPeek/compare/v0.1.0...v0.2.0
[0.1.0]: https://github.com/pfoltynek/LayerPeek/releases/tag/v0.1.0
