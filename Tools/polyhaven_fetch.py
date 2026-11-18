"""Downloads CC0 Poly Haven textures (1k diffuse + OpenGL normal) into the Unity project.

Usage: python polyhaven_fetch.py <unity_assets_dir> id [id ...]
Licence: CC0 (https://polyhaven.com/license). Recorded in Documentation/asset-register.csv.
"""
import os
import sys

import requests

API = "https://api.polyhaven.com/files/"
HEADERS = {"User-Agent": "KigaliRush-dev/0.1"}
MAPS = {"Diffuse": "diff", "nor_gl": "nor"}


def fetch(asset_id, out_root):
    files = requests.get(API + asset_id, headers=HEADERS, timeout=60).json()
    out = os.path.join(out_root, asset_id)
    os.makedirs(out, exist_ok=True)
    for key, suffix in MAPS.items():
        url = files[key]["1k"]["jpg"]["url"]
        path = os.path.join(out, f"{asset_id}_{suffix}.jpg")
        if not os.path.exists(path):
            data = requests.get(url, headers=HEADERS, timeout=120).content
            with open(path, "wb") as f:
                f.write(data)
        print(f"{asset_id}: {suffix} {os.path.getsize(path) // 1024} KB")


if __name__ == "__main__":
    root = os.path.join(sys.argv[1], "World", "PC", "PolyHaven")
    for asset in sys.argv[2:]:
        fetch(asset, root)
