# AA3D Grasshopper Plugin V2 — End-User Install Guide

## Requirements
- **Rhino 8** for Windows (Grasshopper is included)
- **Ollama** running locally — https://ollama.com/

## Step 1 — Download
Download `AA3D.gha` from the **Releases** page (or the **Actions → Artifacts** section) of the GitHub repository.

## Step 2 — Copy to Grasshopper
1. Close Rhino completely.
2. Copy `AA3D.gha` to:
   ```
   %AppData%\Grasshopper\Libraries\
   ```
   (Paste that path directly into Windows Explorer address bar — it expands automatically.)

## Step 3 — Pull the AI models (one-time setup)
Open a Command Prompt and run:
```
ollama pull qwen2.5-coder:14b
ollama pull llama3.2-vision:11b
```
This downloads ~9 GB total. Only needed once.

## Step 4 — Start Ollama
Before using the plugin, make sure Ollama is running:
```
ollama serve
```
You can verify at: http://localhost:11434/api/tags

## Step 5 — Load in Grasshopper
1. Open Rhino 8.
2. Type `Grasshopper` in the command bar.
3. The **AA3D** tab appears in the GH ribbon.
4. Drag the **AA3D** component onto the canvas.

## Step 6 — Use the Plugin
1. Connect a **Boolean Toggle** to the **UI** input.
2. Flip the toggle to `True` — the **AA3D floating panel** opens.
3. Type your architectural description in the **Prompt** field.
4. (Optional) Click **Upload Image** to analyze a floor plan.
5. Click **Generate** — watch the 4-stage progress bar.
6. Geometry appears in Grasshopper and Rhino viewport.

## Troubleshooting
- **AA3D tab missing** — confirm `AA3D.gha` is in `%AppData%\Grasshopper\Libraries\` and restart Rhino.
- **"HttpRequestException"** — Ollama is not running. Run `ollama serve` in CMD.
- **Panel doesn't open** — flip the ShowUI toggle off and back on.
- **Geometry in wrong units** — AA3D outputs millimetres. Use a Scale component (×0.001) if your Rhino doc is in metres.
