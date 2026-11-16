# Kigali Rush

A realistic-3D lane runner set in four districts of Kigali, Rwanda, built in Unity 6 (URP). It is portrait and Android-first. The car drives forward on its own on a three-lane road. You swipe between lanes, dodge traffic, collect time tokens to keep the countdown alive, and try to reach the finish line. A history and geography quiz unlocks cosmetic content.

The repository also holds PC-only showcase scenes built from real map data:

- **City Life:** live a day in Kacyiru, Kimihurura and Remera.
- **Story Mode:** the narrated film *A Day in Kigali*.

The full brief is in [`CLAUDE.md`](CLAUDE.md) (design rules, MVP scope, levels, scoring and milestones). [`KigaliRush_Implementation_Brief_v2.md`](KigaliRush_Implementation_Brief_v2.md) is the same brief.

> Private repository. It contains Unity Asset Store packages that must not be redistributed publicly. See [Licences and credits](#licences-and-credits).

---

## Requirements

- **Unity 6000.3.27f1** (Unity 6.3 LTS) with the Android Build Support module
- **Git LFS**: run `git lfs install` before cloning. Textures, models, audio, heightmaps and the generated worlds and scenes are stored in LFS (about 4 GB).
- **Python 3.11+** for the GIS and voice-over tools. Install the packages with `pip install numpy tifffile pillow`; a virtual environment in `Tools/.venv` is git-ignored.

```bash
git lfs install
git clone https://github.com/Ntare-GAMA/KIGALi-Rush.git
```

Open this folder (`KR`) in Unity Hub: Add, Add project from disk, pick the folder that holds `Assets`. The first import takes a while.

## Repository layout

| Path | What it is |
|---|---|
| `/` (this folder) | The Unity project (Assets, Packages, ProjectSettings) |
| `Assets/Scripts/Core` | Bootstrap, game state machine, object pooler, save, floating origin |
| `Assets/Scripts/Gameplay` | Lane runner (`Run/`), vehicle, input, camera, City Life, world systems (traffic, day/night, weather, events) |
| `Assets/Scripts/Editor` | Scene builders: GIS import, PC city, roads, greenery, people and animation, Story Mode, surrounding land |
| `Assets/Tests` | Edit-mode tests, including the level bot test |
| `Assets/World` | Generated worlds: districts, Kigali street, PC city |
| `Assets/Scenes` | `Run` (lane runner), `PC Only - …` showcase scenes |
| `SourceArt/GIS` | Map data: Copernicus DEM tiles and OSM extracts are git-ignored; `districts/` holds the processed heightmaps, roads, buildings and places |
| `Tools/gis` | Python: OSM extraction, district heightmaps, surrounding-land heightmaps |
| `Tools/voiceover` | Scratch narration generator, for timing only |
| `Documentation` | Asset register, sourcing guide, art direction, voice-over script |

Assemblies: `KR.Core`, `KR.Gameplay`, `KR.Editor`, `KR.Tests.EditMode`.

## Building the scenes

Every generated scene is rebuilt from data with the **Kigali Rush** menu in the Unity Editor.

| Menu | Result |
|---|---|
| Setup → Apply Android Project Settings | IL2CPP, ARM64, ASTC, Linear |
| Setup → Create M1 Blockout Scene | M1 prototype blockout |
| Run → Rebuild Patterns and Levels | The four `LevelDefinition`s and 60 m pattern prefabs |
| Run → Build Run Scene (M2) | The playable lane runner (`Scenes/Run.unity`) |
| GIS → Build Kimihurura / Downtown + Nyabugogo / Remera | Reference district scenes for route planning |
| Scene → Build PC Only (Kimihurura + Kacyiru + Remera) | The main PC showcase, with City Life and the story film. Takes about an hour. |
| Scene → Add Surrounding Land to Open Scene | Real-elevation terrain around the map, without a full rebuild |
| Scene → Check and Fix Grass (open PC scene) | Grass diagnostics and repair |
| Characters → Import Mixamo Animations (curated set) | Imports the animation set (see below) |
| Story → Attach Voice-Over to Open Scene | Attaches narration to the story film |

In the PC scenes, Play starts the Story Mode film, **Tab** switches to free flight and **F5** changes the weather.

### Map data pipeline

```bash
python Tools/gis/extract_kigali.py ...            # OSM roads and buildings from the Rwanda extract
python Tools/gis/make_district_terrain.py SourceArt/GIS [district]
python Tools/gis/make_surround.py SourceArt/GIS east_kigali
```

The `.tif`, `.pbf` and `.geojson` inputs are git-ignored because of their size. Download them again from the sources in the asset register.

### Mixamo animations

The raw Mixamo download library (`SourceArt/Mixamo Animations/`) is git-ignored. The curated clips the game uses are committed in `Assets/Art/Animations/Mixamo`. To change the set, download the clips again, edit `MixamoCatalog.cs` and run the import menu.

### Story narration

The narration lines are in [`Documentation/voiceover-script.csv`](Documentation/voiceover-script.csv). The `Scratch/` clips use a synthetic Windows voice and are for timing only; **a build guard blocks them from player builds**. Put recorded takes in `Assets/Audio/VoiceOver/Story/Recorded/en/<id>.wav`. The French and Kinyarwanda lines are still to be translated.

## Testing

- **Editor:** Window → General → Test Runner → EditMode. `LevelBotTests` checks that each level can be finished when every token is collected, and fails below the required share.
- **Lane runner:** keyboard arrows or A/D, or mouse drag, in the Editor. Swipe on device.
- **Android:** File → Build Profiles → Android → Build And Run. Use APK for testing and AAB for stores.

## Branches

Work is done on topic branches and merged into `main`:

| Branch | Area |
|---|---|
| `feature/core-prototype` | M1: input, vehicle, camera, floating origin |
| `feature/lane-runner-core` | M2: pooling, levels, timer, tokens, scoring, bot test |
| `feature/gis-districts` | GIS data and district importer |
| `feature/kigali-street-scene` | Street scene and city systems |
| `feature/pc-showcase-city` | PC showcase city build |
| `feature/greenery` | Lawns, grass, flowers, woodland |
| `feature/surrounding-land` | Real-elevation land around the map |
| `feature/city-life` | City Life mode |
| `feature/mixamo-characters` | Characters and animation |
| `feature/story-voiceover` | Story film and narration |
| `chore/project-settings` | Project and render settings |
| `assets/third-party-packs` | Imported asset packs |
| `docs/project-docs` | Documentation |

## Project rules (short version)

- Offline-first. Cloud and MTN features sit behind interfaces with Null implementations.
- No `Instantiate`/`Destroy` during a run. Everything is pooled.
- Gameplay is identical on every quality tier; only visuals scale.
- No MTN branding, no real vehicle brands or number plates in public builds, and no AI-generated art in shipped assets.
- Quiz questions are `draft` until a human reviewer approves them. The 1994 Genocide against the Tutsi in Rwanda is never part of a quiz gate, timer, score or leaderboard.

## Licences and credits

Every third-party asset, library, dataset and font is listed in [`Documentation/asset-register.csv`](Documentation/asset-register.csv), with its source, licence and attribution. Entries marked `TODO: verify` still need their licence confirmed before release.

- Map data © [OpenStreetMap](https://www.openstreetmap.org/copyright) contributors, ODbL.
- Elevation: Copernicus DEM GLO-30 (ESA/EU). The exact attribution wording is TODO: verify.
- Unity Asset Store packages are under the Standard Unity Asset Store EULA. They may be used in the game but **must not be published in a public repository**.
- Some items are for the private PC demo only and must be removed before any public build or video, for example the Range Rover model, which is a real trademark. See the register notes.

© Kigali Rush. All rights reserved.
