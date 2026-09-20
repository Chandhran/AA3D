using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Rhino.Geometry;

namespace AA3D
{
    // ═══════════════════════════════════════════════════════════════════════════
    //  GeometryBuilder — Translates the AA3D JSON model into RhinoCommon Breps.
    //
    //  Port of aa3d_geometry.py + opening_geometry.py.
    //  All dimensions in MILLIMETRES (matching the Python originals).
    // ═══════════════════════════════════════════════════════════════════════════

    public sealed class GeometryResult
    {
        public List<Brep>   Breps  { get; } = new List<Brep>();
        public List<string> Layers { get; } = new List<string>();
        public List<string> Names  { get; } = new List<string>();
        public List<string> Log    { get; } = new List<string>();

        public void Add(Brep b, string layer, string name)
        {
            if (b == null || !b.IsValid) return;
            Breps.Add(b);
            Layers.Add(layer);
            Names.Add(name);
        }
    }

    public static class GeometryBuilder
    {
        // ── Layer name constants ─────────────────────────────────────────────
        const string L_WALLS     = "AA3D_Walls";
        const string L_FLOORS    = "AA3D_Floors";
        const string L_COLUMNS   = "AA3D_Columns";
        const string L_ROOF      = "AA3D_Roof";
        const string L_STAIRS    = "AA3D_Stairs";
        const string L_FRAME     = "AA3D_Frame";
        const string L_GLASS     = "AA3D_Glass";
        const string L_SILL      = "AA3D_Sill";
        const string L_CASING    = "AA3D_Casing";
        const string L_DOOR_LEAF = "AA3D_DoorLeaf";
        const string L_HARDWARE  = "AA3D_Hardware";
        const string L_THRESHOLD = "AA3D_Threshold";

        // ── Entry point ──────────────────────────────────────────────────────

        /// <summary>
        /// Build RhinoCommon geometry from the AI-generated JSON model token.
        /// </summary>
        public static GeometryResult Build(JToken model)
        {
            var result = new GeometryResult();
            if (model == null) return result;

            // Top-level room / building parameters
            double roomW  = GetDouble(model, "room_width",   5000);
            double roomD  = GetDouble(model, "room_depth",   4000);
            double roomH  = GetDouble(model, "room_height",  2800);
            double wallT  = GetDouble(model, "wall_thickness", 200);
            double floorZ = GetDouble(model, "floor_level",    0);

            result.Log.Add($"Room: {roomW} × {roomD} × {roomH} mm, wall={wallT} mm, floor Z={floorZ}");

            // ── Floors ───────────────────────────────────────────────────────
            foreach (JToken fl in Arr(model["floors"]))
                BuildFloor(fl, roomW, roomD, wallT, result);

            // ── Walls ────────────────────────────────────────────────────────
            foreach (JToken w in Arr(model["walls"]))
                BuildWall(w, roomW, roomD, wallT, floorZ, roomH, result);

            // ── Windows ──────────────────────────────────────────────────────
            foreach (JToken win in Arr(model["windows"]))
                BuildWindow(win, roomW, roomD, wallT, floorZ, result);

            // ── Doors ────────────────────────────────────────────────────────
            foreach (JToken door in Arr(model["doors"]))
                BuildDoor(door, roomW, roomD, wallT, floorZ, result);

            // ── Columns ──────────────────────────────────────────────────────
            foreach (JToken col in Arr(model["columns"]))
                BuildColumn(col, floorZ, result);

            // ── Stairs ───────────────────────────────────────────────────────
            foreach (JToken st in Arr(model["stairs"]))
                BuildStair(st, result);

            // ── Roof ─────────────────────────────────────────────────────────
            JToken roof = model["roof"];
            if (roof != null && roof.Type != JTokenType.Null)
                BuildRoof(roof, roomW, roomD, roomH, floorZ, wallT, result);

            result.Log.Add($"Total geometry objects: {result.Breps.Count}");
            return result;
        }

