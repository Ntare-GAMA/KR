# Kigali Rush: Art Direction from Reference Images

Reference board: `SourceArt/Reference/_web/` (web images, **reference only**; see licensing below).
Replace each with your own photo of the same place before M3 sign-off.

## 1. Reference log

| File | What it shows | Level fit | Licence / origin | Use |
|---|---|---|---|---|
| ref01_render_development_night | Architectural **render** of a planned development: towers with rooftop solar, a pool, a roundabout, a forested hill | None yet. **TODO: confirm** location and whether it is built | Developer/architect copyright | Mood reference for the L4 night lighting only. Don't model a building that may not exist. |
| ref02_kcc_dome_aerial | Convention Centre dome, curved hotel block, landscaped car park, ring roads | L1, L4 | Unknown author, **TODO: find source** | Road layout and landmark massing (blocker #1 in sourcing-guide applies) |
| ref03_boulevard_median_palms_ALAMY | Dual carriageway at road level: palm median, striped kerbs, double-arm street lights, moto-taxis | L1 (core road look) | **Alamy stock, watermarked** | Reference only. Never trace, texture from, or show it publicly. |
| ref04_hill_view_dome_distance | Elevated view: boulevard with planted median, dome on the skyline, hills behind | L1 | Unknown, **TODO** | Skyline and backdrop composition |
| ref05_citycentre_roundabout | City-centre roundabout with fountain garden, high-rises, a tower with a spire | L2 (Downtown) | Unknown, **TODO** | Downtown massing. Signage and billboards must not be copied. |
| ref06_junction_green_bus_ALAMY | Junction, green city bus, brick walls, red roofs, traffic lights | L2/L3 filler | **Alamy, watermarked** | Reference only |
| ref07_dual_carriageway_bus_stop_ALAMY | Dual carriageway with median bus stop, grass verges, a red-earth path, hills | L3 (Remera?) **TODO: confirm** | **Alamy, watermarked** | Reference only |

## 2. What makes these read as Kigali (build list)

**Road (shared kit, highest priority: on screen 100% of the time)**
- Dark, clean asphalt; white edge lines; **yellow dashed centre line** (ref03, ref07). TODO: verify the marking rules against your footage.
- **Alternating black/white kerb segments** on every median and edge. This is the single strongest visual cue, so make it a trim-sheet strip.
- Zebra crossings with wide bars.
- Raised medians: kerb + grass + low shrubs + flowering plants + **palms** at a regular rhythm (ref03, ref04, ref07).
- Concrete paved footpaths with a narrow grass strip.

**Street furniture**
- Tall white light poles with **curved double arms** (ref03, ref06, ref07): one mesh, GPU-instanced, light cookie at night.
- Traffic signals on white poles; red/white bollards; galvanised guard rails on curves.
- Median bus shelters (ref07).

**Vegetation (instanced, 2–3 LODs, billboards past 80 m)**
- Royal-type palms (median), broadleaf trees, tall conifers/eucalyptus-like silhouettes, one red-flowering tree accent (ref07). Species: TODO, confirm with your photos; it doesn't matter for gameplay.
- Lots of green, with **red-earth** exposed soil on verges and paths.

**Buildings**
- Facade kit A (residential/low): red-brick or rendered walls, terracotta or corrugated red roofs, boundary walls with gates (ref06, ref07).
- Facade kit B (mid-rise offices): white render + blue glass bands (ref05, ref07).
- Landmarks: dome (L1/L4), downtown tower cluster (L2), stadium/arena (L3), all as silhouettes subject to the copyright check.

**Backdrop**
- Every reference has **layered hills covered in housing** behind the city. Use 2–3 hill cards (DEM-shaped meshes with a painted housing texture) plus distance fog. This is cheap and very recognisable.
- Sky: bright cumulus with haze (day); cool blue dusk; ref01's warm windows against dark green for night.

**Traffic**
- Green city buses, white minivans, SUVs, pickups, and **moto-taxis everywhere** (rider + passenger, helmets). Keep liveries generic: base colour is fine, but no operator name or logo. Erase brand text (e.g. a tailgate badge in ref03) and real plates.

## 3. Gameplay vs. real road width

Real Kigali boulevards are mostly **2 lanes per direction with a median**; the game needs **3 lanes**.
Recommended: build the player carriageway as 3 lanes, keep the planted median on the left, and show the oncoming carriageway beyond it as scenery with non-colliding traffic. This keeps the look without changing the gameplay rules.

## 4. Shoot list additions (from these references)

- Kerb stripe close-ups (flat, with scale) → trim sheet
- Light pole, straight on, full height, plus the arm detail
- Median planting, 3 m section, top-down and side
- Boulevard sequence from a car at the L1 route's speed (phone on dash), morning and night
- Hillside housing from 3 viewpoints, for the backdrop cards
- Brick wall, render wall, and red roof material swatches
