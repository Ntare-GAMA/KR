using System.Collections.Generic;
using System.Linq;
using KR.Editor.GIS;
using KR.Editor.Util;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Category = KR.Editor.Kigali.ScatterCollector.Category;

namespace KR.Editor.Kigali
{
    // African Leadership University, Kigali campus (Kigali Innovation City), built on the real OSM site and footprints.
    //
    // Design reference (owner photos + published descriptions, MASS Design Group, 2021):
    //  - one terraced building cascading down the hillside as modular blocks of 2-3 storeys; the floor of one
    //    plaza is the roof of the classroom below (Livinspaces, 8 Jul 2021);
    //  - pale tan clay brick over a concrete frame, open-brickwork screens for ventilation (Architectural Record, 4 May 2021);
    //  - large round columns/pods forming the rounded corners of blocks and terraces, round towers above the roofs;
    //  - thin overhanging slab bands at each floor, tall narrow blue-tinted windows in groups;
    //  - brick-paved courtyards with palms, wide brick stairs with black vertical-bar railings, white roof pergolas;
    //  - a perforated-brick pavilion on brick piers, a round stormwater pond downhill (~1,800 m3).
    // Geometry is generated from those rules, not copied from drawings: room layout and interiors are not modelled.
    // Brick: Poly Haven brown_brick_02 (CC0). Everything else is procedural.
    internal static class AluCampus
    {
        public const string PlaceName = "African Leadership University";
        const float Storey = 3.6f, ModuleSize = 16f, CornerRadius = 2.6f;

        public sealed class Site
        {
            public Vector2[] Poly;
            public float[] Pts;
            public Vector3 Centre;
        }

        public sealed class Context
        {
            public Terrain Terrain;
            public Transform Root;
            public string OutDir;
            public PcShowcaseBuilder.Catalog Cat;
            public ScatterCollector Scatter;
            public System.Random Rng;
            public DistrictImporter.Road[] Roads;
        }

        public static Site Find(PcCityLife.Place[] places)
        {
            var p = places?.FirstOrDefault(x => x.name == PlaceName && x.pts != null && x.pts.Length >= 6);
            return p == null ? null : new Site { Poly = PcCityLife.Polygon(p.pts), Pts = p.pts, Centre = PcCityLife.Centroid(p.pts) };
        }

        // Keeps generic buildings, trees and street props off the campus before the city is scattered.
        public static void Reserve(Site site, PcCityLife.Result life, PcCityLife.Occupancy grid)
        {
            life.Exclusions.Add((site.Poly, 0f, Vector3.zero));
            Bounds2(site.Poly, out var min, out var max);
            for (float z = min.y; z <= max.y; z += 2f)
                for (float x = min.x; x <= max.x; x += 2f)
                    if (PcCityLife.Inside(site.Poly, new Vector2(x, z)))
                        grid.Mark(new Vector3(x, 0f, z), 1.2f);
        }

        // ---------- materials ----------

        sealed class Mats
        {
            public Material Brick, Pavers, Cornice, Roof, Glass, Frame, Screen, Railing, Pergola, Water, Earth, Door, Sign, Planting;
        }

        static Mats MakeMaterials(string outDir)
        {
            var m = new Mats
            {
                Brick = PcMaterials.Photo(outDir, "brown_brick_02", 2.2f, 0.12f, new Color(1.12f, 1.08f, 1.2f)),
                Cornice = EditorAssetUtil.LitMaterial(outDir, "ALU_Cornice", new Color(0.84f, 0.74f, 0.63f), 0.25f),
                Roof = EditorAssetUtil.LitMaterial(outDir, "ALU_Roof", new Color(0.8f, 0.76f, 0.7f), 0.2f),
                Glass = EditorAssetUtil.LitMaterial(outDir, "ALU_Glass", new Color(0.16f, 0.29f, 0.45f), 0.93f),
                Frame = EditorAssetUtil.LitMaterial(outDir, "ALU_Frame", new Color(0.11f, 0.12f, 0.13f), 0.4f),
                Pergola = EditorAssetUtil.LitMaterial(outDir, "ALU_Pergola", new Color(0.93f, 0.93f, 0.91f), 0.35f),
                Water = EditorAssetUtil.LitMaterial(outDir, "ALU_PondWater", new Color(0.36f, 0.42f, 0.4f), 0.95f),
                Earth = PcMaterials.Photo(outDir, "red_laterite_soil_stones", 3f, 0.05f),
                Door = EditorAssetUtil.LitMaterial(outDir, "ALU_Door", new Color(0.16f, 0.2f, 0.27f), 0.45f),
                Sign = EditorAssetUtil.LitMaterial(outDir, "ALU_Sign", new Color(0.95f, 0.95f, 0.95f), 0.4f),
                Planting = PcMaterials.Photo(outDir, "leafy_grass", 2f, 0.05f),
            };
            // Same brick, laid flat and smaller, for the courtyard and terrace pavers.
            var brickTex = m.Brick.GetTexture("_BaseMap") as Texture2D;
            m.Pavers = EditorAssetUtil.LitMaterial(outDir, "ALU_Pavers", new Color(0.97f, 0.9f, 0.84f), 0.15f, brickTex, 1.4f);
            CopyNormal(m.Brick, m.Pavers);
            m.Glass.SetFloat("_Metallic", 0.35f);

            // Open brickwork: staggered square voids in brick-coloured masonry (alpha clipped, both sides).
            var screenTex = EditorAssetUtil.GeneratedTexture($"{outDir}/Textures/T_ALU_Screen.png", 256, (x, y) =>
            {
                int row = y / 32, col = (x + (row % 2) * 16) / 32;
                bool hole = (x + (row % 2) * 16) % 32 is >= 9 and < 25 && y % 32 is >= 9 and < 25;
                float shade = 0.92f + 0.08f * Mathf.PerlinNoise(col * 0.37f, row * 0.53f);
                return hole ? new Color(0f, 0f, 0f, 0f) : new Color(0.74f * shade, 0.58f * shade, 0.45f * shade, 1f);
            });
            m.Screen = Cutout(EditorAssetUtil.LitMaterial(outDir, "ALU_BrickScreen", Color.white, 0.1f, screenTex, 1.6f));
            // Black steel railing: top rail, bottom rail, vertical bars every 12 cm (texture spans 1.1 m).
            var railTex = EditorAssetUtil.GeneratedTexture($"{outDir}/Textures/T_ALU_Railing.png", 128, (x, y) =>
            {
                bool rail = y >= 120 || y is >= 6 and < 11 || x % 14 < 2;
                return rail ? new Color(0.08f, 0.08f, 0.09f, 1f) : new Color(0f, 0f, 0f, 0f);
            });
            m.Railing = Cutout(EditorAssetUtil.LitMaterial(outDir, "ALU_Railing", Color.white, 0.45f, railTex, 1.1f));
            return m;
        }