        // ════════════════════════════════════════════════════════════════════
        //  FLOOR
        // ════════════════════════════════════════════════════════════════════
        static void BuildFloor(JToken fl, double roomW, double roomD, double wallT, GeometryResult res)
        {
            string id    = Str(fl["id"], "floor");
            double z     = GetDouble(fl, "z",         0);
            double thick = GetDouble(fl, "thickness", 200);
            double x0    = GetDouble(fl, "x",         0);
            double y0    = GetDouble(fl, "y",         0);
            double w     = GetDouble(fl, "width",     roomW);
            double d     = GetDouble(fl, "depth",     roomD);

            Brep b = BoxBrep(x0, y0, z - thick, w, d, thick);
            res.Add(b, L_FLOORS, "AA3D_" + id + "_FLOOR");
        }

        // ════════════════════════════════════════════════════════════════════
        //  WALL
        // ════════════════════════════════════════════════════════════════════
        static void BuildWall(JToken w, double roomW, double roomD,
                               double wallT, double floorZ, double roomH,
                               GeometryResult res)
        {
            string id   = Str(w["id"], "wall");
            string face = Str(w["face"], "south").ToLower();
            double z0   = GetDouble(w, "z_bottom", floorZ);
            double z1   = GetDouble(w, "z_top",    floorZ + roomH);

            double x0, y0, ww, wd;
            switch (face)
            {
                case "south":
                    x0 = 0;         y0 = 0;             ww = roomW; wd = wallT; break;
                case "north":
                    x0 = 0;         y0 = roomD - wallT; ww = roomW; wd = wallT; break;
                case "west":
                    x0 = 0;         y0 = 0;             ww = wallT; wd = roomD; break;
                case "east":
                    x0 = roomW - wallT; y0 = 0;         ww = wallT; wd = roomD; break;
                default:
                    // Interior wall: use explicit x/y/width/depth
                    x0 = GetDouble(w, "x", 0);
                    y0 = GetDouble(w, "y", 0);
                    ww = GetDouble(w, "width",  wallT);
                    wd = GetDouble(w, "depth",  2000);
                    break;
            }

            Brep b = BoxBrep(x0, y0, z0, ww, wd, z1 - z0);
            res.Add(b, L_WALLS, "AA3D_" + id + "_WALL");
        }

