"""Looks up landmark coordinates in OpenStreetMap (Overpass API) so district bounds come from data, not memory."""
import json
import sys
import requests

OVERPASS_MIRRORS = [
    "https://overpass-api.de/api/interpreter",
    "https://overpass.kumi.systems/api/interpreter",
    "https://overpass.private.coffee/api/interpreter",
]
# Kigali bounding box (south, west, north, east), generous.
KIGALI_BBOX = (-2.05, 29.95, -1.85, 30.25)

QUERIES = {
    "convention_centre": r'nwr["name"~"Convention Cent", i]',
    "amahoro_stadium": r'nwr["name"~"Amahoro", i]["leisure"~"stadium|sports_centre"]',
    "arena": r'nwr["name"~"Arena", i]["leisure"~"stadium|sports_centre|sports_hall"]',
    "nyabugogo": r'nwr["name"~"Nyabugogo", i]',
    "city_tower": r'nwr["name"~"Kigali City Tower|City Tower", i]',
}


def overpass(query: str) -> dict:
    last = None
    for url in OVERPASS_MIRRORS:
        try:
            r = requests.post(url, data={"data": query}, timeout=120,
                              headers={"User-Agent": "KigaliRush-dev/0.1 (GIS prep)"})
            r.raise_for_status()
            return r.json()
        except (requests.RequestException, ValueError) as ex:
            last = ex
            print(f"   {url} failed: {ex}", file=sys.stderr)
    raise RuntimeError(f"All Overpass mirrors failed: {last}")


def run(selector: str):
    s, w, n, e = KIGALI_BBOX
    q = f'[out:json][timeout:60];{selector}({s},{w},{n},{e});out center tags;'
    data = overpass(q)
    out = []
    for el in data["elements"]:
        lat = el.get("lat") or el.get("center", {}).get("lat")
        lon = el.get("lon") or el.get("center", {}).get("lon")
        t = el.get("tags", {})
        out.append({"type": el["type"], "id": el["id"], "name": t.get("name"),
                    "kind": t.get("leisure") or t.get("amenity") or t.get("building") or t.get("highway") or t.get("place"),
                    "lat": lat, "lon": lon})
    return out


if __name__ == "__main__":
    result = {k: run(v) for k, v in QUERIES.items()}
    path = sys.argv[1] if len(sys.argv) > 1 else "landmarks.json"
    with open(path, "w", encoding="utf8") as f:
        json.dump(result, f, indent=2, ensure_ascii=False)
    for k, items in result.items():
        print(f"== {k}")
        for i in items[:12]:
            print(f"   {i['type']}/{i['id']}  {i['name']}  [{i['kind']}]  {i['lat']}, {i['lon']}")
