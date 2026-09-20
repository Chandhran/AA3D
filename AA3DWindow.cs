using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace AA3D
{
    // ═══════════════════════════════════════════════════════════════════════════
    //  AA3DWindow — WinForms floating panel for the AA3D Grasshopper plugin.
    //
    //  Provides:
    //    • Prompt TextBox    — type or paste your architectural description
    //    • Image Upload      — browse for a floor plan / elevation image
    //                          (triggers llama3.2-vision analysis chain)
    //    • 4-stage progress  — AI Calling → JSON Parsed → Geometry Building → Done
    //    • ProgressBar       — percentage fill
    //    • Log RichTextBox   — build log output from the GH component
    //    • Generate button   — fires Generate() callback wired to AA3DComponent
    //    • Cancel button     — fires Cancel() callback
    // ═══════════════════════════════════════════════════════════════════════════

    public class AA3DWindow : Form
    {
        // ── Public event delegates ───────────────────────────────────────────
        public event Action<string, string, string, string> GenerateRequested;
        // args: (prompt, imagePath, model, endpoint)

        public event Action CancelRequested;

        // ── Stage constants ──────────────────────────────────────────────────
        public const int STAGE_IDLE     = 0;
        public const int STAGE_AI_CALL  = 1;
        public const int STAGE_JSON     = 2;
        public const int STAGE_GEOMETRY = 3;
        public const int STAGE_DONE     = 4;

        // ── Controls ─────────────────────────────────────────────────────────
        private TextBox     _promptBox;
        private TextBox     _imagePathBox;
        private Button      _browseBtn;
        private TextBox     _modelBox;
        private TextBox     _endpointBox;
        private Button      _generateBtn;
        private Button      _cancelBtn;
        private Panel       _stagePanel;
        private Label[]     _stageLabels;
        private ProgressBar _progressBar;
        private Label       _percentLabel;
        private Label       _statusLabel;
        private RichTextBox _logBox;

        // Stage names and colors
        private static readonly string[] STAGE_NAMES = {
            "Idle",
            "AI Calling…",
            "JSON Parsed",
            "Geometry Building",
            "Done ✓"
        };
        private static readonly Color[] STAGE_ACTIVE_COLORS = {
            Color.FromArgb(60, 60, 60),
            Color.FromArgb(230, 120, 30),
            Color.FromArgb(30, 140, 200),
            Color.FromArgb(180, 80, 200),
            Color.FromArgb(30, 160, 80)
        };

        // ── Constructor ──────────────────────────────────────────────────────
        public AA3DWindow()
        {
            InitializeComponent();
        }

        // ── Design ───────────────────────────────────────────────────────────
        private void InitializeComponent()
        {
            this.SuspendLayout();

            // Form properties
            this.Text            = "AA3D — AI Architectural Generator";
            this.Size            = new Size(540, 740);
            this.MinimumSize     = new Size(480, 640);
            this.FormBorderStyle = FormBorderStyle.SizableToolWindow;
            this.StartPosition   = FormStartPosition.Manual;
            this.Location        = new Point(40, 40);
            this.TopMost         = false;
            this.BackColor       = Color.FromArgb(30, 30, 30);
            this.ForeColor       = Color.FromArgb(220, 220, 220);

            int pad = 10;
            int y   = pad;
            int w   = this.ClientSize.Width - 2 * pad;

            // ── Title label ──────────────────────────────────────────────────
            var title = new Label {
                Text      = "AA3D  ·  Local AI Architectural Generator",
                Location  = new Point(pad, y),
                Size      = new Size(w, 22),
                Font      = new Font("Segoe UI", 10f, FontStyle.Bold),
                ForeColor = Color.FromArgb(180, 200, 240),
            };
            this.Controls.Add(title);
            y += 28;

            // ── Prompt section ───────────────────────────────────────────────
            AddLabel("Prompt  (architectural description):", pad, ref y);

            _promptBox = new TextBox {
                Location   = new Point(pad, y),
                Size       = new Size(w, 90),
                Multiline  = true,
                ScrollBars = ScrollBars.Vertical,
                Text       = "A simple rectangular room 5m × 4m with one window on the south wall and one door on the west wall, flat roof.",
                BackColor  = Color.FromArgb(50, 50, 50),
                ForeColor  = Color.FromArgb(230, 230, 230),
                Font       = new Font("Segoe UI", 9f),
                BorderStyle= BorderStyle.FixedSingle,
            };
            this.Controls.Add(_promptBox);
            y += 96;

            // ── Image section ────────────────────────────────────────────────
            AddLabel("Reference Image  (floor plan / elevation — optional):", pad, ref y);

            _imagePathBox = new TextBox {
                Location   = new Point(pad, y),
                Size       = new Size(w - 82, 24),
                Text       = "Browse for a floor plan image to upload to llama3.2-vision…",
                BackColor  = Color.FromArgb(50, 50, 50),
                ForeColor  = Color.FromArgb(120, 120, 120),
                Font       = new Font("Segoe UI", 8.5f),
                BorderStyle= BorderStyle.FixedSingle,
                ReadOnly   = true,
            };
            this.Controls.Add(_imagePathBox);

            _browseBtn = new Button {
                Location  = new Point(pad + w - 78, y),
                Size      = new Size(78, 24),
                Text      = "Browse…",
                BackColor = Color.FromArgb(60, 80, 120),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font      = new Font("Segoe UI", 8.5f),
            };
            _browseBtn.FlatAppearance.BorderColor = Color.FromArgb(90, 110, 160);
            _browseBtn.Click += BrowseBtn_Click;
            this.Controls.Add(_browseBtn);
            y += 30;

            // ── Model / Endpoint section ─────────────────────────────────────
            AddLabel("Ollama Model:", pad, ref y);
            _modelBox = AddTextBox(pad, y, w, "qwen2.5-coder:14b"); y += 30;

            AddLabel("Ollama Endpoint:", pad, ref y);
            _endpointBox = AddTextBox(pad, y, w, "http://localhost:11434"); y += 30;

            // ── Buttons ──────────────────────────────────────────────────────
            _generateBtn = new Button {
                Location  = new Point(pad, y),
                Size      = new Size(w - 94, 32),
                Text      = "▶  Generate",
                BackColor = Color.FromArgb(30, 130, 60),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font      = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            };
            _generateBtn.FlatAppearance.BorderColor = Color.FromArgb(50, 170, 90);
            _generateBtn.Click += GenerateBtn_Click;
            this.Controls.Add(_generateBtn);

            _cancelBtn = new Button {
                Location  = new Point(pad + w - 90, y),
                Size      = new Size(90, 32),
                Text      = "✕  Cancel",
                BackColor = Color.FromArgb(140, 40, 40),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font      = new Font("Segoe UI", 9f),
                Enabled   = false,
            };
            _cancelBtn.FlatAppearance.BorderColor = Color.FromArgb(180, 60, 60);
            _cancelBtn.Click += CancelBtn_Click;
            this.Controls.Add(_cancelBtn);
            y += 40;

            // ── 4-stage progress panel ───────────────────────────────────────
            _stagePanel = new Panel {
                Location  = new Point(pad, y),
                Size      = new Size(w, 46),
                BackColor = Color.FromArgb(22, 22, 22),
            };
            this.Controls.Add(_stagePanel);

            _stageLabels = new Label[4];
            int stageW = w / 4;
            string[] names = { "AI Calling", "JSON Parsed", "Building", "Done" };
            for (int i = 0; i < 4; i++)
            {
                int idx = i;
                var lbl = new Label {
                    Text      = names[i],
                    Location  = new Point(idx * stageW, 0),
                    Size      = new Size(stageW - 2, 46),
                    TextAlign = ContentAlignment.MiddleCenter,
                    Font      = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                    BackColor = Color.FromArgb(40, 40, 40),
                    ForeColor = Color.FromArgb(100, 100, 100),
                    BorderStyle= BorderStyle.FixedSingle,
                };
                _stagePanel.Controls.Add(lbl);
                _stageLabels[i] = lbl;
            }
            y += 52;

            // ── Progress bar ─────────────────────────────────────────────────
            _progressBar = new ProgressBar {
                Location = new Point(pad, y),
                Size     = new Size(w - 50, 18),
                Minimum  = 0,
                Maximum  = 100,
                Value    = 0,
                Style    = ProgressBarStyle.Continuous,
            };
            this.Controls.Add(_progressBar);

            _percentLabel = new Label {
                Location  = new Point(pad + w - 46, y),
                Size      = new Size(46, 18),
                Text      = "0%",
                Font      = new Font("Segoe UI", 8f),
                ForeColor = Color.FromArgb(180, 180, 180),
                TextAlign = ContentAlignment.MiddleRight,
            };
            this.Controls.Add(_percentLabel);
            y += 24;

            // ── Status label ─────────────────────────────────────────────────
            _statusLabel = new Label {
                Location  = new Point(pad, y),
                Size      = new Size(w, 18),
                Text      = "Ready. Flip Run = True in GH, or click Generate.",
                Font      = new Font("Segoe UI", 8f, FontStyle.Italic),
                ForeColor = Color.FromArgb(150, 150, 150),
            };
            this.Controls.Add(_statusLabel);
            y += 22;

            // ── Log area ─────────────────────────────────────────────────────
            AddLabel("Build Log:", pad, ref y);
            _logBox = new RichTextBox {
                Location   = new Point(pad, y),
                Anchor     = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                BackColor  = Color.FromArgb(20, 20, 20),
                ForeColor  = Color.FromArgb(180, 220, 180),
                Font       = new Font("Consolas", 8.5f),
                ReadOnly   = true,
                BorderStyle= BorderStyle.FixedSingle,
                ScrollBars = RichTextBoxScrollBars.Vertical,
            };
            // Stretch log box to fill remaining space
            this.Resize += (s, e) => {
                _logBox.Size = new Size(
                    this.ClientSize.Width - 2 * pad,
                    this.ClientSize.Height - _logBox.Top - pad);
            };
            _logBox.Size = new Size(w, Math.Max(80, this.ClientSize.Height - y - pad));
            this.Controls.Add(_logBox);

            this.ResumeLayout(false);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private void AddLabel(string text, int x, ref int y)
        {
            var lbl = new Label {
                Text      = text,
                Location  = new Point(x, y),
                Size      = new Size(this.ClientSize.Width - 2 * x, 18),
                Font      = new Font("Segoe UI", 8.5f),
                ForeColor = Color.FromArgb(160, 175, 200),
            };
            this.Controls.Add(lbl);
            y += 20;
        }

        private TextBox AddTextBox(int x, int y, int w, string defaultText)
        {
            var tb = new TextBox {
                Location   = new Point(x, y),
                Size       = new Size(w, 24),
                Text       = defaultText,
                BackColor  = Color.FromArgb(50, 50, 50),
                ForeColor  = Color.FromArgb(220, 220, 220),
                Font       = new Font("Segoe UI", 9f),
                BorderStyle= BorderStyle.FixedSingle,
            };
            this.Controls.Add(tb);
            return tb;
        }

        // ── Button events ─────────────────────────────────────────────────────

        private void BrowseBtn_Click(object sender, EventArgs e)
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title  = "Select Floor Plan / Elevation Image";
                dlg.Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp;*.tiff;*.gif|All Files|*.*";
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    _imagePathBox.Text      = dlg.FileName;
                    _imagePathBox.ForeColor = Color.FromArgb(200, 200, 200);
                }
            }
        }

        private void GenerateBtn_Click(object sender, EventArgs e)
        {
            string prompt   = _promptBox.Text.Trim();
            string imgPath  = _imagePathBox.Text.Trim();
            string model    = _modelBox.Text.Trim();
            string endpoint = _endpointBox.Text.Trim();

            if (string.IsNullOrEmpty(prompt))
            {
                MessageBox.Show("Please enter an architectural description.", "AA3D",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            SetButtonState(generating: true);
            GenerateRequested?.Invoke(prompt, imgPath, model, endpoint);
        }

        private void CancelBtn_Click(object sender, EventArgs e)
        {
            CancelRequested?.Invoke();
            SetButtonState(generating: false);
        }

        // ── Thread-safe public API ────────────────────────────────────────────

        /// <summary>Advance the 4-stage progress display. stage = 1–4.</summary>
        public void UpdateStage(int stage)
        {
            if (InvokeRequired) { Invoke(new Action<int>(UpdateStage), stage); return; }

            // Update stage label colors
            for (int i = 0; i < _stageLabels.Length; i++)
            {
                bool active = (i + 1) <= stage;
                _stageLabels[i].BackColor = active
                    ? STAGE_ACTIVE_COLORS[i + 1]
                    : Color.FromArgb(40, 40, 40);
                _stageLabels[i].ForeColor = active
                    ? Color.White
                    : Color.FromArgb(100, 100, 100);
            }

            // Progress bar
            int pct = stage >= STAGE_DONE
                ? 100
                : (int)(stage / 4.0 * 100);
            _progressBar.Value = Math.Min(100, pct);
            _percentLabel.Text = pct + "%";

            // Status text
            _statusLabel.Text = stage < STAGE_DONE
                ? STAGE_NAMES[stage] + "   (" + pct + "%)"
                : "Done — geometry ready in Grasshopper.";
            _statusLabel.ForeColor = stage == STAGE_DONE
                ? Color.FromArgb(80, 200, 120)
                : Color.FromArgb(200, 170, 80);

            if (stage == STAGE_DONE)
                SetButtonState(generating: false);
        }

        /// <summary>Append a line to the log area (thread-safe).</summary>
        public void AppendLog(string message)
        {
            if (InvokeRequired) { Invoke(new Action<string>(AppendLog), message); return; }
            _logBox.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "]  " + message + "\n");
            _logBox.ScrollToCaret();
        }

        /// <summary>Show an error in the log and reset to idle state.</summary>
        public void ShowError(string message)
        {
            if (InvokeRequired) { Invoke(new Action<string>(ShowError), message); return; }
            AppendLog("ERROR: " + message);
            _statusLabel.Text      = "Error — see log.";
            _statusLabel.ForeColor = Color.FromArgb(220, 80, 80);
            SetButtonState(generating: false);
        }

        /// <summary>Reset progress display to idle.</summary>
        public void ResetProgress()
        {
            if (InvokeRequired) { Invoke(new Action(ResetProgress)); return; }
            for (int i = 0; i < _stageLabels.Length; i++)
            {
                _stageLabels[i].BackColor = Color.FromArgb(40, 40, 40);
                _stageLabels[i].ForeColor = Color.FromArgb(100, 100, 100);
            }
            _progressBar.Value = 0;
            _percentLabel.Text = "0%";
            _statusLabel.Text  = "Ready.";
            _statusLabel.ForeColor = Color.FromArgb(150, 150, 150);
        }

        /// <summary>Clear the log area.</summary>
        public void ClearLog()
        {
            if (InvokeRequired) { Invoke(new Action(ClearLog)); return; }
            _logBox.Clear();
        }

        // ── Properties exposed to AA3DComponent ──────────────────────────────

        public string Prompt   => _promptBox.Text.Trim();
        public string ImagePath => _imagePathBox.Text.Trim();
        public string ModelName => _modelBox.Text.Trim();
        public string Endpoint  => _endpointBox.Text.Trim();

        // ── Internal helpers ──────────────────────────────────────────────────

        private void SetButtonState(bool generating)
        {
            _generateBtn.Enabled = !generating;
            _cancelBtn.Enabled   = generating;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Don't destroy; hide instead, so the component can reopen it.
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                this.Hide();
            }
            base.OnFormClosing(e);
        }
    }
}