        // ════════════════════════════════════════════════════════════════════
        //  WINDOW — full port of opening_geometry.py _window()
        // ════════════════════════════════════════════════════════════════════
        static void BuildWindow(JToken o, double roomW, double roomD,
                                 double wallT, double floorZ, GeometryResult res)
        {
            string id    = Str(o["id"], "win");
            double fw    = GetDouble(o["frame"], "width", 60);
            string wall  = Str(o["wall"], "south").ToLower();
            double centerU = GetDouble(o, "center", 2000);
            double sill    = GetDouble(o, "sill",   800);
            double width   = GetDouble(o, "width",  1200);
            double height  = GetDouble(o, "height", 1200);

            var wo = new WallOpening(wall, roomW, roomD, wallT, centerU,
                                     floorZ + sill, width, height);

            double ou0 = centerU - width / 2.0;
            double ou1 = centerU + width / 2.0;
            double z0  = floorZ + sill;
            double z1  = z0 + height;

            // Frame — 4 members spanning full wall thickness
            AddBox(wo, ou0,        ou0 + fw,   z0,        z1,       wallT, 0,  L_FRAME,  "AA3D_" + id + "_FRAME_L",    res);
            AddBox(wo, ou1 - fw,   ou1,        z0,        z1,       wallT, 0,  L_FRAME,  "AA3D_" + id + "_FRAME_R",    res);
            AddBox(wo, ou0 + fw,   ou1 - fw,   z0,        z0 + fw,  wallT, 0,  L_FRAME,  "AA3D_" + id + "_FRAME_SILL", res);
            AddBox(wo, ou0 + fw,   ou1 - fw,   z1 - fw,   z1,       wallT, 0,  L_FRAME,  "AA3D_" + id + "_FRAME_HEAD", res);

            // Glass — 6mm thick at 10mm from interior face
            double cw = width  - 2 * fw;
            double ch = height - 2 * fw;
            if (cw > 0 && ch > 0)
            {
                string typ = OpeningLibrary.NormalizeWindowType(Str(o["type"], "FIXED"));
                OpeningLibrary.Windows.TryGetValue(typ, out OpeningDef def);
                int panels = (def != null && def.PanelOrLeafCount > 1)
                             ? def.PanelOrLeafCount : 1;
                if (typ == "SLIDING" && panels < 2) panels = 2;

                if (panels > 1)
                {
                    double pw = cw / panels;
                    for (int i = 0; i < panels; i++)
                    {
                        double pu0 = ou0 + fw + i * pw + 4;
                        double pu1 = ou0 + fw + (i + 1) * pw - 4;
                        AddBox(wo, pu0, pu1, z0 + fw, z1 - fw, 16.0, 10.0, L_GLASS,
                               $"AA3D_{id}_GLASS_{i + 1:D2}", res);
                    }
                }
                else
                {
                    AddBox(wo, ou0 + fw, ou1 - fw, z0 + fw, z1 - fw, 16.0, 10.0,
                           L_GLASS, "AA3D_" + id + "_GLASS", res);
                }
            }

            // Exterior sill — 40mm projection, 20mm thick, below z0
            AddBox(wo, ou0 - 30, ou1 + 30, z0 - 20, z0, wallT + 40.0, 0.0, L_SILL,
                   "AA3D_" + id + "_EXT_SILL", res);

            // Interior sill — 150mm into room
            AddBox(wo, ou0 - 10, ou1 + 10, z0 - 20, z0, 0.0, -150.0, L_SILL,
                   "AA3D_" + id + "_INT_SILL", res);

            // Interior casing — 25mm wide x 20mm thick, 5mm proud
            const double cw2 = 25.0, cd = 20.0;
            AddBox(wo, ou0 - cw2, ou0,       z0 - cw2,       z1 + cw2, -5.0, -5.0 - cd, L_CASING, "AA3D_" + id + "_CASING_L",    res);
            AddBox(wo, ou1,       ou1 + cw2, z0 - cw2,       z1 + cw2, -5.0, -5.0 - cd, L_CASING, "AA3D_" + id + "_CASING_R",    res);
            AddBox(wo, ou0 - cw2, ou1 + cw2, z1,             z1 + cw2, -5.0, -5.0 - cd, L_CASING, "AA3D_" + id + "_CASING_HEAD", res);
            AddBox(wo, ou0 - cw2, ou1 + cw2, z0 - 20 - cw2, z0 - 20,  -5.0, -5.0 - cd, L_CASING, "AA3D_" + id + "_CASING_SILL", res);
        }

