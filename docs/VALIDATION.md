# Validation scope — 2026-10-15

## Existing product acceptance

Before extraction, the real Bridge window automatically selected a loaded ECHO library and enabled sharing. Desktop ECHO Link used actual mDNS discovery, browsed the library and played a synthetic 20-second WAV with native player state/position verification, pause and seek/resume. Clicking the actual stop-sharing button withdrew the device from discovery and the visible Link row. Other ECHO devices were preserved.

The actual Android client running on the JVM connected to the native Bridge, browsed its catalog, prepared the selected track in the phone playback queue, received a media URL and read its WAV Range response (206). Stopping Bridge sharing revoked catalog/stream access. Discovery list removal policy was also covered.

No authorized Android device or emulator was attached. Actual Android NsdManager discovery/service-loss callbacks, Media3 audible playback and background behavior are **not physically validated**. Desktop playback used synthetic silence and does not establish listening quality or all-format compatibility. Two-device Wi-Fi/firewall acceptance remains separate.

## Standalone extraction

Extraction retains product code and existing focused tests. The separate export changes only build resource wiring, standalone version/build metadata and the generic application icon. Fresh standalone publish, focused regression and synthetic native-window smoke are recorded locally before publishing the source release. Public release notes report their actual result.

Fresh self-contained Windows publish passed in 12.3 seconds. All 13 focused Bridge tests passed with zero failures/skips in an 11.4-second command. Synthetic native-window startup passed in 3.4 seconds. Signing preflight failed because no Windows publisher/certificate was configured, so no binary archive was published. These checks took 27.2 seconds cumulatively, within the 120-second budget.

No user media, databases, private settings, credentials, original repository history or development binary directories are included in the source release. These initial checks made no signed-binary or Steam-release claim.

## Unsigned portable test distribution

The project owner subsequently explicitly authorized an unsigned Windows x64 test ZIP in the existing Release. Signed packaging remains the default; the explicit unsigned test mode records skipped signature verification and requires a warning file, notices, clean corresponding source and final file hashes.

The portable archive includes complete .NET 10.0.12 runtime files and exact license/notice texts from resolved packages and pinned upstream sources. The immutable application source remains v26.10.15 / 5ac2f9e1381a7f5b454899a4c1e1d64037f3d239. Only packaging and documentation change; the archive manifest records the packaging revision separately.

Final archive validation uses the extracted EXE, synthetic library/WAV and isolated settings, covers actual window startup, automatic sharing, catalog/Range access and the real stop-sharing button, and checks every extracted file against the manifest. Public Release notes report the actual outcome. This does not establish Smart App Control compatibility, physical Android playback, all audio formats or two-device firewall acceptance.

## 26.10.15.1 revision

This unsigned preview includes the requested-size artwork and lyric fixes and SQLite folder sorting before pagination. Android capability-driven actions and readable folder labels live in the Android client and require its updated build; they are not bundled into this Windows archive.

Fresh standalone regression, publish and extracted-archive acceptance results are recorded in the 26.10.15.1 Release notes. The exact application source commit and every packaged file hash are recorded in release-manifest.json. The prior 26.10.15 release and its source tag remain unchanged. Mobile device/audio, two-device Wi-Fi and Smart App Control compatibility remain outside this local acceptance.
