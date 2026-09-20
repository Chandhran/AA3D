using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AA3D
{
    /// <summary>
    /// HTTP wrapper for llama3.2-vision:11b image analysis.
    /// Converts an image to base64, sends it to Ollama with the
    /// full arch_drawing_library context, and returns the vision JSON string.
    /// </summary>
    public static class VisionClient
    {
        public const string DEFAULT_VISION_MODEL = "llama3.2-vision:11b";

        // Shared HttpClient with 300-second timeout (vision model is slow)
        private static readonly HttpClient _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(300)
        };

        // ── Full Architectural Drawing Library (system context for vision) ──
        // This content is the complete arch_drawing_library.md embedded verbatim.
        private const string ARCH_DRAWING_LIBRARY = @"
# Architectural Drawing Library
## AA3D Vision + Code Model Reference — Version 1.0

DUAL-PURPOSE DOCUMENT:
1. llama3.2-vision:11b — reads floor plans, sections, and elevations and extracts geometry data
2. Qwen 2.5 Coder 14B  — uses the extracted data to generate geometry

All dimensions are in MILLIMETRES (mm) unless explicitly noted.

CORE PHILOSOPHY — ONE CODE = ONE NODE = ONE SET OF SLIDERS
- One code = one type: all instances with same code share identical dimensions
- One code = one GH node: single GhPython component controls ALL instances of D1
- Vision model extraction: when you see D1 in 3 places, extract ONE set of dimensions
- Output: {""code"": ""D1"", ""type"": ""single_swing"", ""width"": 900, ""height"": 2100, ""count"": 3}

SECTION 1 — HOW TO READ ARCHITECTURAL FLOOR PLANS
- Structural walls (load-bearing): drawn as two parallel thick lines with hatching/solid fill between
  - Typical thickness: 230mm (1-brick), 345mm (1.5-brick), 460mm (2-brick)
  - IS 962:1989 structural walls use Type A line (continuous thick)
- Partition walls: thinner parallel lines, usually without hatching
  - Typical thickness: 115mm (half-brick), 100mm (block), 75mm (gypboard)
- Default thickness: 230mm for exterior, 115mm for interior (if no dimension given)
- Room labels placed at centroid: MASTER BR, BEDROOM, LIVING, DINING, KITCHEN, TOILET, BATHROOM, PASSAGE, STORE, BALCONY
- Area may be written as: 12.50 Sq.M or 12.50 m²
- Drawing scale: check title block for ""SCALE: 1:100"" or graphic scale bar
- Column grid lines: thin chain lines labelled A, B, C (X-axis) and 1, 2, 3 (Y-axis)
- Staircases: parallel horizontal lines (treads), arrow with UP or DN, diagonal cut line

SECTION 2 — DOOR SYMBOL RECOGNITION
Door types and plan symbols:
- SINGLE LEAF SWING (D1, D2...): straight line (door panel) + quarter-circle arc (swing path)
  - Inward swing: arc goes INTO the room; Outward: arc goes AWAY
  - Standard: internal 800-900mm x 2100mm, toilet 750-800mm x 2000mm, external 1000-1200mm x 2100mm
- DOUBLE LEAF SWING (DD, DD1): two lines meeting at center + two arcs
  - Standard: 1500-1800mm total width, each leaf 750-900mm
- SLIDING (DS, DS1): parallel lines inside wall thickness, sliding direction arrow
  - Standard: 1200-2400mm wide, 2100-2700mm high
- POCKET SLIDING (DP, DP1): dotted rectangle inside wall (hidden panel)
- FOLDING/BIFOLD (DF, DF1): zigzag/accordion pattern
  - Per-leaf: 450-600mm; total: 900-4800mm
- COLLAPSIBLE/ROLLING SHUTTER (DCS, RS): cross-hatched fill + dashed rectangle at top (drum housing)
- REVOLVING (DRV, DR): circle with cross (4 leaves at 90° or 3 at 120°)
  - Diameter: 1800-3000mm (residential), 2400-3600mm (commercial)
- FLUSH DOOR (DFL): identical plan symbol to single swing, identified only by annotation
- FIRE DOOR (FD, FD1): same as single swing with F suffix, sometimes bold line for panel

SECTION 3 — WINDOW SYMBOL RECOGNITION
Window types and plan symbols:
- FIXED GLASS (WF, W): three parallel lines across wall thickness (frame-glass-frame)
  - Standard: 600-3000mm wide, 600-2100mm high, sill 750-1200mm
- SLIDING (WS, WS1): two parallel lines + one offset line (overlapping panel)
  - Standard: 900-2400mm wide, 600-1500mm high, sill 900mm
- CASEMENT (WC, WC1): small quarter-circle arc at one side (hinge side)
  - Per leaf: 450-1200mm; height: 900-1800mm; sill 750-1000mm
- AWNING (WA, WA1): small arc at top (top-hinged, bottom swings out)
  - Standard: 600-1800mm wide, 300-600mm high, sill 1800-2100mm (near ceiling)
- LOUVRED (WL, WL1): parallel diagonal lines inside frame (45° slats)
  - Standard: 450-900mm wide, 300-900mm high
- BAY WINDOW (WB, WB1): wall line projects outward with 3 faces (center + two angled sides at 30-45°)
  - Projection: 300-900mm; center: 900-1800mm; total: 1800-3600mm; height: 1200-2100mm; sill 600-900mm
- FRENCH WINDOW (WFR, FW): full-height opening from floor, drawn like door but with glazing lines
  - Standard: 900-3600mm wide, 2100-2700mm high, sill 0mm (goes to floor)
- VENTILATOR (VT, V): small window symbol placed high on wall (300-600mm below ceiling)
  - Standard: 300-900mm wide, 150-450mm high, sill 2100-2400mm

SECTION 6 — ANNOTATION CODE SYSTEM
Door codes: D1-D3 (swing), DD (double swing), DS (sliding), DP (pocket), DF (folding), DFL (flush), DCS/RS (shutter), DRV (revolving), FD (fire door)
Window codes: W1-W3, WF (fixed), WS (sliding), WC (casement), WA (awning), WL (louvre), WB (bay), WFR (French)
Ventilator codes: VT1, VT2, V1, EF (exhaust fan), AC (AC grille), SL/SK (skylight)

STANDARD DIMENSIONS (CPWD Manual + IS 4021) — ALL IN MILLIMETRES
Walls: structural exterior 230mm, partition 115mm
Floors: slab 125-200mm, lintel 150mm
Default door: width 900mm, height 2100mm
Default window: width 1200mm, height 1200mm, sill 900mm
Default column: 300x300mm rectangular or 150mm radius circular
Default stair: width 1200mm, 16 risers, riser 175mm, tread 280mm

APPENDIX B — REQUIRED OUTPUT FORMAT
You MUST output ONLY this JSON structure. No other text, no explanation, no markdown.
Start with {{ and end with }}.

{{
  ""drawing_type"": ""floor_plan"",
  ""scale"": ""1:100"",
  ""north_direction"": ""up"",
  ""building_origin"": [0, 0],
  ""units"": ""mm"",

  ""rooms"": [
    {{
      ""id"": ""R001"",
      ""name"": ""LIVING ROOM"",
      ""boundary"": [[0,0],[5000,0],[5000,4000],[0,4000]],
      ""area_sqm"": 20.0,
      ""level_z"": 0,
      ""wet_area"": false
    }}
  ],

  ""walls"": [
    {{
      ""id"": ""W001"",
      ""start"": [0, 0],
      ""end"": [5000, 0],
      ""thickness_mm"": 230,
      ""type"": ""structural"",
      ""face"": ""south"",
      ""height_mm"": 3000
    }}
  ],

  ""doors"": [
    {{
      ""code"": ""D1"",
      ""type"": ""single_swing"",
      ""count"": 1,
      ""wall_id"": ""W001"",
      ""position"": 0.4,
      ""width_mm"": 900,
      ""height_mm"": 2100,
      ""hinge_side"": ""left"",
      ""swing"": ""inward""
    }}
  ],

  ""windows"": [
    {{
      ""code"": ""W1"",
      ""type"": ""sliding"",
      ""count"": 1,
      ""wall_id"": ""W002"",
      ""position"": 0.5,
      ""width_mm"": 1200,
      ""height_mm"": 1200,
      ""sill_height_mm"": 900
    }}
  ],

  ""ventilators"": [],

  ""columns"": [],

  ""stairs"": []
}}
";

        /// <summary>
        /// Analyse an architectural drawing image and return the vision JSON.
        /// </summary>
        /// <param name="imagePath">Full path to the image file (PNG, JPG, BMP, etc.)</param>
        /// <param name="visionModel">Ollama vision model name (default: llama3.2-vision:11b)</param>
        /// <param name="baseEndpoint">Ollama base URL without path (e.g. http://localhost:11434)</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Vision JSON string matching the Appendix B format</returns>
        public static async Task<string> AnalyzeImageAsync(
            string imagePath,
            string visionModel,
            string baseEndpoint,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(imagePath))
                throw new ArgumentException("Image path is required for vision analysis.");

            if (!File.Exists(imagePath))
                throw new FileNotFoundException($"Image file not found: {imagePath}");

            // 1. Read image and convert to base64
            byte[] imageBytes  = await Task.Run(() => File.ReadAllBytes(imagePath), ct).ConfigureAwait(false);
            string base64Image = Convert.ToBase64String(imageBytes);

            // 2. Build the prompt
            string visionPrompt = BuildVisionPrompt();

            // 3. Build Ollama API endpoint
            string endpoint = baseEndpoint.TrimEnd('/') + "/api/generate";

            // 4. Build request body
            var requestBody = new
            {
                model   = string.IsNullOrWhiteSpace(visionModel) ? DEFAULT_VISION_MODEL : visionModel,
                prompt  = visionPrompt,
                images  = new[] { base64Image },
                stream  = false,
                options = new
                {
                    temperature = 0.1,   // very low — we need structured JSON
                    top_p       = 0.9,
                    num_predict = 4096
                }
            };

            string jsonBody = JsonConvert.SerializeObject(requestBody);
            using var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

            // 5. POST to Ollama
            using var response = await _http.PostAsync(endpoint, content, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            string responseText = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            // 6. Parse Ollama response
            JObject jObj = JObject.Parse(responseText);
            string visionText = jObj["response"]?.ToString();

            if (string.IsNullOrWhiteSpace(visionText))
                throw new Exception("Vision model returned empty response.");

            return visionText;
        }

        // ── Private helpers ──────────────────────────────────────────────────

        private static string BuildVisionPrompt()
        {
            return $@"You are AA3D Vision, an expert architectural drawing interpreter.
You will analyse the provided architectural floor plan image using the complete
Architectural Drawing Library reference below.

TASK: Extract all architectural elements from this floor plan and output them
as a single valid JSON object. Output ONLY the JSON — no markdown, no explanation,
no code blocks. Start your reply with {{ and end with }}.

Reference Library:
{ARCH_DRAWING_LIBRARY}

EXTRACTION RULES:
1. Read all dimension strings — they override any scale-based measurement
2. Group elements by annotation code (D1, W1, VT1, etc.) — one code = one entry
3. Measure wall thickness from the drawing; default to 230mm exterior / 115mm interior
4. Identify room boundaries from closed wall polygons
5. Read door/window positions as 0.0-1.0 fraction along the wall they are in
6. Report coordinates in millimetres from building origin (bottom-left corner)
7. If a dimension is unclear, use standard defaults from the reference library
8. Output ONLY the JSON matching the Appendix B format — nothing else

Reply with ONLY the JSON object. Start with {{ and end with }}.";
        }

        /// <summary>
        /// Quick liveness check — tests if the Ollama server is reachable.
        /// </summary>
        public static async Task<bool> IsAliveAsync(string baseEndpoint, CancellationToken ct)
        {
            try
            {
                string tagsUrl = baseEndpoint.TrimEnd('/') + "/api/tags";
                using var resp = await _http.GetAsync(tagsUrl, ct).ConfigureAwait(false);
                return resp.IsSuccessStatusCode;
            }
            catch { return false; }
        }
    }
}