        // ════════════════════════════════════════════════════════════════════
        //  DOOR — full port of opening_geometry.py _door()
        // ════════════════════════════════════════════════════════════════════
        static void BuildDoor(JToken o, double roomW, double roomD,
                               double wallT, double floorZ, GeometryResult res)
        {
            string id     = Str(o["id"], "door");
            double fw     = GetDouble(o["frame"], "width", 60);
            string wall   = Str(o["wall"], "south").ToLower();
            double centerU = GetDouble(o, "center",  1500);
            double sill    = GetDouble(o, "sill",    0);
            double width   = GetDouble(o, "width",   900);
            double height  = GetDouble(o, "height",  2100);

            var wo = new WallOpening(wall, roomW, roomD, wallT, centerU,
                                     floorZ + sill, width, height);

            double ou0 = centerU - width / 2.0;
            double ou1 = centerU + width / 2.0;
            double z0  = floorZ + sill;
            double z1  = z0 + height;

            // 3-member frame (no sill for doors)
            AddBox(wo, ou0,      ou0 + fw, z0, z1,       wallT, 0, L_FRAME, "AA3D_" + id + "_FRAME_L",    res);
            AddBox(wo, ou1 - fw, ou1,      z0, z1,       wallT, 0, L_FRAME, "AA3D_" + id + "_FRAME_R",    res);
            AddBox(wo, ou0 + fw, ou1 - fw, z1 - fw, z1, wallT, 0, L_FRAME, "AA3D_" + id + "_FRAME_HEAD", res);

            // Stop bead on interior face
            AddBox(wo, ou0 + fw,       ou0 + fw + 15, z0, z1 - fw,       15.0, 0.0,  L_FRAME, "AA3D_" + id + "_STOP_L",    res);
            AddBox(wo, ou1 - fw - 15,  ou1 - fw,      z0, z1 - fw,       15.0, 0.0,  L_FRAME, "AA3D_" + id + "_STOP_R",    res);
            AddBox(wo, ou0 + fw + 15,  ou1 - fw - 15, z1 - fw - 15, z1 - fw, 15.0, 0.0, L_FRAME, "AA3D_" + id + "_STOP_HEAD", res);

            // Threshold
            AddBox(wo, ou0 + fw, ou1 - fw, z0, z0 + 20, wallT, 0, L_THRESHOLD, "AA3D_" + id + "_THRESHOLD", res);

            // Leaf/leaves
            string typ  = OpeningLibrary.NormalizeDoorType(Str(o["type"], "SINGLE_HINGED"));
            int leafDef = (typ == "DOUBLE_HINGED" || typ == "FRENCH" || typ == "DUTCH") ? 2 : 1;
            int count   = Math.Max(1, Math.Min(4,
                          (int)GetDouble(o, "leaf_count", leafDef)));

            double clearW  = width - 2 * fw;
            double clearH  = height - fw;
            double leafW   = clearW / count;
            const double leafT = 40.0, gap = 3.0;

            for (int i = 0; i < count; i++)
            {
                double lu0 = ou0 + fw + i * leafW + gap;
                double lu1 = ou0 + fw + (i + 1) * leafW - gap;
                double lz0 = z0 + 20 + gap;
                double lz1 = z0 + clearH - gap;
                AddBox(wo, lu0, lu1, lz0, lz1, 15.0 + leafT, 15.0, L_DOOR_LEAF,
                       $"AA3D_{id}_LEAF_{i + 1:D2}", res);
            }

            // Hinges (3×, on left jamb face)
            double[] hingeZs = {
                z0 + 20 + 150,
                z0 + clearH / 2.0,
                z0 + clearH - 150
            };
            for (int j = 0; j < hingeZs.Length; j++)
            {
                double hz = hingeZs[j];
                AddBox(wo, ou0 + fw, ou0 + fw + 90, hz - 4, hz + 4,
                       15.0 + leafT, 15.0 - 4, L_HARDWARE, $"AA3D_{id}_HINGE_{j + 1:D2}", res);
            }

            // Handle — right/latch side at 1000mm AFF
            double aff = z0 + 1000.0;
            double hu0 = ou1 - fw - 75;
            double hu1 = hu0 + 20;
            AddBox(wo, hu0, hu1, aff - 45, aff + 45, 15.0 - 15, 15.0 - 95,
                   L_HARDWARE, "AA3D_" + id + "_HANDLE", res);

            // Interior casing
            const double cw2 = 25.0, cd = 20.0;
            AddBox(wo, ou0 - cw2, ou0,       z0, z1 + cw2, -5.0, -5.0 - cd, L_CASING, "AA3D_" + id + "_CASING_L",    res);
            AddBox(wo, ou1,       ou1 + cw2, z0, z1 + cw2, -5.0, -5.0 - cd, L_CASING, "AA3D_" + id + "_CASING_R",    res);
            AddBox(wo, ou0 - cw2, ou1 + cw2, z1, z1 + cw2, -5.0, -5.0 - cd, L_CASING, "AA3D_" + id + "_CASING_HEAD", res);
        }

