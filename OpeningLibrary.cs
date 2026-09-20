using System;
using System.Collections.Generic;

namespace AA3D
{
    /// <summary>
    /// Port of opening_library.py — canonical window and door type definitions.
    /// </summary>
    public static class OpeningLibrary
    {
        // ── Window catalog ───────────────────────────────────────────────
        public static readonly IReadOnlyDictionary<string, OpeningDef> Windows =
            new Dictionary<string, OpeningDef>(StringComparer.OrdinalIgnoreCase)
        {
            ["FIXED"]         = new OpeningDef("fixed",          "single",         0),
            ["SLIDING"]       = new OpeningDef("sliding",        "2_panel",        2),
            ["SINGLE_HUNG"]   = new OpeningDef("hung",           "single_sash",    0),
            ["DOUBLE_HUNG"]   = new OpeningDef("hung",           "double_sash",    0),
            ["CASEMENT"]      = new OpeningDef("hinged",         "single",         0),
            ["AWNING"]        = new OpeningDef("hinged",         "top_hinged",     0),
            ["HOPPER"]        = new OpeningDef("hinged",         "bottom_hinged",  0),
            ["PIVOT"]         = new OpeningDef("pivot",          "single",         0),
            ["TILT_TURN"]     = new OpeningDef("tilt_turn",      "single",         0),
            ["TILT_SLIDE"]    = new OpeningDef("tilt_slide",     "single",         0),
            ["LOUVERED"]      = new OpeningDef("louvered",       "multiple_louver",10),
            ["DUAL_ACTION"]   = new OpeningDef("dual_action",    "single",         0),
            ["BAY"]           = new OpeningDef("fixed_or_operable","bay",          0),
            ["BOW"]           = new OpeningDef("fixed_or_operable","bow",          0),
            ["CORNER"]        = new OpeningDef("fixed_or_operable","corner",       0),
            ["TRANSOM"]       = new OpeningDef("fixed_or_operable","transom",      0),
            ["CLERESTORY"]    = new OpeningDef("fixed_or_operable","high_level",   0),
            ["ARCHED"]        = new OpeningDef("fixed_or_operable","arched",       0),
            ["ROUND"]         = new OpeningDef("fixed_or_operable","round",        0),
            ["TRAPEZOIDAL"]   = new OpeningDef("fixed_or_operable","trapezoid",    0),
            ["SKYLIGHT"]      = new OpeningDef("fixed_or_operable","roof",         0),
            ["RIBBON"]        = new OpeningDef("fixed_or_operable","continuous",   0),
            ["SPECIALTY"]     = new OpeningDef("custom",         "custom",         0),
        };

        // ── Door catalog ─────────────────────────────────────────────────
        public static readonly IReadOnlyDictionary<string, OpeningDef> Doors =
            new Dictionary<string, OpeningDef>(StringComparer.OrdinalIgnoreCase)
        {
            ["SINGLE_HINGED"]       = new OpeningDef("hinged",          "single",         1),
            ["DOUBLE_HINGED"]       = new OpeningDef("hinged",          "double",         2),
            ["PIVOT"]               = new OpeningDef("pivot",           "single",         1),
            ["OFFSET_PIVOT"]        = new OpeningDef("pivot",           "single",         1),
            ["SLIDING"]             = new OpeningDef("sliding",         "single",         1),
            ["DOUBLE_SLIDING"]      = new OpeningDef("sliding",         "bi_parting",     2),
            ["POCKET_SLIDING"]      = new OpeningDef("sliding",         "pocket",         1),
            ["BI_FOLD"]             = new OpeningDef("folding",         "bi_fold",        2),
            ["FOLDING_SLIDING"]     = new OpeningDef("folding_sliding", "multi_panel",    4),
            ["REVOLVING"]           = new OpeningDef("revolving",       "four_wing",      4),
            ["LIFT_SLIDE"]          = new OpeningDef("lift_slide",      "multi_panel",    2),
            ["TELESCOPIC_SLIDING"]  = new OpeningDef("sliding",         "telescopic",     3),
            ["CURVED_SLIDING"]      = new OpeningDef("sliding",         "curved",         2),
            ["FRENCH"]              = new OpeningDef("hinged",          "double",         2),
            ["DUTCH"]               = new OpeningDef("hinged",          "split",          2),
            ["AUTOMATIC_SWING"]     = new OpeningDef("hinged",          "automatic",      1),
            ["AUTOMATIC_SLIDING"]   = new OpeningDef("sliding",         "automatic",      1),
            ["ROLLER_SHUTTER"]      = new OpeningDef("rolling",         "single",         1),
            ["SECTIONAL_OVERHEAD"]  = new OpeningDef("overhead",        "sectional",      1),
            ["INDUSTRIAL_FAST"]     = new OpeningDef("rapid",           "industrial",     1),
            ["SECURITY_REVOLVING"]  = new OpeningDef("revolving",       "security",       4),
            ["SPECIALTY"]           = new OpeningDef("custom",          "custom",         1),
        };

