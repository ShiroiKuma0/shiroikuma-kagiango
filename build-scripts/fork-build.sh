#!/usr/bin/env bash
#
# fork-build.sh — build the signed NoNet APK for the 白い熊 鍵暗号 fork, copy it to ~/tmp,
# and bump the fork build number. This is the keepass2android (Makefile/Xamarin) analog of the
# Fossify siblings' `./gradlew buildFoss` task.
#
# Single source of truth for the fork version is AndroidManifest_nonet.xml:
#   android:versionName="<UPSTREAM>+<NNN>"   e.g. 1.15-r3+007   (N zero-padded to three digits)
#   android:versionCode="<UPSTREAM_CODE * 10000 + N>"   e.g. 251*10000+7 = 2510007  (N unpadded)
# The script builds the CURRENT value, then bumps N by 1 for the next build (like buildFoss).
#
# Prerequisites (the Makefile also enforces the env vars):
#   - dotnet (with the android workload), nuget, a JDK (17+), and the Android SDK + NDK.
#   - ANDROID_SDK_ROOT, ANDROID_HOME, ANDROID_NDK_ROOT exported.
#   - keystore.properties present at the repo root (gitignored) for release signing.
#
# Usage:  ./build-scripts/fork-build.sh
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$REPO_ROOT"

# Load machine-local build env (JDK/SDK/NDK/dotnet/nuget paths) if present.
if [[ -f build-env.sh ]]; then
  # shellcheck disable=SC1091
  source build-env.sh
fi

MANIFEST="src/keepass2android-app/Manifests/AndroidManifest_nonet.xml"
FLAVOR="NoNet"
PUBLISH_DIR="src/keepass2android-app/bin/Release/net9.0-android/publish"
APK_PREFIX="shiroikuma-kagiango"
TMP_DIR="${HOME}/tmp"

# --- read current fork version straight from the manifest -----------------------------------------
vcode="$(grep -oE 'android:versionCode="[0-9]+"' "$MANIFEST" | head -1 | grep -oE '[0-9]+')"
vname="$(grep -oE 'android:versionName="[^"]+"' "$MANIFEST" | head -1 | sed -E 's/.*="([^"]+)"/\1/')"
if [[ -z "$vcode" || -z "$vname" ]]; then
  echo "ERROR: could not read versionCode/versionName from $MANIFEST" >&2
  exit 1
fi
base_name="${vname%+*}"     # 1.15-r3+007 -> 1.15-r3
build_n="${vname##*+}"      # 1.15-r3+007 -> 007
# 10# forces base 10. Bash reads a leading-zero literal as OCTAL, so without this the padded
# counter would abort the build with "value too great for base" on +008 and +009 — a failure that
# waits two builds after the padding lands and then looks like it came from nowhere.
build_n=$(( 10#$build_n ))
base_code=$(( vcode / 10000 ))

echo ">>> building ${APK_PREFIX}_${vname}  (versionCode ${vcode}, flavor ${FLAVOR})"

# --- signing (release) ----------------------------------------------------------------------------
MAKE_SIGN_ARGS=()
if [[ -f keystore.properties ]]; then
  # shellcheck disable=SC1091
  source keystore.properties
  export MyAndroidSigningStorePass="${storePassword}"
  export MyAndroidSigningKeyPass="${keyPassword}"
  MAKE_SIGN_ARGS=(KeyStore="${storeFile}" KeyAlias="${keyAlias}")
else
  echo "WARNING: keystore.properties not found — APK will be debug-signed and may not install." >&2
fi

# --- build (native + java + nuget + manifestlink + signed publish) --------------------------------
# MSBuild's incremental packaging has produced stale signed APKs (old versionCode/code) even though
# `dotnet publish` ran — purge the packaging outputs so the APK is always regenerated.
rm -rf "${PUBLISH_DIR}"
rm -f src/keepass2android-app/bin/Release/net9.0-android/*.apk
rm -f src/keepass2android-app/obj/Release/net9.0-android/android/bin/*.apk

make apk Flavor="${FLAVOR}" Configuration=Release "${MAKE_SIGN_ARGS[@]}"

# --- locate the signed APK ------------------------------------------------------------------------
apk="$(ls -t "${PUBLISH_DIR}"/*-Signed.apk 2>/dev/null | head -1 || true)"
[[ -z "$apk" ]] && apk="$(ls -t "${PUBLISH_DIR}"/*.apk 2>/dev/null | head -1 || true)"
if [[ -z "$apk" ]]; then
  echo "ERROR: no APK found in ${PUBLISH_DIR}. Inspect the build output above." >&2
  exit 1
fi

# --- verify the APK really carries the version we set out to build --------------------------------
aapt_bin="$(ls "${ANDROID_SDK_ROOT}"/build-tools/*/aapt 2>/dev/null | tail -1 || true)"
if [[ -n "$aapt_bin" ]]; then
  built_vcode="$("$aapt_bin" dump badging "$apk" 2>/dev/null | grep -oE "versionCode='[0-9]+'" | grep -oE '[0-9]+' | head -1)"
  if [[ "$built_vcode" != "$vcode" ]]; then
    echo "ERROR: stale build — APK has versionCode ${built_vcode}, expected ${vcode}. Not copying." >&2
    exit 1
  fi
fi

# --- copy to ~/tmp with the fork filename ---------------------------------------------------------
mkdir -p "${TMP_DIR}"
out="${TMP_DIR}/${APK_PREFIX}_${vname}.apk"
cp "$apk" "$out"
echo ">>> ${out}"
echo ">>> versionCode ${vcode}"

# --- bump the build number for next time (only reached on a successful build) ---------------------
next_n=$(( build_n + 1 ))
next_code=$(( base_code * 10000 + next_n ))
# Zero-padded to three digits (global after-build rule) so ~/tmp listings, the phone's
# /sdcard/tmp and the release list all sort in build order. versionCode keeps the plain number.
next_name="${base_name}+$(printf '%03d' "${next_n}")"
sed -i -E "s/(android:versionCode=\")[0-9]+(\")/\1${next_code}\2/" "$MANIFEST"
sed -i -E "s/(android:versionName=\")[^\"]+(\")/\1${next_name}\2/" "$MANIFEST"
echo ">>> next build will be ${next_name} (versionCode ${next_code})"