        static void CopyNormal(Material from, Material to)
        {
            var n = from.GetTexture("_BumpMap");
            if (n == null)
                return;
            to.SetTexture("_BumpMap", n);
            to.SetTextureScale("_BumpMap", to.GetTextureScale("_BaseMap"));
            to.EnableKeyword("_NORMALMAP");
        }

        static Material Cutout(Material mat)
        {
            mat.SetFloat("_AlphaClip", 1f);
            mat.SetFloat("_Cutoff", 0.5f);
            mat.EnableKeyword("_ALPHATEST_ON");
            mat.SetFloat("_Cull", 0f);
            mat.doubleSidedGI = true;
            mat.renderQueue = (int)RenderQueue.AlphaTest;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // ---------- build ----------

        sealed class Cell
        {
            public Vector3 Centre;
            public float Yaw, W, D;
            public bool Block, Pavilion;
            public float Base, Roof;
            public int Storeys;
        }

        public static void Build(Context c, Site site, DistrictImporter.Building[] buildings)
        {
            var mats = MakeMaterials(c.OutDir);
            var geo = new Geo();
            var deco = new Geo();      // no colliders: railings, glass, pergolas, screens, signs
            var root = new GameObject($"{PlaceName} (campus)").transform;
            root.SetParent(c.Root, false);

            var footprints = buildings.Where(b => b.pts.Length >= 6 && PcCityLife.Inside(site.Poly, Flat(PcCityLife.Centroid(b.pts))))
                                      .OrderByDescending(b => PcCityLife.Area(b.pts)).ToList();
            int blocks = 0, courts = 0;
            bool pavilionDone = false;
            Cell signTower = null;
            foreach (var b in footprints)
            {
                if (!PcCityLife.OrientedBox(b.pts, c.Terrain, out var centre, out float yaw, out float w, out float d, out _))
                    continue;
                if (PcCityLife.Area(b.pts) < 800f)
                {
                    SmallBlock(c, geo, deco, mats, centre, yaw, w * 0.95f, d * 0.95f);
                    blocks++;
                    continue;
                }
                var rot = Quaternion.Euler(0f, yaw, 0f);
                int nu = Mathf.Max(2, Mathf.RoundToInt(w / ModuleSize)), nv = Mathf.Max(2, Mathf.RoundToInt(d / ModuleSize));
                float cu = w / nu, cv = d / nv;
                var cells = new Cell[nu, nv];
                // Uphill blocks are taller; the cascade comes from the ground falling away under the grid.
                float gMin = float.MaxValue, gMax = float.MinValue;
                for (int i = 0; i < nu; i++)
                    for (int j = 0; j < nv; j++)
                    {
                        var p = centre + rot * new Vector3((i + 0.5f) * cu - w * 0.5f, 0f, (j + 0.5f) * cv - d * 0.5f);
                        var cell = new Cell { Centre = p, Yaw = yaw, W = cu, D = cv };
                        GroundRange(c.Terrain, p, yaw, cu, cv, out float lo, out float hi);
                        cell.Base = hi;
                        cells[i, j] = cell;
                        gMin = Mathf.Min(gMin, hi);
                        gMax = Mathf.Max(gMax, hi);
                    }
                for (int i = 0; i < nu; i++)
                    for (int j = 0; j < nv; j++)
                    {
                        var cell = cells[i, j];
                        bool interior = i > 0 && j > 0 && i < nu - 1 && j < nv - 1;
                        bool court = (i + 2 * j) % 3 == 1 && (interior || c.Rng.NextDouble() < 0.45);
                        cell.Block = !court;
                        float uphill = gMax - gMin < 0.5f ? 0.5f : (cell.Base - gMin) / (gMax - gMin);
                        cell.Storeys = uphill > 0.55f ? (c.Rng.NextDouble() < 0.6 ? 3 : 2) : 2;
                        cell.Roof = cell.Base + cell.Storeys * Storey;
                    }
                // The perforated-brick pavilion takes the highest block cell of the largest footprint.
                if (!pavilionDone)
                {
                    Cell best = null;
                    foreach (var cell in cells)
                        if (cell.Block && (best == null || cell.Base > best.Base))
                            best = cell;
                    if (best != null)
                    {
                        best.Pavilion = true;
                        best.Storeys = 2;
                        best.Roof = best.Base + 2 * Storey;
                        pavilionDone = true;
                    }
                }
                for (int i = 0; i < nu; i++)
                    for (int j = 0; j < nv; j++)
                    {
                        var cell = cells[i, j];
                        if (!cell.Block)
                        {
                            Courtyard(c, geo, deco, mats, cell);
                            courts++;
                            continue;
                        }
                        // Neighbour roof heights hide windows on shared walls.
                        float Cover(int ii, int jj) => ii < 0 || jj < 0 || ii >= nu || jj >= nv || !cells[ii, jj].Block ? float.MinValue : cells[ii, jj].Roof;
                        var cover = new[] { Cover(i + 1, j), Cover(i, j + 1), Cover(i - 1, j), Cover(i, j - 1) };
                        if (cell.Pavilion)
                            Pavilion(c, geo, deco, mats, cell);
                        else
                            Block(c, geo, deco, mats, cell, cover);
                        if (!cell.Pavilion && signTower == null && cell.Storeys == 3)
                            signTower = cell;
                        blocks++;
                    }
            }
            if (signTower != null)
                Tower(c, geo, deco, mats, signTower, root, sign: true);

            Pond(c, geo, deco, mats, site, footprints);
            Fence(c, geo, deco, mats, site, root);

            var store = new PcCityLife.MeshStore($"{c.OutDir}/ALU_Campus_Meshes.asset");
            geo.Emit("Campus (brick, terraces, stairs)", root, store, collider: true);
            deco.Emit("Campus details (glass, railings, screens, pergolas)", root, store, collider: false);
            Debug.Log($"[ALU] Campus built from {footprints.Count} OSM footprints: {blocks} blocks, {courts} courtyards, " +
                      $"pavilion {(pavilionDone ? "yes" : "no")}, sign tower {(signTower != null ? "yes" : "no")}.");
        }

        // Rounded-corner brick block: plinth down to the lowest ground, floor bands, window groups, roof, railings,
        // sometimes a pergola and a round tower.
        static void Block(Context c, Geo g, Geo deco, Mats m, Cell cell, float[] cover)
        {
            float hx = cell.W * 0.5f - 0.15f, hz = cell.D * 0.5f - 0.15f;
            GroundRange(c.Terrain, cell.Centre, cell.Yaw, cell.W, cell.D, out float lo, out _);
            var ring = RoundedRect(cell.Centre, cell.Yaw, hx, hz, CornerRadius);
            g.Prism(m.Brick, null, ring, lo - 0.4f, cell.Roof);
            for (int k = 1; k <= cell.Storeys; k++)
            {
                float y = cell.Base + k * Storey;
                var band = RoundedRect(cell.Centre, cell.Yaw, hx + 0.3f, hz + 0.3f, CornerRadius + 0.3f);
                g.Prism(m.Cornice, k == cell.Storeys ? m.Roof : m.Cornice, band, y - 0.32f, y, bottom: true);
            }
            var rot = Quaternion.Euler(0f, cell.Yaw, 0f);
            for (int side = 0; side < 4; side++)
            {
                // side 0 = +x, 1 = +z, 2 = -x, 3 = -z (local)
                Vector3 n = rot * Side(side);
                Vector3 t = rot * Side((side + 1) % 4);
                float half = side % 2 == 0 ? hx : hz, run = (side % 2 == 0 ? hz : hx) - CornerRadius;
                var mid = cell.Centre + n * half;
                for (int k = 0; k < cell.Storeys; k++)
                {
                    float floor = cell.Base + k * Storey;
                    if (floor + 0.7f < cover[side])
                        continue;
                    WindowGroup(c, deco, m, mid, n, t, run, floor, ground: k == 0);
                }
                // Lower storeys exposed on the downhill side (the plinth) get windows too, so the cascade reads.
                for (float floor = cell.Base - Storey; floor > lo + 0.5f; floor -= Storey)
                {
                    float groundHere = Ground(c.Terrain, mid);
                    if (groundHere < floor && floor + 0.7f >= cover[side])
                        WindowGroup(c, deco, m, mid, n, t, run, floor, ground: false);
                }
            }
            // Roof terraces: railing round the edge, a pergola on some.
            var roofRing = RoundedRect(cell.Centre, cell.Yaw, hx - 0.1f, hz - 0.1f, CornerRadius - 0.1f);
            if (c.Rng.NextDouble() < 0.6)
                RailingLoop(deco, m, roofRing, cell.Roof);
            if (c.Rng.NextDouble() < 0.3)
                Pergola(deco, m, cell.Centre + Vector3.up * cell.Roof, cell.Yaw, hx * 0.9f, hz * 0.55f);
            if (c.Rng.NextDouble() < 0.35)
                Tower(c, g, deco, m, cell, null, sign: false);
        }

        static void WindowGroup(Context c, Geo deco, Mats m, Vector3 mid, Vector3 n, Vector3 t, float run, float floor, bool ground)
        {
            float span = run * 2f - 0.6f;
            if (span < 1.5f)
                return;
            if (ground && c.Rng.NextDouble() < 0.35)
            {
                // Full-height glazing between brick piers (entrances, commons).
                float width = Mathf.Min(span, 7.5f);
                Window(deco, m, mid, n, t, -width * 0.5f, width * 0.5f, floor + 0.15f, floor + 3.05f);
                return;
            }
            int count = Mathf.Clamp(Mathf.FloorToInt(span / 1.35f), 1, 8);
            if (count > 4 && c.Rng.NextDouble() < 0.4)
                count = 2 + c.Rng.Next(3);
            float pitch = 1.35f, start = -(count - 1) * pitch * 0.5f;
            for (int i = 0; i < count; i++)
            {
                float u = start + i * pitch;
                Window(deco, m, mid, n, t, u - 0.45f, u + 0.45f, floor + 0.6f, floor + 3.0f);
            }
        }

        static void Window(Geo deco, Mats m, Vector3 mid, Vector3 n, Vector3 t, float u0, float u1, float y0, float y1)
        {
            Vector3 P(float u, float y, float off) { var p = mid + t * u + n * off; p.y = y; return p; }
            deco.Quad(m.Frame, P(u0 - 0.08f, y0 - 0.08f, 0.03f), P(u1 + 0.08f, y0 - 0.08f, 0.03f), P(u1 + 0.08f, y1 + 0.08f, 0.03f), P(u0 - 0.08f, y1 + 0.08f, 0.03f));
            deco.Quad(m.Glass, P(u0, y0, 0.05f), P(u1, y0, 0.05f), P(u1, y1, 0.05f), P(u0, y1, 0.05f));
            // Mullion splitting tall panes, as in the photos.
            if (u1 - u0 > 2.5f)
                for (float u = u0 + 1.5f; u < u1 - 0.5f; u += 1.5f)
                    deco.Quad(m.Frame, P(u - 0.04f, y0, 0.06f), P(u + 0.04f, y0, 0.06f), P(u + 0.04f, y1, 0.06f), P(u - 0.04f, y1, 0.06f));
            else
                deco.Quad(m.Frame, P(u0, (y0 + y1) * 0.55f - 0.03f, 0.06f), P(u1, (y0 + y1) * 0.55f - 0.03f, 0.06f),
                          P(u1, (y0 + y1) * 0.55f + 0.03f, 0.06f), P(u0, (y0 + y1) * 0.55f + 0.03f, 0.06f));
        }

        // Round brick tower on a block corner, one storey above the roof, with a flat overhanging cap.
        static void Tower(Context c, Geo g, Geo deco, Mats m, Cell cell, Transform root, bool sign)
        {
            float hx = cell.W * 0.5f - 0.15f, hz = cell.D * 0.5f - 0.15f;
            var rot = Quaternion.Euler(0f, cell.Yaw, 0f);
            float sx = sign ? 1f : (c.Rng.NextDouble() < 0.5 ? 1f : -1f), sz = sign ? -1f : (c.Rng.NextDouble() < 0.5 ? 1f : -1f);
            var axis = cell.Centre + rot * new Vector3(sx * (hx - CornerRadius), 0f, sz * (hz - CornerRadius));
            GroundRange(c.Terrain, cell.Centre, cell.Yaw, cell.W, cell.D, out float lo, out _);
            float top = cell.Roof + Storey * 0.95f;
            g.Cylinder(m.Brick, null, axis, CornerRadius + 0.05f, lo - 0.4f, top, 24);
            g.Cylinder(m.Cornice, m.Roof, axis, CornerRadius + 0.4f, top, top + 0.35f, 24, bottom: true);
            // Slot windows facing out.
            var outward = rot * new Vector3(sx, 0f, sz).normalized;
            for (int k = 0; k < 2; k++)
            {
                var dir = Quaternion.Euler(0f, (k == 0 ? -22f : 22f), 0f) * outward;
                var tan = Vector3.Cross(dir, Vector3.up);   // so the pane faces out (normal = dir)
                var mid = axis + dir * (CornerRadius + 0.02f);
                Window(deco, m, mid, dir, tan, -0.3f, 0.3f, cell.Roof + 0.6f, cell.Roof + 2.6f);
            }
            if (!sign || root == null)
                return;
            // Vertical "ALU" sign plate on the tower, as on the campus.
            var face = rot * new Vector3(0f, 0f, sz);
            var plateCentre = axis + face * (CornerRadius + 0.12f);
            plateCentre.y = cell.Roof - 2.4f;
            var tanF = Vector3.Cross(Vector3.up, face);
            Vector3 Pp(float u, float v) => plateCentre + tanF * u + Vector3.up * v;
            deco.Quad(m.Sign, Pp(-0.45f, -2f), Pp(0.45f, -2f), Pp(0.45f, 2f), Pp(-0.45f, 2f), flip: true);
            var text = new GameObject("ALU sign");
            text.transform.SetParent(root, false);
            text.transform.position = plateCentre + face * 0.03f;
            text.transform.rotation = Quaternion.LookRotation(-face, Vector3.up);
            var tm = text.AddComponent<TextMesh>();
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            tm.font = font;
            tm.text = "A\nL\nU";
            tm.fontSize = 96;
            tm.characterSize = 0.115f;
            tm.lineSpacing = 0.9f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = new Color(0.1f, 0.2f, 0.42f);
            text.GetComponent<MeshRenderer>().sharedMaterial = font.material;
        }

        // Perforated-brick pavilion lifted on brick piers (ground floor open, upper storey screened).
        static void Pavilion(Context c, Geo g, Geo deco, Mats m, Cell cell)
        {
            float hx = cell.W * 0.5f - 0.15f, hz = cell.D * 0.5f - 0.15f;
            var rot = Quaternion.Euler(0f, cell.Yaw, 0f);
            GroundRange(c.Terrain, cell.Centre, cell.Yaw, cell.W, cell.D, out float lo, out _);
            // Paved plinth.
            g.Prism(m.Brick, m.Pavers, RoundedRect(cell.Centre, cell.Yaw, hx + 2f, hz + 2f, CornerRadius + 2f), lo - 0.4f, cell.Base + 0.15f);
            float first = cell.Base + Storey, top = cell.Roof;
            // Piers round the edge.
            for (int side = 0; side < 4; side++)
            {
                Vector3 n = rot * Side(side), t = rot * Side((side + 1) % 4);
                float half = side % 2 == 0 ? hx : hz, run = (side % 2 == 0 ? hz : hx) - 1f;
                for (float u = -run; u <= run + 0.01f; u += Mathf.Max(3.2f, run * 2f / Mathf.Max(1, Mathf.Round(run * 2f / 3.6f))))
                {
                    var p = cell.Centre + n * (half - 0.6f) + t * u;
                    g.Box(m.Brick, new Vector3(p.x, (cell.Base + first) * 0.5f, p.z), new Vector3(0.75f, first - cell.Base, 0.75f), rot);
                }
            }
            // Inner glazed core on the ground floor.
            var core = RoundedRect(cell.Centre, cell.Yaw, hx * 0.45f, hz * 0.45f, 1.5f);
            deco.Prism(m.Glass, null, core, cell.Base + 0.15f, first);
            // Upper storey: solid floor slab, perforated brick skin, inner glass, roof.
            g.Prism(m.Cornice, m.Pavers, RoundedRect(cell.Centre, cell.Yaw, hx, hz, CornerRadius), first - 0.4f, first, bottom: true);
            deco.Prism(m.Screen, null, RoundedRect(cell.Centre, cell.Yaw, hx, hz, CornerRadius), first, top - 0.3f, twoSided: true);
            deco.Prism(m.Glass, null, RoundedRect(cell.Centre, cell.Yaw, hx - 1.4f, hz - 1.4f, 1.4f), first, top - 0.3f);
            g.Prism(m.Cornice, m.Roof, RoundedRect(cell.Centre, cell.Yaw, hx + 0.2f, hz + 0.2f, CornerRadius + 0.2f), top - 0.3f, top + 0.05f, bottom: true);
        }

        // Brick-paved courtyard: two levels joined by wide brick stairs with cheek walls and railings; palms, benches, a lamp.
        static void Courtyard(Context c, Geo g, Geo deco, Mats m, Cell cell)
        {
            var rot = Quaternion.Euler(0f, cell.Yaw, 0f);
            Vector3 ax = rot * Vector3.right, az = rot * Vector3.forward;
            float hx = cell.W * 0.5f, hz = cell.D * 0.5f;
            float gx = Ground(c.Terrain, cell.Centre + ax * hx) - Ground(c.Terrain, cell.Centre - ax * hx);
            float gz = Ground(c.Terrain, cell.Centre + az * hz) - Ground(c.Terrain, cell.Centre - az * hz);
            // Stairs climb along "up" (towards higher ground).
            Vector3 up = Mathf.Abs(gx) > Mathf.Abs(gz) ? ax * Mathf.Sign(gx) : az * Mathf.Sign(gz);
            Vector3 across = Vector3.Cross(Vector3.up, up);
            float halfLen = Mathf.Abs(gx) > Mathf.Abs(gz) ? hx : hz, halfWid = Mathf.Abs(gx) > Mathf.Abs(gz) ? hz : hx;
            GroundRange(c.Terrain, cell.Centre, cell.Yaw, cell.W, cell.D, out float lo, out float hi);
            float low = Ground(c.Terrain, cell.Centre - up * halfLen * 0.6f) + 0.15f;
            float high = Mathf.Max(low, Ground(c.Terrain, cell.Centre + up * halfLen * 0.6f) + 0.15f);
            float rise = high - low;
            var yawRot = Quaternion.LookRotation(up, Vector3.up);
            if (rise < 0.5f)
            {
                float y = (low + high) * 0.5f;
                g.Box(m.Pavers, cell.Centre + Vector3.up * ((y + lo - 0.6f) * 0.5f), new Vector3(halfWid * 2f, y - (lo - 0.6f), halfLen * 2f), yawRot);
                Dress(c, cell.Centre, up, across, halfLen, halfWid, y);
                return;
            }
            int steps = Mathf.CeilToInt(rise / 0.17f);
            float stairLen = Mathf.Min(halfLen * 1.4f, steps * 0.36f), tread = stairLen / steps;
            float lowerLen = halfLen - stairLen * 0.5f;                 // each landing
            float bottom = lo - 0.6f;
            var lowerC = cell.Centre - up * (halfLen - lowerLen * 0.5f);
            var upperC = cell.Centre + up * (halfLen - lowerLen * 0.5f);
            g.Box(m.Pavers, Mid(lowerC, bottom, low), new Vector3(halfWid * 2f, low - bottom, lowerLen), yawRot);
            g.Box(m.Pavers, Mid(upperC, bottom, high), new Vector3(halfWid * 2f, high - bottom, lowerLen), yawRot);
            float stairWid = Mathf.Min(halfWid * 2f - 2f, 9f);
            for (int s = 0; s < steps; s++)
            {
                var sc = cell.Centre - up * (stairLen * 0.5f) + up * (tread * (s + 0.5f));
                float top = low + rise * (s + 1) / steps;
                g.Box(m.Pavers, Mid(sc, bottom, top), new Vector3(stairWid, top - bottom, tread), yawRot);
            }
            // Planted terraces either side of the stairs (stepped beds, as in the courtyard photo).
            float bedWid = (halfWid * 2f - stairWid) * 0.5f;
            for (int sideSign = -1; sideSign <= 1; sideSign += 2)
            {
                var bc = cell.Centre + across * sideSign * (stairWid * 0.5f + bedWid * 0.5f);
                g.Box(m.Brick, Mid(bc - up * stairLen * 0.25f, bottom, low + rise * 0.5f), new Vector3(bedWid, low + rise * 0.5f - bottom, stairLen * 0.5f), yawRot);
                g.Box(m.Brick, Mid(bc + up * stairLen * 0.25f, bottom, high), new Vector3(bedWid, high - bottom, stairLen * 0.5f), yawRot);
                g.Box(m.Planting, Mid(bc - up * stairLen * 0.25f, low + rise * 0.5f, low + rise * 0.5f + 0.05f), new Vector3(bedWid - 0.5f, 0.05f, stairLen * 0.5f - 0.5f), yawRot);
                // Railings along the stair edges and the upper landing edge.
                var a = cell.Centre + across * sideSign * stairWid * 0.5f - up * stairLen * 0.5f;
                var bpt = cell.Centre + across * sideSign * stairWid * 0.5f + up * stairLen * 0.5f;
                deco.Quad(m.Railing, At(a, low), At(bpt, high), At(bpt, high + 1.1f), At(a, low + 1.1f), twoSided: true, localUv: true);
            }
            var edgeA = cell.Centre + up * (stairLen * 0.5f) - across * halfWid;
            var edgeB = cell.Centre + up * (stairLen * 0.5f) - across * (stairWid * 0.5f);
            deco.Quad(m.Railing, At(edgeA, high), At(edgeB, high), At(edgeB, high + 1.1f), At(edgeA, high + 1.1f), twoSided: true, localUv: true);
            Dress(c, lowerC, up, across, lowerLen * 0.5f, halfWid, low);
            Dress(c, upperC, up, across, lowerLen * 0.5f, halfWid, high);
        }

        static void Dress(Context c, Vector3 centre, Vector3 up, Vector3 across, float halfLen, float halfWid, float y)
        {
            var cat = c.Cat;
            float yaw = Quaternion.LookRotation(up).eulerAngles.y;
            if (cat.Palms != null && cat.Palms.Length > 0)
                for (int i = 0; i < 2; i++)
                {
                    var palm = cat.Palms[c.Rng.Next(cat.Palms.Length)];
                    var p = centre + across * ((i == 0 ? -1f : 1f) * (halfWid - 1.6f)) + up * R(c.Rng, -halfLen * 0.4f, halfLen * 0.4f);
                    p.y = y;
                    c.Scatter.Add(palm, c.Scatter.Fit(palm, p, R(c.Rng, 0f, 360f), R(c.Rng, 7f, 11f), 3f), Category.Tree);
                }
            if (cat.Benches != null && cat.Benches.Length > 0)
                for (int i = 0; i < 2; i++)
                {
                    var bench = cat.Benches[c.Rng.Next(cat.Benches.Length)];
                    var p = centre + across * ((i == 0 ? -1f : 1f) * (halfWid * 0.45f)) - up * (halfLen * 0.5f);
                    p.y = y;
                    c.Scatter.Add(bench, c.Scatter.Fit(bench, p, yaw + 90f, 0.9f, 2.2f), Category.Prop);
                }
            if (cat.Lamps != null && cat.Lamps.Length > 0)
            {
                var lamp = cat.Lamps[0];
                var p = centre + across * (halfWid - 0.8f);
                p.y = y;
                c.Scatter.Add(lamp, c.Scatter.Fit(lamp, p, yaw, 4.2f, 1.2f), Category.Prop);
            }
        }

        // One-storey service building (the small OSM footprints): brick, rounded corners, two dark doors.
        static void SmallBlock(Context c, Geo g, Geo deco, Mats m, Vector3 centre, float yaw, float w, float d)
        {
            GroundRange(c.Terrain, centre, yaw, w, d, out float lo, out float hi);
            float hx = w * 0.5f, hz = d * 0.5f, r = Mathf.Min(1.2f, Mathf.Min(hx, hz) * 0.4f), top = hi + 3.4f;
            g.Prism(m.Brick, null, RoundedRect(centre, yaw, hx, hz, r), lo - 0.4f, top);
            g.Prism(m.Cornice, m.Roof, RoundedRect(centre, yaw, hx + 0.25f, hz + 0.25f, r + 0.25f), top - 0.3f, top + 0.05f, bottom: true);
            var rot = Quaternion.Euler(0f, yaw, 0f);
            Vector3 n = rot * Vector3.back, t = rot * Vector3.left;
            var mid = centre + n * hz;
            if (hx > 2.5f)
                for (int i = -1; i <= 1; i += 2)
                {
                    float u = i * Mathf.Min(hx * 0.4f, 2.2f);
                    Vector3 P(float du, float y) { var p = mid + t * (u + du) + n * 0.04f; p.y = y; return p; }
                    deco.Quad(m.Door, P(-0.85f, hi), P(0.85f, hi), P(0.85f, hi + 2.6f), P(-0.85f, hi + 2.6f), flip: true);
                }
        }

        static void Pergola(Geo deco, Mats m, Vector3 centre, float yaw, float hx, float hz)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            float h = 2.7f;
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    var p = centre + rot * new Vector3(sx * (hx - 0.2f), 0f, sz * (hz - 0.2f));
                    deco.Box(m.Pergola, p + Vector3.up * (h * 0.5f), new Vector3(0.18f, h, 0.18f), rot);
                }
            for (int sz = -1; sz <= 1; sz += 2)
                deco.Box(m.Pergola, centre + rot * new Vector3(0f, h + 0.1f, sz * (hz - 0.2f)), new Vector3(hx * 2f, 0.22f, 0.2f), rot);
            for (float x = -hx + 0.3f; x <= hx - 0.3f; x += 0.55f)
                deco.Box(m.Pergola, centre + rot * new Vector3(x, h + 0.3f, 0f), new Vector3(0.07f, 0.24f, hz * 2f + 0.4f), rot);
        }

