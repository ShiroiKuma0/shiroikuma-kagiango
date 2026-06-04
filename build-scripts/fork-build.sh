#!/usr/bin/env bash
#
# fork-build.sh — build the signed NoNet APK for the 白い熊 鍵暗号 fork, copy it to ~/tmp,
# and bump the fork build number. This is the keepass2android (Makefile/Xamarin) analog of the
# Fossify siblings' `./gradlew buildFoss` task.
#
# Single source of truth for the fork version is AndroidManifest_nonet.xml:
#   android:versionName="<UPSTREAM>+<N>"   e.g. 1.15-r2+1
#   android:versionCode="<UPSTREAM_CODE * 10000 + N>"   e.g. 250*10000+1 = 2500001
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
base_name="${vname%+*}"     # 1.15-r2+1 -> 1.15-r2
build_n="${vname##*+}"      # 1.15-r2+1 -> 1
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
make apk Flavor="${FLAVOR}" Configuration=Release "${MAKE_SIGN_ARGS[@]}"

# --- locate the signed APK ------------------------------------------------------------------------
apk="$(ls -t "${PUBLISH_DIR}"/*-Signed.apk 2>/dev/null | head -1 || true)"
[[ -z "$apk" ]] && apk="$(ls -t "${PUBLISH_DIR}"/*.apk 2>/dev/null | head -1 || true)"
if [[ -z "$apk" ]]; then
  echo "ERROR: no APK found in ${PUBLISH_DIR}. Inspect the build output above." >&2
  exit 1
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
next_name="${base_name}+${next_n}"
sed -i -E "s/(android:versionCode=\")[0-9]+(\")/\1${next_code}\2/" "$MANIFEST"
sed -i -E "s/(android:versionName=\")[^\"]+(\")/\1${next_name}\2/" "$MANIFEST"
echo ">>> next build will be ${next_name} (versionCode ${next_code})"
