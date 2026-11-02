# Changelog

All notable changes to jGlancer for Windows. Versions are `major.minor.patch`: the minor number grows with new
features or something removed, the patch number with fixes; the version changes with each published release.

## 0.5.3 — 2026-11-02

- Changed: a new app icon: a white picture frame with mountains and a sun on dark glass, with blue and red light at the corners.

## 0.5.2 — 2026-11-02

- Changed: a new app icon: a glowing sound wave on a dark tile in the colors of the app.

## 0.5.1 — 2026-11-02

- Changed: in the audio player the top bar is more transparent and hides when the pointer rests, as in the other
  viewers; moving the pointer brings it back.

## 0.5.0 — 2026-11-02

The first public version, free software under the GNU GPL 3.0.

- New: jGlancer looks for a newer version on GitHub once a day and offers it: its notes, Update, Later, Skip This
  Version. An update starts only after its signature has been checked. Settings → General turns this off and has
  Check Now.
- New: About links to the author's GitHub and to the source code, and names the licence.

## 0.4.12 — 2026-11-01

- New: a Black theme (Settings → Appearance → Theme): black background, panels, title bar, mini player and menus,
  fainter lines; dark gray only for buttons, fields and hovered rows. The Dark theme stays as it was.
- Changed: the title bars of the installer, Settings and dialogs match the theme (Windows 11).
- Changed: the accent is exactly the color chosen, in every theme: the default is the icon's blue `#1F3BFF` on switches,
  selection edges and progress bars. Text and thin glyphs in the accent stay a lighter shade of it where needed, so
  they can be read.
- Changed: every ruler with ticks (seeking in video, volume, thumbnail size) is a plain rounded slider. The setting for
  ticks on the ruler is gone. The audio waveform stays.
- Changed: new solid, sharp icons for the Video, Photo and Audio filters, the player's controls and the Settings sections.
- Changed: the filters are quiet: no filled blocks and capitals, the chosen ones slightly raised.
- Changed: the mini player has the blurred cover behind it, as the big player (Settings → Appearance → Effects).
- Changed: smaller big headings, the same corners on buttons and rows, no glow on the playhead.
- Changed: the audio player's scrubber has two looks: Waveform (the narrow one) and Timeline; the wide waveform is gone.
- Fixed: switching videos no longer holds up the window while the last one closes.
- Fixed: a click on the folder in the mini player also scrolls to the file and selects it.
- Fixed: exported segments' temporary files are removed when the player closes.
- Fixed: after an update, shortcuts kept showing the old icon until Windows refreshed it.

## 0.4.11 — 2026-11-01

- New: the app icon, a white eye with a sound wave in its pupil on blue.
- Changed: the accent color is now the blue of the icon.
- New: Settings → Appearance → Accent Color: nine ready colors, a hex field and Windows' color dialog. It applies at
  once in every window; a color too light or too dark for a theme is adjusted there so text stays readable.
- Fixed: switches in the light theme showed Windows' accent color instead of the app's.

## 0.4.10 — 2026-10-20

- New: Settings → Library → Stem Splitting chooses the model: Compact (91 / 60 MB, the default) or Full (the original
  weights, 174 / 115 MB). They sound the same; Full is there for those who want the original. Each downloaded model
  is listed with its size and Remove.

## 0.4.9 — 2026-10-20

- Fixed: another crash while moving files (songs) with a preview open; pictures and covers that are gone no longer
  take the app down anywhere.

## 0.4.8 — 2026-10-20

- Fixed: the app could crash when the selection changed quickly with Quick Preview or the preview pane showing a
  photo (for example while moving several files).

## 0.4.7 — 2026-10-20

- Changed: Quick Preview stays on top of all windows.

## 0.4.6 — 2026-10-20

- New: Quick Preview has Open in Viewer (↗): the file opens in the full viewer.
- New: drag files from Explorer onto the window to open them (a folder dropped on the library still adds it).
- Fixed: after a stack was unfolded, the preview kept its frame timeline.

## 0.4.5 — 2026-10-20

- New: videos with several audio tracks get an Audio Tracks button: pick one, or play several together (Play
  Together, or All Tracks).
- New: subtitles — those in the video and subtitle files named like it beside it (.srt, .ass…); the file's default
  shows by itself, the Subtitles button picks another or turns them off.

## 0.4.4 — 2026-10-20

- Changed: the HDR button shows only for HDR video (HDR10 or HLG); other videos always play the usual way.

## 0.4.3 — 2026-10-20

- New: HDR in the video player — the HDR button (remembered) shows HDR10 and HLG videos at their own brightness on
  an HDR display (turn on "Use HDR" in Windows' display settings). The top bar and the controls then sit above and
  below the picture instead of over it, and the filmstrip hides; off, everything is as before.

## 0.4.2 — 2026-10-20

- New: an image sequence's badge has an arrow — › unfolds the frames, ‹ folds them back.
- New: a folded image sequence in the preview pane and Quick Preview has a timeline: drag through its frames like a
  video, or press play (24 frames a second); "12 / 90" shows the frame.

## 0.4.1 — 2026-10-20

- Fixed: the stem models could not be downloaded (they were in a private repository); they now come from the public
  github.com/volodya88i/jGlancer-models.

## 0.4.0 — 2026-10-20

- New: Split into Stems — a song's menu splits it into vocals, drums, bass and the rest (or 6 stems: also guitar and
  piano) as separate 24-bit WAV files, in a new "<song> split" folder next to it or a folder you choose. It runs on
  this computer with the Demucs model (on the graphics card when it can: a 3-minute song in about 15 seconds),
  in the background with a progress card and Stop; Ctrl+Z moves the result to the Recycle Bin. The model (91 MB for
  4 stems, 60 MB for 6) is downloaded once, only when you agree; Settings → Library shows it with Remove. Your music
  never leaves the computer.

