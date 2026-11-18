# Kigali Rush: Story Mode plan (Kimironko and Kibagabaga)

Status: draft for the owner, 2026-11-16. Nothing here overrides `CLAUDE.md` until the owner updates it.
Marked **TODO: verify** wherever a fact, licence or rule still needs checking against an official source.

---

## 0. Decisions needed first (scope and rules)

This request goes beyond the current brief (`CLAUDE.md` §2 rule 7, §4). Each item below needs an owner decision before it is built.

| # | Request | Conflict / risk | Proposed decision |
|---|---|---|---|
| 1 | Story Mode set in Kimironko and Kibagabaga | Not in MVP scope. The Android lane runner stays the first release. | Make Story Mode the next phase of the existing PC "City Life" mode, which already has needs, school, work, friends, clues and news. |
| 2 | Other players in the world | Multiplayer is listed under "Later". | Phase it. First ship **ghost runs** (other players' recorded routes and times replayed, offline-safe) and shared milestone leaderboards. Live multiplayer comes later. |
| 3 | Build African Leadership University "as it is", realistic, with the same class names, areas and rules | **Owner decision (2026-11-16):** student project, not published, so the real ALU name and campus are used. If the project is ever published, revisit this (no freedom of panorama in Rwanda; `sourcing-guide.md` §0.1). | Real name kept. **Room names, areas and campus rules still have to come from you or the student handbook; I won't invent them.** |
| 4 | Live weather, time and news "as in real life" | The brief is offline-first. Real news content is copyrighted, and in-game news must stay generic and non-political (§4). | Use **real Kigali time** (UTC+2) and **real weather** when online, with offline fallback to typical Kigali weather for the date. News is **written by us**: generic, local and non-political, updated with game patches. No live headlines. |
| 5 | Realistic police activity | No government or security sites (§4). Real uniforms and insignia are protected. | Traffic police only: roundabout control, checkpoints, speed cameras, helmet and seatbelt checks. Uniforms and vehicles are restyled with no real insignia, and every behaviour is taken from public road-safety material. |
| 6 | Rename everything real | Required for businesses, brands and the police. Place names (Kimironko, Kibagabaga, Kigali) are geography, not brands, and ALU stays (row 3). | Keep geographic names and ALU. Rename businesses, bus operators, card systems and police branding. Keep the rename list in `Documentation/name-map.csv` (to create). |

> **Correction to the brief.** Your OSM extract shows a "Kibagabaga Cell Office" (way 206839949), so **Kibagabaga is a cell inside Kimironko sector**, not a second sector. **TODO: verify** against the Gasabo District administrative list. The plan below treats the area as one sector with two neighbourhoods.

---

## 1. What to source, and where

### 1.1 Map and terrain (done for v1; upgrades listed)

| Need | Source | Licence | Status |
|---|---|---|---|
| Roads, building footprints, places | **OpenStreetMap** via Geofabrik Rwanda extract (already `GIS-001`, `SourceArt/GIS/rwanda-latest.osm.pbf`) | ODbL 1.0; attribution "© OpenStreetMap contributors" in credits | **Done.** District `kimironko_kibagabaga` generated with 2,596 road segments and 43,878 buildings (`SourceArt/GIS/districts/kimironko_kibagabaga/preview.png`). Re-download the extract before art lock so recent buildings are included. |
| Ground elevation | **Copernicus DEM GLO-30** (already `GIS-002/005/006`) | Copernicus DEM licence, free incl. commercial, attribution required | **Done.** 7 km square, 6.8 m per sample, elevation 1,362 to 1,750 m. GLO-30 is a surface model (it includes trees and roofs), so it's smoothed in the pipeline. |
| Sharper terrain (optional) | Rwanda national LiDAR/orthophoto or DTM from the **Rwanda Land Management and Use Authority** (RLMUA) or the national geoportal | **TODO: verify** availability, resolution and licence | Request in writing. Only needed if the 30 m DEM looks wrong next to your photos. |
| Missing buildings | **Google Open Buildings v3** footprints | CC BY 4.0 (**TODO: verify** current version) | Merge in only where OSM is sparse; log as a new register row. |
| Street-level reference | **Mapillary** images of Kimironko and Kibagabaga | CC BY-SA 4.0. Use as reference only, never as textures. | Check coverage for KG 11 Ave, Kimironko market and taxi park, and the Kibagabaga hospital area. |
| Your own reference (best source) | **Your phone photos and video**: every facade type, the market, taxi park, roundabouts, the campus (with permission) | Yours | Shoot a fixed checklist (§1.6) at morning, midday, evening and night. |
| Aerial | **Drone survey** of the campus and market area | Yours, but needs **Rwanda Civil Aviation Authority** authorisation (**TODO: verify** current rules) | Optional. Never use Google or Bing imagery (`CLAUDE.md` §3). |

### 1.2 Traffic, road rules and jams

| Need | Source | Notes |
|---|---|---|
| The rules of the road | **Supplied:** Law n° 014/2026 of 06/03/2026 governing road traffic (Official Gazette, Special of 10/03/2026; it repeals Law 34/87). **Still needed:** the **Order of the Minister** that this law points to for the detailed rules, signs, speed limits, the list of faults with fines and demerit points (Art. 5(2), 25(3), 33(2)), and vehicle technical defects (Art. 12(2)). | Rules built from the law, with citations: drive on the right (Art. 4); officers' instructions prevail over signals (Art. 6(3)); electronic fault notice (Art. 35); blood alcohol limit 0.80 g/L (Art. 9); refusing to stop: FRW 400,000–700,000 and 3–6 months (Art. 39); no licence: FRW 100,000–200,000 and/or 15–30 days, and the vehicle is immobilised (Art. 40, 12(1)(a)); merit and demerit points (Art. 25). Red lights, speed limits, seatbelt and helmet are placeholders until the Order arrives. |
| Where and when jams happen | **Your own counts**: film key junctions (Kimironko market roundabout, Kibagabaga road, the road to campus) for 15 minutes at 7:00, 12:00, 17:30 and 20:00 on weekdays and Saturday | Turns into a density curve per road and hour. Don't scrape Google traffic. TomTom and HERE traffic APIs are paid and licensed. |
| Bus routes and stops | OSM `route=bus` relations, plus your own observation | Rename operators and card systems. |
| Umuganda | Monthly community work on the last Saturday morning, when traffic is restricted | **TODO: verify** current hours before building it into a milestone. |
| Car Free Day | Kigali Car Free Day (Sunday mornings, some routes closed) | **TODO: verify** schedule and routes. |

### 1.3 Weather and time

| Need | Source | Licence |
|---|---|---|
| Live Kigali weather | **MET Norway Locationforecast API** (api.met.no): free, commercial use allowed with attribution, needs an identifying User-Agent | CC BY 4.0 / NLOD (**TODO: verify**) |
| Alternative | **Meteo Rwanda** (official national service) | **TODO: verify** whether there's a public API and its terms |
| Avoid without a paid plan | Open-Meteo's free tier is non-commercial | Paid tier needed for a commercial game |
| Offline fallback | Monthly Kigali climatology (two rainy seasons, typical afternoon showers) from Meteo Rwanda published normals | **TODO: verify** figures. Stored in a ScriptableObject. |
| Time | Device clock converted to Kigali time (UTC+2, no daylight saving) | none |

### 1.4 People, speech and animation

| Need | Source | Notes |
|---|---|---|
| Animations | **Mixamo**: 2,187 clips already in `SourceArt/Mixamo Animations` (register `MX-001`) | **TODO: verify** Adobe terms. Use for walk cycles, sitting, drinking tea, reading, phone use, the moto passenger pose and the police hand signals. |
| Characters | Mixamo characters (`The Boss.fbx` present), or authored in **Character Creator / Blender** | Need Rwandan and pan-African looks and everyday Kigali clothing. Don't use real people's likenesses. |
| Voices (best) | **Recorded voice actors**: classmates and friends, with signed consent and licence forms, in Kinyarwanda, English and French | One session per character. Scripts go in `voiceover-script.csv`. |
| Voices (fallback) | A commercial text-to-speech service whose licence allows game use | **TODO: verify** Kinyarwanda voice quality and licence. Windows speech output is scratch only (`VO-000`). |
| Lip sync | **uLipSync** (open source, **TODO: verify** licence) or jaw movement driven by loudness | Runs offline. |
| Dialogue writing | You, plus Kinyarwanda review by a native speaker | Every phrase the game teaches is reviewed before release, as with quiz questions. |

### 1.5 The campus

You need to supply all of this; I can't invent any of it:
- **Written permission** from ALU (or its legal or communications team) for a realistic depiction.
- **Floor plans** or a hand sketch with measurements of the areas used: classrooms, library, cafeteria, open study areas, admin, gates, parking.
- **The real room names and their use.** You said keep them. Permission covers this, or we rename them.
- **The current campus rules**, from the student handbook or code of conduct: hours, ID and gate entry, dress, quiet zones, food areas, visitor rules.
- **Photos** of every facade and interior, plus materials and furniture.
- **The academic rhythm**: the summative and formative calendar pattern, class blocks and typical deadline times.

### 1.6 Photo checklist (Kimironko and Kibagabaga)
Market entrances and stalls; the taxi park; roundabouts and traffic signals; moto stands; bus stops; small shops and boutiques; houses (plastered, brick and gated); walls and gates; drainage channels; pavements and kerbs; street lights; signage styles (to recreate as unbranded versions); trees and hedges. Shoot in the morning, at midday, in the evening and at night.

---

## 2. Story Mode design: "Rush Week"

**Pitch.** You're an ALU student living in Kibagabaga during summative week. Every day is a run against the clock: get to class, meet your group, visit people, drink tea, read, study and still have a life, all while learning how Kimironko really works. It's a race, but the fastest player is the one who knows the city, not the one who breaks the rules.

### 2.1 Core loop
1. **Morning brief.** The day's milestones, each with a deadline window. Real Kigali time and weather are used when online.
2. **Plan.** Choose a transport for each leg: walk, moto (as a passenger), bus or car. Jams, rain and Umuganda change the best choice.
3. **Rush.** Travel and complete the milestone. Following the road rules is the fastest way, because breaking a rule costs more time than it saves (police stop, fine, lost minutes).
4. **Learn.** Each milestone gives a **knowledge card**: a place, a phrase, a custom or a road rule. All are reviewed, like quiz questions.
5. **Balance.** Needs from City Life (energy, food, social, knowledge, hygiene) affect speed and focus. Skipping meals or sleep has a cost.
6. **Results.** Time for each milestone, stars, and a comparison with other players' ghosts.

### 2.2 Milestones (the first week)
All business and institution names here are placeholders to be renamed.

| Day | Milestone | Rush rule | What the player learns |
|---|---|---|---|
| Mon | Make the 9:00 class from your Kibagabaga room | Arrive before the doors close. Morning jam on the main road, so the moto is faster but needs a helmet. | Moto etiquette, helmet rule, greeting phrases |
| Mon | Visit Mama X in Kibagabaga and **drink tea** with her | Tea takes real minutes. Leaving early is rude and you lose the knowledge card. | Kinyarwanda greetings, tea customs (reviewed) |
| Tue | Kimironko Market: buy materials for the group project | List, budget and closing time. The market is busiest at midday. | Market layout, bargaining phrases, local produce |
| Tue | **Read a book** for the summative at the campus library or a quiet café | Uninterrupted reading time counts, and noise zones slow you down. | Study skills, knowledge points |
| Wed | Group meeting on campus, then **submit the summative** by the deadline | A hard deadline. Rain at 15:00 makes the bus slower than walking. | Campus rules and areas (as you supply them) |
| Thu | Personal life: football, gym or choir, then a friend's birthday | Two events that overlap; pick one or plan a route to do both. | City rhythm in the evening |
| Fri | Final summative presentation plus helping a friend move | Back-to-back deadlines across the sector | Bus routes, roundabout priority |
| Sat | Umuganda morning, then free roam | Roads restricted until the end (**TODO: verify** hours). Plan around it. | Civic life (factual, non-political) |

### 2.3 Other players
- **v1, offline-safe:** a ghost of the fastest player per milestone, plus a weekly milestone leaderboard behind `ILeaderboardService`, with replay validation as in `CLAUDE.md` §6.
- **v2:** shared world presence (other students visible on the map, emotes, study-group invites) using Unity Netcode and Relay. Costs and moderation need deciding first.

### 2.4 Cars and people stay in balance
- Pedestrians use footways and crossings and walk at normal speed (1.2–1.4 m/s). Cars yield at crossings.
- Traffic never ignores the player, and the player can't push traffic around.
- The camera frames the player at street level. Vehicles are cast at real size, never oversized.

### 2.5 Police
- **Officers:** traffic officers at the main roundabout at peak hours, mobile checkpoints and fixed speed cameras.
- **Triggers:** speeding, a moto ride without a helmet, a seatbelt off, running a red light, or ignoring a crossing.
- **Consequence:** a stop sequence (pull over, check, warning or fine), which costs time and money.
- **Behaviour source:** every behaviour comes from the rule texts in §1.2, with no invented procedure.

### 2.6 Weather, time and news
- **Clock:** the day/night cycle follows Kigali time when the clock option is on.
- **Weather:** rain, cloud and wind come from the live forecast when online, and from climatology offline. Rain slows walking and motos, wets roads, and makes queues form at shelters.
- **News:** the in-game screen broadcasts the real weather and time, plus generic local news written by us (events, market days, road works). No real headlines, no politics.

---

## 3. Systems to build (on top of what exists)

| System | Builds on | New pieces |
|---|---|---|
| Story map | `PcShowcaseBuilder`, the GIS pipeline | District `kimironko_kibagabaga` (added); campus block-out; market and taxi-park interiors |
| Milestones | `CityLifeDirector`, `ActivitySpot` | `MilestoneDefinition` ScriptableObject (place, window, par time, prerequisites, rewards) and `RushTimer` |
| Dialogue and speech | `FriendDefinition`, `DialogueLayer`, `StoryVoiceOver` | Voice lines per dialogue line, lip sync, Kinyarwanda, English and French |
| Road rules and police | `TrafficSignalSystem`, `RoadNetworkTraffic` | `RoadRuleSet` ScriptableObject with a citation per rule, violation detector, police officer AI, stop sequence |
| Jams | `RoadNetworkTraffic` | Density curve per road and hour from your counts |
| Transport | `DrivableCar`, `PlayerMotor` | Moto passenger, bus boarding, fare card (renamed) |
| Weather and time | `WeatherCycle`, `DayNightCycle` | `IWeatherService` with MET Norway, climatology and Null implementations; Kigali clock |
| Knowledge | Quiz record format (§9) | Knowledge cards reusing the quiz review fields (`sources`, `reviewStatus`) |
| Other players | `ILeaderboardService` | Ghost recorder and player, milestone boards |

---

## 4. Milestones for building it

| Phase | Weeks | Deliverable | Acceptance |
|---|---|---|---|
| S0 Data | done / 1 | District map and terrain generated; base scene builds | A local recognises the street layout in `preview.png` |
| S1 Permissions and photos | 1–2 | ALU written reply; the photo checklist shot; road-rule texts collected | Documents saved in `Documentation/` |
| S2 Campus block-out | 3–4 | Campus areas at the correct scale from plans and photos | Walkthrough matches the plan |
| S3 Milestone core | 5–7 | Monday's milestones playable end to end with timers | Bot finishes each milestone inside its par time when following the rules |
| S4 Road rules, police, jams | 8–10 | Rule set with citations, violations, stops, hourly density | Breaking a rule is never faster than following it |
| S5 People and speech | 11–13 | Mixamo-animated characters, recorded voices, lip sync | Tea visit fully voiced in Kinyarwanda and English |
| S6 Weather, time, news | 14 | Live weather with offline fallback, Kigali clock, authored news | Plays with no network |
| S7 Ghosts and boards | 15–16 | Ghost runs, milestone leaderboard | Validated replays only |
| S8 Week complete | 17–20 | Monday to Saturday milestones, polish, profiling | 60 fps on the PC target |

---

## 5. Done in this session
- Added `kimironko_kibagabaga` to `Tools/gis/make_district_terrain.py`. It's centred between Kimironko Market (OSM way 106506354), Kibagabaga hospital (way 788813030) and the ALU campus (way 891369684), and covers a 7 km square.
- Generated terrain, roads, buildings, places and the 21 km surround in `SourceArt/GIS/districts/kimironko_kibagabaga/`.
- Added a batch task, `BuildStoryScene`, that builds the base scene `Assets/Scenes/PC Only - Story Kimironko Kibagabaga.unity` with the existing PC showcase builder.

## 6. Session 2 (2026-11-16)
- **Road law:** Law 014/2026 read; the rules above are built from it, and the rule list cites the article for each one.
- **Story Mode runtime** (`Assets/Scripts/Gameplay/Story/`):
  - `StoryDirector`: milestones, rush clock, stars, police stops, saving, ghost of your best run.
  - `RoadRulesMonitor`: right-hand side, red signals, speed, licence, seatbelt.
  - `PolicePost`: officers, checkpoints, speed cameras.
  - `KigaliWeather`: MET Norway live weather, with an offline pattern as fallback.
  - `KigaliClock`: real Kigali time.
  - `TrafficRhythm`: rush-hour pace on the vehicle traffic.
  - `StoryHud`: on-screen display.
- **Content** (`Assets/World/Story/`, built by `StoryModeBuilder`): Monday to Wednesday milestones placed at real OSM positions:
  - home in Kibagabaga;
  - ALU campus;
  - Mama Ineza's house about 350 m from home;
  - Kimironko Market.

  Also 9 road rules and 7 draft knowledge cards.
- **YouTube references:** I can't watch video. Save the clips, or screenshots from them, in `SourceArt/Footage/` and I'll extract frames to compare against the scene.
- **Still needed:**
  - the Minister's Order;
  - ALU room names, areas and rules;
  - campus photos or plans;
  - voice recordings (arriving next);
  - junction traffic counts;
  - a MET Norway contact string in `KigaliWeather.userAgent`.
