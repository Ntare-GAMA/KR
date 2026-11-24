# Kigali Rush: Sourcing Guide

Where every asset, tool, data source and reference comes from, and where it goes in the project.
Rules this follows (from the project brief, §3): no non-commercial licences, no AI-generated art in shipped assets,
no commercial map imagery, every third-party item logged in `asset-register.csv` before import.

Status legend: **OK** = licence suits commercial use (still re-read on download day) · **VERIFY** = check terms before use · **AVOID**.
Last checked: 2026-11-01. Licences change, so record the licence URL and access date in the register on the day you download.

---

## 0. Blockers to resolve before M3 (art)

| # | Issue | Why it matters | Action |
|---|---|---|---|
| 1 | **Landmark replicas.** Wikimedia Commons lists Rwanda as having *no freedom of panorama* (Law No. 31/2009 on IP). | A recognisable 3D replica of the Convention Centre dome, BK Arena or Amahoro Stadium may need the rights holder's permission. | Ask a Rwandan IP lawyer, or RDB's IP office, before modelling hero landmarks. Fallbacks: written permission from the owner or operator, or an "inspired-by" silhouette. **TODO: verify** against the statute on WIPO Lex. |
| 2 | **"BK Arena" is a sponsor name.** | Hard rule 6: no branding. | Don't name it in-game; use a neutral label such as "Remera Arena", or none. Same for any shop, bank or telecom signage in facade photos. Paint it out. |
| 3 | **Children in the target audience** (proposal §1). | Google Play Families policy limits which SDKs (analytics, ads) you can ship. Rwanda's data-protection law (Law N° 058/2021) applies. | Decide the age rating before M5 analytics. If under-13s are targeted, use only Families-certified SDKs or no analytics. **TODO: verify** both with current Play policy and NCSA guidance. |
| 4 | **Quiz wording "five provinces".** | Officially there are four provinces plus the City of Kigali, so "five provinces" is not a settled answer as worded. | Word it as "four provinces and the City of Kigali", or "five first-level administrative units". The reviewer confirms. |
| 5 | **Disk space**: about 96 GB free. | Unity 6 + Android modules + Library + Gradle caches + Blender source files easily take 40–60 GB. | Free 50–100 GB first. Keep `Builds/` and raw photos on an external drive. |
| 6 | **Proposal vs brief.** The proposal describes an endless runner on a 16-week plan; brief v2 has 4 fixed levels over 20 weeks. | Conflicting scope. | Brief v2 wins. Endless mode stays in "Later". |

---

## 1. Software (all free unless noted)

| Need | Use | Status | Notes |
|---|---|---|---|
| Engine | **Unity 6.3 LTS** (current LTS, supported to Dec 2027) via Unity Hub, with the Android Build Support, OpenJDK, Android SDK & NDK modules | OK / VERIFY | Unity 6.0 LTS support ended Oct 2026, so don't start on 6.0. Check the Unity Personal revenue/funding cap in the current terms. |
| Packages | Input System, URP, Localization, TextMeshPro (in uGUI), Addressables (optional), Unity Test Framework, Profile Analyzer, Memory Profiler | OK | All are first-party packages from the Unity Registry. |
| 3D | **Blender** (latest LTS) | OK | GPL covers the software, not your output. |
| OSM import in Blender | **Blosm** (formerly blender-osm), free tier | VERIFY | Free tier imports OSM buildings and roads; premium is paid. Alternative: export from QGIS. |
| GIS | **QGIS** | OK | Clips OSM and DEM data, measures road widths and lengths. |
| Textures | **GIMP** or **Krita**; **Materialize** (photo → PBR maps) | OK / VERIFY | Check Materialize's current licence and repository before use. |
| Audio | **Audacity** (free) or **Reaper** (cheap licence, about USD 60) | OK | Reaper is optional. |
| Version control | Git + Git LFS (GitHub), **or** Unity Version Control free tier | VERIFY | GitHub's free LFS quota is small, so check current limits. Keep raw photos and .blend files out of LFS and back them up separately. |
| Backend (M6) | Firebase: Auth (anonymous), Firestore, Cloud Functions for replay validation | VERIFY | Deploying Cloud Functions needs the **Blaze** (pay-as-you-go) plan. Blaze keeps the free quota, but set a budget alert at USD 5. |
| Profiling | Unity Profiler, Frame Debugger, **Android GPU Inspector**, Arm Performance Studio (Mali) or Snapdragon Profiler (Adreno) | OK | Pick the tool that matches the GPU in your test phone. |