        static void RailingLoop(Geo deco, Mats m, List<Vector3> ring, float y)
        {
            for (int i = 0; i < ring.Count; i++)
            {
                var a = ring[i];
                var b = ring[(i + 1) % ring.Count];
                deco.Quad(m.Railing, At(a, y), At(b, y), At(b, y + 1.1f), At(a, y + 1.1f), twoSided: true, localUv: true);
            }
        }

        // Round stormwater pond at the lowest open ground of the site, inside a raised earth embankment.
        static void Pond(Context c, Geo g, Geo deco, Mats m, Site site, List<DistrictImporter.Building> footprints)
        {
            Bounds2(site.Poly, out var min, out var max);
            Vector3? best = null;
            float bestY = float.MaxValue;
            for (float z = min.y + 18f; z <= max.y - 18f; z += 6f)
                for (float x = min.x + 18f; x <= max.x - 18f; x += 6f)
                {
                    var p2 = new Vector2(x, z);
                    if (!PcCityLife.Inside(site.Poly, p2))
                        continue;
                    if (footprints.Any(b => Vector2.Distance(Flat(PcCityLife.Centroid(b.pts)), p2) < Mathf.Sqrt(PcCityLife.Area(b.pts)) * 0.8f + 18f))
                        continue;
                    if (c.Roads != null && c.Roads.Any(r => NearRoad(r, p2, 28f)))
                        continue;
                    float y = Ground(c.Terrain, new Vector3(x, 0f, z));
                    if (y < bestY)
                    {
                        bestY = y;
                        best = new Vector3(x, 0f, z);
                    }
                }
            if (best == null)
                return;
            var centre = best.Value;
            float top = float.MinValue;
            for (int a = 0; a < 12; a++)
            {
                var p = centre + Quaternion.Euler(0f, a * 30f, 0f) * Vector3.forward * 14f;
                top = Mathf.Max(top, Ground(c.Terrain, p));
            }
            top += 0.5f;
            g.Cylinder(m.Earth, m.Earth, centre, 15f, bestY - 1.5f, top, 32);
            g.Cylinder(m.Water, m.Water, centre, 12.5f, top, top + 0.03f, 32);
        }

