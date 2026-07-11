# Integrated Asset Pipeline Roadmap — MissileGirl/Gagarin

## Role

MissileGirl/Gagarin is the change-detection, manifest, generation, recovery, and storage layer in the GraphicsSetter + MissileGirl pipeline.

It owns:

- mod and file fingerprints
- dependency-aware cache invalidation
- XML and texture cache manifests
- atomic cache generations and rollback
- content-addressable immutable blob storage
- cache capacity limits and cleanup
- corruption detection and recovery
- cache hit/miss and startup telemetry

GraphicsSetter remains the sole owner of `ModContentLoader<Texture2D>.LoadTexture` and consumes texture blobs through a cache service instead of a competing Harmony patch.

## Phase 1 — Safety

Tracked by draft PR #2.

- Replace process-lifetime cache-use state with Harmony per-invocation state.
- Fall back to RimWorld XML processing when hash or Unified XML data is damaged.
- Clone inheritance-resolved XML before cache-only mutation.
- Write settings and XML through validated temporary files before activation.
- Establish load order after Graphics Settings+ when both mods are present.

## Phase 2 — Manifest V2 and mod fingerprints

Each active mod fingerprint should include:

- package ID
- declared mod version
- normalized root path
- About.xml fingerprint
- LoadFolders.xml fingerprint
- `Assemblies/*.dll` fingerprints
- aggregate Defs/Patches XML fingerprint
- aggregate texture metadata/fingerprint

Use two-stage detection:

1. Compare relative path, size, and last-write time.
2. Compute a strong content hash only for new or changed files.

The manifest also records:

- game build
- cache schema version
- XML pipeline schema version
- decoder schema version
- platform capability profile
- texture policy profile identifiers

This must detect DLL-only updates, custom patch-code updates, LoadFolders changes, texture-only changes, code-generated Def changes where declarations are available, same-package replacements, and mod-root moves.

## Phase 3 — Invalidation domains

Keep independent state for:

- completed unified XML
- per-file XML parsing and hashes
- texture source metadata
- GPU-ready texture blobs
- runtime-only rendering policy

Expected invalidation:

| Change | Completed XML | Per-file XML | Texture blobs |
| --- | --- | --- | --- |
| Mod add/remove/reorder | rebuild | reuse unchanged files | inspect changed mods |
| XML/Patch update | rebuild | changed files only | retain |
| DLL update | rebuild | normally retain | normally retain |
| Texture update | retain | retain | matching textures only |
| DDS/decoder/max-size/mip/compression profile | retain | retain | new texture profile |
| Bias/filter/aniso | retain | retain | retain |
| Game build | rebuild | revalidate | retain only when decoder/platform compatible |
| Corruption | roll back/rebuild affected domain | affected files | affected blobs |

## Phase 4 — Atomic generations

Target layout:

```text
Cache/
  active.txt
  generations/
    gen-00041/
      manifest.bin
      unified.xml
      READY
    gen-00042.tmp/
  blobs/
```

Generation workflow:

1. Create a new `.tmp` generation.
2. Write all files without modifying the active generation.
3. Validate manifest, XML, checksums, and required blob references.
4. Create `READY` last.
5. Atomically switch `active.txt`.
6. Retain at least one previous READY generation until the new generation has booted successfully.

Recovery order:

1. Mark the selected generation broken.
2. Try the previous READY generation.
3. If none is usable, set `Context.IsUsingCache = false`.
4. Continue through RimWorld's normal XML pipeline and build a replacement generation.

Never delete active data before a replacement is valid.

## Phase 5 — Content-addressable blobs

- Address immutable blob bytes by strong content hash plus schema and texture metadata.
- Deduplicate safe identical outputs across mods and generations.
- Store references and last-use metadata separately from immutable bytes.
- Validate checksum and exact expected length on read.
- Quarantine one corrupt blob without invalidating unrelated XML or textures.
- Enforce a configurable capacity with LRU cleanup of unreferenced and old blobs.

Expose a service to GraphicsSetter that resolves a texture key to validated blob metadata/data. Service absence or failure is a cache miss, never a startup failure.

## Phase 6 — Cache controls

Replace one ambiguous clear button with explicit actions:

- rebuild completed XML
- clear texture blobs
- revalidate current mod configuration
- clear all cache domains
- prune old generations and unreferenced blobs

Prefer marking data stale and building a replacement before deletion.

## Phase 7 — Compatibility declarations

Extend RocketRules-style declarations with:

- `TexturePolicy`
- `TextureDirty`
- `XmlCacheUnsafe`
- custom patch/code invalidation hints
- explicit code-generated Def dependencies

## Phase 8 — Telemetry

Persist per startup:

- selected generation and fallback reason
- invalidation reason by domain
- fast metadata checks and strong-hash count/time
- XML cache load/save time
- texture blob hit/miss and bytes mapped
- cache size and cleanup result
- cold/warm startup duration
- peak RAM where available

## Acceptance criteria

- DLL-only, LoadFolders-only, texture-only, path-only, and same-package replacements are detected.
- Interrupted writes leave the previous READY generation bootable.
- A corrupt Unified XML, manifest, or single texture blob cannot abort startup.
- Texture-only changes do not force XML reconstruction.
- Bias, filter mode, and anisotropic level changes do not purge texture blobs.
- Cold/warm runtime tests document load time, hash time, RAM peak, generation fallback, and cache hit rate.
