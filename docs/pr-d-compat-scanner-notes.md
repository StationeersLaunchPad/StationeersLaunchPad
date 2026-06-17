# PR D: compatibility scanner (reusable, UI-decoupled)

This PR adds only the compatibility scanner, with no UI and no dependency on
the redesigned window. It is the piece the maintainer asked to be reusable in
other areas such as automatic mod updates.

## What is in this PR

- `Metadata/ModCompatScanner.cs`: statically reads enabled mod assemblies with
  Mono.Cecil (without executing them), walks IL, and flags references to
  game members (Assembly-CSharp / Assembly-CSharp-firstpass) that no longer
  exist. Those are what throw MissingMethodException / MissingFieldException at
  runtime when a mod was built against an older game version. Conservative on
  purpose (skips generics and constructors, matches methods by name plus
  parameter count) to limit false positives.
- `SLP.common.props`: adds the `Mono.Cecil` GameReference (the DLL already
  ships in BepInEx/core).

## Reusable API (no UI)

- `ModCompatScanner.ScanAll(IReadOnlyList<ModInfo> mods)`: run off the main
  thread; does file IO and reflection. Results are cached per assembly path
  and modified time.
- `ModCompatScanner.GetMissing(ModInfo mod)`: the missing game members found
  for a mod, or empty.
- `ModCompatScanner.HasScanned`: whether a scan has completed.

Any caller can use this. For automatic mod updates, run `ScanAll` after an
update is downloaded and before it is enabled, then surface `GetMissing` to
warn that the new version is incompatible with the installed game build.

## Deliberately not included

- No ImGui panel and no wiring into the redesigned shell.
- The broader pre-load health check (missing/disabled dependencies,
  duplicates, circular order, Workshop state, remembered past failures) is a
  separate aggregator. It consumes this scanner but also needs the dependency
  analysis helpers and the mod date metadata, so it ships with the
  health-check / UI work rather than here. This keeps PR D single purpose and
  free of UI coupling.
