#!/usr/bin/env bash
# Build LinkDesk.apk khong can Android SDK/Gradle.
# Can: openjdk, aapt, dalvik-exchange (dx), zipalign, apksigner (apt install aapt dalvik-exchange zipalign apksigner)
# va android.jar API 34 (dung robolectric android-all-14 tu Maven Central).
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
SRC="$HERE/../android/app/src/main"
OUT="${OUT:-$HERE/out}"
ANDROID_JAR="${ANDROID_JAR:-$HERE/android-all-14.jar}"
KEYSTORE="${KEYSTORE:-$HERE/linkdesk.keystore}"
KS_PASS="${KS_PASS:-linkdesk}"

if [ ! -f "$ANDROID_JAR" ]; then
  curl -fSL -o "$ANDROID_JAR" https://repo.maven.apache.org/maven2/org/robolectric/android-all/14-robolectric-10818077/android-all-14-robolectric-10818077.jar
fi
if [ ! -f "$KEYSTORE" ]; then
  keytool -genkeypair -keystore "$KEYSTORE" -storepass "$KS_PASS" -keypass "$KS_PASS" -alias linkdesk \
    -keyalg RSA -keysize 2048 -validity 10000 -dname "CN=LinkDesk"
fi

VERSION_CODE="${VERSION_CODE:-18}"
VERSION_NAME="${VERSION_NAME:-0.16.1}"
rm -rf "$OUT"; mkdir -p "$OUT/gen" "$OUT/classes"
# Manifest nguon theo kieu Gradle (khong co package/uses-sdk) -> them vao cho aapt
sed -e "s#<manifest xmlns:android=\"http://schemas.android.com/apk/res/android\">#<manifest xmlns:android=\"http://schemas.android.com/apk/res/android\" package=\"vn.linkdesk\" android:versionCode=\"$VERSION_CODE\" android:versionName=\"$VERSION_NAME\"><uses-sdk android:minSdkVersion=\"26\" android:targetSdkVersion=\"34\" />#" \
  "$SRC/AndroidManifest.xml" > "$OUT/AndroidManifest.xml"
grep -q 'package="vn.linkdesk"' "$OUT/AndroidManifest.xml"
aapt package -f -m -J "$OUT/gen" -M "$OUT/AndroidManifest.xml" -S "$SRC/res" -A "$SRC/assets" \
  -I "$ANDROID_JAR" --min-sdk-version 26 --target-sdk-version 34 -F "$OUT/base.apk"

find "$SRC/java" "$OUT/gen" -name '*.java' > "$OUT/sources.txt"
javac -nowarn -Xlint:-options -source 8 -target 8 -encoding UTF-8 -cp "$ANDROID_JAR" -d "$OUT/classes" @"$OUT/sources.txt"

dalvik-exchange --dex --min-sdk-version=26 --output="$OUT/classes.dex" "$OUT/classes"
cp "$OUT/base.apk" "$OUT/unaligned.apk"
(cd "$OUT" && zip -q -j unaligned.apk classes.dex)
zipalign -f -p 4 "$OUT/unaligned.apk" "$OUT/aligned.apk"
apksigner sign --ks "$KEYSTORE" --ks-pass "pass:$KS_PASS" --key-pass "pass:$KS_PASS" \
  --min-sdk-version 26 --out "$OUT/LinkDesk.apk" "$OUT/aligned.apk"
apksigner verify --print-certs "$OUT/LinkDesk.apk" | head -3
ls -la "$OUT/LinkDesk.apk"
