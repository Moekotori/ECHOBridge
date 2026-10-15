# Contributing

Keep Bridge a bounded, read-only companion. ECHO remains the library schema/scan/write owner; the receiving player's native audio backend remains the playback owner. Do not add a second scanner, audio renderer, user-file tag writes, unbounded library/media caches or Steam SDK imports.

Keep independent responsibilities in named modules, preserve the read-only method allowlist and direct-LAN gate, and test concrete behavior rather than UI wording or copied algorithms. See docs/BUILDING.md for focused tests. Protect original user files and preserve concurrent work.

Contributions are provided under AGPL-3.0-only unless a separately licensed component is explicitly identified. New third-party materials require a source/version/license record and applicable notices. Credentials, local machine paths, personal libraries, media and signing certificates must never be committed.