        // ════════════════════════════════════════════════════════════════════
        //  COLUMN
        // ════════════════════════════════════════════════════════════════════
        static void BuildColumn(JToken col, double floorZ, GeometryResult res)
        {
            string id   = Str(col["id"], "col");
            double x    = GetDouble(col, "x",      0);
            double y    = GetDouble(col, "y",      0);
            double z0   = GetDouble(col, "z",      floorZ);
            double h    = GetDouble(col, "height", 3000);
            double w    = GetDouble(col, "width",  300);
            double d    = GetDouble(col, "depth",  300);

            // Centered on (x, y)
            Brep b = BoxBrep(x - w / 2, y - d / 2, z0, w, d, h);
            res.Add(b, L_COLUMNS, "AA3D_" + id + "_COL");
        }

        // ════════════════════════════════════════════════════════════════════
        //  STAIR — simple stringer staircase
        // ════════════════════════════════════════════════════════════════════
        static void BuildStair(JToken st, GeometryResult res)
        {
            string id    = Str(st["id"],        "stair");
            double x0    = GetDouble(st, "x",   0);
            double y0    = GetDouble(st, "y",   0);
            double z0    = GetDouble(st, "z",   0);
            double w     = GetDouble(st, "width", 1200);
            int    steps = Math.Max(1, (int)GetDouble(st, "steps", 12));
            double riser = GetDouble(st, "riser_height", 170);
            double going = GetDouble(st, "tread_depth",  270);
            double thick = GetDouble(st, "slab_thickness", 150);

            for (int i = 0; i < steps; i++)
            {
                Brep b = BoxBrep(x0, y0 + i * going, z0 + i * riser, w, going, riser + thick);
                res.Add(b, L_STAIRS, $"AA3D_{id}_STEP_{i + 1:D2}");
            }
        }

        // ════════════════════════════════════════════════════════════════════
        //  ROOF — flat or gable
        // ════════════════════════════════════════════════════════════════════
        static void BuildRoof(JToken roof, double roomW, double roomD,
                               double roomH, double floorZ, double wallT,
                               GeometryResult res)
        {
            string typ   = Str(roof["type"], "flat").ToLower();
            double thick = GetDouble(roof, "thickness", 200);
            double z0    = GetDouble(roof, "z",         floorZ + roomH);
            double overhang = GetDouble(roof, "overhang", 300);

            if (typ == "gable")
            {
                double ridge = GetDouble(roof, "ridge_height", 1500);
                // Two sloping roof planes as simple boxes (approximation)
                double halfD = (roomD + 2 * overhang) / 2.0;
                double slopeL = Math.Sqrt(ridge * ridge + halfD * halfD);

                // Left slope slab (simplified as horizontal box — rotate in Rhino if needed)
                Brep b1 = BoxBrep(-overhang, -overhang, z0, roomW + 2 * overhang, halfD, thick);
                res.Add(b1, L_ROOF, "AA3D_ROOF_SLOPE_L");

                Brep b2 = BoxBrep(-overhang, roomD / 2.0, z0, roomW + 2 * overhang, halfD + overhang, thick);
                res.Add(b2, L_ROOF, "AA3D_ROOF_SLOPE_R");
            }
            else  // flat
            {
                Brep b = BoxBrep(-overhang, -overhang, z0,
                                 roomW + 2 * overhang, roomD + 2 * overhang, thick);
                res.Add(b, L_ROOF, "AA3D_ROOF_SLAB");
            }
        }

