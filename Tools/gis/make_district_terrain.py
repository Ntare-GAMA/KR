"""Builds per-district Unity terrain heightmaps, local-space road/building data and preview maps.

Usage: python make_district_terrain.py <gis_dir> [district]   (default: all districts)
Inputs in <gis_dir>: cop30_S02_00_E030_00.tif, kigali_roads.geojson, kigali_buildings.geojson
Outputs in <gis_dir>/districts/<name>/:
  heightmap_1025.raw   16-bit little-endian, Unity Terrain import (Byte Order: Windows)
  terrain.json         size and height range to type into Unity's Terrain settings
  roads_local.json     road polylines in metres, origin = SW corner, x = east, z = north, y = ground height
  buildings_local.json footprints in metres + height estimate
  preview.png          hillshade + roads + buildings, for checking against reality

Elevation: Copernicus DEM GLO-30 (DSM; includes buildings/trees, so it is smoothed here).
Roads/buildings: © OpenStreetMap contributors, ODbL.
"""
import json
import math
import os
import sys

import numpy as np
import tifffile
from PIL import Image, ImageDraw

DISTRICTS = {
    # name: (centre lat, centre lon, size in metres, levels)
    # Centres come from OSM landmark lookups (landmarks.json).
    # Story Mode: Kimironko sector incl. Kibagabaga cell, and the ALU campus on its eastern edge.
    # Centre is midway between the OSM features below; 7 km square covers all three with margin.
    "kimironko_kibagabaga": (-1.94000, 30.13200, 7000, "Story Mode"),
}
# Dressing hubs per district (name, lat, lon), all from OSM: place nodes / landmark footprints.
HUBS = {
    "kimironko_kibagabaga": [
        ("Kimironko", -1.9494168, 30.1266978),          # Kimironko Market, way 106506354
        ("Kibagabaga", -1.9314424, 30.1123116),         # Kibagabaga hospital, way 788813030
        ("ALU campus", -1.9302588, 30.1513859),         # African Leadership University, way 891369684
    ],
}
RES = 1025
M_PER_DEG_LAT = 110_574.0
ROAD_STYLE = {  # width px in preview, colour
    "trunk": (5, (255, 210, 60)), "primary": (4, (255, 170, 60)), "secondary": (3, (255, 255, 255)),
    "tertiary": (2, (230, 230, 230)), "trunk_link": (3, (255, 210, 60)), "primary_link": (3, (255, 170, 60)),
}


UNITY_ROAD_TYPES = {"trunk", "primary", "secondary", "tertiary", "unclassified", "residential",
                    "trunk_link", "primary_link", "secondary_link", "tertiary_link", "living_street"}
TYPE_HEIGHTS = {"apartments": 15.0, "commercial": 12.0, "office": 15.0, "hotel": 18.0, "retail": 6.0,
                "government": 12.0, "school": 6.0, "university": 9.0, "hospital": 9.0, "church": 10.0,
                "industrial": 8.0, "warehouse": 7.0, "house": 4.0, "residential": 4.5, "roof": 3.0}


def default_height(b):
    # Most OSM buildings in Kigali have no height tag; vary 1-2 storeys deterministically by id.
    return TYPE_HEIGHTS.get(b["type"], 3.5 + (b["osm"] % 3) * 1.5)


def m_per_deg_lon(lat):
    return 111_320.0 * math.cos(math.radians(lat))


def load_dem(path):
    with tifffile.TiffFile(path) as tf:
        p = tf.pages[0]
        tp, sc = p.tags["ModelTiepointTag"].value, p.tags["ModelPixelScaleTag"].value
        return p.asarray().astype(np.float32), tp[3], tp[4], sc[0], sc[1]


