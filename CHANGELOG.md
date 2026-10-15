# Changelog

## 26.10.15 — Initial source preview

- Standalone native Windows shell and tray lifecycle; no Steam client required.
- Automatic existing-library selection, read-only ECHO Core catalog and bounded original-media HTTP Range sharing.
- ECHO Link LAN discovery, dynamic-port advertisement and service withdrawal when sharing stops.
- Sharing enabled by default for new configurations; persisted manual stop; optional login startup.
- Retained AGPL corresponding source, canonical SQL/compiler inputs, licensed search data and focused regression tests.

Fresh standalone Windows publish and 13 Bridge regression tests passed. Synthetic native-window startup smoke passed. Existing desktop Link playback/withdrawal and Android JVM client protocol checks are recorded in docs/VALIDATION.md.

The existing source release is supplemented with a complete **unsigned Windows x64 portable test ZIP**, explicitly authorized on 2026-10-15. The filename, bundled warning and Release instructions identify the unsigned/test status. It includes .NET 10 runtime files and third-party notices; extract the whole folder and run `ECHO Bridge.exe`. The immutable original application source/tag is retained, and only packaging/docs change in the follow-up commit.

Signed Windows distribution and Android physical-device NSD/audible playback acceptance remain pending. No scanning, audio renderer or Steam publishing is included.
