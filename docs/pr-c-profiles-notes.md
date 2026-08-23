# PR C: mod profiles, compatibility notes

Status: not a standalone code PR. The profile feature is already covered by
PR #139 ("Add mod profile system for saving/switching mod configurations").
This document records how the redesign work folds into #139 so we do not ship
a second, competing profile/preset backend.

## Decision

Do not merge the local `PresetStore` / `ModPreset` code. PR #139 is the
canonical profile system and is strictly more capable. The redesigned UI will
call #139's API instead of its own preset store.

## What PR #139 provides (verified on branch feat/profile)

Files: `Metadata/ProfileData.cs`, `Metadata/ProfileStorage.cs`,
`Metadata/ProfileManager.cs`, `UI/ProfileSelectorPanel.cs`,
`UI/ProfileStartupDialog.cs`, plus additions to `Metadata/ModList.cs` and
`LaunchPadConfig.cs`.

Storage (XML, no JSON):
- Profiles live in `{SavePath}/profiles/*.xml`.
- Active profile is tracked in `{SavePath}/active.xml`.
- `ProfileData` (XmlRoot "ModProfile"): `Name`, `Description`, and a list of
  `Mod` entries, each with `DirectoryPath`, `WorkshopHandle`, `Enabled`.
- Mod order in the `Mods` list carries the load order.
- Serialized through the game's `XmlSerialization` helper, same as the rest of
  the loader config.

## How the local presets compare

The local `PresetStore` (`Metadata/ModPreset.cs`) saves a `ModConfig`
(enabled state plus load order) to `{SavePath}/presets/*.xml`. It is a subset
of #139:
- Both are named XML snapshots under `{SavePath}`.
- #139 adds a description field, explicit per-mod identity
  (DirectoryPath + WorkshopHandle), and active-profile persistence.
- #139 already separates "profiles" (local quick switching) from "modpacks"
  (zip sharing for servers), which matches our design split. Keep that split.

There is no behavior in the local presets that #139 does not already cover.

## Plan for the future PR (after #139 merges or updates)

1. Delete `Metadata/ModPreset.cs` (`PresetStore`).
2. Remove `LaunchPadConfig.ListPresets` / `SavePreset` / `LoadPreset` /
   `DeletePreset` and the `presets/` directory usage.
3. In the redesigned toolbar, replace `DrawPresetBar` so its combo and
   Save / Load / Delete buttons drive #139's `ProfileManager` (list, save,
   load, apply, delete, active profile) instead of the local preset store.
   Reuse `ProfileSelectorPanel` where it fits rather than drawing a parallel
   widget.
4. Keep the modpack import/export (zip + `modconfig.xml`) separate from
   profiles, exactly as both #139 and this design already do.
5. Do not introduce JSON anywhere in this path. #139 is XML end to end.

## Until #139 is merged

The redesigned UI (PR E) must not introduce its own profile/preset backend.
If a profile control is shown before #139 lands, gate it behind #139 so the
two do not ship duplicate storage.
