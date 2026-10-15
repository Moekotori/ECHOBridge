# Validation scope — 2026-10-15

## Existing product acceptance

Before extraction, the real Bridge window automatically selected a loaded ECHO library and enabled sharing. Desktop ECHO Link used actual mDNS discovery, browsed the library and played a synthetic 20-second WAV with native player state/position verification, pause and seek/resume. Clicking the actual stop-sharing button withdrew the device from discovery and the visible Link row. Other ECHO devices were preserved.

The actual Android client running on the JVM connected to the native Bridge, browsed its catalog, prepared the selected track in the phone playback queue, received a media URL and read its WAV Range response (206). Stopping Bridge sharing revoked catalog/stream access. Discovery list removal policy was also covered.

No authorized Android device or emulator was attached. Actual Android NsdManager discovery/service-loss callbacks, Media3 audible playback and background behavior are **not physically validated**. Desktop playback used synthetic silence and does not establish listening quality or all-format compatibility. Two-device Wi-Fi/firewall acceptance remains separate.

## Standalone extraction

Extraction retains product code and existing focused tests. The separate export changes only build resource wiring, standalone version/build metadata and the generic application icon. Fresh standalone publish, focused regression and synthetic native-window smoke are recorded locally before publishing the source release. Public release notes report their actual result.

Fresh self-contained Windows publish passed in 12.3 seconds. All 13 focused Bridge tests passed with zero failures/skips in an 11.4-second command. Synthetic native-window startup passed in 3.4 seconds. Signing preflight failed because no Windows publisher/certificate was configured, so no binary archive was published. These checks took 27.2 seconds cumulatively, within the 120-second budget.

No user media, databases, private settings, credentials, original repository history or development binary directories are included in the source release. Windows binary publication is blocked until certificate configuration, final PE signatures and exact runtime notices pass. This source release makes no signed-binary or Steam-release claim.
