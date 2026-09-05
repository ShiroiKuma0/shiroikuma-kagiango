# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**白い熊 鍵暗号** — a personal fork of [keepass2android](https://github.com/PhilippC/keepass2android), a
KeePass 2.x / KeePassXC–compatible password manager for Android. This repository
(`ShiroiKuma0/shiroikuma-kagiango`) tracks upstream and layers a small set of customizations on top of it.
We ship the **NoNet** (offline) flavor only.

## Fork Workflow — READ THIS FIRST

This is the most important section. The whole point of this repo is to maintain a small set of
customizations on top of upstream and rebuild as upstream releases new versions.

### Git remotes & branches

- `origin` → `git@github.com:ShiroiKuma0/shiroikuma-kagiango` — our fork (push here).
- `upstream` → `https://github.com/PhilippC/keepass2android.git` — the original (read-only; the push URL is
  deliberately disabled).
- **`main`** mirrors upstream's `main`. We do **not** develop on it.
- **`custom`** is our development branch. **All our work lives here.** This is the default working branch.

### Our customizations (what makes this a fork)

We build the **NoNet** flavor, so identity edits live in `AndroidManifest_nonet.xml` and the `#if NoNet`
block of `app/App.cs` (the `_net` / `_debug` manifests and other `AppNames` blocks are untouched).