        // ── Aliases (mirrors opening_library.py ALIASES dict) ────────────
        private static readonly Dictionary<string, string> _windowAliases =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["SLIDER"]              = "SLIDING",
            ["GLIDER"]              = "SLIDING",
            ["HORIZONTAL_SLIDER"]   = "SLIDING",
            ["SINGLE_SLIDER"]       = "SLIDING",
            ["DOUBLE_SLIDER"]       = "SLIDING",
            ["PICTURE"]             = "FIXED",
            ["PICTURE_WINDOW"]      = "FIXED",
            ["SINGLE_HUNG_WINDOW"]  = "SINGLE_HUNG",
            ["DOUBLE_HUNG_WINDOW"]  = "DOUBLE_HUNG",
        };

        private static readonly Dictionary<string, string> _doorAliases =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["HINGED"]              = "SINGLE_HINGED",
            ["SWING"]               = "SINGLE_HINGED",
            ["SINGLE_DOOR"]         = "SINGLE_HINGED",
            ["DOUBLE_DOOR"]         = "DOUBLE_HINGED",
            ["DOUBLE_HINGED_DOOR"]  = "DOUBLE_HINGED",
            ["OFF_CENTER_PIVOT"]    = "OFFSET_PIVOT",
            ["OFFCENTER_PIVOT"]     = "OFFSET_PIVOT",
            ["OFFSET_PIVOT_DOOR"]   = "OFFSET_PIVOT",
            ["POCKET"]              = "POCKET_SLIDING",
            ["FOLDING"]             = "BI_FOLD",
            ["SLIDING_FOLDING"]     = "FOLDING_SLIDING",
        };

        // ── Public helpers ───────────────────────────────────────────────

        public static string NormalizeWindowType(string raw)
        {
            string key = (raw ?? "").Trim().ToUpperInvariant()
                                    .Replace("-", "_").Replace(" ", "_");
            if (_windowAliases.TryGetValue(key, out string alias)) key = alias;
            return Windows.ContainsKey(key) ? key : "FIXED";
        }

        public static string NormalizeDoorType(string raw)
        {
            string key = (raw ?? "").Trim().ToUpperInvariant()
                                    .Replace("-", "_").Replace(" ", "_");
            if (_doorAliases.TryGetValue(key, out string alias)) key = alias;
            return Doors.ContainsKey(key) ? key : "SINGLE_HINGED";
        }
    }

    public sealed class OpeningDef
    {
        public string Operation     { get; }
        public string Configuration { get; }
        public int    PanelOrLeafCount { get; }

        public OpeningDef(string operation, string configuration, int panelOrLeafCount)
        {
            Operation         = operation;
            Configuration     = configuration;
            PanelOrLeafCount  = panelOrLeafCount;
        }
    }
}
