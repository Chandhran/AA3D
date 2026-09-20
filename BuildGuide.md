# AA3D Grasshopper Plugin V2 — Build Guide

## What's new in V2

V2 adds a **floating WinForms UI panel** and a **vision pipeline** for floor plan image analysis.

| Feature | V1 | V2 |
|---------|----|----|
| UI | GH canvas inputs only | Floating dark-theme panel |
| Image analysis | ✗ | ✅ llama3.2-vision:11b |
| Progress tracking | GH messages only | 4-stage progress bar + live log |
| gh_knowledge_base embedded | Partial | Full (all 12 sections) |

---

## Prerequisites

| Tool | Version | Where to get it |
|------|---------|-----------------|
| Visual Studio 2022 | Community (free) | https://visualstudio.microsoft.com/vs/community/ |
| .NET Desktop workload | (tick during VS install) | — |
| Rhino 8 | Commercial | Already installed at `C:\Program Files\Rhino 8\` |
| Ollama | Latest | https://ollama.com → Download for Windows |
| qwen2.5-coder:14b | — | `ollama pull qwen2.5-coder:14b` in CMD |
| llama3.2-vision:11b | — | `ollama pull llama3.2-vision:11b` in CMD (**new in V2**) |

> **Note:** `llama3.2-vision:11b` requires ~8 GB VRAM. On an RTX 4070 SUPER it runs well.
> If VRAM is tight, load it after qwen unloads (Ollama swaps automatically).

---

## Files in this folder

```
AA3D_V2/
├── AA3D.csproj          ← project / build config (updated: WinForms reference, v2.0)
├── AA3DPlugin.cs        ← GH assembly registration (unchanged from V1)
├── AA3DComponent.cs     ← GH component — ShowUI toggle + pipeline orchestration
├── AA3DWindow.cs        ← WinForms floating UI panel (NEW in V2)
├── VisionClient.cs      ← HTTP client for llama3.2-vision:11b (NEW in V2)
├── OllamaClient.cs      ← HTTP wrapper for qwen2.5-coder (unchanged from V1)
├── GeometryBuilder.cs   ← JSON model → RhinoCommon Breps (unchanged from V1)
├── OpeningLibrary.cs    ← Window & door type catalog (unchanged from V1)
└── BuildGuide.md        ← this file
```

---

## Step 1 — Open in Visual Studio

1. Launch **Visual Studio 2022**.
2. **File → Open → Project/Solution** → navigate to the `AA3D_V2` folder → select `AA3D.csproj`.
3. VS will load the project and restore the `Newtonsoft.Json` NuGet package automatically (~10 s).

---

## Step 2 — Verify Rhino SDK path

Open `AA3D.csproj` and check:

```xml
<RhinoDir Condition="'$(RhinoDir)'==''">C:\Program Files\Rhino 8\System</RhinoDir>
```

If Rhino 8 is installed elsewhere, change this path. The three DLLs needed are:
- `RhinoCommon.dll`
- `Grasshopper\Grasshopper.dll`
- `Grasshopper\GH_IO.dll`

---

## Step 3 — Build

1. Set configuration to **Release** in the top toolbar.
2. **Build → Build Solution** (or `Ctrl+Shift+B`).

The post-build step automatically:
- Renames `AA3D.dll` → `AA3D.gha`
- Copies it to `%AppData%\Grasshopper\Libraries\`

You should see:
```
✅  Installed AA3D.gha to C:\Users\<you>\AppData\Roaming\Grasshopper\Libraries
```

---

## Step 4 — Load in Grasshopper

1. **Close Rhino completely** (Grasshopper caches plugin lists at startup).
2. Re-open Rhino 8.
3. Type `Grasshopper` in the Rhino command bar.
4. The **AA3D** tab appears in the GH ribbon. Drag the **AA3D** component onto the canvas.

---

## Step 5 — Use the V2 UI Panel

### Component Input

| Input | Default | Description |
|-------|---------|-------------|
| **UI** ShowUI | `False` | Flip to `True` to open the floating AA3D panel |

### Component Outputs

| Output | Type | Description |
|--------|------|-------------|
| **G** Geometry | `Brep` list | Generated walls, floors, openings, etc. |
| **L** Layers | `string` list | Layer name for each Brep |
| **N** Names | `string` list | Object name for each Brep |
| **JSON** | `string` | Raw AI JSON (for inspection) |
| **Log** | `string` list | Build log |

### Opening the Panel

1. Place the **AA3D** component on the canvas.
2. Connect a **Boolean Toggle** to the **UI** input.
3. Double-click the toggle to flip it to `True` — the **AA3D panel** opens.
4. The panel stays open until you close it. Closing it hides it (not destroyed).
   Re-flipping the toggle brings it back.

### Panel Controls

| Control | Description |
|---------|-------------|
| **Prompt** | Type your architectural description here |
| **Upload Image** | Browse for a floor plan / reference image (PNG, JPG, BMP) |
| **Model** | Ollama model for code generation (default: `qwen2.5-coder:14b`) |
| **Endpoint** | Ollama URL (default: `http://localhost:11434`) |
| **Generate** | Start the AI pipeline |
| **Cancel** | Stop mid-generation |
| **Progress bar** | Shows pipeline stage completion |
| **Stage label** | Shows current stage name and colour |
| **Log** | Live log of all pipeline events |