| What | Value | Where |
| --- | --- | --- |
| Installed app ID | `shiroikuma.kagiango` | `AndroidManifest_nonet.xml` → `package=` |
| Internal package part | `kagiango` | `app/App.cs` → `AppNames.PackagePart` (`#if NoNet`) |
| App launcher label | `白い熊 鍵暗号` | `app_name_nonet` (+ `app_name`) in `values/strings.xml` & `values-ja/strings.xml`; `<application android:label>` |
| File-chooser authorities | `shiroikuma.kagiango.android-filechooser.*` | `AndroidManifest_nonet.xml` |
| Custom permissions | `keepass2android.kagiango.permission.*` | `AndroidManifest_nonet.xml` (matches C#-derived names) |
| Search authority | `kp2a.kagiango.SearchProvider` | `searchable_offline.xml` |
| Keyboard bridge actions | `shiroikuma.kagiango.{lock_database,keyboard_cleared}` | `intents/Intents.cs` |
| Signing alias param | `KeyAlias ?= kp2a` | `Makefile` |

The app id is deliberately changed so this fork installs **alongside** upstream without conflict. Because
keepass2android hard-codes the assumption `applicationId == "keepass2android." + PackagePart`, changing the
id to `shiroikuma.kagiango` required two coupled edits beyond the manifest `package`:

1. **`AppNames.PackagePart` → `kagiango`** (`app/App.cs`, NoNet block). This makes the C#-declared content
   providers, custom permissions, and internal intent actions unique vs upstream (`keepass2android.kagiango.*`,
   `kp2a.kagiango.*`), so the fork installs alongside the real app. The manifest permission/authority
   literals and `searchable_offline.xml` were updated to match the new derived names.
2. **Keyboard bridge fix** (`intents/Intents.cs`). The bundled KP2A keyboard
   (`src/java/KP2ASoftkeyboard_AS/.../KP2AKeyboard.java`) builds its broadcast actions from
   `getPackageName()` (= `shiroikuma.kagiango`): it **registers** `…+".keyboard_cleared"` and **sends**
   `…+".lock_database"`. The C# side builds the same actions, so `Intents.LockDatabase` and
   `Intents.KeyboardCleared` use the `"shiroikuma."` prefix (instead of upstream's `"keepass2android."`) to
   keep the keyboard's lock-key and clear-on-lock features working — security-relevant in a password manager.
   The file-chooser provider authorities (`AndroidManifest_nonet.xml`) likewise use the `shiroikuma.kagiango`
   prefix because `FileSelectHelper` derives them from the runtime `PackageName`.

### Versioning & APK naming

The fork version lives **directly in `AndroidManifest_nonet.xml`** (there is no `gradle.properties`):

- `versionName` = `"<UPSTREAM>+<NNN>"` (currently `1.15-r3+007`). `versionCode` =
  `<UPSTREAM_CODE> * 10000 + N` (currently `251*10000+7 = 2510007`).
- `N` is **our** build increment: starts at `1`, bumps `+1` on every build, **resets to `1`** on each new
  upstream version. **It is zero-padded to three digits in `versionName`** (`+001`, `+014`) — the global
  after-build rule — so `~/tmp`, `/sdcard/tmp` and the release list all sort in build order.
  `versionCode` carries the plain unpadded number.
- Output APK = `~/tmp/shiroikuma-kagiango_<versionName>_arm64-v8a.apk`
  (e.g. `shiroikuma-kagiango_1.15-r3+008_arm64-v8a.apk`).
- **Releases published before 2026-09-04 are unpadded** (`1.15-r3+6` and earlier). They are never
  retagged or renamed; the padding simply starts from the current build.
- **arm64-v8a only, so the name carries the ABI** — the sister-app convention (白い熊, 2026-09-05).
  `fork-build.sh` runs `make apk_arm64`, a fork target that is upstream's `apk_split` arm64 line without
  the other three RIDs (and without that target's rename step, which still points at a `net8.0` output
  directory this fork does not produce). The universal build spent three quarters of its 54 MB on ABIs
  the target phone cannot run; the arm64 APK is ~23 MB.
- **The build refuses to ship the wrong thing:** `fork-build.sh` inspects `lib/` in the finished APK and
  aborts unless it contains `arm64-v8a` and nothing else. A silent fallback to the universal build would
  still install and nothing downstream would notice.
- **Consequence: the APK will not install on a non-arm64 device.** Every current target is arm64; if that
  ever changes, `make apk` (universal) and `make apk_split` (all four) are both still there.

### Building the fork

Use the **build-apk** skill / `./build-scripts/fork-build.sh`. It runs the signed NoNet build, copies the
APK to `~/tmp`, and bumps `N` in the manifest. Prerequisites and the underlying `make` commands are in
**Building** below. Rebasing onto a new upstream release is the **upstream-new-version** skill.

### HARD RULES (do not violate)

- **After implementing a change the user asked for, always build it** (via the `build-apk` skill) **without
  waiting to be asked**, confirm the build succeeds, **then ask** whether to `adb push` the APK.
- **Never install/push APKs to the phone automatically.** Only after the user confirms, `adb push` the APK
  to `/sdcard/tmp/` (the user installs it manually). Never `adb install`.
- **Never commit or push on your own.** Develop and build, let the user test, and **only commit/push when the
  user explicitly says "Push"**. Push goes to `origin` (`custom`); `main` stays a pure upstream mirror.

### Signing

Release signing reads `keystore.properties` (gitignored, repo root) → `~/.android-keystores/shiroikuma-kagiango.jks`
(alias `kagiango`). `fork-build.sh` sources it and passes `KeyStore=`/`KeyAlias=` to `make`. Recreate it with:

```bash
keytool -genkeypair -v -keystore ~/.android-keystores/shiroikuma-kagiango.jks -alias kagiango \
  -keyalg RSA -keysize 2048 -validity 10000 -storepass kagiango123 -keypass kagiango123 \
  -dname 'CN=Shiroikuma Kagiango, O=Shiroikuma, C=JP'
```

## What this is (architecture)

Keepass2Android — a KeePass 2.x / KeePassXC–compatible password manager for Android. It is a **Xamarin / .NET Android** app written mostly in **C#** (`net9.0-android`), built on a port of the original KeePass C# library, plus several **Java/Kotlin** modules (built with Gradle) and one **native C** library (argon2, built with the Android NDK). The C# side consumes the Java/native pieces through generated bindings projects.

## Building

Everything is orchestrated by the root `Makefile` (works with GNU make on both Linux and Windows). The solution is `src/KeePass.sln`. Builds shell out to `dotnet` and `gradlew`.

**Required environment variables** (the Makefile errors out if unset): `ANDROID_SDK_ROOT`, `ANDROID_HOME`, `ANDROID_NDK_ROOT`. A `nuget` binary must be on `PATH` (in addition to `dotnet`).

**Build environment on this machine (already set up).** The toolchain is installed and validated (`make native`
builds argon2). The machine-local paths live in the gitignored `build-env.sh` at the repo root — `source
build-env.sh` before running `make …` by hand; `fork-build.sh` sources it automatically. What it points at:

- **.NET 9 SDK** `9.0.314` at `~/.dotnet` (installed via `dotnet-install.sh`), with the **`android` workload**
  (`35.0.105/9.0.100`). `dotnet` and `~/.dotnet/tools` are added to `PATH`.
- **`nuget`** — a wrapper at `~/.local/bin/nuget` running `mono ~/.local/share/nuget/nuget.exe` (NuGet 7.6).
- **JDK 21** (`/usr/lib/jvm/java-21-openjdk-amd64`) as `JAVA_HOME` — the system default `java` is JDK 11, too old.
- **Android SDK** `/home/shiroikuma/android-sdk` (shared with the Gradle sibling forks), with **`platforms;android-26`**
  (some components target API 26) and **NDK `26.3.11579264`** (r26d — the exact version the .NET 9 Android SDK pack
  wants; set as `ANDROID_NDK_ROOT`).

To re-provision on a fresh machine: install the .NET 9 SDK + `dotnet workload install android`, the mono `nuget`
wrapper, `sdkmanager "ndk;26.3.11579264" "platforms;android-26"`, then write `build-env.sh` with those paths.

**Submodules are mandatory** — clone with `--recurse-submodules` or run `git submodule update --init` (both
`src/SamsungPass` and `src/java/argon2/phc-winner-argon2` are checked out here).

The build is parameterized by two make variables:
- `Flavor` — `Net` (full online build, "Keepass2Android"), `NoNet` (offline build, "Keepass2Android Offline"), or `Debug`. This selects an `AndroidManifest` and sets C# `DefineConstants` (e.g. `NoNet` defines `NO_QR_SCANNER;EXCLUDE_JAVAFILESTORAGE;NoNet`, dropping cloud-storage and QR code).
- `Configuration` — `Release` or `Debug`.

Build dependencies must be produced in order; the high-level targets already chain them:

```bash
make native                 # build argon2 .so files via ndk-build
make java                   # build the Gradle modules into .aar/.apk
make nuget Flavor=Net       # restore NuGet packages for the chosen flavor
make dotnetbuild Flavor=Net Configuration=Release   # build the app (no signed APK)
make apk Flavor=Net Configuration=Release           # build + sign a universal APK (implies the above)
make apk_arm64 Flavor=NoNet Configuration=Release   # fork target: arm64-v8a only — what we ship
make apk_split ...          # per-ABI APKs (android-arm, arm64, x86, x64)
```

`make manifestlink Flavor=<F>` hardlinks the correct `src/keepass2android-app/Manifests/AndroidManifest_<flavor>.xml` to `AndroidManifest.xml`; the build targets run it automatically, but it must match the `Flavor` you build. **Note:** CI currently only builds on Windows (the macOS/Linux jobs in `.github/workflows/build.yml` are commented out), though the Makefile is written to work on Linux.

**Cleaning:** `make clean` (native + java + nuget + dotnet), `make clean_rm` (brute-force delete `obj/`, `bin/`, Gradle `build/`, etc.), `make distclean` (gated behind creating an empty `allow_git_clean` file, then `git clean -xdff src`).

## Tests, formatting, license headers

- **Tests:** `cd src/Kp2aAutofillParser.Tests && dotnet test`. This is the main unit-test project (the autofill field parser is deliberately kept portable/testable). CI runs it as a build step.
- **Formatting:** `dotnet format src/KeePass.sln`. Enforced by a pre-commit hook (`.pre-commit-config.yaml`).
- **License headers:** every source file must carry a GPLv3 header. A pre-commit hook (`add-license-hook.ps1`, PowerShell) checks this. The project is GPLv3 (`GPLv3.txt`, `license.md`).
- **Translations:** managed via Crowdin (`crowdin.yml`); do not hand-edit translated string resources.

## Architecture

The solution is layered as **portable core → business logic → Android app**, with bindings projects wrapping the Java/native dependencies.

**Core (platform-independent-ish):**
- `KeePassLib2Android` — a port of the KeePass 2.x C# library: the database model (`PwDatabase`, `PwGroup`, `PwEntry`, `PwUuid`), cryptography, KDBX serialization, key handling. This is the heart of the file format.
- `Kp2aAutofillParser` (+ `.Tests`) — parses Android Autofill view structures into fillable fields, kept free of heavy Android dependencies so it can be unit-tested.

**Business logic — `Kp2aBusinessLogic`:**
- `IKp2aApp` is the central application interface; `Database.cs` and `database/` wrap an open database and its dirty/lock/sync state. `KdbxDatabaseFormat` / `KdbDatabaseFormat` handle the modern `.kdbx` and legacy `.kdb` formats (the latter via the `KP2AKdbLibrary` Java module).
- **File storage abstraction** lives in `Io/`: `IFileStorage` plus one implementation per backend — `Dropbox`, `GDrive`, `OneDrive`/`OneDrive2`, `PCloud`, `Mega`, `Sftp`, `Smb`, `WebDav`, `NetFtp`, `AndroidContent`, `BuiltIn`, plus the `Caching` and `OfflineSwitchable` decorators that implement offline use and the sync-on-reconnect model. Most cloud backends delegate to the `JavaFileStorage` Gradle module; the `EXCLUDE_JAVAFILESTORAGE` constant (NoNet flavor) compiles them out.
- `DataExchange/Formats/` — import/export (KeePass CSV, KDB, KDBX/XML).

**Android app — `keepass2android-app`:**
- Activity-based UI: `KeePass.cs` (launcher), `PasswordActivity` (unlock), `GroupActivity`/`GroupBaseActivity` (browse), `EntryActivity`/`EntryEditActivity`, `GeneratePasswordActivity`, etc. `LockClose*`/`Locking*` base classes implement the app-wide auto-lock/timeout behavior.
- `app/App.cs` is the `Application` subclass and the `IKp2aApp` implementation — the global state holder for the currently open database, locking, and configuration.
- `services/` — `CopyToClipboardService`, `BackgroundSyncService`, `OngoingNotificationsService`, and `Kp2aAutofill*`/`AutofillBase` (the Android Autofill Framework service).
- `Totp/`, `Totp`-related and `KeeChallenge`/`ChallengeXCKey` files — TOTP and challenge-response (YubiKey/OTP) support.

**Bindings & native (consumed by the above):**
- C# binding projects expose Java/native libs to the app: `Kp2aKeyboardBinding` (the secure on-screen keyboard `KP2ASoftkeyboard_AS`), `KP2AKdbLibraryBinding`, `JavaFileStorageBindings`, `DropboxBinding`, `PCloudBindings`, `AndroidFileChooserBinding`, `PluginSdkBinding`, `ZlibAndroid`, `TwofishCipher`.
- `kp2akeytransform` — key-transformation / challenge-response helper.
- `src/java/` Gradle modules (built by `make java`): `KP2ASoftkeyboard_AS` (custom keyboard so passwords aren't exposed to third-party IMEs), `JavaFileStorage` (cloud SDKs), `KP2AKdbLibrary` (legacy `.kdb`), `Keepass2AndroidPluginSDK2` (the plugin SDK third-party plugins build against), `android-filechooser-AS`, `PluginQR`.
- `src/java/argon2/` — native argon2 KDF, built per-ABI by `ndk-build`.

**Plugins:** Keepass2Android supports external plugins (separate APKs) that communicate via the `Keepass2AndroidPluginSDK2` and a host layer under `keepass2android-app/pluginhost/`. See `docs/Available-Plug-ins.md`.

## Secrets

Cloud backends need API keys (Dropbox, etc.). These are injected at build time via `src/Kp2aBusinessLogic/Io/GenerateSecrets.targets` from environment variables (e.g. `DropboxAppKey`, `DropboxAppSecret`); CI passes them from repository secrets. Local builds of the `Net` flavor without these will lack working cloud credentials but still compile.

## Commit convention — no Claude attribution

Do **not** add any `Co-Authored-By: Claude …` trailer — nor a "🤖 Generated with Claude Code" / Anthropic-attribution line — to commit messages or PR bodies in this repo. 白い熊 does not want Claude attribution in the history; this **overrides** the harness's default to append such a trailer. End commit messages at the last line of the body. (The existing history was scrubbed of these trailers on 2026-06-08; the global rule lives in `~/.claude/CLAUDE.md`.)