## 0.3.7 — 2026-10-20

- New: the keyboard's media keys (play/pause, next, previous) drive the open video or audio player, else the mini
  player, wherever the focus is. jGlancer takes them only while one of these plays something, so other players
  keep them otherwise.

## 0.3.6 — 2026-10-20

- New: Alt-click "‹ Library" in the audio player and the song goes on in the mini player from the same moment, with
  the player's songs as its queue; "Play in Mini Player" in the player's menu does the same.
- Changed: play, pause, previous and next are solid, sharp glyphs (as on the Mac) in the players, the mini player
  and Quick Preview.

## 0.3.5 — 2026-10-19

- New: image sequences — five or more numbered frames of one name in a folder (render_001.png … render_090.png)
  fold into one stack in the grid, with the frame count on a badge; a click on the badge or a double-click unfolds
  the frames out of the stack (and folds them back). Actions on a folded stack apply to all its frames. Camera
  names such as IMG_ or DSC_ are left alone. Settings → Library → Image Sequences turns it off.
- New: the audio waveform flows into each new song's shape as a wave running left to right, instead of jumping in
  when it has been read; the previous song's settles into a line meanwhile (off with Reduce motion).
- Changed: the narrow waveform is the default.

## 0.3.4 — 2026-10-19

- Fixed: jGlancer did not appear in Windows' Default apps (its capabilities were registered one level too deep),
  so "Make Default…" could not work; registering again moves them to the right place.
- Fixed: after installing for everyone, the Documents folder opened instead of the Default apps page.

## 0.3.3 — 2026-10-19

- Fixed: a file opened from Explorer while an album it is not in was shown played the album's first song (or
  showed its first photo) instead of the file; it now opens with the files of its own folder.

## 0.3.2 — 2026-10-19

- New: jGlancer as the default app for photos, video or music — Settings → Windows has a button for each kind that
  is not jGlancer's yet, and the installer asks the same three questions. Windows lets no program take file types
  itself: jGlancer offers just the chosen kinds and opens Windows' page, where one press of "Set default" does it.
- Fixed: running the installer of the version already installed offers to reinstall instead of "updating" to it.

## 0.3.1 — 2026-10-19

- Changed: the mouse wheel moves the volume and tile-size rulers finely, like dragging; Shift with the wheel steps
  from tick to tick, like Shift while dragging.

## 0.3.0 — 2026-10-19

- New: installer jGlancer-Setup-<version>.exe in the app's own look: for the current user (no administrator) or for
  everyone (Program Files), Start menu and desktop shortcuts, an entry in Windows Apps & features, updates over an
  older version; the uninstaller removes the program and its Explorer entries and asks before deleting the library.
- Fixed: the first-run screen no longer shows empty bars; check boxes and radio buttons use the app's accent color.
- Fixed: no colored fringes on the player's numbers in the light theme.
- Measured: launch 0.4 s, idle 100 MB; a real 2,600-file folder scrolls smoothly at 3200 × 1200 within 420 MB.

## 0.2.0 — 2026-10-14

- New: find duplicates across the library (Edit ▸ Find Duplicates…): copies in groups, the extra ones go to the
  Recycle Bin (undoable) only after a byte-for-byte check.
- New: compare two photos side by side (Compare in the menu, Ctrl+Alt+C) with linked zoom and pan (L), swap (X).
- New: pinned folders — any folder can be pinned to the top of the sidebar (folder menu ▸ Pin to Sidebar).
- New: save the video frame — P saves it as a PNG next to the video, Shift+P copies it; camera button in the player.
- New: Help ▸ About jGlancer with the version and the third-party licences (shipped in the Licenses folder).
- Faster: the first photo shows at once (the grid's thumbnail until the photo is decoded); photos decode straight
  into the picture's memory; background metadata reading waits while the viewer decodes.

## 0.1.0 — 2026-10-14

The first version.

- Library: folders with their trees, a virtualized grid of thumbnails (photos, video, audio covers), kind filters,
  sort, tile size, names, include subfolders, flags in seven colors, search, albums, split view with two panes.
- Preview pane and Quick Preview (Space) with the mini player; Get Info with extended details.
- Photo viewer (zoom, pan, swipe, filmstrip, rotate), video and audio players (speed, frame steps, segment
  selection and export, waveform, zoom and swipe for video), a mini player at the bottom of the window.
- File operations with undo: move, copy, rename, batch rename, Recycle Bin; Open With and Edit In.
- Settings: appearance presets (Performance, Beauty), themes, playback, shortcuts, Explorer integration.
- Explorer: Open With entries, Quick Preview from Explorer and with Space, viewer-alone launch, one app instance.
- Adding big folders in the background with a progress card; a whole drive asks first.
