# Kigali Rush VR

A first-person virtual reality driving and life game set in **Kimironko** and **Kibagabaga** (Gasabo, Kigali), with the African Leadership University campus, built in Unity 6 (URP) for the **Meta Quest** (standalone).

You play an ALU student racing deadlines through Rush Week: visit a friend, drink tea, get to class, read a book, buy at the market. Every milestone has a par time, and you get around on foot or in your own car on real streets built from map data. You learn to drive the way it is done in Rwanda: drive on the right, stop at red lights and STOP signs, keep to the limit, wear your seatbelt. A driving coach warns you before you break a rule, and learners are not fined until they have driven 3 km cleanly.

> Private repository. It contains Unity Asset Store packages and Mixamo animations that must not be redistributed publicly. See [Licences and credits](#licences-and-credits).

---

## Documents

| Document | What it covers |
|---|---|
| [Game Design Document (updated, 24 Nov 2026)](DOCS/Kigali%20Rush%20VR%20-%20Game%20Design%20Document.docx) | The game as built: core loop, controls, mechanics, Rush Week missions, world, road rules, interface and comfort, technical design, **Performance considerations** (FPS benchmark, profiling, techniques, screenshots), scope and known issues |
| [Performance Considerations](DOCS/Performance%20Considerations.docx) | The full performance write-up with charts |
| [First GDD (13 Nov 2026)](DOCS/Game%20Design%20Document.pdf) | The original concept document this vertical slice grew from |
| [Story script](Documentation/story-script.md) | Missions, characters and conversations |
| [Asset register](Documentation/asset-register.csv) | Every third-party asset with its source and licence |

## What is in the game

- **The neighbourhood:** Kimironko, Kibagabaga and the ALU campus, joined by the real main roads, about 5.8 km². Built from OpenStreetMap roads and building footprints on Copernicus elevation data.
- **First person in VR:** your hands (PolyOne VR Hands) follow the controllers when you walk and hold the steering wheel when you drive. The controllers themselves are never drawn.
- **Your car:** the PolyDrive Bavaria Sport on real physics: suspension, grip, rear-wheel drive, an automatic gearbox (P/R/D), anti-lock brakes, hill-start assist and hill-hold. A traction assist at crawling speed lets you drive out of a ditch or off steep grass under your own control.
- **Traffic and road rules:** signalised junctions with three-lamp signals, STOP signs, speed limits, police posts, rush-hour jams. Rules from Law n° 014/2026; fines and points stay placeholders until the Minister's Order is supplied.
- **A day in Kigali:** every session starts in the morning and runs into the evening. Headlights, brake and tail lights, street lamps and night-reflective road signs work. The weather changes each session (sunny first).
- **Satnav:** a city map drawn from OSM, a route planner on the real road network (one-way streets respected) and turn-by-turn directions.
- **In the car, one screen:** the dashboard screen holds everything (map, directions, speed and limit, gear, seatbelt, lights, the milestone clock, the coach). Nothing else floats in view.
- **Walking, nothing in your face:** the info panel opens over your left hand only when you press the left ☰ button; buzzes tell you when there is news.
- **Rush Week story:** ten missions over three days (arrive, talk, a parking test, a road-rule quiz, a poster photo) with five characters: Maman Fils, Aline, Jean-Paul, Officer Mukamana and Mzee Gakuba. Script: [`Documentation/story-script.md`](Documentation/story-script.md).
- **Discover, collect, level up:** public places in each sector go into your journal; XP (each reward given once), levels, badges, a photo gallery on the headset, and a guided Kimironko tour.
- **Comfort and safety:** snap or smooth turning, vignette strength, teleport walking, large text, seated play, recentre; the game pauses with the panel open or the headset off, saves on sleep, keeps a backup save and resumes a mission after a restart.

## Controls (Meta Quest Touch controllers)

| | Left controller | Right controller |
|---|---|---|
| Thumbstick | Walk (or aim a teleport); menus and replies; steers only with "Steering: left stick" | Snap or smooth turn; in the car indicators (left/right) and radio (up/down) |
| Thumbstick click | Run; headlights in the car | Gear P / D / R with "Steering: left stick" |
| Trigger | Brake | Accelerate; camera shutter when walking |
| Grip | Hold things: pick up, carry and throw; pull door handles; in the car hold the steering-wheel rim | The same; in the car hold the rim or the gear lever; walking with nothing in reach, raises the photo camera |
| Buttons | X start milestone / talk / select, Y seatbelt | A get in / out of the car (out only in P), B handbrake |
| Menu ☰ | Info panel: Status, Journal, Progress, Photos, Settings (pauses the game) | |

Hands (driving, the default "Steering: hands on the wheel and gear lever"): grip the rim and turn the wheel with your hands (with both hands the turns are averaged; let go and it returns to straight as you roll). The gear lever is on the console by your right knee: grip it and push it forward for P, the middle for R, pull it back for D (only when stopped). Press the middle of the wheel for the horn. The first time you sit in the car, the Comfort check offers three presets against motion sickness (also in Settings).

Hands (walking): squeeze the grip near a cup, book, bottle, fruit or ball to pick it up; open the hand to drop it, or swing and open to throw. Take a building's door handle and pull to open it and go in. In a restaurant, bring the food or drink to your mouth to eat or sip. Each grab gives a short buzz.

Desktop (Editor testing): G picks up what you look at (G again puts it down, right-click throws), F3 performance overlay, F9 FPS benchmark, WASD walk/drive, mouse look, E car, Enter start / talk / select, B seatbelt, R gear, L lights, Space handbrake, Tab info panel, arrows navigate it (Settings has the screen resolution, display mode and Quit to main menu), C camera and click to shoot, M full map, N destination list (arrows pick a category and a place, Enter sets it; directions show on the minimap and as a line on the ground).

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
| `Assets/Scripts/Gameplay/Story` | Rush Week, in eight folders: `Missions` (director, milestones, save, discoveries), `Conversations`, `RoadRules` (monitor, coach, police stops, fines), `Emergency` (accidents, ambulance, police response), `Navigation` (satnav, maps, routes), `Interiors` (buildings, venues, restaurants), `Environment` (clock, weather, traffic rhythm), `UI` (info panel, dashboard, phone, wallet) |
| `Assets/Scripts/Gameplay/Interaction` | Hands that grab, hold and throw (`IGrabbable`, `IHandDriver`, `HandInteractor`, `Grabbable`, `DoorHandle`, `Edible`) |
| `Assets/Scripts/Gameplay/Diagnostics` | Performance overlay and the repeatable FPS benchmark |
| `Assets/Scripts/Core` | Engine-light helpers: frame statistics, Profiler counters, atomic file writes |
| `Assets/Scripts/Tests/EditMode` | Unit tests (NUnit, Unity Test Framework) |
| `Assets/Scripts/Gameplay/World` | Traffic, signals, STOP signs, day and night, street lamps, instanced city, road graph, city map |
| `Assets/Scripts/Editor` | Editor-only tools: performance (LOD setup, scene report, screenshots), Quest project setup and build (`XR`) |
| `Assets/World/PC/kimironko_kibagabaga` | The generated world (terrain, roads, instanced city, car materials, city map) |
| `Assets/Scenes` | `PC Only - Story Kimironko Kibagabaga` (the game scene) |
| `SourceArt/GIS/districts/kimironko_kibagabaga` | Processed heightmap, roads, buildings and places (raw `.tif`, `.pbf`, `.geojson` inputs are git-ignored) |
| `Tools/gis` | Python: OSM extraction, district heightmaps, surrounding land |
| `Documentation` | Asset register, sourcing guide, Story Mode plan, art direction |

Assemblies: `KR.Core`, `KR.Gameplay`, `KR.Editor`, `KR.Editor.XR`, `KR.Tests.EditMode`.

## Code architecture

How the code is organised, with the classes that show each principle:

| Principle or pattern | Where | Why |
|---|---|---|
| **Separate assemblies (layers)** | `KR.Core` ← `KR.Gameplay` ← `KR.Editor`; tests in `KR.Tests.EditMode` | Core has no game code; editor tools never ship; each layer compiles on its own. |
| **Interfaces (dependency inversion, open/closed)** | `IGrabbable` (`Grabbable`, `DoorHandle`), `IHandDriver` (`HandDrivers.cs`) used by `HandInteractor` | The hand works with anything you can hold or pull and with each input device, without knowing their classes. New grabbable things need no change to the hand. |
| **Single responsibility** | `EmergencyResponse` decides who is sent and what happens; `EmergencyVehicle` drives one vehicle; `AmbulanceCrew` plays the stretcher scene; `OfficerOnFoot` walks and talks | Each class has one reason to change. |
| **Observer (C# events)** | `DrivableCar.GearChanged`, `CarImpacts.PersonHit`, `RoadRulesMonitor.Violation`, `Grabbable.Grabbed`, `SeatSpot.SatDown`, `ConversationRunner.Finished`, `StoryDirector.BadgeEarned` | The gear lever, the police, the fines and the HUD react to the car and the player without the car knowing about them. |
| **Data-driven design (ScriptableObjects)** | `StoryWeek`, `MilestoneDefinition`, `PlaceCatalog`, `StreetNetwork`, `PlayerTuning`, `InteractionTuning` | Story content, places, fines and tuning values are assets, not code. |
| **Object pooling** | `HeadlightPool`, `StreetLightPool`, traffic cars, emergency vehicles made once and reused | No creating or destroying objects while playing: 0 KB garbage per frame on the Quest. |
| **Self-registering systems** | `[RuntimeInitializeOnLoadMethod]` hooks (for example `EmergencyResponse`, `GearShifter`, `CarMirrors`, `QuestPerformance`) | Each system attaches itself when the scene loads, so the scene does not need hand wiring and systems can be removed independently. |
| **Unit tests** | `RoadGraphTests`, `StoryRulesTests`, `InteractionTests`, `FrameStatsTests` (20 tests) | Route finding, road rules, grabbing and frame statistics are checked without playing. |

Known debt: `RoadNetworkTraffic` (traffic driving, overtaking and obstacles) and `StoryDirector` (milestones, fines and flags) are still large and would be split next, the same way as `EmergencyResponse`.

## Building

The game scene and the generated world (terrain, roads, city, traffic, people, the player and the car) are in the repository, so there is nothing to generate: open `Assets/Scenes/PC Only - Story Kimironko Kibagabaga.unity` and press Play to try it in the Editor.

To build for the headset, switch the platform to Android (File → Build Profiles), then:

| Menu | Batch method | Result |
|---|---|---|
| Kigali Rush → VR → Apply Meta Quest settings | `KR.Editor.XR.QuestSetup.ApplyBatch` | OpenXR (Meta Quest, Touch controllers), ARM64 IL2CPP, Vulkan, ASTC, single-pass instanced |
| | `KR.Editor.XR.QuestSetup.BuildApkBatch` | `Builds/Quest/KigaliRushVR.apk` |

For the Unity Profiler on the headset, tick Development Build and Autoconnect Profiler in the build profile.

Install on the headset:

```bash
adb install -r Builds/Quest/KigaliRushVR.apk
```

### Map data

The map data was prepared with the Python scripts in `Tools/gis` (OSM roads and buildings, district heightmaps). The raw `.tif`, `.pbf` and `.geojson` inputs are git-ignored because of their size; their sources are in the asset register.

## Testing

Unit tests (frame statistics, road routing and one-way streets, hands and door handles, the police stop, atomic saves):

```bash
Unity -batchmode -projectPath . -runTests -testPlatform EditMode -testResults Logs/tests_editmode.xml
```

Or Window → General → Test Runner → EditMode → Run All.

On the headset: walk, drive, open the info panel (left ☰), and on Settings switch on the performance overlay or run the FPS benchmark.

## Performance

- **Overlay:** F3 on a desktop, or Settings → Performance overlay on the headset. It shows FPS, frame time against the display budget (13.9 ms at 72 Hz), main-thread time, draw calls, batches, SetPass calls, triangles, GC allocation per frame and memory. Draw-call counters need a development build.
- **Benchmark:** F9, Settings → Run the FPS benchmark, or `KigaliRushStory.exe -benchmark` (add `-lodBias 1000` for a "LODs off" comparison). It follows the same 818 m street route every time, at 10:00 in calm weather with the clock held. It writes a per-second CSV and a summary (average FPS, 1% and 0.1% lows, share of frames over budget, draw calls) to `persistentDataPath/Benchmarks`. On the Quest, fetch the results with `adb pull /sdcard/Android/data/com.kigalirush.storyvr/files/Benchmarks`.
- **Scene report:** Kigali Rush → Performance → Scene report, which writes `Logs/perf_report.txt` with renderers, triangles, LOD groups, static flags and instancing per group, and the textures without mipmaps.
- **Measured (Quest Pro, 22 Nov 2026, before the 24 Nov changes):** 40.0 FPS average against the 72 Hz target, 1% low 23.9, main thread about 26 ms of a 13.9 ms budget, about 1,009 draw calls, 24 SetPass calls, 438k triangles, 0 KB garbage per frame. The limit is the number of draw calls on the CPU, not triangles. Full write-up with charts and screenshots: the Performance considerations section of the [GDD](DOCS/Kigali%20Rush%20VR%20-%20Game%20Design%20Document.docx) and [`DOCS/Performance Considerations.docx`](DOCS/Performance%20Considerations.docx).
- **Unity Profiler on the headset:** make a development build with Autoconnect Profiler (see Building). Then Window → Analysis → Profiler and pick the Quest as the target.
- **Profiler markers:** `KR.Traffic`, `KR.Interiors`, `KR.InstancedCity.Draw`, `KR.StreetLights`, `KR.Signals`, `KR.Story`, `KR.Emergency`, `KR.CrossingWalkers`, `KR.Headlights`, `KR.DayNight`.
- **Techniques in the build:**
  - IL2CPP on ARM64;
  - single-pass instanced stereo and foveated rendering (OpenXR);
  - SRP Batcher;
  - GPU instancing (the instanced city scatter and instanced materials);
  - static batching (roads, road edges, road markings);
  - 27,391 LOD groups (parked cars get a low-poly LOD1);
  - per-layer cull distances (tightened on the Quest on 24 Nov: details 90 m, street dressing 150 m, trees 180 m, houses 320 m);
  - a lighter rear-view mirror on the Quest (no trees or small details, 110 m, every third frame);
  - dynamic batching off on purpose (the SRP Batcher replaces it in URP);
  - no baked occlusion culling, on purpose (open hillside streets with long sightlines; distance culling and fog instead);
  - object pools (traffic, building doors, street lamps and headlights);
  - mipmapped ASTC textures (only skyboxes and UI have none);
  - interiors made only when you go in.

## Project rules (short version)

- Offline-first: a full session works with no network.
- Only the play area is built and every budget is set for the Quest (72 fps, rendered twice).
- No MTN branding, and no real vehicle brands or number plates in public builds.
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