        // Perimeter fence (dark steel) along the OSM site boundary, with a gate on the side nearest a road.
        static void Fence(Context c, Geo g, Geo deco, Mats m, Site site, Transform root)
        {
            var poly = site.Poly;
            // Gate: the boundary point closest to any road vertex outside the site.
            Vector2 gate = poly[0];
            float bestD = float.MaxValue;
            if (c.Roads != null)
                foreach (var road in c.Roads)
                    for (int i = 0; i + 2 < road.pts.Length; i += 3)    // road points are (x, y, z) triples
                    {
                        var rp = new Vector2(road.pts[i], road.pts[i + 2]);
                        if (PcCityLife.Inside(poly, rp))
                            continue;
                        for (int k = 0; k < poly.Length; k++)
                        {
                            var q = ClosestOnSegment(poly[k], poly[(k + 1) % poly.Length], rp);
                            float dd = Vector2.Distance(q, rp);
                            if (dd < bestD)
                            {
                                bestD = dd;
                                gate = q;
                            }
                        }
                    }
            for (int k = 0; k < poly.Length; k++)
            {
                Vector2 a = poly[k], b = poly[(k + 1) % poly.Length];
                float len = Vector2.Distance(a, b);
                int pieces = Mathf.Max(1, Mathf.CeilToInt(len / 4f));
                for (int s = 0; s < pieces; s++)
                {
                    var p0 = Vector2.Lerp(a, b, (float)s / pieces);
                    var p1 = Vector2.Lerp(a, b, (float)(s + 1) / pieces);
                    if (Vector2.Distance((p0 + p1) * 0.5f, gate) < 7f)
                        continue;
                    var w0 = new Vector3(p0.x, 0f, p0.y);
                    var w1 = new Vector3(p1.x, 0f, p1.y);
                    float y0 = Ground(c.Terrain, w0), y1 = Ground(c.Terrain, w1);
                    deco.Quad(m.Railing, At(w0, y0), At(w1, y1), At(w1, y1 + 1.8f), At(w0, y0 + 1.8f), twoSided: true, localUv: true);
                }
            }
            // Gate pillars and name.
            var gw = new Vector3(gate.x, 0f, gate.y);
            Vector2 inward2 = (Flat(site.Centre) - gate).normalized;
            var inward = new Vector3(inward2.x, 0f, inward2.y);
            var along = Vector3.Cross(Vector3.up, inward);
            float gy = Ground(c.Terrain, gw);
            var rot = Quaternion.LookRotation(inward);
            for (int s = -1; s <= 1; s += 2)
            {
                var p = gw + along * (s * 6.5f);
                g.Box(m.Brick, new Vector3(p.x, gy + 1.2f, p.z), new Vector3(1.1f, 4.4f, 1.1f), rot);
                g.Box(m.Cornice, new Vector3(p.x, gy + 3.5f, p.z), new Vector3(1.4f, 0.25f, 1.4f), rot);
            }
            var wall = gw + along * 9.5f - inward * 0.2f;
            g.Box(m.Brick, new Vector3(wall.x, gy + 0.9f, wall.z), new Vector3(5f, 2.6f, 0.5f), rot);
            var text = new GameObject("Gate sign");
            text.transform.SetParent(root, false);
            text.transform.position = new Vector3(wall.x, gy + 1.6f, wall.z) - inward * 0.27f;
            text.transform.rotation = Quaternion.LookRotation(inward, Vector3.up);
            var tm = text.AddComponent<TextMesh>();
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            tm.font = font;
            tm.text = "AFRICAN LEADERSHIP\nUNIVERSITY";
            tm.fontSize = 64;
            tm.characterSize = 0.05f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = new Color(0.12f, 0.2f, 0.4f);
            text.GetComponent<MeshRenderer>().sharedMaterial = font.material;
        }

