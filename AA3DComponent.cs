using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Grasshopper.Kernel;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Rhino.Geometry;

namespace AA3D
{
    // ═══════════════════════════════════════════════════════════════════════════
    //  AA3DComponent — V2: Grasshopper component with WinForms floating UI panel.
    //
    //  The GH canvas component has a single "Open Panel" button input.
    //  All prompts, image uploads, model settings and progress are in the
    //  floating AA3DWindow panel.
    //
    //  Pipeline:
    //    1. If image path supplied: VisionClient → llama3.2-vision:11b → JSON
    //    2. OllamaClient → qwen2.5-coder:14b → JSON (with vision JSON as context)
    //    3. GeometryBuilder → Breps
    //
    //  Inputs:
    //    [0] ShowUI   (bool)   — flip True to show/open the floating panel
    //
    //  Outputs:
    //    [0] Geometry (Brep list)
    //    [1] Layers   (string list)
    //    [2] Names    (string list)
    //    [3] JSON     (string)
    //    [4] Log      (string list)
    // ═══════════════════════════════════════════════════════════════════════════

    public class AA3DComponent : GH_Component
    {
        // ── State ────────────────────────────────────────────────────────────
        private bool                    _running     = false;
        private CancellationTokenSource _cts         = null;

        private GeometryResult          _lastResult  = null;
        private string                  _lastJson    = null;
        private string                  _lastError   = null;
        private bool                    _resultReady = false;

        private bool                    _prevShow    = false;

        // The floating WinForms panel (created once, hidden instead of closed)
        private AA3DWindow              _window      = null;

        // ── Constructor ──────────────────────────────────────────────────────
        public AA3DComponent()
            : base("AA3D", "AA3D",
                   "Local AI Architectural Generator — V2 with UI panel and vision pipeline.",
                   "AA3D", "Generate")
        { }

        public override Guid ComponentGuid =>
            new Guid("A3D10002-0000-0000-0000-AA3D00000003");

        public override GH_Exposure Exposure => GH_Exposure.primary;

        // ── Parameter registration ───────────────────────────────────────────
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddBooleanParameter("ShowUI", "UI",
                "Flip to True to open the AA3D floating panel",
                GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddBrepParameter  ("Geometry", "G",   "Generated 3-D geometry (Breps)",  GH_ParamAccess.list);
            pManager.AddTextParameter  ("Layers",   "L",   "Layer name per geometry object",   GH_ParamAccess.list);
            pManager.AddTextParameter  ("Names",    "N",   "Object name per geometry object",  GH_ParamAccess.list);
            pManager.AddTextParameter  ("JSON",     "JSON","Raw AI JSON response",             GH_ParamAccess.item);
            pManager.AddTextParameter  ("Log",      "Log", "Build log",                        GH_ParamAccess.list);
        }

        // ── Solve ────────────────────────────────────────────────────────────
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            bool showUI = false;
            DA.GetData(0, ref showUI);

            // Open or bring panel to front when ShowUI flips True
            bool justToggled = showUI && !_prevShow;
            _prevShow = showUI;

            if (showUI && justToggled)
                EnsureWindowVisible();

            // Consume a ready result from the background worker
            if (_resultReady)
            {
                _resultReady = false;

                if (_lastError != null)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, _lastError);
                    ClearOutputs(DA);
                    return;
                }

