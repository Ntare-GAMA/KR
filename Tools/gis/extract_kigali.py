"""Extracts Kigali roads, building footprints and named landmarks from a Geofabrik .osm.pbf.

Usage: python extract_kigali.py <rwanda.osm.pbf> <out_dir>
Data © OpenStreetMap contributors, ODbL.
"""
import json
import os
import re
import sys

import osmium

S, W, N, E = -2.05, 29.95, -1.85, 30.25  # Kigali, generous

LANDMARK_PATTERNS = {
    "convention_centre": re.compile(r"convention cent", re.I),
    "amahoro_stadium": re.compile(r"amahoro", re.I),
    "arena": re.compile(r"\barena\b", re.I),
    "nyabugogo": re.compile(r"nyabugogo", re.I),
    "city_tower": re.compile(r"city tower", re.I),
}

ROAD_TYPES = {"motorway", "trunk", "primary", "secondary", "tertiary", "unclassified",
              "residential", "trunk_link", "primary_link", "secondary_link", "tertiary_link",
              "service", "living_street"}


def inside(lat, lon):
    return S <= lat <= N and W <= lon <= E


class Handler(osmium.SimpleHandler):
    def __init__(self):
        super().__init__()
        self.roads, self.buildings, self.landmarks = [], [], {k: [] for k in LANDMARK_PATTERNS}

    def _landmark(self, kind, oid, tags, lat, lon):
        name = tags.get("name")
        if not name:
            return
        for key, pat in LANDMARK_PATTERNS.items():
            if pat.search(name):
                self.landmarks[key].append({"osm": f"{kind}/{oid}", "name": name, "lat": lat, "lon": lon,
                                            "tags": {k: tags[k] for k in ("amenity", "leisure", "building", "highway", "place", "tourism") if k in tags}})

    def node(self, n):
        if n.location.valid() and inside(n.location.lat, n.location.lon) and "name" in n.tags:
            self._landmark("node", n.id, dict(n.tags), n.location.lat, n.location.lon)

    def way(self, w):
        try:
            coords = [(nd.lon, nd.lat) for nd in w.nodes]
        except osmium.InvalidLocationError:
            return
        if not coords or not any(inside(lat, lon) for lon, lat in coords):
            return
        tags = dict(w.tags)
        hw = tags.get("highway")
        if hw in ROAD_TYPES:
            self.roads.append({"type": "Feature",
                               "properties": {"osm": w.id, "highway": hw, "name": tags.get("name"),
                                              "ref": tags.get("ref"), "lanes": tags.get("lanes"),
                                              "oneway": tags.get("oneway"), "surface": tags.get("surface")},
                               "geometry": {"type": "LineString", "coordinates": coords}})
        if "building" in tags and len(coords) >= 4 and coords[0] == coords[-1]:
            self.buildings.append({"type": "Feature",
                                   "properties": {"osm": w.id, "building": tags["building"],
                                                  "levels": tags.get("building:levels"), "height": tags.get("height"),
                                                  "name": tags.get("name")},
                                   "geometry": {"type": "Polygon", "coordinates": [coords]}})
        if "name" in tags and len(coords) >= 2:
            lon = sum(c[0] for c in coords) / len(coords)
            lat = sum(c[1] for c in coords) / len(coords)
            self._landmark("way", w.id, tags, lat, lon)


def main(pbf, out_dir):
    os.makedirs(out_dir, exist_ok=True)
    h = Handler()
    h.apply_file(pbf, locations=True, idx="flex_mem")
    with open(os.path.join(out_dir, "kigali_roads.geojson"), "w", encoding="utf8") as f:
        json.dump({"type": "FeatureCollection", "features": h.roads}, f)
    with open(os.path.join(out_dir, "kigali_buildings.geojson"), "w", encoding="utf8") as f:
        json.dump({"type": "FeatureCollection", "features": h.buildings}, f)
    with open(os.path.join(out_dir, "landmarks.json"), "w", encoding="utf8") as f:
        json.dump(h.landmarks, f, indent=2, ensure_ascii=False)
    print(f"roads: {len(h.roads)}  buildings: {len(h.buildings)}")
    for k, items in h.landmarks.items():
        print(f"== {k}")
        for i in items[:10]:
            print(f"   {i['osm']}  {i['name']}  {i['tags']}  {i['lat']:.5f}, {i['lon']:.5f}")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