        // ════════════════════════════════════════════════════════════════════
        //  WallOpening helper — mirrors WallOpening class in opening_geometry.py
        // ════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Add a box relative to a WallOpening's coordinate system.
        /// d_far / d_near are both in "d_from_int" space:
        ///   positive = into wall toward exterior
        ///   negative = proud into room
        /// </summary>
        static void AddBox(WallOpening wo,
                           double u0, double u1, double z0, double z1,
                           double dFar, double dNear,
                           string layer, string name, GeometryResult res)
        {
            if (u1 <= u0 || z1 <= z0) return;
            double a = Math.Min(dFar, dNear);
            double b = Math.Max(dFar, dNear);
            if (Math.Abs(b - a) < 1.0) return;

            // 8 corner points — mirrors WallOpening.box() / _pt()
            Point3d[] pts = new Point3d[8];
            pts[0] = wo.Pt(u0, z0, b);
            pts[1] = wo.Pt(u1, z0, b);
            pts[2] = wo.Pt(u1, z1, b);
            pts[3] = wo.Pt(u0, z1, b);
            pts[4] = wo.Pt(u0, z0, a);
            pts[5] = wo.Pt(u1, z0, a);
            pts[6] = wo.Pt(u1, z1, a);
            pts[7] = wo.Pt(u0, z1, a);

            Brep brep = BrepFromEightPoints(pts);
            res.Add(brep, layer, name);
        }

        // ── Geometry primitives ──────────────────────────────────────────────

        static Brep BoxBrep(double x, double y, double z,
                            double width, double depth, double height)
        {
            if (width <= 0 || depth <= 0 || height <= 0) return null;
            var box = new Box(new BoundingBox(
                new Point3d(x, y, z),
                new Point3d(x + width, y + depth, z + height)));
            return box.ToBrep();
        }

        /// <summary>
        /// Build a closed Brep from 8 corner points (same order as rs.AddBox).
        /// Bottom quad: pts[0..3], Top quad: pts[4..7] aligned to bottom.
        /// </summary>
        static Brep BrepFromEightPoints(Point3d[] p)
        {
            // Build 6 faces as planar surfaces then join
            try
            {
                // Use BrepBox — the Box constructor doesn't take arbitrary quads,
                // but we can approximate well enough for rectilinear cases by
                // computing the bounding box of the 8 pts. For the wall-opening
                // geometry all boxes ARE rectilinear so this is exact.
                BoundingBox bb = new BoundingBox(p);
                Box box = new Box(bb);
                return box.ToBrep();
            }
            catch
            {
                return null;
            }
        }

        // ── JSON helpers ─────────────────────────────────────────────────────

        static double GetDouble(JToken token, string key, double fallback)
        {
            if (token == null) return fallback;
            JToken v = token[key];
            if (v == null || v.Type == JTokenType.Null) return fallback;
            try { return (double)v; } catch { return fallback; }
        }

        static string Str(JToken token, string fallback)
        {
            if (token == null || token.Type == JTokenType.Null) return fallback;
            return token.ToString();
        }

        static IEnumerable<JToken> Arr(JToken token)
        {
            if (token is JArray arr) return arr;
            return Array.Empty<JToken>();
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    //  WallOpening — port of the Python WallOpening class
    // ════════════════════════════════════════════════════════════════════════

    internal sealed class WallOpening
    {
        readonly string _wall;
        readonly double _intFace;
        readonly double _normalIn;
        readonly bool   _axisIsY;   // true = south/north, false = east/west

        public WallOpening(string wall, double roomW, double roomD,
                           double wallT, double centerU,
                           double zBottom, double width, double height)
        {
            _wall = wall;
            switch (wall)
            {
                case "south": _axisIsY = true;  _intFace = wallT;          _normalIn = +1; break;
                case "north": _axisIsY = true;  _intFace = roomD - wallT;  _normalIn = -1; break;
                case "west":  _axisIsY = false; _intFace = wallT;          _normalIn = +1; break;
                case "east":  _axisIsY = false; _intFace = roomW - wallT;  _normalIn = -1; break;
                default: throw new ArgumentException("Unknown wall: " + wall);
            }
        }

        /// <summary>
        /// Compute a 3-D point in model space from wall-relative coordinates.
        /// u       = position along the wall (x for N/S walls, y for E/W walls)
        /// z       = height
        /// dFromInt = distance from interior face (positive → toward exterior, negative → into room)
        /// </summary>
        public Point3d Pt(double u, double z, double dFromInt)
        {
            double d = _intFace - _normalIn * dFromInt;
            return _axisIsY
                ? new Point3d(u, d, z)
                : new Point3d(d, u, z);
        }
    }
}