---

## 2. Textures, materials, HDRIs

| Source | Licence | Use for | Status |
|---|---|---|---|
| **Poly Haven** (polyhaven.com) | CC0 | HDRIs (one per level: morning, midday, dusk, night), asphalt, concrete, plaster, soil, foliage textures | OK |
| **ambientCG** (ambientcg.com) | CC0 | Road, kerb, brick, metal, corrugated roofing, paving | OK |
| **ShareTextures** (sharetextures.com) | CC0 | Extra PBR sets | OK, but re-check per asset |
| **Your own photos** | You own them | Facade albedo, signage, kerbs, the red-earth verges typical of Kigali, local paving | OK, and best for "recognisable without a caption" |
| Fab / Megascans | Fab Standard Licence (Epic says usable in any engine) | Only if a CC0 source lacks something | VERIFY. Legacy Quixel claims may be Unreal-only. Download under a Fab Standard grant and save the licence page. |
| Google Earth/Maps, Bing, Apple Maps, Street View, 3D Tiles | Proprietary | Nothing | **AVOID** (project brief §3) |
| Mapillary / KartaView street imagery | CC BY-SA | Visual reference only, never as textures | VERIFY |
| Pixabay, Freepik, "free texture" sites with mixed or AI content | Mixed | — | **AVOID**: risk of AI-generated or unclear provenance |

Place in project: `Assets/Art/Environment/Materials/_ThirdParty/<Source>/` with a `LICENSE.txt`. Your own photo-derived textures go in `Assets/Art/Environment/<District>/Textures/`.
Texture import: ASTC, max size set per quality tier (512/1024/2048), mipmaps on, and an atlas per district facade kit.

---

## 3. 3D models

| Category | Recommended route | Sources | Notes |
|---|---|---|---|
| **Blockout** (M1–M2) | Free placeholder kits | **Kenney** (CC0), **Quaternius** (CC0) | Placeholders only; replace by M3. Still log them in the register. |
| **Player cars ×3** | Model in Blender (unbranded) **or** buy a generic Asset Store base and strip the badges | Unity Asset Store (Standard EULA allows commercial use in games), Fab (VERIFY) | LOD0 ≤ 15k tris. No grille shapes or badges that identify a real brand. Shape alone can be trade dress, so keep the cars generic. |
| **Traffic** | Same: generic saloon, SUV, pickup, minibus, city bus, moto-taxi with rider | Asset Store or your own Blender models | Minibuses, city buses and moto-taxis are what make it read as Kigali. Base colours and liveries on your own footage, with no operator names or logos. |
| **Barriers, cones, street furniture, street lights, palms, bougainvillea, hedges** | Model in Blender, or use CC0 | Poly Haven models (CC0), Kenney/Quaternius for blockout | Kigali's medians are distinctive (planted, well kept). Photograph them. |
| **Facade kit** (3–5 modules per district) | Model in Blender, textured from your photos | Your own photos | This is the main thing that makes a district recognisable. |
| **Hero landmarks** | Blender, after blocker #1 is cleared | Your own photos + OSM footprint | Low-poly silhouette, readable at distance, with baked AO. |
| **Sketchfab** downloads | Per model | CC0 / CC BY only | **AVOID** CC BY-NC and anything tagged NoAI, and check every model. CC BY needs a credit line. |

Asset Store purchases: keep receipts (proposal §11.1), save the EULA version, and put the files under `Assets/_ThirdParty/<Publisher>/` with `LICENSE.txt`.

---

## 4. Maps, terrain and geography data

| Need | Source | Licence | Notes |
|---|---|---|---|
| Road skeleton per district | **OpenStreetMap** via **Geofabrik Rwanda extract** (download.geofabrik.de/africa/rwanda) or Overpass Turbo for small areas | ODbL | Credit line "© OpenStreetMap contributors" goes in Credits (already in §8). Game geometry is a Produced Work, so attribution is needed but share-alike is not. Don't publish a modified OSM database. |
| Elevation (Kigali is hilly, so it shapes skylines and backgrounds) | **Copernicus DEM GLO-30** or **SRTM** | Free with attribution | VERIFY Copernicus attribution wording. Use it only for background hills and for gradients on the route spline. |
| Admin boundaries (Heritage Map: provinces and districts) | OSM boundaries, or **geoBoundaries** (geoboundaries.org) | ODbL / CC BY (VERIFY per dataset) | Simplify heavily for a 2D map sprite. Draw the final map art yourself. |

