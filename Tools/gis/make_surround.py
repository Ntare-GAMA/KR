"""Real-elevation land around a district, so the map has no edge.

Usage: python make_surround.py <gis_dir> [district]       (default district: kimironko_kibagabaga)
Run after make_district_terrain.py. Reads every cop30_*.tif in <gis_dir> (Copernicus DEM GLO-30 tiles,
1x1 degree, named by their south-west corner: S03_00_E030_00 covers lat -3..-2, lon 30..31).

Writes <gis_dir>/districts/<district>/:
  surround_<n>.raw   16-bit little-endian, row 0 = south, a 3x3 block of district-sized tiles centred on the
                     district (the centre tile is the district itself, used only to match the seams)
  surround.json      resolution, size, origin and elevation range of the raw file

Samples sit on the same 7.8 m grid as the district heightmap and use the same smoothing, so the shared edges match.
Areas no tile covers are filled from the nearest covered sample.
"""
import glob
import json
import math
import os
import re
import sys
import warnings

import numpy as np
import tifffile

sys.path.insert(0, os.path.dirname(__file__))
from make_district_terrain import M_PER_DEG_LAT, RES, box_blur, m_per_deg_lon, sample_bilinear  # noqa: E402

ARCSEC = 1.0 / 3600.0


def load_tiles(gis):
    tiles = []
    for path in glob.glob(os.path.join(gis, "cop30_*.tif")):
        m = re.search(r"([NS])(\d+)_00_([EW])(\d+)_00", os.path.basename(path))
        if not m:
            continue
        with tifffile.TiffFile(path) as tf:
            p = tf.pages[0]
            tp, sc = p.tags["ModelTiepointTag"].value, p.tags["ModelPixelScaleTag"].value
            tiles.append((p.asarray().astype(np.float32), tp[3], tp[4], sc[0], sc[1]))
        print(f"tile {os.path.basename(path)}")
    return tiles


def mosaic(tiles, south, north, west, east):
    """1-arcsecond grid covering the box (row 0 = north), NaN where no tile has data."""
    rows = int(math.ceil((north - south) / ARCSEC)) + 1
    cols = int(math.ceil((east - west) / ARCSEC)) + 1
    out = np.full((rows, cols), np.nan, np.float32)
    lat = north - np.arange(rows) * ARCSEC
    lon = west + np.arange(cols) * ARCSEC
    for dem, ox, oy, sx, sy in tiles:
        r = np.round((oy - lat) / sy).astype(int)
        c = np.round((lon - ox) / sx).astype(int)
        rin = (r >= 0) & (r < dem.shape[0])
        cin = (c >= 0) & (c < dem.shape[1])
        if rin.any() and cin.any():
            block = dem[np.ix_(r[rin], c[cin])]
            out[np.ix_(np.where(rin)[0], np.where(cin)[0])] = block
    missing = np.isnan(out)
    if missing.any():
        print(f"no DEM for {missing.mean() * 100:.1f}% of the surround; filling from the nearest data")
        fill_nearest(out)
    return out, north, west


def fill_nearest(a):
    # Grow the known area one ring at a time (only the far corners are ever missing).
    while np.isnan(a).any():
        pad = np.pad(a, 1, mode="edge")
        n = np.stack([pad[:-2, 1:-1], pad[2:, 1:-1], pad[1:-1, :-2], pad[1:-1, 2:]])
        with warnings.catch_warnings():
            warnings.simplefilter("ignore")
            grown = np.nanmean(n, axis=0)
        holes = np.isnan(a) & ~np.isnan(grown)
        if not holes.any():
            break
        a[holes] = grown[holes]


def main(gis, district):
    out = os.path.join(gis, "districts", district)
    with open(os.path.join(out, "terrain.json"), encoding="utf8") as f:
        meta = json.load(f)
    lat0, lon0 = meta["centre_lat"], meta["centre_lon"]
    size = meta["unity_terrain"]["width_m"]
    b = meta["bounds"]

    n = (RES - 1) * 3 + 1                      # three district tiles per side, shared edges
    span_m = size * 3
    dlat = span_m / M_PER_DEG_LAT
    dlon = span_m / m_per_deg_lon(lat0)
    south, west = b["south"] - size / M_PER_DEG_LAT, b["west"] - size / m_per_deg_lon(lat0)
    north, east = south + dlat, west + dlon

    margin = 12 * ARCSEC
    grid, gn, gw = mosaic(load_tiles(gis), south - margin, north + margin, west - margin, east + margin)
    grid = box_blur(box_blur(grid, 1), 1)      # same DSM smoothing as the district heightmap

    lats = np.linspace(south, north, n)
    lons = np.linspace(west, east, n)
    LON, LAT = np.meshgrid(lons, lats)
    h = sample_bilinear(grid, (LON - gw) / ARCSEC, (gn - LAT) / ARCSEC)
    hmin, hmax = float(h.min()), float(h.max())
    span = max(hmax - hmin, 1.0)
    ((h - hmin) / span * 65535).round().astype("<u2").tofile(os.path.join(out, f"surround_{n}.raw"))

    info = {
        "resolution": n, "size_m": span_m, "origin_x": -size, "origin_z": -size,
        "elevation_min_m": round(hmin, 3), "elevation_span_m": round(span, 3),
        "district_elevation_min_m": meta["elevation_min_m"],
        "sources": ["Copernicus DEM GLO-30 (ESA/EU; DLR e.V., Airbus) TODO: verify exact attribution text"],
    }
    with open(os.path.join(out, "surround.json"), "w", encoding="utf8") as fo:
        json.dump(info, fo, indent=2)

    # Seam check: the centre tile should reproduce the district heightmap.
    d = np.fromfile(os.path.join(out, f"heightmap_{RES}.raw"), "<u2").reshape(RES, RES).astype(np.float32)
    d = d / 65535 * meta["unity_terrain"]["height_m"] + meta["elevation_min_m"]
    c = h[RES - 1:2 * RES - 1, RES - 1:2 * RES - 1]
    edge = np.concatenate([c[0] - d[0], c[-1] - d[-1], c[:, 0] - d[:, 0], c[:, -1] - d[:, -1]])
    print(f"{district}: surround {span_m / 1000:.0f} km square, elev {hmin:.0f}-{hmax:.0f} m; "
          f"seam difference to the district edge: mean {np.abs(edge).mean():.2f} m, max {np.abs(edge).max():.2f} m")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2] if len(sys.argv) > 2 else "kimironko_kibagabaga")
