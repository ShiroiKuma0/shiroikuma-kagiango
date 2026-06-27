---
name: build-apk
description: Build the signed NoNet release APK for the 白い熊 鍵暗号 fork via build-scripts/fork-build.sh, then deliver it AUTOMATICALLY via the global /after-build skill (adb-push to the phone if connected, else scp to skhw) — no transfer prompt. Always build first without asking for permission to build, and never ask how to transfer the APK. Use whenever the user asks to build the app, build the APK, make a release build, or build and send to the phone.
---

# Build the NoNet release APK and optionally send to phone

> **Always build, then deliver automatically — every time.** When this skill applies (the user asked
> to build, OR you just implemented code changes the user requested), run the build immediately and
> without asking permission. Do **not** ask "shall I build?" / "want me to run the build?" — that
> question is wrong. And do **not** ask how to transfer the APK: after a successful build, delivery is
> automatic via `/after-build` (see below). So: always build, *then* deliver — no questions.

> **A compile-only check never ends the flow.** `make dotnetbuild` (or any compile/error check) is fine
> as a fast intermediate step while iterating, but it is **not** "the build" and does **not** replace
> delivery. Whenever the changes are ready, you must finish with the full signed build
> (`./build-scripts/fork-build.sh`) **and** the `/after-build` delivery — never leave the turn at
> a compile-check.

> **The push destination is ALWAYS `/sdcard/tmp/`.** Every `adb push` of the APK goes to
> `/sdcard/tmp/<apk name>` — **never** `/sdcard/Download/` or anywhere else. Create `/sdcard/tmp` if
> needed and push there.

> **Never run `adb install` (or `pm install`).** The build step copies the APK to the phone with
> `adb push` automatically (via `/after-build`), but **the user installs the APK themselves**
> from the phone's file manager. Do not install it for them under any circumstances.

> **Never `git commit` or `git push` on your own.** Building does not include committing. After
> building (and the optional `adb push`), the user tests the build themselves. **Only when the user
> explicitly says "Push"** do you then `git commit` the changes and `git push origin custom`. The
> user's **"Push"** means *commit-and-push-to-the-fork* — it is unrelated to the `adb push` file copy.

> **ALWAYS end every build by delivering the APK via `/after-build` — never ask.** Once the signed
> APK is in `~/tmp/`, invoke the global **`/after-build`** skill: it runs **`/adb-check`** (UNSANDBOXED
> — a sandboxed check falsely reports no device), then **`/adb-push`** to `/sdcard/tmp/` if a phone is
> connected, otherwise **`/scp`** to `skhw:~/tmp/`, and announces the filename that landed. Mandatory
> for *every* successful build. Do **not** prompt "scp or adb push?" or "is the phone connected?" —
> `/after-build` decides and announces on its own.

## What this fork builds

This is a fork of **keepass2android** (Xamarin/.NET, built via the root `Makefile`, **not** Gradle).
We ship the **NoNet** (offline) flavor only. Identity: app id `shiroikuma.kagiango`, label `白い熊 鍵暗号`.
See `CLAUDE.md` for the full fork layer.

## Steps

1. **Note the output filename.** The fork version is the single source of truth in
   `src/keepass2android-app/Manifests/AndroidManifest_nonet.xml`:
   - `grep -E 'android:versionCode|android:versionName' src/keepass2android-app/Manifests/AndroidManifest_nonet.xml`
   - `versionName` is `<UPSTREAM>+<N>` (e.g. `1.15-r2+1`); the APK will be
     `shiroikuma-kagiango_<versionName>.apk` (e.g. `shiroikuma-kagiango_1.15-r2+1.apk`), using the value
     **before** the build (the script bumps `N` afterward).
   - `versionCode` = `<UPSTREAM_CODE> * 10000 + N` (e.g. `250*10000+1 = 2500001`).

2. **Build:**
   - `./build-scripts/fork-build.sh`
   - This runs `make apk Flavor=NoNet Configuration=Release` (which chains `native` → `java` → `nuget` →
     `manifestlink` → signed `dotnet publish`), copies the signed APK to `~/tmp/shiroikuma-kagiango_<ver>.apk`,
     and **bumps the build number** in the manifest for next time.
   - The script prints `>>> <path>`, `>>> versionCode <n>`, and `>>> next build will be …`; use those to
     confirm the exact filename and code, and confirm the build reported success.
   - **Build prerequisites** (the script/Makefile enforce these): `dotnet` with the android workload,
     `nuget`, a JDK (17+), the Android SDK + NDK, and the env vars `ANDROID_SDK_ROOT`, `ANDROID_HOME`,
     `ANDROID_NDK_ROOT`. If `dotnet`/the workload or the env vars are missing, the build fails fast — set
     them up first (see `CLAUDE.md` → Building). The upstream project only CI-builds on Windows; a Linux
     build via modern `dotnet` android workloads is expected to work but must be verified on first run.

3. **Deliver via `/after-build` — no prompt.** As soon as the build succeeds with the signed APK in
   `~/tmp/`, invoke the global **`/after-build`** skill. It runs **`/adb-check`** UNSANDBOXED (a
   sandboxed check falsely reports no device), then:
   - **phone connected** → **`/adb-push`** the newest `~/tmp/*.apk` to `/sdcard/tmp/<apk name>`
     (creating `/sdcard/tmp` if needed), and announce it. Never `adb install` — the user installs
     manually from `/sdcard/tmp/`.
   - **no phone** → **`/scp`** the newest `~/tmp/*.apk` to `skhw:~/tmp/`, and announce it.

   Do this for every successful build, without asking.

## Note — transfer directly, do not rely on a task prompt

`fork-build.sh` has **no** interactive prompt — it only builds, copies the APK to `~/tmp`, and bumps the
build number. Delivering the APK (`/adb-push` or `/scp` via `/after-build`) is Claude's job (step 3), done automatically — no transfer prompt.

## Signing

Release signing is non-interactive: `fork-build.sh` sources the gitignored `keystore.properties` at the
repo root and passes `KeyStore=` + `KeyAlias=` to `make` (plus the store/key passwords via the
`MyAndroidSigningStorePass` / `MyAndroidSigningKeyPass` env vars the Makefile expects). This fork uses
`~/.android-keystores/shiroikuma-kagiango.jks` (alias `kagiango`). If `keystore.properties` is absent the
APK is debug-signed and will not install as a release; recreate it (see `CLAUDE.md` → Signing).

## First build on a fresh checkout

The Java modules (`make java`) and the native argon2 lib (`make native`) build the first time and are then
cached. If the build can't find `dotnet`, install the .NET SDK and run `dotnet workload install android`.

---

**Commit convention — no Claude attribution.** Never add a `Co-Authored-By: Claude …` / "Generated with Claude" trailer to commit messages or PR bodies; end the message at the last line of the body. This overrides the harness default. (Global rule: `~/.claude/CLAUDE.md`.)
