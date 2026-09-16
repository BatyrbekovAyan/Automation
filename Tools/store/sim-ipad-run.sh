#!/usr/bin/env bash
# Build the Unity iOS-Simulator export for the iPad simulator, install it, start a StoreKit /
# RevenueCat log stream, and launch the app. App Review forensics 2026-09-16 (rejection №2,
# «in-app purchases were not responsive» on iPad Air 11-inch (M3), iPadOS 26.6).
#
#   Tools/store/sim-ipad-run.sh            # build + install + launch + log stream
#   Tools/store/sim-ipad-run.sh --launch   # skip the xcodebuild step (app already built)
#
# Prereqs: DemoSimulatorBuild export at $EXPORT (headless: Unity -batchmode -executeMethod
# DemoSimulatorBuild.Build -demoBuildPath "$EXPORT"); a booted iPad simulator (UDID below).
set -euo pipefail
EXPORT="${EXPORT:-/Users/ayan/Projects/Builds/Build - iOS Sim}"
UDID="${UDID:-66446C26-94E6-4989-B8A1-7A4CBE1DDB4E}"   # iPad Air 11-inch (M4), iOS 26.5
BUNDLE="com.synergysoft.choosereply"
# DerivedData must live OUTSIDE the Unity export: Unity's post-build ChangeFilesReadOnly walks the
# whole export tree, and an Xcode DerivedData inside it (hundreds of thousands of object files)
# turned a 10-minute build into a >1 h crawl on 2026-09-16.
DERIVED="${DERIVED:-/Users/ayan/Projects/Builds/DerivedData-iOS-Sim}"
OUT="/Users/ayan/Projects/Automation/Tools/test-output"
mkdir -p "$OUT"

if [[ "${1:-}" != "--launch" ]]; then
  # unity.webp ships simulator slices for arm64 only; this Intel Mac links x86_64. The stash of
  # x86_64 libwebp archives + the splice script survive from the demo-video toolchain (memory:
  # demo video mode A). Idempotent — «already fat» on a second run.
  SPLICE=/Users/ayan/Projects/Builds/demo-tools/webp-splice.sh
  [[ -x "$SPLICE" ]] && bash "$SPLICE" "$EXPORT" || echo "no webp-splice.sh — x86_64 link will fail on Intel"
  # The export links Pods_UnityFramework (EDM4U runs `pod install` with an empty Podfile even
  # though RevenueCat comes in via SPM) — so it MUST be built through the workspace; the bare
  # .xcodeproj fails with «framework 'Pods_UnityFramework' not found» at UnityFramework link.
  xcodebuild -workspace "$EXPORT/Unity-iPhone.xcworkspace" -scheme Unity-iPhone -configuration Release \
    -sdk iphonesimulator -destination "id=$UDID" -derivedDataPath "$DERIVED" \
    CODE_SIGNING_ALLOWED=NO build 2>&1 | tee "$OUT/sim-xcodebuild.log" | grep -E "error:|warning: .*StoreKit|BUILD (SUCCEEDED|FAILED)" || true
fi

APP=$(find "$DERIVED/Build/Products" -maxdepth 2 -name "*.app" -path "*iphonesimulator*" | head -1)
[[ -n "$APP" ]] || { echo "no .app under $DERIVED"; exit 1; }
echo "app: $APP"

xcrun simctl bootstatus "$UDID" -b >/dev/null
xcrun simctl uninstall "$UDID" "$BUNDLE" 2>/dev/null || true
xcrun simctl install "$UDID" "$APP"

# StoreKit + RevenueCat + our own Unity logs, to a file the session can grep.
pkill -f "simctl spawn $UDID log stream" 2>/dev/null || true
nohup xcrun simctl spawn "$UDID" log stream --style compact \
  --predicate 'subsystem CONTAINS "storekit" OR subsystem CONTAINS "StoreKit" OR subsystem CONTAINS "revenuecat" OR process == "ChooseReply" OR process == "Choose Reply" OR eventMessage CONTAINS "RevenueCat" OR eventMessage CONTAINS "confirmation scene"' \
  > "$OUT/sim-log-stream.log" 2>&1 &
echo "log stream pid $! -> $OUT/sim-log-stream.log"

xcrun simctl launch "$UDID" "$BUNDLE"
echo "launched $BUNDLE on $UDID"
