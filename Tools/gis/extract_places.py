"""Extracts named everyday places (stadiums, pitches, parks, markets, bars, cafes, bus stations, hotels...) in Kigali.

Usage: python extract_places.py <rwanda.osm.pbf> <out_dir>
Writes kigali_places.geojson (polygons for ways, points for nodes). Data © OpenStreetMap contributors, ODbL.
Government, police, military and diplomatic sites are deliberately excluded.
"""
import json
import os
import sys

import osmium

S, W, N, E = -2.05, 29.95, -1.85, 30.25

KINDS = {
    ("leisure", "stadium"): "stadium",
    ("leisure", "sports_centre"): "sports_centre",
    ("leisure", "pitch"): "pitch",
    ("leisure", "park"): "park",
    ("leisure", "garden"): "park",
    ("amenity", "marketplace"): "market",
    ("amenity", "bus_station"): "bus_station",
    ("amenity", "conference_centre"): "conference_centre",
    ("amenity", "bar"): "bar",
    ("amenity", "pub"): "bar",
    ("amenity", "nightclub"): "nightclub",
    ("amenity", "cafe"): "cafe",
    ("amenity", "restaurant"): "restaurant",
    ("amenity", "place_of_worship"): "worship",
    ("amenity", "school"): "school",
    ("amenity", "university"): "university",
    ("amenity", "hospital"): "hospital",
    ("tourism", "hotel"): "hotel",
    ("tourism", "museum"): "museum",
    ("shop", "mall"): "mall",
    ("shop", "supermarket"): "supermarket",
}
EXCLUDE = {("amenity", "police"), ("office", "government"), ("military", None), ("landuse", "military"),
           ("office", "diplomatic"), ("amenity", "embassy"), ("government", None)}


def kind_of(tags):
    for k, v in tags.items():
        if (k, v) in EXCLUDE or (k, None) in EXCLUDE:
            return None
    for (k, v), kind in KINDS.items():
        if tags.get(k) == v:
            return kind
    return None


def inside(lat, lon):
    return S <= lat <= N and W <= lon <= E


class Handler(osmium.SimpleHandler):
    def __init__(self):
        super().__init__()
        self.features = []

    def node(self, n):
        tags = dict(n.tags)
        kind = kind_of(tags)
        if kind and n.location.valid() and inside(n.location.lat, n.location.lon):
            self.features.append({"type": "Feature",
                                  "properties": {"osm": f"node/{n.id}", "kind": kind, "name": tags.get("name")},
                                  "geometry": {"type": "Point", "coordinates": [n.location.lon, n.location.lat]}})

    def way(self, w):
        tags = dict(w.tags)
        kind = kind_of(tags)
        if not kind:
            return
        try:
            coords = [(nd.lon, nd.lat) for nd in w.nodes]
        except osmium.InvalidLocationError:
            return
        if len(coords) < 4 or coords[0] != coords[-1] or not any(inside(lat, lon) for lon, lat in coords):
            return
        self.features.append({"type": "Feature",
                              "properties": {"osm": f"way/{w.id}", "kind": kind, "name": tags.get("name")},
                              "geometry": {"type": "Polygon", "coordinates": [coords]}})


if __name__ == "__main__":
    h = Handler()
    h.apply_file(sys.argv[1], locations=True, idx="flex_mem")
    os.makedirs(sys.argv[2], exist_ok=True)
    with open(os.path.join(sys.argv[2], "kigali_places.geojson"), "w", encoding="utf8") as f:
        json.dump({"type": "FeatureCollection", "features": h.features}, f)
    counts = {}
    for feat in h.features:
        counts[feat["properties"]["kind"]] = counts.get(feat["properties"]["kind"], 0) + 1
    print(f"places: {len(h.features)}", dict(sorted(counts.items())))