def sample_bilinear(dem, x, y):
    x0 = np.clip(np.floor(x).astype(int), 0, dem.shape[1] - 2)
    y0 = np.clip(np.floor(y).astype(int), 0, dem.shape[0] - 2)
    fx, fy = x - x0, y - y0
    a, b = dem[y0, x0], dem[y0, x0 + 1]
    c, d = dem[y0 + 1, x0], dem[y0 + 1, x0 + 1]
    return (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy


def box_blur(a, r):
    if r <= 0:
        return a
    k = np.ones(2 * r + 1, np.float32) / (2 * r + 1)
    pad = np.pad(a, r, mode="edge")
    tmp = np.apply_along_axis(lambda v: np.convolve(v, k, mode="valid"), 1, pad)
    return np.apply_along_axis(lambda v: np.convolve(v, k, mode="valid"), 0, tmp)


def hillshade(h, cell, az=315, alt=45):
    gy, gx = np.gradient(h, cell)
    slope = np.arctan(np.hypot(gx, gy))
    aspect = np.arctan2(-gx, gy)
    az, alt = math.radians(az), math.radians(alt)
    hs = np.sin(alt) * np.cos(slope) + np.cos(alt) * np.sin(slope) * np.cos(az - aspect)
    return np.clip(hs, 0, 1)


def build(name, lat0, lon0, size, levels, dem_info, roads, buildings, out_root, places=()):
    dem, ox, oy, sx, sy = dem_info
    half_lat = (size / 2) / M_PER_DEG_LAT
    half_lon = (size / 2) / m_per_deg_lon(lat0)
    south, north, west, east = lat0 - half_lat, lat0 + half_lat, lon0 - half_lon, lon0 + half_lon

    # Smooth the DSM at source resolution (~31 m) to knock down rooftops and tree canopy.
    r0, r1 = int((oy - north) / sy) - 10, int((oy - south) / sy) + 10
    c0, c1 = int((west - ox) / sx) - 10, int((east - ox) / sx) + 10
    crop = box_blur(box_blur(dem[r0:r1, c0:c1], 1), 1)

    # Unity heightmap row 0 = south edge (z = 0).
    lats = np.linspace(south, north, RES)
    lons = np.linspace(west, east, RES)
    LON, LAT = np.meshgrid(lons, lats)
    px = (LON - ox) / sx - c0
    py = (oy - LAT) / sy - r0
    h = sample_bilinear(crop, px, py)
    hmin, hmax = float(h.min()), float(h.max())
    span = max(hmax - hmin, 1.0)

    out = os.path.join(out_root, name)
    os.makedirs(out, exist_ok=True)
    ((h - hmin) / span * 65535).round().astype("<u2").tofile(os.path.join(out, "heightmap_1025.raw"))

    def to_local(lon, lat):
        return (lon - west) * m_per_deg_lon(lat0), (lat - south) * M_PER_DEG_LAT

    def ground(x, z):
        i = min(max(int(round(z / size * (RES - 1))), 0), RES - 1)
        j = min(max(int(round(x / size * (RES - 1))), 0), RES - 1)
        return float(h[i, j])

    def in_box(lon, lat):
        return south <= lat <= north and west <= lon <= east

    local_roads = []
    for f in roads:
        cs = f["geometry"]["coordinates"]
        if not any(in_box(lon, lat) for lon, lat in cs):
            continue
        pts = []
        for lon, lat in cs:
            x, z = to_local(lon, lat)
            pts.append([round(x, 2), round(ground(x, z) - hmin, 2), round(z, 2)])
        local_roads.append({**f["properties"], "points": pts})

    local_buildings = []
    for f in buildings:
        ring = f["geometry"]["coordinates"][0]
        if not in_box(*ring[0]):
            continue
        p = f["properties"]
        height = None
        for key, mul in (("height", 1.0), ("levels", 3.2)):
            try:
                height = float(str(p.get(key)).split()[0]) * mul
                break
            except (TypeError, ValueError):
                pass
        pts = [[round(a, 2), round(b, 2)] for a, b in (to_local(lon, lat) for lon, lat in ring)]
        local_buildings.append({"osm": p["osm"], "type": p["building"], "name": p.get("name"),
                                "height_m": height, "footprint": pts})

    with open(os.path.join(out, "roads_local.json"), "w", encoding="utf8") as fo:
        json.dump(local_roads, fo)
    with open(os.path.join(out, "buildings_local.json"), "w", encoding="utf8") as fo:
        json.dump(local_buildings, fo)

    # Flat-array copies that Unity's JsonUtility can parse (no top-level or nested arrays).
    unity_roads = [{"highway": r["highway"], "name": r["name"] or "", "lanes": r["lanes"] or "",
                    "oneway": r["oneway"] or "", "pts": [c for p in r["points"] for c in p]}
                   for r in local_roads if r["highway"] in UNITY_ROAD_TYPES]
    unity_buildings = [{"h": round(b["height_m"] or default_height(b), 1),
                        "pts": [c for p in b["footprint"][:-1] for c in p]}
                       for b in local_buildings if len(b["footprint"]) >= 4]
    with open(os.path.join(out, "roads_unity.json"), "w", encoding="utf8") as fo:
        json.dump({"roads": unity_roads}, fo)

    # Named everyday places (optional input from extract_places.py): polygons, or a single point for nodes.
    unity_places = []
    for f in places:
        g = f["geometry"]
        ring = g["coordinates"][0] if g["type"] == "Polygon" else [g["coordinates"]]
        if not any(in_box(lon, lat) for lon, lat in ring):
            continue
        pts = [round(c, 2) for lon, lat in (ring[:-1] if len(ring) > 1 else ring) for c in to_local(lon, lat)]
        unity_places.append({"kind": f["properties"]["kind"], "name": f["properties"]["name"] or "", "pts": pts})
    with open(os.path.join(out, "places_unity.json"), "w", encoding="utf8") as fo:
        json.dump({"places": unity_places}, fo)
    with open(os.path.join(out, "buildings_unity.json"), "w", encoding="utf8") as fo:
        json.dump({"buildings": unity_buildings}, fo)
    meta = {
        "district": name, "levels": levels, "centre_lat": lat0, "centre_lon": lon0,
        "bounds": {"south": south, "north": north, "west": west, "east": east},
        "unity_terrain": {"heightmap_resolution": RES, "width_m": size, "length_m": size,
                          "height_m": round(span, 2), "byte_order": "Windows", "depth": "16 bit",
                          "flip_vertically": False},
        "elevation_min_m": round(hmin, 2), "elevation_max_m": round(hmax, 2),
        "metres_per_heightmap_sample": round(size / (RES - 1), 2),
        "counts": {"roads": len(local_roads), "buildings": len(local_buildings)},
        "hubs": [{"name": hn, "x": round(to_local(hlon, hlat)[0], 1), "z": round(to_local(hlon, hlat)[1], 1)}
                 for hn, hlat, hlon in HUBS.get(name, [(name, lat0, lon0)])],
        "sources": ["Copernicus DEM GLO-30 (ESA/EU; DLR e.V., Airbus) TODO: verify exact attribution text",
                    "© OpenStreetMap contributors (ODbL)"],
    }
    with open(os.path.join(out, "terrain.json"), "w", encoding="utf8") as fo:
        json.dump(meta, fo, indent=2)

    # Preview: north up, so flip rows (row 0 of h is south).
    cell = size / (RES - 1)
    hs = hillshade(h, cell)[::-1]
    elev = ((h - hmin) / span)[::-1]
    base = np.stack([60 + 90 * elev * hs + 60 * hs, 80 + 90 * elev * hs + 50 * hs, 50 + 40 * hs], -1)
    img = Image.fromarray(np.clip(base, 0, 255).astype(np.uint8), "RGB")
    d = ImageDraw.Draw(img, "RGBA")
    s = (RES - 1) / size

    def px_of(x, z):
        return x * s, (size - z) * s

    for b in local_buildings:
        d.polygon([px_of(x, z) for x, z in b["footprint"]], fill=(200, 120, 90, 110))
    for r in sorted(local_roads, key=lambda r: r["highway"] in ROAD_STYLE):
        w, col = ROAD_STYLE.get(r["highway"], (1, (170, 170, 170)))
        d.line([px_of(p[0], p[2]) for p in r["points"]], fill=col + (255,), width=w)
    cx, cz = to_local(lon0, lat0)
    X, Y = px_of(cx, cz)
    d.ellipse([X - 8, Y - 8, X + 8, Y + 8], outline=(255, 0, 0, 255), width=3)
    d.rectangle([0, 0, RES, 28], fill=(0, 0, 0, 170))
    d.text((8, 8), f"{name} ({levels})  {size/1000:.0f} km square  elev {hmin:.0f}-{hmax:.0f} m  "
                   f"N up  (c) OpenStreetMap contributors, Copernicus DEM", fill=(255, 255, 255, 255))
    img.save(os.path.join(out, "preview.png"))
    print(f"{name}: elev {hmin:.0f}-{hmax:.0f} m, roads {len(local_roads)}, buildings {len(local_buildings)}")


def main(gis, only=None):
    dem_info = load_dem(os.path.join(gis, "cop30_S02_00_E030_00.tif"))
    with open(os.path.join(gis, "kigali_roads.geojson"), encoding="utf8") as f:
        roads = json.load(f)["features"]
    with open(os.path.join(gis, "kigali_buildings.geojson"), encoding="utf8") as f:
        buildings = json.load(f)["features"]
    places = []
    places_path = os.path.join(gis, "kigali_places.geojson")
    if os.path.exists(places_path):
        with open(places_path, encoding="utf8") as f:
            places = json.load(f)["features"]
    for name, (lat, lon, size, levels) in DISTRICTS.items():
        if only and name != only:
            continue
        build(name, lat, lon, size, levels, dem_info, roads, buildings, os.path.join(gis, "districts"), places)


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2] if len(sys.argv) > 2 else None)
