# Changelog

All notable changes to this project are documented here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and the project uses [semantic versioning](https://semver.org/).

## [Unreleased]

### Added

- **Emoji group icons.** Pick an emoji for a group from a quick palette or type your own
  (`Win`+`.`). Rendered in full colour through Direct2D with `ENABLE_COLOR_FONT` —
  GDI+ and WPF both draw colour fonts as flat black silhouettes, so neither could be
  used. Multi-codepoint sequences (skin tones, ZWJ families) shape correctly.
- The settings window shows the real tile, produced by the same code that writes the
  `.ico`, rather than an approximation drawn with WPF text.
- Group icons are rebuilt automatically when the emoji, colour or name changes, and every
  shortcut that uses them — including the copy Windows made when the group was pinned —
  is repointed at the new file. Icon files carry a content hash in their name, because
  Explorer caches icons by path and would otherwise keep drawing the old picture.
- Icons are also reconciled at startup, so a config edited or restored while the resident
  was not running heals itself.
- **Refresh pinned taskbar icons** in the tray menu, for the one case Windows will not
  handle on its own (see the README).
- Every save now keeps the previous configuration as `groups.json.bak`.

- **Update notifications that lead somewhere.** The tray notification is now clickable and
  opens the download page, and settings shows the running version, a **Check now** button
  that ignores the daily throttle, and the result of the last check.
- The update check reads a static `latest.json` from the project site instead of the
  GitHub API, whose 60-requests-per-hour-per-IP limit would silently break checking for
  everyone in an office behind one address. Only absolute HTTPS links from the feed are
  ever opened, and an unparseable version is treated as "no update".
- `DisableUpdateCheck` and `UpdateFeedUrl` policies under
  `HKLM\SOFTWARE\Policies\TaskbarFold`, so a managed deployment can silence the check or
  point at its own feed.
- A failed check no longer counts against the daily throttle, so a machine that was
  offline in the morning does not wait another day.

### Fixed

- Searching installed applications now matches the application id as well as the display
  name. Codex is listed by Windows as "ChatGPT", so searching for "codex" found nothing.
- Editing a group's name or emoji and immediately selecting another group discarded the
  edit; the delayed bindings that caused it were removed and the save is debounced instead.
- Group and shortcut rows reported their view-model type name to accessibility tools.

## [0.1.0] — 2026-07-27

First working build. Covers milestones M0 through M5 of `PROJECT.md`.

### Added

- **Groups.** Create, rename, duplicate and delete taskbar mini-folders. Each group
  keeps an immutable GUID, so renaming it never breaks an existing taskbar pin.
- **Flyout.** Borderless window with a configurable icon grid, per-group colour and
  opacity, Windows 11 acrylic backdrop where available, and a 120 ms fade-and-slide.
- **Resident process.** A tray process holds a pre-created window per group. A pinned
  shortcut runs `TaskbarFold.exe --group <GUID>`, which sends the id and cursor
  position over a per-user named pipe and exits without creating any UI.
- **Multi-monitor and DPI.** Per-monitor v2 awareness; the flyout is placed against the
  taskbar of the monitor holding the cursor, derived per monitor rather than from the
  primary taskbar, and works with an auto-hidden taskbar.
- **Icons.** Extracted through `IShellItemImageFactory`, which returns the largest asset
  the shell has and covers Store apps as well as files. Cached as PNG under
  `%APPDATA%`, invalidated by the source file's timestamp and size.
- **Store apps.** Enumerated from `shell:AppsFolder` with their AppUserModelIDs, and
  launched through the shell — no executable path required, no elevation.
- **Keyboard.** Type-to-search, arrow navigation, Enter to launch, Escape to clear then
  close, digits 1–9 for quick launch, and optional global hotkeys per group.
- **Drag and drop.** Reorder icons inside the flyout itself, and drop files from
  Explorer straight into a group in settings.
- **Broken shortcuts.** A moved application is searched for by file name and rebound
  instead of turning into a permanent broken icon.
- **Pinning.** One button generates the `.lnk` with the group's AppUserModelID and a
  multi-resolution icon, then opens Explorer with it selected. The file is edited in
  place on later changes, because recreating it would drop the taskbar pin.
- **Themes.** Light and dark, following the Windows setting by default.
- **Updates.** Checked asynchronously at most once a day; nothing on the UI path waits.
- **Config.** Single `groups.json` under `%APPDATA%\TaskbarFold`, written atomically,
  versioned and migrated, with corrupt files quarantined rather than discarded.
- Portable zip and a per-user Inno Setup installer that never requests elevation.

### Known limitations

- Pinning to the taskbar cannot be automated. Windows 10 and 11 removed the API and
  the registry workarounds break with feature updates, so the final drag is manual.
- The acrylic backdrop needs Windows 11 build 22621 or newer; older systems get a
  solid colour with transparency.

[Unreleased]: https://github.com/stavtime/taskbarfold/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/stavtime/taskbarfold/releases/tag/v0.1.0
