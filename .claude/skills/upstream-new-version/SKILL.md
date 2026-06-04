---
name: upstream-new-version
description: Rebase our fork onto a new upstream release of PhilippC/keepass2android. Use when the user says a new upstream version is out, asks to update/sync to upstream, bump to the new keepass2android release, or rebase custom onto the latest upstream.
---

# Rebase the fork onto a new upstream release

This codifies the "new upstream version" half of the fork workflow. The goal: move `main` to the new
upstream release, replay our `custom` customizations on top of it, and produce a fresh `+1` build.

This is a fork of **keepass2android** (Xamarin/.NET, built via the root `Makefile`). We ship the **NoNet**
(offline) flavor only. There is **no** patched-Commons step (that is a Fossify-sibling thing — ignore it).

> **Never `git push` or `git commit` unprompted, and never `adb install`.** Same hard rules as everyday
> development (see CLAUDE.md). After the rebase + build you stop and let the user test; you only
> `git push` when they explicitly say **"Push"**.

## Background — how versioning works here

The fork version is stored **directly in `AndroidManifest_nonet.xml`** (there is no `gradle.properties`):

- `versionName` = `"<UPSTREAM_VERSION_NAME>+<N>"` (e.g. `1.15-r2+1`).
- `versionCode` = `<UPSTREAM_VERSION_CODE> * 10000 + N` (e.g. `250*10000+1 = 2500001`).
- `N` is **our** fork build increment. It **resets to `1`** on each new upstream version and bumps by `1`
  on every build (the `build-apk` / `fork-build.sh` step does the bump).

So when upstream's `versionCode` climbs (e.g. 250 → 260), our codes for the new line (`2600001`,
`2600002`, …) all exceed the previous line's (`2500001`, …), keeping upgrades monotonic.

## Steps

1. **Fetch upstream:**
   - `git fetch upstream --tags`
   - Identify the new release. Upstream's default branch is `main`; releases are also tagged
     (`git tag --sort=-creatordate | head`). Confirm the new upstream `versionCode` / `versionName` from
     the NoNet manifest at that point:
     `git show upstream/main:src/keepass2android-app/Manifests/AndroidManifest_nonet.xml | grep -E 'versionCode|versionName'`.

2. **Advance `main` to the new upstream release** (it mirrors upstream, no fork work lives there):
   - `git checkout main`
   - `git merge --ff-only upstream/main` (or `git reset --hard <tag>` if tracking an exact tag).

3. **Rebase `custom` onto the new `main`:**
   - `git checkout custom`
   - `git rebase main`
   - Resolve conflicts so **all** our customizations survive (see the table below). The conflict-prone
     file is **`AndroidManifest_nonet.xml`** (version + package + label + authorities + permissions); also
     watch `values*/strings.xml`, `app/App.cs`, `intents/Intents.cs`, `searchable_offline.xml`, `Makefile`.

4. **Set the fork version in `AndroidManifest_nonet.xml`:**
   - Set `versionName` to `<NEW_UPSTREAM_VERSION_NAME>+1` and `versionCode` to
     `<NEW_UPSTREAM_VERSION_CODE> * 10000 + 1`. (During the rebase the version lines conflict because
     upstream changed them — resolve by taking upstream's **new** base, then applying our `+1`.)

5. **Verify our customizations are intact** (after resolving the rebase):

   | What | Expected value | Where |
   | --- | --- | --- |
   | Installed app ID | `shiroikuma.kagiango` | `AndroidManifest_nonet.xml` → `package=` |
   | Internal package part | `kagiango` | `app/App.cs` → `AppNames.PackagePart` (the `#if NoNet` block) |
   | App launcher label | `白い熊 鍵暗号` | `app_name_nonet` (+ `app_name`) in `values/strings.xml` **and** `values-ja/strings.xml`; `<application android:label="@string/app_name_nonet">` |
   | File-chooser authorities | `shiroikuma.kagiango.android-filechooser.{localfile,history}` | `AndroidManifest_nonet.xml` (must match `FileSelectHelper`'s `PackageName`-derived value) |
   | Custom permissions | `keepass2android.kagiango.permission.*` | `AndroidManifest_nonet.xml` (lines 63/64/275/276) — must match the C# `"keepass2android."+PackagePart+".permission."` form |
   | Search authority | `kp2a.kagiango.SearchProvider` | `searchable_offline.xml` (must match `SearchProvider.Authority`) |
   | Keyboard bridge actions | `shiroikuma.kagiango.{lock_database,keyboard_cleared}` | `intents/Intents.cs` → `LockDatabase`, `KeyboardCleared` use the `"shiroikuma."` prefix |
   | Signing alias param | `KeyAlias ?= kp2a` + `-p:AndroidSigningKeyAlias="$(KeyAlias)"` | `Makefile` |
   | Fork version | `versionName <upstream>+1`, `versionCode <code>*10000+1` | `AndroidManifest_nonet.xml` |

   **Critical coupling to re-check:** the bundled KP2A keyboard (`src/java/KP2ASoftkeyboard_AS/.../KP2AKeyboard.java`)
   derives its broadcast actions from `getPackageName()` (= applicationId `shiroikuma.kagiango`). Our C#
   `Intents.LockDatabase` / `Intents.KeyboardCleared` therefore use the `"shiroikuma."` prefix (not
   `"keepass2android."`) so the keyboard's lock-key / clear-on-lock still match. If upstream changes how
   either side builds these actions, re-port this fix rather than blindly keeping the old diff.

6. **Build the new `+1`** via the **build-apk** skill (`./build-scripts/fork-build.sh`), then **ask** before
   any `adb push`. This is the first build of the new upstream line (`<newVersion>+1`).

7. **Stop.** Let the user test. Commit/push only on their explicit **"Push"** (force-push may be needed for
   `custom` since rebasing rewrites history: `git push --force-with-lease origin custom`; `main` is a
   fast-forward: `git push origin main`).

## Notes

- Keep our changes a **small, legible layer** on top of upstream — prefer rebasing (linear history) over
  merging, so the customization set stays easy to audit and replay.
- We only build the **NoNet** flavor, so only `AndroidManifest_nonet.xml` (not `_net`/`_debug`) and the
  `#if NoNet` block of `App.cs` carry our identity edits.
- If upstream restructures a file we customize, port our change to the new structure rather than forcing
  the old diff.
- `upstream` is configured push-disabled (read-only); only `origin` is pushable.
