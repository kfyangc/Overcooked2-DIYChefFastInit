# Cache format probes

Run the mesh probe from the repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/research/cache-formats/Run-MeshFormatProbe.ps1
```

Optional parameters: `-GameDir <game-directory>` and `-ResourcesDir <source-resource-directory>`.

This builds the linked production importer/cache sources for .NET 3.5, then loads the probe in the game's x86 Mono runtime through the existing `chef-cache-probe/MonoCacheHost.cs`. It does not launch the game, create Unity objects, change source resources, or read/write the game's existing cache. Build outputs, reports, and a fresh isolated mesh cache for each run are stored under repository `artifacts/cache-formats/`.

The probe counts strict UTF-8 and NUL-containing OBJ files, records source/cache sizes, and compares every position, normal, UV component and triangle index after a production cache round trip. It measures the whole corpus, its largest OBJ, and `01-ArcticFox` if present. Each comparison has one warm-up and seven timed samples, includes cache validation, and reports the median, range, stage timings and Gen0 collections. These are worker-only measurements with warm operating-system file cache; they do not measure total game/UI latency.
