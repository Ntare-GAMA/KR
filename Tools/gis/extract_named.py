"""Extracts every named everyday place in one district (shops, cafes, restaurants, pharmacies, banks, clinics, hotels,
salons, fuel stations, schools, churches, named buildings, bus stops) plus house numbers, in Unity coordinates.

Usage: python extract_named.py <rwanda.osm.pbf> <district_dir>
Reads <district_dir>/terrain.json for the bounds; writes <district_dir>/named_unity.json.
Data (c) OpenStreetMap contributors, ODbL. Names are exactly as mapped in OSM; nothing is invented.
Excluded on purpose: police, government, military, diplomatic and public-administration sites, and MTN-branded
places (no MTN branding in the game).
"""
import json
import math
import os
import sys

import osmium

M_PER_DEG_LAT = 110_574.0

# (key, value) -> (kind, group). Group decides the sign colour, the staff and the conversation in the game.
KINDS = {
    ("amenity", "cafe"): ("cafe", "food"),
    ("amenity", "restaurant"): ("restaurant", "food"),
    ("amenity", "fast_food"): ("fast_food", "food"),
    ("amenity", "bar"): ("bar", "food"),
    ("amenity", "pub"): ("bar", "food"),
    ("amenity", "internet_cafe"): ("internet_cafe", "service"),
    ("shop", "bakery"): ("bakery", "food"),
    ("shop", "dairy"): ("dairy", "food"),
    ("shop", "supermarket"): ("supermarket", "shop"),
    ("shop", "convenience"): ("shop", "shop"),
    ("shop", "kiosk"): ("shop", "shop"),
    ("shop", "clothes"): ("clothes", "shop"),
    ("shop", "shoes"): ("clothes", "shop"),
    ("shop", "hardware"): ("hardware", "shop"),
    ("shop", "electronics"): ("electronics", "shop"),
    ("shop", "mobile_phone"): ("electronics", "shop"),
    ("shop", "books"): ("books", "shop"),
    ("shop", "gift"): ("shop", "shop"),
    ("shop", "toys"): ("shop", "shop"),
    ("shop", "agrarian"): ("shop", "shop"),
    ("shop", "mall"): ("mall", "shop"),
    ("shop", "hairdresser"): ("salon", "service"),
    ("shop", "beauty"): ("salon", "service"),
    ("shop", "travel_agency"): ("office", "service"),
    ("shop", "car"): ("garage", "service"),
    ("shop", "car_repair"): ("garage", "service"),
    ("shop", "motorcycle"): ("garage", "service"),
    ("amenity", "car_wash"): ("car_wash", "service"),
    ("amenity", "car_rental"): ("office", "service"),
    ("amenity", "fuel"): ("fuel", "service"),
    ("amenity", "pharmacy"): ("pharmacy", "health"),
    ("amenity", "clinic"): ("clinic", "health"),
    ("amenity", "doctors"): ("clinic", "health"),
    ("amenity", "hospital"): ("hospital", "health"),
    ("healthcare", "laboratory"): ("clinic", "health"),
    ("amenity", "veterinary"): ("clinic", "health"),
    ("amenity", "bank"): ("bank", "finance"),
    ("tourism", "hotel"): ("hotel", "lodging"),
    ("tourism", "guest_house"): ("hotel", "lodging"),
    ("tourism", "motel"): ("hotel", "lodging"),
    ("tourism", "hostel"): ("hotel", "lodging"),
    ("tourism", "apartment"): ("hotel", "lodging"),
    ("amenity", "school"): ("school", "education"),
    ("amenity", "university"): ("university", "education"),
    ("amenity", "childcare"): ("school", "education"),
    ("office", "educational_institution"): ("school", "education"),
    ("amenity", "place_of_worship"): ("worship", "worship"),
    ("amenity", "marketplace"): ("market", "shop"),
    ("leisure", "fitness_centre"): ("gym", "leisure"),
    ("leisure", "swimming_pool"): ("pool", "leisure"),
    ("leisure", "sports_centre"): ("sports", "leisure"),
    ("leisure", "stadium"): ("sports", "leisure"),
    ("leisure", "park"): ("park", "leisure"),
    ("leisure", "garden"): ("park", "leisure"),
    ("leisure", "playground"): ("park", "leisure"),
    ("amenity", "bus_station"): ("bus_station", "transport"),
    ("highway", "bus_stop"): ("bus_stop", "transport"),
    ("office", "company"): ("office", "office"),
    ("office", "ngo"): ("office", "office"),
    ("office", "telecommunication"): ("office", "office"),
    ("building", "office"): ("office", "office"),
    ("building", "commercial"): ("office", "office"),
}
EXCLUDE_TAGS = {("amenity", "police"), ("office", "government"), ("office", "diplomatic"), ("amenity", "embassy"),
                ("amenity", "public_building"), ("amenity", "townhall"), ("amenity", "courthouse"), ("amenity", "prison"),
                ("landuse", "military"), ("building", "public"), ("building", "government"), ("building", "military")}
