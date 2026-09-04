# 白い熊 鍵暗号 — changes on top of keepass2android

Fork of [keepass2android](https://github.com/PhilippC/keepass2android) `1.15-r3`, branch `custom`.
Everything below is on top of stock. Current release: **1.15-r3+6** (2026-09-04).

Rebased onto upstream `1.15-r3` (versionCode 251), which brings the #3066 fix — background sync no
longer loses the keyfile — plus Crowdin translation updates. The fork build counter restarts at `+1`
on each new upstream line; versionCode `<UPSTREAM_CODE>*10000+N` keeps upgrades monotonic across it.

## Fork identity & packaging
- App id **`shiroikuma.kagiango`**, launcher label **白い熊 鍵暗号** — installs side-by-side with the official Keepass2Android.
- `AppNames.PackagePart` → `kagiango`, making the C#-derived content providers, custom permissions, and internal intent actions unique vs upstream (`keepass2android.kagiango.*`, `kp2a.kagiango.*`); manifest permission/authority literals and `searchable_offline.xml` updated to match.
- Keyboard bridge fix: `Intents.LockDatabase` / `Intents.KeyboardCleared` use the `shiroikuma.kagiango` prefix so the bundled KP2A keyboard's lock-key and clear-on-lock keep working under the new app id; file-chooser provider authorities likewise.
- **NoNet (offline) flavor only** — no cloud-storage SDKs, no QR scanner.
- Fork versioning `<UPSTREAM>+<N>` (versionCode `<UPSTREAM_CODE>*10000+N`) with the manifest as single source of truth; `build-scripts/fork-build.sh` builds the signed release, copies it to `~/tmp`, and auto-bumps `N`.
- Release signing via gitignored `keystore.properties` → `~/.android-keystores/shiroikuma-kagiango.jks`.
- Build hardening: packaging outputs are purged before every build and the built APK's embedded `versionCode` is verified against the manifest — a stale MSBuild package aborts the build instead of shipping.
- New-issue form fixed and repository de-branded to this fork.

## 白い熊 鍵暗号 UI theming page
- New `ShiroikumaUiActivity` (Settings list, main-screen overflow menu, or long-press the settings cog): a programmatically built, deeply indented page of sections exposing **per-element colors and fonts**.
- Theming layer under `Theming/`: `ThemeColors` (slot table), `ThemeConfig` (prefs), `FontManager` (import/cache/list external fonts), `Kp2aTheme` (apply helpers), `ThemeSeeder` (signature palette with versioned re-seeding; newly added slots self-heal onto existing installs without touching user choices), plus `FontPickerDialog` and `ColorPickerDialog`.
- External-font picker renders each font name in its own glyphs; app-wide global font; per-slot font family/weight/size.
- App-language selector at the top of the page.
- Signature palette: pure `#000000` backgrounds, pure `#FFFF00` text/borders/icons.
- **kxkb heading style**: a section is a full-width 1px spacer marking the border with the previous group, then a 20 sp bold accent title carrying a **word-width** underline; sub-headings are the same one size down (17 sp, 1.5 dp underline), indented and without the full-width spacer.

## Themed surfaces (black-yellow everywhere)
- **Unlock screen** (`PasswordActivity`): background, labels, password field, buttons, filename, collapsing-toolbar title.
- **Entry & group lists** (`PwEntryView`/`PwGroupView`): titles, usernames, group paths; traced list icons (black body, yellow ring, yellow line content).
- **Title rows**: group screens, search results, and TOTP search use a NoActionBar theme + own toolbar (the Material 3 ActionBar surface tint ignores runtime overrides) so background, title, icons, and status bar are fully themeable.
- **FABs**: black background, yellow border, yellow icon.
- **Settings pages** (app settings, database settings, and the UI page itself): NoActionBar + own toolbar; preference rows themed live as they attach (titles, subtitles, category headers, icons, accent-tinted switches and checkboxes); slots for settings background / item title / item subtitle / category header.
- **Entry view**: own toolbar, themed background, field-label and field-value slots, traced-icon tint for the per-field icons and three-dot menus, accent on outlined buttons (previous versions, restore/remove history, attachments) and the TOTP countdown bar, themed edit FAB; re-themes when a plugin injects a field.
- **Start screen, database-selection screens, QuickUnlock**: title row and status bar themed.
- **Dialogs**: generic alert-dialog theming (black rounded surface, 2dp yellow border, themed title/message, accent buttons).
- **Launcher icon**: black-yellow (traced lock mark in `#FFFF00`).

## Export / Import — one-ZIP settings backup
- **Export / Import is the first section of the UI page** (Kōjiki-style placement): the panel entry, the backup folder, and — directly beneath them rather than in a section of their own — the automation switch, the 「Use authorization token?」 switch, and the token row (shown only while a token is actually being asked for).
- **The panel** (`ExportImportDialog`): a bordered black-and-yellow box carrying title, intro, an all-files-access prompt when that permission is missing, the tappable backup-folder box, the newest-backup line (queried each time the panel opens, with its size), 全選択 plus the category checkboxes, and the button bar.
- **Button bar in ArcaneChat form**: Cancel alone on the left, Import and Export grouped on the right, all round pills — black fill, thin accent stroke, accent text and ripple.
- **Backup folder** is shown in warn-red until it is set — in the panel *and* on the UI page row — and can be typed in or picked with a built-in folder browser. It is stored in its own prefs file, so it never travels inside a backup.
- **Eleven categories**, mirroring the app's own settings screens: colors (UI page), fonts (UI page) with **imported font files** as an independently selectable sub-option, display & language, security, QuickUnlock, password access (keyboard/autofill/TOTP), file handling, TrayTotp, password-generator profiles, debug log.
- Category keys are **scanned from the preference XML screens at runtime**, so a setting a future upstream release adds is carried by the next rebase rather than by remembering to edit a list.
- **One ZIP per export**, named `shiroikuma-kagiango_<yyyy-MM-dd_HH-mm-ss>.zip` (the sister-app family convention — no version, no infix, no suffix), holding `manifest.json` plus one `<id>.json` per category and the font files under `fonts/`.
- **Nothing partial ever survives.** Both callers — the panel and the headless receiver — go through one writer (`Kp2aBackup.ExportToDirectory`) that builds the archive under `<name>.part` and renames it only once it is whole, removing the partial in a `finally` that covers cancellation, exceptions and success alike. A run that fails or is stopped leaves the backup folder exactly as it found it: no short archive, no stray partial, and nothing half-written that could be offered for import.
- **Which categories start ticked is the app's own answer**, carried on the category definition (`DefaultSelected`, defaulting to on) and used by the panel and the automation contract alike, so the in-app sheet and a sister app's picker open on one set of ticks. Only the **debug log** starts unticked — it is derived, disposable and regenerated on use.
- **Import merges** per key rather than wiping, so a restore never destroys settings a category didn't cover and re-importing the same file is idempotent; categories absent from the archive are skipped, and one failing category never aborts the others.
- **Security — the export is an allow-list, not a deny-list.** Only keys a category claims are written. On top of that, `kp2a_ioc_*` (the biometric unlock's Android-Keystore-wrapped master password, with its `_iv`/`_mode` companions) and `KP2A.PasswordAct.AuxFileIoc*` (serialized `IOConnectionInfo`s, which can carry a remote-storage user name and password) are excluded outright. The same filter runs on **import**, so a hand-edited ZIP cannot inject a credential key back into the app.
- **Dialog chain**: acknowledging a successful export or import closes the info dialog, the panel, and the UI settings page together; the import dialog offers "Restart now" (relaunch so every restored setting is re-read) and "Later", both of which close the chain. Failures ("Export failed…", "No categories selected.") close only themselves, leaving the panel open to fix and retry.
- Info dialogs use the fork's black surface with the 2 dp yellow border.
- `MANAGE_EXTERNAL_STORAGE` is declared so the backup folder can be anywhere on shared storage.

## 保存復元 automation contract (v2)
- `StateExportReceiver` implements the sister-app wire contract on `shiroikuma.kagiango.action.EXPORT_STATE`, `…LIST_CATEGORIES` and `…CANCEL_EXPORT`, so 自由作業盤's 保存復元 project can back this app up headlessly in one run.
- `EXPORT_STATE` runs the ordinary category ZIP export with no Activity: extras optional `token`, optional `path` (an absolute directory that overrides the configured folder), optional `items` (comma list of category ids; absent = the default set, i.e. exactly the categories answered as `on`), optional `progress_action`, plus the `reply_action` / `reply_package` / `reply_id` trio.
- `LIST_CATEGORIES` answers `id<TAB>label<TAB>parent<TAB>on|off` per line — the third field is the parent id on sub-options (`fonts.files` under `fonts`) and empty otherwise, and the fourth says whether the item starts ticked, so the caller's picker is drawn from this app's answer rather than a guess.
- **`CANCEL_EXPORT` stops a running export**, declared on the same exported receiver — a stop path on a (correctly non-exported) service could not be reached from another app at all. It takes an optional `token` plus an optional `reply_id`, **sends no reply of its own**, and is a silent no-op when nothing is running, when the export already finished, or when the `reply_id` names another request — so it is safe to send at any time.
- A cancel is honoured **between entries**: the write loop polls a volatile flag and unwinds at the next category boundary — never a thread interrupt, never a mid-`write` kill, never `System.exit`. The partial file is deleted, and the cancelled run answers its **own original request** with `ERROR:cancelled` through the normal reply channel, under the same single-fire interlock that guards a success, so the run is proven ended rather than left carrying on unseen.
- **Reply is a fresh broadcast** with `FLAG_INCLUDE_STOPPED_PACKAGES` — no `ResultReceiver`, `PendingIntent` or `Messenger`, and no reliance on the ordered-broadcast result, both of which EMUI severs between third-party apps. Exactly one terminal reply per request, behind a single-fire interlock; the work runs on a background thread under `GoAsync()`.
- **Progress carries real counts**, never a percentage (`区分 3/11 — …` with structured `current`/`total`/`unit`), throttled to one every 500 ms with the completion one always sent.
- Distinct, debuggable errors: `automation disabled`, `bad token`, `device-locked`, `no-directory`, `no-storage-access`, `unknown category in items: …`.
- **The gate inverted in v2, and for one reason: a pasted secret cannot survive a wipe.** `automation_enabled` now defaults **on** and a new `automation_require_token` defaults **off**, so an app that has just been restored onto a clean phone answers the batch without anyone having configured anything. Both checks live in a single `AutomationAuth.Refuse()` returning either nothing or the exact `ERROR:` line, so "disabled" and "bad token" cannot drift apart.
- **A token sent to an app that is not asking for one is ignored, never refused.** Tokens outlive the setting they were pasted for; refusing them would turn one switch being off into half a batch mysteriously failing. When a token *is* required it is still compared constant-time.
- The token still lives in its own prefs file and is never part of an export. Its row shows it abbreviated, copies the whole thing on tap, and carries a **Regenerate** action that warns pasted copies must be updated — and it is **hidden entirely** unless 「Use authorization token?」 is on, so a 48-character secret never sits under an off switch inviting a pointless paste.

## The data door — backup *with* data, for a caller we can identify
- **`AutomationProvider`**, an exported `ContentProvider` at `shiroikuma.kagiango.automation`, answering `describe` / `export` / `import` / `cancel` — so 白い熊 応用管理 can back this app up with its settings and put them back on a wiped phone. A provider rather than another broadcast action because **a broadcast cannot tell you who sent it**, and because a caller drawing a list of every installed app needs a synchronous answer.
- **Three checks on the caller, in order, each because the one before it is not enough** (`AutomationCallers`): an **exact package name** — never a prefix, since a package name is not a namespace anyone owns and any sideloaded app may call itself `shiroikuma.evil`; the **uid the kernel reports**, because a declared attribution can be borrowed and a uid cannot; and a **pinned signing certificate**, because whichever caller package is absent from a device is a name anyone can take — and a clean phone is exactly a device where not everything is installed yet.
- **The payload moves through a `ParcelFileDescriptor` the caller opens** — not a path, not a `content://` URI. The descriptor is duplicated inside the provider call (the original dies with the binder transaction) and closed in a `finally`, and the reply is sent only *after* it is closed, since a caller cannot checksum or encrypt a file we are still holding open.
- **`import` exists only here.** It never gets a broadcast action: an import overwrites this app's preferences, and the receiver above is exported with no permission — an import there would let any app on the phone rewrite a password manager's security settings.
- Work runs in `AutomationDataService`, a non-exported `dataSync` foreground service. The notification is raised **before** the job is even looked up, because once `startForegroundService()` has been called the platform kills the process if no notification follows — so a caller retrying with a stale job id would otherwise kill the very app it was backing up. If the platform refuses a background foreground-service start, the job still runs in-process rather than being dropped; if nothing can run it, the descriptor is closed and the caller is told, never left waiting. A 60-second watchdog reaps a job whose service start was accepted but never delivered.
- An import is **spooled to a file** rather than held in memory, and each category is committed **synchronously** — the caller force-stops the app the instant we report success, so an asynchronous write would be lost and the restore would claim success over missing data.
- **`describe` tells the truth about a password manager's backup.** Its `contains` list ends with the verbatim line *"Settings only — never the database, the master password or a stored login"*, so a green backup row in 応用管理 can never be mistaken for a backup of your `.kdbx` — which this export deliberately does not carry.
- Capability discovery through three `shiroikuma.automation.*` manifest `<meta-data>` entries, readable across every installed package **without waking the app** — a frozen app cannot be asked anything. `<queries>` names both caller packages, without which our reply broadcast's `setPackage` would fail silently on Android 11+ and the caller's certificate could not be read at all.

## Change log
- Fork entries (dated, newest first) merged **chronologically** with the upstream change log in one dialog.
- Dialog fully themed: black surface, yellow border on all four sides (text pane padded so it cannot cover the stroke), themed body/heading colors driven by the theme slots, accent OK button.

## Fingerprint unlock
- **App-drawn black-yellow fingerprint dialog** (`ShiroikumaFingerprintDialog`) replacing the untheme-able system BiometricPrompt: themed title/subtitle/status, yellow fingerprint icon, accent Cancel.
- Authenticates via the compat fingerprint API against the same Android-keystore cipher; on devices where the legacy API is unusable it silently falls back to the system prompt (auto-fallback on immediate errors, before any sensor contact).
- Used by the unlock screen's automatic prompt, the fingerprint button, QuickUnlock, and fingerprint setup (the automatic prompt previously bypassed the fork dialog via an adapter-typed overload — fixed).
- Diagnostics: probe results are appended to `Android/data/shiroikuma.kagiango/files/fp-probe.txt` (EMUI suppresses app logcat output).

## Localization
- All fork-added strings provided in English and Japanese (`values` / `values-ja`).
