# MissileGirl/Gagarin integrated asset pipeline

MissileGirl/Gagarin owns change detection, cache generations, corruption recovery, and content-addressable storage. GraphicsSetter remains the only runtime patch owner for `ModContentLoader<Texture2D>.LoadTexture` and consumes Gagarin through the public reflection-safe `TextureCacheBridge`.

## Manifest V2

Every active mod is fingerprinted by package ID, normalized root path, About/LoadFolders metadata, `Assemblies/*.dll`, Defs/Patches XML, and texture sources. Relative path, size, and last-write time form the fast check; SHA-256 is recomputed only for new or changed files and reused for unchanged files.

The manifest also records RimWorld build, schema/algorithm versions, active load order, and creation time. Changes are classified into independent XML, code, texture, metadata, load-order, game-build, and algorithm domains.

## Invalidation behavior

- XML/Patch, DLL, metadata, load-order, or game-build changes rebuild the completed XML cache.
- Texture-only changes retain XML and invalidate only the affected mod's texture blob references.
- DDS/decoder/resolution/mip/compression changes create new blob profiles without touching XML.
- Filter mode, mip bias, and anisotropic level never delete disk blobs.
- `RocketRules` supports `XmlCacheUnsafe`, `TextureDirty`, and path/package-specific `TexturePolicy` declarations.

## Atomic generations and recovery

A replacement XML cache is copied into a unique temporary generation, validated with its Manifest V2, marked `READY` last, renamed into place, and activated by an atomic `active.txt` update. Multiple READY generations are retained.

A failure while reading an active generation marks that generation `BROKEN`, selects the newest previous READY generation, and falls back to RimWorld's normal XML path for the current startup. A failure while building a replacement preserves the existing READY generation instead of damaging it. Active data is never deleted before a replacement is complete.

## GPU-ready content-addressable blobs

Texture payloads are addressed by their immutable SHA-256 bytes and referenced by logical texture-cache keys. The index records source path, package ID, profile, exact size, creation time, and last access. Reads validate file existence and size. A corrupt or missing blob is an ordinary cache miss and is removed independently.

The store deduplicates identical payloads, supports per-source and per-package invalidation, deletes unreferenced bytes, and enforces a configurable LRU capacity. GraphicsSetter can resolve and memory-map these blobs through `Gagarin.TextureCacheBridge` without a compile-time dependency.

## Cache controls and telemetry

The Gagarin UI provides separate actions for current-mod revalidation, XML-only rebuild, texture-cache deletion, all-cache deletion, and old-generation/blob pruning. Retained generation count, age expiry, texture-cache capacity, and telemetry are configurable.

Startup telemetry records selected generation, invalidation plan, XML-cache use, pipeline initialization/startup time, texture hit/miss statistics, and bytes read/written. Alert settings and numeric throttling settings are persisted in both toggle directions.

## Failure behavior

Malformed asset hashes, manifests, Unified XML, settings, and individual texture blobs are isolated. They disable or rebuild only the affected cache domain and do not abort RimWorld startup.