EXCLUDE_KEYS = {"military", "government", "diplomatic", "police"}
# Words that mark a public-administration or security site even when the tags do not.
EXCLUDE_WORDS = ("police", "military", "army", "ministry", "embassy", "umurenge", "cell office", "sector office",
                 "district office", "monitoring office", "commission", "gendarmerie", "prison", "monuc", "farg",
                 "cnlg", "rwanda education board", "zigama")
BRANDS_EXCLUDED = ("mtn",)


def excluded(tags, name):
    for k, v in tags.items():
        if (k, v) in EXCLUDE_TAGS or k in EXCLUDE_KEYS:
            return True
    low = name.lower()
    if any(w in low for w in EXCLUDE_WORDS):
        return True
    return any(low == b or low.startswith(b + " ") or (" " + b + " ") in f" {low} " for b in BRANDS_EXCLUDED)


def classify(tags):
    for (k, v), kg in KINDS.items():
        if tags.get(k) == v:
            return kg
    if "building" in tags:
        return ("building", "building")
    return None


def main(pbf, district_dir):
    meta = json.load(open(os.path.join(district_dir, "terrain.json"), encoding="utf8"))
    b = meta["bounds"]
    south, north, west, east = b["south"], b["north"], b["west"], b["east"]
    lat0 = meta["centre_lat"]
    mlon = 111_320.0 * math.cos(math.radians(lat0))

    def local(lon, lat):
        return round((lon - west) * mlon, 2), round((lat - south) * M_PER_DEG_LAT, 2)

    def inside(lon, lat):
        return south <= lat <= north and west <= lon <= east

    named, numbers = [], []

    def add(tags, osm, lon, lat, footprint=None):
        if not inside(lon, lat):
            return
        name = (tags.get("name") or tags.get("name:en") or "").strip()
        street = tags.get("addr:street") or ""
        number = tags.get("addr:housenumber") or ""
        x, z = local(lon, lat)
        if name:
            if excluded(tags, name):
                return
            kg = classify(tags)
            if kg is None:
                return
            rec = {"osm": osm, "kind": kg[0], "group": kg[1], "name": name, "street": street, "number": number,
                   "x": x, "z": z}
            if footprint:
                rec["footprint"] = footprint
            named.append(rec)
        elif number[:1].isdigit() and len(number) <= 8 and "building" in tags and not excluded(tags, ""):
            numbers.append({"osm": osm, "street": street, "number": number, "x": x, "z": z})

    class Handler(osmium.SimpleHandler):
        def node(self, n):
            if n.location.valid() and ("name" in n.tags or "addr:housenumber" in n.tags):
                add(dict(n.tags), f"node/{n.id}", n.location.lon, n.location.lat)

        def way(self, w):
            if "name" not in w.tags and "addr:housenumber" not in w.tags:
                return
            tags = dict(w.tags)
            if classify(tags) is None and "addr:housenumber" not in tags:
                return
            if "highway" in tags and tags.get("highway") != "bus_stop":
                return
            try:
                coords = [(nd.lon, nd.lat) for nd in w.nodes]
            except osmium.InvalidLocationError:
                return
            if len(coords) < 3:
                return
            ring = coords[:-1] if coords[0] == coords[-1] else coords
            lon = sum(c[0] for c in ring) / len(ring)
            lat = sum(c[1] for c in ring) / len(ring)
            fp = [c for lo, la in ring for c in local(lo, la)]
            add(tags, f"way/{w.id}", lon, lat, fp)

    Handler().apply_file(pbf, locations=True)
    # A node and a building can both carry the same name; keep one (the building, for its footprint).
    seen, unique = {}, []
    for r in sorted(named, key=lambda r: "footprint" not in r):
        key = (r["name"].lower(), round(r["x"] / 40), round(r["z"] / 40))
        if key in seen:
            continue
        seen[key] = True
        unique.append(r)
    out = os.path.join(district_dir, "named_unity.json")
    with open(out, "w", encoding="utf8") as fo:
        json.dump({"source": "(c) OpenStreetMap contributors, ODbL", "named": unique, "numbers": numbers}, fo,
                  ensure_ascii=False)
    print(f"{len(unique)} named places, {len(numbers)} house numbers -> {out}")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
