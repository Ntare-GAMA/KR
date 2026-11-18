# Kigali Rush VR

A first-person virtual reality driving and life game set in **Kimironko** and **Kibagabaga** (Gasabo, Kigali), with the African Leadership University campus, built in Unity 6 (URP) for the **Meta Quest** (standalone).

You play an ALU student racing deadlines through Rush Week: visit a friend, drink tea, get to class, read a book, buy at the market. Every milestone has a par time, and you get around on foot or in your own car on real streets built from map data. You learn to drive the way it is done in Rwanda: drive on the right, stop at red lights and STOP signs, keep to the limit, wear your seatbelt. A driving coach warns you before you break a rule, and learners are not fined until they have driven 3 km cleanly.

> Private repository. It contains Unity Asset Store packages and Mixamo animations that must not be redistributed publicly. See [Licences and credits](#licences-and-credits).

---

## What is in the game

- **The neighbourhood:** Kimironko, Kibagabaga and the ALU campus, joined by the real main roads, about 5.8 km². Built from OpenStreetMap roads and building footprints on Copernicus elevation data.
- **First person in VR:** your hands (PolyOne VR Hands) follow the controllers when you walk and hold the steering wheel when you drive. The controllers themselves are never drawn.
- **Your car:** the PolyDrive Bavaria Sport on real physics: suspension, grip, rear-wheel drive, an automatic gearbox (D/R), anti-lock brakes, hill-start assist and hill-hold. A traction assist at crawling speed lets you drive out of a ditch or off steep grass under your own control.
- **Traffic and road rules:** signalised junctions with three-lamp signals, STOP signs, speed limits, police posts, rush-hour jams. Rules from Law n° 014/2026; fines and points stay placeholders until the Minister's Order is supplied.
- **A day in Kigali:** every session starts in the morning and runs into the evening. Headlights, brake and tail lights, street lamps and night-reflective road signs work. The weather changes each session (sunny first).
- **Satnav:** a city map drawn from OSM, a route planner on the real road network (one-way streets respected) and turn-by-turn directions.
- **In the car, one screen:** the dashboard screen holds everything (map, directions, speed and limit, gear, seatbelt, lights, the milestone clock, the coach). Nothing else floats in view.
- **Walking, nothing in your face:** the info panel opens over your left hand only when you press the left ☰ button; buzzes tell you when there is news.

## Controls (Meta Quest Touch controllers)

| | Left controller | Right controller |
|---|---|---|
| Thumbstick | Walk; steer in the car | Snap turn |
| Thumbstick click | Run; headlights in the car | Gear D / R (when stopped) |
| Trigger | Brake | Accelerate |
| Grip | (closes the hand) | Next satnav destination |
| Buttons | X start milestone, Y seatbelt | A get in / out of the car, B handbrake |
| Menu ☰ | Info panel (walking) | |

Desktop (Editor testing): WASD walk/drive, mouse look, E car, Enter start, B seatbelt, R gear, L lights, Space handbrake, Tab info, M full map, N destination.

## Requirements

- **Unity 6000.3.27f1** (Unity 6.3 LTS) with Android Build Support (OpenJDK, Android SDK and NDK)
- **Git LFS**: run `git lfs install` before cloning. Textures, models, audio, heightmaps and the generated world are stored in LFS.
- **Python 3.11+** for the GIS tools: `pip install numpy tifffile pillow` (a virtual environment in `Tools/.venv` is git-ignored)
- A **Meta Quest** in developer mode to run the APK

```bash
git lfs install
git clone https://github.com/Ntare-GAMA/KR.git
```

Open this folder in Unity Hub (Add, Add project from disk, pick the folder that holds `Assets`). The first import takes a while.

## Repository layout

| Path | What it is |
|---|---|
| `/` | The Unity project (Assets, Packages, ProjectSettings) |
| `Assets/Scripts/Gameplay/Player` | Walking, the car (physics, lights), hands, VR rig, controller input |
| `Assets/Scripts/Gameplay/Story` | Rush Week: milestones, road-rule monitor, driving coach, police, weather, clock, satnav, dashboard screen and VR panel |
| `Assets/Scripts/Gameplay/World` | Traffic, signals, STOP signs, day and night, street lamps, instanced city, road graph, city map |
| `Assets/Scripts/Editor` | Scene builders: GIS import, play area, roads, greenery, people, player and car, Story Mode, city map, Quest setup |
| `Assets/World/PC/kimironko_kibagabaga` | The generated world (terrain, roads, instanced city, car materials, city map) |
| `Assets/Scenes` | `PC Only - Story Kimironko Kibagabaga` (the game scene) |
| `SourceArt/GIS/districts/kimironko_kibagabaga` | Processed heightmap, roads, buildings and places (raw `.tif`, `.pbf`, `.geojson` inputs are git-ignored) |
| `Tools/gis` | Python: OSM extraction, district heightmaps, surrounding land |
| `Documentation` | Asset register, sourcing guide, Story Mode plan, art direction |

Assemblies: `KR.Core`, `KR.Gameplay`, `KR.Editor`, `KR.Editor.XR`.

## Building

Everything generated is rebuilt from data with the **Kigali Rush** menu in the Unity Editor, or from the command line (`-batchmode -executeMethod ...`).

| Menu | Batch method | Result |
|---|---|---|
| Scene → Build Story scene (Kimironko + Kibagabaga) | `KR.Editor.Batch.BatchTasks.BuildStoryScene` | The world: terrain, roads, signals, city, traffic, people, the player, the car, the city map (about 20 min) |
| Story → Add Story Mode to Kimironko scene | `KR.Editor.Batch.BatchTasks.AddStoryMode` | Rush Week, road rules, coach, police posts, satnav, VR rig |
| VR → Apply Meta Quest settings | `KR.Editor.XR.QuestSetup.ApplyBatch` | OpenXR (Meta Quest, Touch controllers), ARM64 IL2CPP, Vulkan, ASTC, single-pass instanced |
| | `KR.Editor.XR.QuestSetup.BuildApkBatch` | `Builds/Quest/KigaliRushVR.apk` (Android build target) |
| | `KR.Editor.Batch.BatchTasks.CaptureCar` | Renders of the car and the driver's view (`-captureDir <folder>`) |

Install on the headset:

```bash
adb install -r Builds/Quest/KigaliRushVR.apk
```

### Map data pipeline

```bash
python Tools/gis/extract_kigali.py ...            # OSM roads and buildings from the Rwanda extract
python Tools/gis/make_district_terrain.py SourceArt/GIS kimironko_kibagabaga
```

The `.tif`, `.pbf` and `.geojson` inputs are git-ignored because of their size. Download them again from the sources in the asset register.

### Mixamo animations

The raw Mixamo download library is git-ignored. The curated clips the game uses are in `Assets/Art/Animations/Mixamo`. To change the set, download the clips again, edit `MixamoCatalog.cs` and run Characters → Import Mixamo Animations.

## Project rules (short version)

- Offline-first: a full session works with no network.
- Only the play area is built and every budget is set for the Quest (72 fps, rendered twice).
- No MTN branding, no real vehicle brands or number plates in public builds, and no AI-generated art in shipped assets.
- Road-rule fines and points are placeholders until the Minister's Order is supplied; nothing is presented as official that is not.
- Knowledge cards stay `draft` until a human reviewer approves them. The 1994 Genocide against the Tutsi in Rwanda is never part of a timer, score or game mechanic.

## Licences and credits

Every third-party asset, library, dataset and font is listed in [`Documentation/asset-register.csv`](Documentation/asset-register.csv), with its source, licence and attribution. Entries marked `TODO: verify` still need their licence confirmed before release.

- Map data © [OpenStreetMap](https://www.openstreetmap.org/copyright) contributors, ODbL.
- Elevation: Copernicus DEM GLO-30 (ESA/EU). The exact attribution wording is TODO: verify.
- Unity Asset Store packages (including PolyDrive and PolyOne VR Hands) are under the Standard Unity Asset Store EULA. They may be used in the game but **must not be published in a public repository**.
- Mixamo animations: Adobe terms (TODO: verify); not for a public repository.
- Some items are for the private demo only and must be removed before any public build or video, for example the Range Rover model, which is a real trademark. See the register notes.

© Kigali Rush. All rights reserved.
