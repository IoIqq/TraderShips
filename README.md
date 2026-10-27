# Trader ships (continued)

[![Steam Workshop](https://img.shields.io/badge/Steam-Workshop-1b2838?logo=steam)](https://steamcommunity.com/sharedfiles/filedetails/?id=3828162134)
[![GitHub](https://img.shields.io/badge/GitHub-Repository-181717?logo=github)](https://github.com/IoIqq/TraderShips)

Adds trader spaceships to the game. Ships will visit your colony, preferring your landing zone, and trade with you just like visiting caravans — no trade beacon and no orbital trader screen needed.

Trader ships are meant to replace orbital traders, but you can set both to be active in mod settings.

This is the **RimWorld 1.7 (Waylith engine)** continuation of [AUTOMATIC1111's original Trader ships](https://github.com/AUTOMATIC1111/TraderShips). All original design, art and code are his work; this fork exists only to keep the mod working after the engine rewrite.

## Installation

1. Subscribe on the [Steam Workshop (ID: 3828162134)](https://steamcommunity.com/sharedfiles/filedetails/?id=3828162134), or clone this repo.
2. Make sure **Harmony** is installed and loaded before this mod.
3. Enable *Trader ships (continued)* and restart.

If installing manually, copy the contents of `About`, `1.7`, `Languages`, `Sounds` and `Textures`, plus the generated `LoadFolders.xml`, into a folder under `RimWorld/Mods/`.

## What changed for 1.7

RimWorld 1.7 replaced Unity and Mono with Ludeon's in-house **Waylith engine**, which runs on **.NET 10 / CoreCLR** with a real Vulkan renderer. That broke several things this mod relied on:

| Problem | Fix |
| --- | --- |
| `Designator.DraggableDimensions` was removed | Use `DrawStyleCategory => DrawStyleCategoryDefOf.Areas`, matching vanilla area designators |
| `Skyfaller` exposed the animation timer only through a private field we read via Harmony `Traverse` | Use the new `protected virtual GetDrawPositionAndRotation(ref Vector3, out float)` hook instead |
| `Skyfaller` now owns a cached `ShadowMaterial` | Dropped the mod's duplicate material cache |
| Harmony was a hard dependency | Now optional — resolved via `GenTypes.GetTypeInAnyAssembly`, patched reflectively, degrades to vanilla orbital traders when absent |
| `[DebugAction]` was not registering in heavily modded setups | Debug menu now self-registers into `Dialog_Debug.rootNode`; the old `DebugMenuOptionMode.Tool` flow silently did nothing |

The assembly targets `net10.0`, which is what the 1.7 ecosystem uses (130 of 186 sampled 1.7 mods).

See [PORTING-1.7.md](PORTING-1.7.md) for the full write-up, including two bugs that only showed up at runtime.

## Building

Requires the **.NET 10 SDK** (the game itself runs on .NET 10; a .NET 9 SDK will fail with `CS1705`).

```bash
cd Source
dotnet build TraderShips17.csproj -c Release
```

The output lands in `1.7/Assemblies/TraderShips.dll`.

Paths to `Assembly-CSharp.dll`, `UnityStubs.dll` and `Waylith.dll` are set through `RimWorldDir` in `TraderShips17.csproj`; adjust it if your install is elsewhere.

To deploy to your local `Mods` folder:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/deploy-1.7.ps1
```

The script rebuilds the destination folder from scratch each run — `Copy-Item -Recurse` into an existing directory nests the source inside it, which produces a broken `1.7/1.7/` layout.

## Settings

All options live under **Options → Mod settings → Trader ships**:

- **Disable orbital traders** — ships replace orbital traders (on by default).
- **Require comms console** — ships won't come without one (on by default, so this does not change when trade becomes available).
- **Colored ships**, plus saturation/value ranges for randomly generated colours.
- **Enable quests** — trade-ship related quests.

## Languages

English, Spanish (original), Simplified Chinese and Traditional Chinese.

## Credits

- **AUTOMATIC1111** — original mod, ship art, design, most of the code.
- **IoIqq** — 1.7 / Waylith engine port, Chinese translations.

Licensed under the MIT License, same as the original. See [LICENSE](LICENSE).