---

## 5. Audio

| Need | Source | Licence | Status |
|---|---|---|---|
| Engine loops, tyres, horns, impacts, UI clicks | **Sonniss GDC Game Audio Bundles** | Royalty-free, commercial use, no attribution | OK. Download the specific yearly bundles you need; they are large, so keep them off the system drive. |
| Extra SFX | **Freesound** (filter: CC0 only; CC BY allowed with credit) | Per sound | VERIFY each sound. **AVOID** CC BY-NC. |
| UI sounds | **Kenney** audio packs | CC0 | OK |
| City ambience (moto-taxis, buses, market, birds at dusk) | **Record it yourself** in each district on a phone or small recorder | You own it | Best authenticity. Avoid capturing identifiable conversations. |
| Music | **Commission a Rwandan musician or producer** under a written licence or assignment covering games, trailers and the MTN distribution | Contract | Best option for local identity. Budget from the "3D assets" or "Marketing" lines. No AI-generated music. |
| — | YouTube Audio Library, Pixabay Music, "royalty-free" mixed sites | Unclear for games | **AVOID** |

Place in project: `Assets/Audio/{Music,SFX,Ambience}/`, plus `_ThirdParty/` for licensed packs.

### Story Mode (VR): what to source next, and where

Car horns are already synthesised in code (`AccidentScene`), so they need no file. Everything below is still silent or a placeholder. Add each item to `Documentation/asset-register.csv` with its licence before it goes in.

| Need | Where to get it | Licence check |
|---|---|---|
| Your car: engine idle and rev loop, indicator click, door open and close, seatbelt click, handbrake | Sonniss GDC bundles (sonniss.com); Freesound (freesound.org, CC0 filter) | Sonniss: royalty-free. Freesound: CC0 only, or CC BY with a credit line. |
| Traffic: passing cars, moto-taxis, buses braking | Record on KG 11 Ave and around Kimironko Market (best); Sonniss as a fallback | Your own recording; no identifiable conversations |
| Ambience: Kimironko market, café terrace (cups, chatter), ALU courtyard, birds at dawn, evening insects, rain on iron roofs | Record on site; Freesound CC0 for gaps | As above |
| Footsteps (pavement, cobbles, grass), crowd murmur | Sonniss; Kenney audio packs (kenney.nl, CC0) | OK |
| UI: panel open, select, photo shutter | Kenney "Interface Sounds" and "UI Audio" packs (CC0) | OK |
| Café furniture, crockery, counters, shop shelves (for interiors later) | Kenney "Furniture Kit" (CC0); Poly Haven models (polyhaven.com, CC0); Quaternius packs (quaternius.com, CC0) | OK. The cups, plates and bottles in the game now are generated in code (`CleanCity.Tableware`) and need no licence. |
| People: characters that walk properly (the current rigs drift) | Mixamo (mixamo.com): characters and animations, FBX for Unity, T-pose | VERIFY Adobe's current terms for use in a commercial game, and log each character. |
| Street signs, shop fronts, number plates (blank) | Your own photos of Kimironko and Kibagabaga streets | Yours. Avoid logos and brand marks. |

---

## 6. Fonts (TextMeshPro)

All the fonts below are from **Google Fonts** under the **SIL Open Font License (OFL)**, which allows embedding in games with the licence text included in Credits. They must cover Latin-1 + Latin Extended-A for French (é è ê à ç œ « ») and Kinyarwanda (basic Latin with apostrophes).

| Role | Candidate | Why |
|---|---|---|
| Road-sign panels, level titles | **Overpass** (inspired by Highway Gothic) or **Barlow / Barlow Condensed** (inspired by Californian road signage) | Matches the semi-diegetic road-sign style in §8 |
| HUD clock and numerals | **Barlow Semi Condensed** with TMP `<mspace>` for steady digits, or **Share Tech Mono** | Dashboard look, and digits don't jitter |
| Body text, quiz | **Inter** or **Noto Sans** | Good readability, wide language coverage; Noto is the safe fallback |