                SetOutputs(DA, _lastResult, _lastJson);
                return;
            }

            // Status messages
            if (_running)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "⏳ Generation in progress…");

            if (_lastResult != null)
                SetOutputs(DA, _lastResult, _lastJson);
            else
                ClearOutputs(DA);
        }

        // ── Window management ─────────────────────────────────────────────────
        private void EnsureWindowVisible()
        {
            if (_window == null || _window.IsDisposed)
            {
                _window = new AA3DWindow();
                _window.GenerateRequested += OnGenerateRequested;
                _window.CancelRequested   += OnCancelRequested;
            }

            if (!_window.Visible)
                _window.Show();

            _window.BringToFront();
        }

        private void OnGenerateRequested(string prompt, string imagePath, string model, string endpoint)
        {
            if (_running)
            {
                _window?.AppendLog("⚠ Already generating — cancel first.");
                return;
            }

            _lastError  = null;
            _lastResult = null;
            _lastJson   = null;

            StartGeneration(prompt, imagePath, model, endpoint);
        }

        private void OnCancelRequested()
        {
            CancelGeneration();
            _window?.AppendLog("🛑 Generation cancelled.");
            _window?.UpdateStage(AA3DWindow.STAGE_IDLE);
        }

        // ── Generation worker ─────────────────────────────────────────────────
        private void StartGeneration(string prompt, string imagePath, string model, string endpoint)
        {
            CancelGeneration();

            _cts     = new CancellationTokenSource();
            _running = true;

            var ct = _cts.Token;

            Task.Run(async () =>
            {
                try
                {
                    // ── STAGE 1 — AI Calling (vision or direct) ──────────────
                    _window?.UpdateStage(AA3DWindow.STAGE_AI_CALL);
                    _window?.AppendLog("🔍 Starting AI pipeline…");

                    string visionJson   = null;
                    string ollamaEP     = endpoint.TrimEnd('/');

                    // Vision step (if image provided)
                    if (!string.IsNullOrWhiteSpace(imagePath) &&
                        System.IO.File.Exists(imagePath))
                    {
                        _window?.AppendLog($"👁 Running vision analysis on: {System.IO.Path.GetFileName(imagePath)}");
                        visionJson = await VisionClient.AnalyzeImageAsync(
                            imagePath,
                            VisionClient.DEFAULT_VISION_MODEL,
                            ollamaEP,
                            ct).ConfigureAwait(false);

                        _window?.AppendLog("✅ Vision analysis complete.");
                    }

                    // Build the full code-gen prompt
                    string fullPrompt = BuildSystemPrompt(prompt, visionJson);

                    _window?.AppendLog($"🤖 Calling {model}…");
                    string rawText = await OllamaClient.GenerateAsync(
                        fullPrompt, model,
                        ollamaEP + "/api/generate",
                        ct).ConfigureAwait(false);

                    // ── STAGE 2 — JSON Parsed ────────────────────────────────
                    _window?.UpdateStage(AA3DWindow.STAGE_JSON);
                    _window?.AppendLog("🔎 Extracting JSON from response…");

                    string json = ExtractJson(rawText);
                    if (string.IsNullOrWhiteSpace(json))
                        throw new Exception("No valid JSON found in Ollama response.\n\nRaw:\n" + rawText);

                    _window?.AppendLog("✅ JSON parsed successfully.");

                    // ── STAGE 3 — Geometry Building ──────────────────────────
                    _window?.UpdateStage(AA3DWindow.STAGE_GEOMETRY);
                    _window?.AppendLog("🏗 Building geometry…");

                    JToken root    = JToken.Parse(json);
                    JToken model_  = root["model"] ?? root;
                    GeometryResult result = GeometryBuilder.Build(model_);

                    _window?.AppendLog($"✅ Built {result.Breps.Count} geometry objects.");

                    // ── STAGE 4 — Done ───────────────────────────────────────
                    _lastResult  = result;
                    _lastJson    = json;
                    _lastError   = null;
                    _resultReady = true;
                    _running     = false;

                    _window?.UpdateStage(AA3DWindow.STAGE_DONE);
                    _window?.AppendLog("🎉 Generation complete! Outputs updated in Grasshopper.");
                }
                catch (OperationCanceledException)
                {
                    _running     = false;
                    _resultReady = false;
                }
                catch (Exception ex)
                {
                    _lastError   = ex.Message;
                    _resultReady = true;
                    _running     = false;

                    _window?.ShowError(ex.Message);
                    _window?.UpdateStage(AA3DWindow.STAGE_IDLE);
                    _window?.AppendLog("❌ Error: " + ex.Message);
                }
                finally
                {
                    Rhino.RhinoApp.InvokeOnUiThread(new Action(() =>
                    {
                        if (!ct.IsCancellationRequested)
                            ExpireSolution(true);
                    }));
                }
            }, ct);
        }

        private void CancelGeneration()
        {
            if (_cts != null)
            {
                try { _cts.Cancel(); } catch { }
                _cts.Dispose();
                _cts = null;
            }
            _running = false;
        }

        // ── Output helpers ────────────────────────────────────────────────────
        private void SetOutputs(IGH_DataAccess DA, GeometryResult result, string json)
        {
            DA.SetDataList(0, result?.Breps  ?? new List<Brep>());
            DA.SetDataList(1, result?.Layers ?? new List<string>());
            DA.SetDataList(2, result?.Names  ?? new List<string>());
            DA.SetData    (3, json ?? "");
            DA.SetDataList(4, result?.Log    ?? new List<string>());
        }

        private void ClearOutputs(IGH_DataAccess DA)
        {
            DA.SetDataList(0, new List<Brep>());
            DA.SetDataList(1, new List<string>());
            DA.SetDataList(2, new List<string>());
            DA.SetData    (3, "");
            DA.SetDataList(4, new List<string>());
        }

        // ── System prompt builder ─────────────────────────────────────────────
        private static string BuildSystemPrompt(string userPrompt, string visionJson)
        {
            // JSON schema that the model must produce
            const string SCHEMA = @"
{
  ""model"": {
    ""room_width"":    5000,
    ""room_depth"":    4000,
    ""room_height"":   2800,
    ""wall_thickness"": 200,
    ""floor_level"":      0,
    ""floors"": [
      {""id"": ""F1"", ""z"": 0, ""thickness"": 200, ""x"": 0, ""y"": 0, ""width"": 5000, ""depth"": 4000}
    ],
    ""walls"": [
      {""id"": ""W1"", ""face"": ""south"", ""z_bottom"": 0, ""z_top"": 2800},
      {""id"": ""W2"", ""face"": ""north"", ""z_bottom"": 0, ""z_top"": 2800},
      {""id"": ""W3"", ""face"": ""east"",  ""z_bottom"": 0, ""z_top"": 2800},
      {""id"": ""W4"", ""face"": ""west"",  ""z_bottom"": 0, ""z_top"": 2800}
    ],
    ""windows"": [
      {""id"": ""WIN1"", ""wall"": ""south"", ""center"": 2500, ""sill"": 800, ""width"": 1200, ""height"": 1200,
       ""type"": ""FIXED"", ""frame"": {""width"": 60}}
    ],
    ""doors"": [
      {""id"": ""DR1"", ""wall"": ""west"", ""center"": 1000, ""sill"": 0, ""width"": 900, ""height"": 2100,
       ""type"": ""SINGLE_HINGED"", ""frame"": {""width"": 60}, ""leaf_count"": 1}
    ],
    ""columns"": [],
    ""stairs"": [],
    ""roof"": {""type"": ""flat"", ""thickness"": 200, ""overhang"": 300}
  }
}";

            // Full Grasshopper Knowledge Base embedded verbatim
            const string GH_KNOWLEDGE_BASE = @"
GRASSHOPPER KNOWLEDGE BASE FOR ARCHITECTURAL MODELING
All dimensions in MILLIMETRES unless stated otherwise.

CORE PHILOSOPHY — PARAMETRIC PRIMITIVE CHAIN:
- Start from the simplest primitive. Build up. Never skip steps.
- Rectangular room → Rectangle curve → Extrude
- Dome → Arc profile → Revolve
- Column → Circle or rectangle → Extrude
- Staircase → Origin point → Loop of boxes
- Pitched roof → Ridge point → Two slope lines → Loft

SECTION 2 — GhPython Conventions:
- All input port names become Python variables inside the script
- Use rg = Rhino.Geometry, rs = rhinoscriptsyntax, sc = scriptcontext
- Output geometry by assigning to output variable (a, b, or renamed port)
- NEVER use rs.GetObject(), rs.GetPoint() — these halt execution
- NEVER hardcode coordinates — all dims must come from slider inputs
- Cast slider inputs used in loops: int(num_risers), not float

SECTION 3 — Core Geometric Primitives (all in mm):
- Point3d: rg.Point3d(x, y, z)
- Vector3d: rg.Vector3d(dx, dy, dz); .Unitize() normalises in-place
- Line: rg.Line(start_pt, end_pt); .ToNurbsCurve() for surface ops
- Polyline: rg.Polyline(pts); first and last pt must match for closed
- Arc: rg.Arc(plane, radius, angle_radians); .ToNurbsCurve()
- Circle: rg.Circle(plane, radius); .ToNurbsCurve()
- Rectangle: rg.Rectangle3d(plane, width, depth); .ToNurbsCurve()
- NurbsCurve: rg.NurbsCurve.CreateInterpolatedCurve(pts, degree)
- Plane: rg.Plane(origin, normal) or rg.Plane.WorldXY

SECTION 4 — Surface Creation:
- Extrude: rg.Surface.CreateExtrusion(closed_curve, vector); .ToBrep().CapPlanarHoles(0.01)
- Loft: rg.Brep.CreateFromLoft(curves, Unset, Unset, LoftType.Normal, False)
- Revolve: rg.RevSurface.Create(profile_curve, axis_line, 0, 2*pi); .ToBrep()
- Sweep1: rg.Brep.CreateFromSweep(rail, [profiles], False, 0.01)
- PlanarSrf: rg.Brep.CreatePlanarBreps(closed_curve, 0.01) — curve must be closed+planar
- EdgeSrf: rg.NurbsSurface.CreateEdgeSurface(4_curves)

SECTION 5 — Solid / Brep Creation:
- Box: rg.Box(plane, Interval(0,w), Interval(0,d), Interval(0,h)); .ToBrep()
- Cylinder: rg.Cylinder(circle, height); .ToBrep(True, True)
- Sphere: rg.Sphere(center_pt, radius); .ToBrep()
- Cap: brep.CapPlanarHoles(0.01) — required before Boolean ops
- BooleanDifference: rg.Brep.CreateBooleanDifference([a], [b], 0.01)
- BooleanUnion: rg.Brep.CreateBooleanUnion([a, b], 0.01)
- CRITICAL: both Breps must be valid closed solids before any Boolean

SECTION 6 — Architectural Patterns:
WALL: start_pt → end_pt → direction vec → perp vec → 4 footprint corners
  → Polyline → PlanarSrf → Extrude(wall_height) → CapPlanarHoles
FLOOR SLAB: origin → Rectangle(width, depth) → PlanarSrf → Extrude(-thickness) → Cap
DOOR OPENING: wall_brep → cutter Box at door_pos along wall → BooleanDifference
  cutter z-range: [0, door_height]; position = door_pos (0-1) × wall_length
WINDOW OPENING: same as door + sill_height offset on Z; cutter_origin += (0,0,sill_height)
COLUMN circular: base_pt → Circle(col_radius) → Cylinder(col_height) → ToBrep(True,True)
COLUMN rectangular: 4 corner pts → Polyline → PlanarSrf → Extrude(col_height) → Cap
STAIRCASE: origin → loop: step_box per riser, x offsets by tread_depth, z by riser_height
ROOF flat: Rectangle at roof_z → PlanarSrf → Extrude(slab_thickness) → + parapet walls
ROOF pitched: ridge line above center → left/right eave lines → Loft(Straight)
DOME: semicircle arc profile → RevSurface(0, 2π around Z axis)
SPACE FRAME: point grid → member lines (chord+diagonal) → Pipe each member

SECTION 7 — Standard Slider Defaults (ALL MM):
wall_height: 3000 (2400–6000)
wall_thickness: 230 (115–450)
sill_height: 900 (600–1200)
floor_thickness: 150 (100–300)
roof_slab_thickness: 200 (150–350)
door_height: 2100 (1800–2700)
door_width: 900 (750–1200)
window_height: 1200 (600–2100)
window_width: 1200 (600–2400)
col_radius: 150 (75–400)
col_width: 300 (230–600)
col_height: 3000 (2400–8000)
parapet_height: 1000 (600–1500)
riser_height: 175 (150–200)
tread_depth: 280 (250–320)
stair_width: 1200 (900–2400)
dome_radius: 5000 (1000–20000)

SECTION 8 — Layer Assignment:
AA3D_Walls, AA3D_Floors, AA3D_Openings, AA3D_Columns,
AA3D_Roof, AA3D_Stairs, AA3D_Structure, AA3D_Facade, AA3D_Site

SECTION 10 — Common Mistakes to Avoid:
- Starting with solid Box instead of profile curve
- Hardcoding coordinates
- Using rs.GetObject() or rs.GetPoint()
- CreatePlanarBreps on open or non-planar curve (returns None silently)
- BooleanDifference on non-closed Breps
- Outputting [None, None, Brep, None] — filter: [b for b in list if b is not None]
- Not calling .Unitize() before using a vector for offset
- Not casting slider inputs to int() for loop counts
";

            // Vision drawing analysis context (if available)
            string visionContext = "";
            if (!string.IsNullOrWhiteSpace(visionJson))
            {
                visionContext = $@"

ARCHITECTURAL DRAWING ANALYSIS (from vision model):
The user provided a floor plan / drawing image. The vision model extracted the following
structured data from it. Use this data as the authoritative source for room dimensions,
wall positions, door and window locations. Override any conflicting information in
the user prompt with this data.

Vision Analysis JSON:
{visionJson}

Instructions:
- Use the room boundaries, wall positions, and opening data above
- Convert all dimensions from mm as stated in the vision data
- Place doors and windows at the positions indicated by wall_id + position fields
- Respect wet_area flags for bathroom/kitchen placements
";
            }

            return $@"You are AA3D, an expert architectural 3-D modelling AI.
Generate a complete JSON model for this architectural request.
Output ONLY valid JSON — no markdown, no explanation, no code blocks.
The JSON must exactly follow this schema (fill in realistic values for the prompt):

{SCHEMA}

{GH_KNOWLEDGE_BASE}
{visionContext}

User request: {userPrompt}

Reply with ONLY the JSON object. Start your reply with {{ and end with }}.";
        }

        // ── JSON extraction ───────────────────────────────────────────────────
        private static string ExtractJson(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            // Strip markdown code fences
            var fenceMatch = Regex.Match(text, @"```(?:json)?\s*(\{[\s\S]*?\})\s*```");
            if (fenceMatch.Success)
            {
                string candidate = fenceMatch.Groups[1].Value;
                if (IsValidJson(candidate)) return candidate;
            }

            // Find outermost { … } block
            int start = text.IndexOf('{');
            int end   = FindMatchingBrace(text, start);
            if (start >= 0 && end > start)
            {
                string candidate = text.Substring(start, end - start + 1);
                if (IsValidJson(candidate)) return candidate;
            }

            return IsValidJson(text.Trim()) ? text.Trim() : null;
        }

        private static bool IsValidJson(string s)
        {
            try { JToken.Parse(s); return true; }
            catch { return false; }
        }

        private static int FindMatchingBrace(string text, int open)
        {
            if (open < 0 || open >= text.Length) return -1;
            int depth = 0;
            bool inString = false;
            bool escape   = false;

            for (int i = open; i < text.Length; i++)
            {
                char c = text[i];
                if (escape)               { escape = false; continue; }
                if (c == '\\' && inString){ escape = true;  continue; }
                if (c == '"')             { inString = !inString; continue; }
                if (inString)             continue;
                if (c == '{')             depth++;
                else if (c == '}')        { depth--; if (depth == 0) return i; }
            }
            return -1;
        }

        // ── Clean-up ──────────────────────────────────────────────────────────
        public override void RemovedFromDocument(GH_Document document)
        {
            CancelGeneration();

            if (_window != null && !_window.IsDisposed)
            {
                _window.GenerateRequested -= OnGenerateRequested;
                _window.CancelRequested   -= OnCancelRequested;
                _window.Dispose();
                _window = null;
            }

            base.RemovedFromDocument(document);
        }
    }
}