        // ---------- capture (preview renders for checking the model) ----------

        public static void Capture(string scenePath, string folder, int width = 1600, int height = 900)
        {
            System.IO.Directory.CreateDirectory(folder);
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            var campus = GameObject.Find($"{PlaceName} (campus)");
            if (campus == null)
            {
                Debug.LogError("[ALU] No campus in the scene.");
                return;
            }
            var bounds = new Bounds(campus.transform.position, Vector3.zero);
            bool first = true;
            // Frame the buildings (roof meshes), not the fence and pond at the site edge.
            foreach (var r in campus.GetComponentsInChildren<MeshRenderer>())
            {
                if (r.GetComponent<TextMesh>() != null || !r.sharedMaterial.name.Contains("Roof"))
                    continue;
                if (first) { bounds = r.bounds; first = false; }
                else bounds.Encapsulate(r.bounds);
            }
            var cam = Camera.main;
            cam.farClipPlane = Mathf.Max(cam.farClipPlane, 4000f);
            var centre = bounds.center;
            float size = Mathf.Max(bounds.size.x, bounds.size.z);
            var sign = GameObject.Find("ALU sign");
            var shots = new List<(string, Vector3, Vector3)>
            {
                ("1 Aerial NW", centre + new Vector3(-0.8f, 0.7f, 0.8f) * size, centre),
                ("2 Aerial SE", centre + new Vector3(0.8f, 0.6f, -0.8f) * size, centre),
                ("3 Overhead", centre + new Vector3(0.05f, 1.4f, -0.1f) * size, centre),
                ("4 Ground W", centre + new Vector3(-1.1f, 0.1f, 0.1f) * size, centre + Vector3.up * 4f),
                ("5 Ground E", centre + new Vector3(1.1f, 0.1f, -0.1f) * size, centre + Vector3.up * 4f),
            };
            if (sign != null)
                shots.Add(("6 Sign tower", sign.transform.position - sign.transform.forward * 30f + Vector3.up * 2f, sign.transform.position));
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGBHalf);
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            foreach (var (name, pos, look) in shots)
            {
                cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos));
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();
                System.IO.File.WriteAllBytes(System.IO.Path.Combine(folder, name + ".png"), tex.EncodeToPNG());
            }
            cam.targetTexture = null;
            RenderTexture.active = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
            Debug.Log($"[ALU] Captured {shots.Count} views to {folder}");
        }

        // ---------- helpers ----------

        static Vector3 Side(int i) => i switch { 0 => Vector3.right, 1 => Vector3.forward, 2 => Vector3.left, _ => Vector3.back };

        static Vector2 Flat(Vector3 p) => new Vector2(p.x, p.z);

        static Vector3 At(Vector3 p, float y) => new Vector3(p.x, y, p.z);

        static Vector3 Mid(Vector3 p, float y0, float y1) => new Vector3(p.x, (y0 + y1) * 0.5f, p.z);

        static float R(System.Random rng, float a, float b) => a + (float)rng.NextDouble() * (b - a);

        static float Ground(Terrain t, Vector3 p) => t.SampleHeight(p) + t.transform.position.y;

        static void GroundRange(Terrain t, Vector3 centre, float yaw, float w, float d, out float lo, out float hi)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            lo = float.MaxValue;
            hi = float.MinValue;
            for (int i = -1; i <= 1; i++)
                for (int j = -1; j <= 1; j++)
                {
                    float y = Ground(t, centre + rot * new Vector3(i * w * 0.5f, 0f, j * d * 0.5f));
                    lo = Mathf.Min(lo, y);
                    hi = Mathf.Max(hi, y);
                }
        }

        static void Bounds2(Vector2[] poly, out Vector2 min, out Vector2 max)
        {
            min = new Vector2(float.MaxValue, float.MaxValue);
            max = new Vector2(float.MinValue, float.MinValue);
            foreach (var p in poly)
            {
                min = Vector2.Min(min, p);
                max = Vector2.Max(max, p);
            }
        }

        static bool NearRoad(DistrictImporter.Road road, Vector2 p, float dist)
        {
            for (int i = 0; i + 5 < road.pts.Length; i += 3)    // road points are (x, y, z) triples
                if (Vector2.Distance(ClosestOnSegment(new Vector2(road.pts[i], road.pts[i + 2]), new Vector2(road.pts[i + 3], road.pts[i + 5]), p), p) < dist)
                    return true;
            return false;
        }

        static Vector2 ClosestOnSegment(Vector2 a, Vector2 b, Vector2 p)
        {
            var ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
            return a + ab * t;
        }

        // Rounded rectangle, counter-clockwise seen from above (x right, z up), y = 0.
        static List<Vector3> RoundedRect(Vector3 centre, float yaw, float hx, float hz, float r, int seg = 6)
        {
            r = Mathf.Clamp(r, 0.05f, Mathf.Min(hx, hz) - 0.05f);
            var rot = Quaternion.Euler(0f, yaw, 0f);
            var pts = new List<Vector3>();
            var corners = new[] { new Vector2(hx - r, hz - r), new Vector2(-(hx - r), hz - r), new Vector2(-(hx - r), -(hz - r)), new Vector2(hx - r, -(hz - r)) };
            for (int k = 0; k < 4; k++)
                for (int s = 0; s <= seg; s++)
                {
                    float a = (k * 90f + 90f * s / seg) * Mathf.Deg2Rad;
                    var local = new Vector3(corners[k].x + Mathf.Cos(a) * r, 0f, corners[k].y + Mathf.Sin(a) * r);
                    var p = centre + rot * local;
                    pts.Add(new Vector3(p.x, 0f, p.z));
                }
            // Keep CCW in world space whatever the yaw handedness.
            float area = 0f;
            for (int i = 0; i < pts.Count; i++)
            {
                var a = pts[i];
                var b = pts[(i + 1) % pts.Count];
                area += a.x * b.z - b.x * a.z;
            }
            if (area < 0f)
                pts.Reverse();
            return pts;
        }

        // Per-material mesh accumulator. Faces are clockwise seen from the front (Unity convention).
        sealed class Geo
        {
            sealed class Part
            {
                public readonly List<Vector3> V = new List<Vector3>(), N = new List<Vector3>();
                public readonly List<Vector2> UV = new List<Vector2>();
                public readonly List<int> T = new List<int>();
            }

            readonly Dictionary<Material, Part> _parts = new Dictionary<Material, Part>();

            Part Get(Material m)
            {
                if (!_parts.TryGetValue(m, out var p))
                    _parts[m] = p = new Part();
                return p;
            }

            static Vector2 WorldUv(Vector3 p, Vector3 n, Vector3 tangent)
            {
                if (Mathf.Abs(n.y) > 0.7f)
                    return new Vector2(p.x, p.z);
                var th = new Vector3(tangent.x, 0f, tangent.z);
                th = th.sqrMagnitude < 1e-6f ? Vector3.Cross(Vector3.up, n) : th.normalized;
                return new Vector2(Vector3.Dot(p, th), p.y);
            }

            // a = bottom-left, b = bottom-right, c = top-right, d = top-left, as seen from the front.
            // flip: the given order is as seen from the back (used for wall-attached details given outward normals).
            public void Quad(Material m, Vector3 a, Vector3 b, Vector3 c, Vector3 d, bool flip = false, bool twoSided = false, bool localUv = false)
            {
                if (flip)
                {
                    (a, b) = (b, a);
                    (c, d) = (d, c);
                }
                AddQuad(m, a, b, c, d, localUv);
                if (twoSided)
                    AddQuad(m, b, a, d, c, localUv);
            }

            void AddQuad(Material m, Vector3 a, Vector3 b, Vector3 c, Vector3 d, bool localUv)
            {
                var part = Get(m);
                var n = Vector3.Cross(d - a, b - a);
                if (n.sqrMagnitude < 1e-10f)
                    return;
                n.Normalize();
                int i0 = part.V.Count;
                var tangent = b - a;
                foreach (var p in new[] { a, b, c, d })
                {
                    part.V.Add(p);
                    part.N.Add(n);
                    if (localUv)
                    {
                        var t = tangent.normalized;
                        float u = Vector3.Dot(p - a, t);
                        part.UV.Add(new Vector2(u, p.y - Mathf.Lerp(a.y, b.y, Mathf.Clamp01(u / Mathf.Max(0.001f, tangent.magnitude)))));
                    }
                    else
                        part.UV.Add(WorldUv(p, n, tangent));
                }
                part.T.AddRange(new[] { i0, i0 + 3, i0 + 2, i0, i0 + 2, i0 + 1 });
            }

            void Tri(Material m, Vector3 a, Vector3 b, Vector3 c)
            {
                var part = Get(m);
                var n = Vector3.Cross(b - a, c - a);
                if (n.sqrMagnitude < 1e-12f)
                    return;
                n.Normalize();
                int i0 = part.V.Count;
                foreach (var p in new[] { a, b, c })
                {
                    part.V.Add(p);
                    part.N.Add(n);
                    part.UV.Add(WorldUv(p, n, b - a));
                }
                part.T.AddRange(new[] { i0, i0 + 1, i0 + 2 });
            }

            // Vertical extrusion of a CCW ring between y0 and y1; optional top cap and bottom cap.
            public void Prism(Material side, Material top, List<Vector3> ring, float y0, float y1, bool bottom = false, bool twoSided = false)
            {
                for (int i = 0; i < ring.Count; i++)
                {
                    var p = ring[i];
                    var q = ring[(i + 1) % ring.Count];
                    AddQuad(side, At(p, y0), At(q, y0), At(q, y1), At(p, y1), false);
                    if (twoSided)
                        AddQuad(side, At(q, y0), At(p, y0), At(p, y1), At(q, y1), false);
                }
                var o = Vector3.zero;
                foreach (var p in ring)
                    o += p;
                o /= ring.Count;
                if (top != null)
                    for (int i = 0; i < ring.Count; i++)
                        Tri(top, At(o, y1), At(ring[(i + 1) % ring.Count], y1), At(ring[i], y1));
                if (bottom)
                    for (int i = 0; i < ring.Count; i++)
                        Tri(top ?? side, At(o, y0), At(ring[i], y0), At(ring[(i + 1) % ring.Count], y0));
            }

            public void Cylinder(Material side, Material cap, Vector3 axis, float r, float y0, float y1, int seg, bool bottom = false)
            {
                var ring = new List<Vector3>();
                for (int s = 0; s < seg; s++)
                {
                    float a = s * Mathf.PI * 2f / seg;
                    ring.Add(new Vector3(axis.x + Mathf.Cos(a) * r, 0f, axis.z + Mathf.Sin(a) * r));
                }
                Prism(side, cap, ring, y0, y1, bottom);
            }

            public void Box(Material m, Vector3 centre, Vector3 size, Quaternion rot)
            {
                var h = size * 0.5f;
                var ring = new List<Vector3>
                {
                    centre + rot * new Vector3(h.x, 0f, h.z), centre + rot * new Vector3(-h.x, 0f, h.z),
                    centre + rot * new Vector3(-h.x, 0f, -h.z), centre + rot * new Vector3(h.x, 0f, -h.z),
                };
                float area = 0f;
                for (int i = 0; i < 4; i++)
                    area += ring[i].x * ring[(i + 1) % 4].z - ring[(i + 1) % 4].x * ring[i].z;
                if (area < 0f)
                    ring.Reverse();
                Prism(m, m, ring, centre.y - h.y, centre.y + h.y, bottom: true);
            }

            public void Emit(string name, Transform parent, PcCityLife.MeshStore store, bool collider)
            {
                var host = new GameObject(name);
                host.transform.SetParent(parent, false);
                host.isStatic = true;
                foreach (var kv in _parts)
                {
                    if (kv.Value.V.Count == 0)
                        continue;
                    var mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
                    mesh.SetVertices(kv.Value.V);
                    mesh.SetNormals(kv.Value.N);
                    mesh.SetUVs(0, kv.Value.UV);
                    mesh.SetTriangles(kv.Value.T, 0);
                    mesh.RecalculateBounds();
                    mesh.RecalculateTangents();
                    store.Add(mesh, $"ALU_{kv.Key.name}_{(collider ? "solid" : "detail")}");
                    var go = new GameObject(kv.Key.name);
                    go.transform.SetParent(host.transform, false);
                    go.isStatic = true;
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    go.AddComponent<MeshRenderer>().sharedMaterial = kv.Key;
                    if (collider)
                        go.AddComponent<MeshCollider>().sharedMesh = mesh;
                }
            }
        }
    }
}