### 4-Stage Progress Indicator

| Stage | Label | Colour |
|-------|-------|--------|
| 1 | 🟠 AI Calling | Orange |
| 2 | 🔵 JSON Parsed | Blue |
| 3 | 🟣 Geometry Building | Purple |
| 4 | 🟢 Done | Green |

---

## Step 6 — Vision Pipeline (Image Upload)

When you upload a floor plan image:

1. **Vision model** (`llama3.2-vision:11b`) receives the image + full `arch_drawing_library` context.
2. It extracts rooms, walls, doors, windows, columns, and stairs as structured JSON.
3. That JSON is injected into the **code-gen prompt** for `qwen2.5-coder:14b` as authoritative data.
4. Qwen generates the geometry JSON, which `GeometryBuilder` turns into Breps.

**Supported image formats:** PNG, JPG/JPEG, BMP, GIF, TIFF

**Tips:**
- High-resolution scans work best (300+ DPI for hand-drawn plans).
- Ensure dimension strings are legible — vision model reads them directly.
- The vision model follows IS 962:1989 conventions by default (standard Indian architectural drawings).

---

## Step 7 — Make sure Ollama is running

```
ollama serve
```

Verify: open a browser → `http://localhost:11434/api/tags` — you should see a JSON list of your models.

To confirm both models are pulled:
```
ollama list
```

You should see both `qwen2.5-coder:14b` and `llama3.2-vision:11b` in the list.

---

## Troubleshooting

### Panel doesn't open
- Flip **ShowUI** toggle to `False`, then back to `True`.
- Check Rhino command history for assembly-load errors.
- Check `%AppData%\Grasshopper\Libraries\AA3D.gha` exists.

### "No valid JSON found in Ollama response"
- Wire the **JSON** output to a Panel to see what the model returned.
- Try a smaller model: `qwen2.5-coder:7b`.
- If JSON appears inside markdown fences, the extractor handles it automatically.

### Vision step is slow / times out
- `llama3.2-vision:11b` has a 300-second timeout. Large images may push this.
- Resize images to 2048×2048 or smaller for faster analysis.
- Monitor VRAM: if Ollama swaps models, the first call loads the model (~30 s).

### "HttpRequestException" error
- Ollama is not running. Start it: `ollama serve`.
- Or the endpoint is wrong.

### Build fails: "Cannot find RhinoCommon.dll"
- Edit `<RhinoDir>` in `AA3D.csproj` to match your Rhino 8 install path.

### Build fails: "UseWindowsForms not recognized"
- Ensure you are targeting `net48` (not netstandard or netcoreapp).
- VS 2022 with the **.NET desktop workload** installed handles this automatically.

### Build fails: "Newtonsoft.Json not found"
- Right-click project → **Manage NuGet Packages** → **Browse** → install `Newtonsoft.Json` 13.x.

### Geometry appears at wrong location
- All dimensions are in **millimetres**.
- If your Rhino document is in metres, scale geometry: wire **G** into a **Scale** component with factor `0.001`.

---

## Rebuilding after changes

1. Edit any `.cs` file in VS.
2. **Build → Build Solution** (`Ctrl+Shift+B`).
3. **Restart Rhino** to reload the plugin.

During active development, use the `GrasshopperDeveloperSettings` component with "Memory Load GHA" enabled to skip restarts.

---

## File locations summary

| File | Location |
|------|----------|
| Source code | This `AA3D_V2/` folder |
| Compiled plugin | `%AppData%\Grasshopper\Libraries\AA3D.gha` |
| Ollama code model | `%USERPROFILE%\.ollama\models\qwen2.5-coder` |
| Ollama vision model | `%USERPROFILE%\.ollama\models\llama3.2-vision` |

---

## Next steps / extensions

- **Bake to Rhino with layers** — use Elefront or the GhPython bake pattern in `gh_knowledge_base.md`.
- **Multiple floors** — prompt "a 3-storey building"; the AI populates the `floors` array.
- **Custom icon** — replace `null` in `AA3DPluginInfo.Icon` with a 24×24 `Bitmap`.
- **Streaming log** — switch `stream: true` in `OllamaClient.cs` and pipe tokens to `AppendLog`.
- **Larger vision model** — swap `llama3.2-vision:11b` for `llama3.2-vision:90b` for complex drawings.
- **Save panel state** — serialize last prompt/model/endpoint to `%AppData%\AA3D\settings.json`.