TMP font assets: build with a dynamic fallback to Noto Sans. Check strings in all three locales at 1080×1920 and 720×1280.

---

## 7. Quiz sources (drafts only; a human approves)

Use primary or official sources first. Drafts are written with `reviewStatus: draft` only.

| Tier | Source | Use for |
|---|---|---|
| 1 | Law determining administrative entities of Rwanda (Organic Law of 2005 and later amendments), via **Official Gazette** / **MINALOC** (minaloc.gov.rw) | Provinces, districts, Kigali's three districts. **TODO: verify** law number and current amendment. |
| 1 | National Urbanisation Policy / **MININFRA** | Six secondary cities. **TODO: verify** the policy document. |
| 1 | **gov.rw**, Parliament (parliament.gov.rw) | Independence date, constitutional facts |
| 1 | **Rwanda Cultural Heritage Academy** (formerly Institute of National Museums of Rwanda) | Monarchy, Kigeli V Ndahindurwa, the Gihanga tradition, cultural sites. **TODO: verify** current name and URL. |
| 2 | University of Rwanda historians (your expert reviewer) | Review and sign-off on every record |
| — | Wikipedia | Lead-finding only; never cite it as the source |

Remembrance: Kigali Genocide Memorial and CNLG/MINUBUMWE content stays out of the MVP (brief §3/§4).

---

## 8. Places: what to photograph and film in Kigali

Equipment: phone on a cheap tripod; shoot RAW if possible; include a grey card or white paper for white balance; shoot facades **straight on**, overcast or in open shade, with a 1 m reference in frame; and record GPS (it helps match OSM).

| Level | District | Capture | Time of day to match |
|---|---|---|---|
| L1 Morning Commute | Kimihurura / Kacyiru | Convention Centre roundabout and approach roads (from public pavement), median planting, kerbs, street lights, residential walls and gates, mid-rise office facades | Morning |
| L2 Market Rush | Nyabugogo / Downtown (Nyarugenge) | Bus park edges, market facades, shopfront rhythm (no signage text), corrugated roofs, barriers, crowds as **silhouettes only**, minibus and bus colour schemes | Midday |
| L3 Evening Rush | Remera | Stadium and arena surroundings (blocker #1 first), wide boulevard, pedestrian bridges, street lighting at dusk | Dusk |
| L4 The Gauntlet | Kimihurura (night) | The same roads as L1: headlights, street-light colour temperature, how the dome looks lit, lit facades | Night |

Also capture: a 10–20 s video of driving each route (for pacing and the "recognisable" check), road-marking styles, kerb paint patterns, and ambient audio.

**Do not photograph** government, police or military installations or security staff, and don't linger near the Office of the President in Kacyiru or the Parliament in Kimihurura. **Drones** need Rwanda Civil Aviation Authority authorisation (**TODO: verify** current rules); don't fly one without it. Don't use identifiable faces or number plates in any texture.

---

## 9. Hardware and testing

| Item | Recommendation | Budget line |
|---|---|---|
| Test phone (Mid tier) | A popular mid-range Android in Rwanda: about 4–6 GB RAM, Adreno 6xx or Mali-G5x/G6x class. Buying used is fine. | Hardware USD 550 |
| Second phone (Low tier) | 2–3 GB RAM, to validate the Low tier and 30 FPS | Same |
| Players for completion targets (§5) | 10–15 local testers, both new and skilled | Testing USD 250 |

---

## 10. Where things go in the project

```
Assets/
  _ThirdParty/<Source>/            # every external asset; must contain LICENSE.txt (M3 build check)
  Art/Vehicles/{Player,Traffic}/
  Art/Environment/{Common,Kimihurura,Nyabugogo,Remera}/{Meshes,Textures,Materials,Prefabs}
  Art/Environment/Landmarks/
  Art/UI/{Sprites,Atlases,Fonts}/
  Audio/{Music,SFX,Ambience}/
  Data/{Levels,Patterns,Tuning,Quiz}/  # ScriptableObjects + questions.json
  Prefabs/{Player,Traffic,Obstacles,Pickups,Patterns,Environment}/
  Scenes/  Scripts/{Core,Gameplay,Presentation,Services/...}/  Settings/
Documentation/  asset-register.csv  sourcing-guide.md
SourceArt/      # outside Assets/: .blend, raw photos, DEM/OSM extracts (not imported, backed up separately)
```
