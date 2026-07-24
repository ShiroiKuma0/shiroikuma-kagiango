# 白い熊 鍵暗号 — changes on top of keepass2android

Fork of [keepass2android](https://github.com/PhilippC/keepass2android) `1.15-r2`, branch `custom`.
Everything below is on top of stock. Current release: **1.15-r2+13** (2026-07-24).

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
