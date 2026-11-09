using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace TSuite.Update;

/// <summary>
/// Temaya uygun, ozel boyamali, kenarliksiz "Guncelleme var" karti.
/// 380px genislik, 12px yaricap, 1px kenarlik, 16px ic bosluk, 150ms fade-in, cift tamponlu.
/// </summary>
public sealed class UpdateCard : Form
{
    const int W = 380, H = 258, Pad = 16, Radius = 12;
    readonly UpdateController _c;
    readonly UpdateTheme _t;
    readonly Font _title = new("Segoe UI Semibold", 13f, FontStyle.Regular, GraphicsUnit.Point);
    readonly Font _body = new("Segoe UI", 9.5f, FontStyle.Regular, GraphicsUnit.Point);
    readonly Font _small = new("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
    readonly System.Windows.Forms.Timer _fade = new() { Interval = 15 };
    readonly DateTime _t0 = DateTime.UtcNow;
    int _scroll, _notesTotal;
    Rectangle _notesRect;
    readonly CardButton _primary, _later, _skip;
    bool _positioning;
    bool _fadeStarted;

    public UpdateCard(UpdateController controller, UpdateTheme theme)
    {
        _c = controller; _t = theme;
        AutoScaleMode = AutoScaleMode.None; // All custom geometry is scaled explicitly.
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false; TopMost = true;
        ClientSize = new Size(W, H);
        BackColor = theme.Bg;
        Opacity = 0;
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.ResizeRedraw, true);
        DoubleBuffered = true;
        _primary = new CardButton(this, true) { TabIndex = 0 };
        _later = new CardButton(this, false) { Text = "Sonra", AccessibleName = "Sonra", TabIndex = 1 };
        _skip = new CardButton(this, false, true) { Text = "Bu sürümü atla", AccessibleName = "Bu sürümü atla", TabIndex = 2 };
        Controls.AddRange([_primary, _later, _skip]);
        _primary.Click += async (_, _) => { if (!Busy) await _c.StartUpdateAsync(); };
        _later.Click += (_, _) => Later();
        _skip.Click += (_, _) => { _c.Skip(); Close(); };
        UpdateButtons();

        _c.StateChanged += OnState;
        _c.ProgressChanged += OnProgress;
        _fade.Tick += (_, _) =>
        {
            double p = Math.Min(1, (DateTime.UtcNow - _t0).TotalMilliseconds / 150.0);
            Opacity = p; if (p >= 1) _fade.Stop();
        };
    }

    protected override bool ShowWithoutActivation => true;

    bool Busy => _c.State is UpdateState.Downloading or UpdateState.Launching;
    int S(int value) => (int)Math.Round(value * DeviceDpi / 96.0);
    Rectangle R(int x, int y, int width, int height) => new(S(x), S(y), S(width), S(height));
    Point P(int x, int y) => new(S(x), S(y));

    protected override void OnLoad(EventArgs e)
    {
        UpdateButtons();
        PositionCard();
        base.OnLoad(e);
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        PositionCard();
    }

    void PositionCard()
    {
        if (_positioning) return;
        _positioning = true;
        try
        {
            var wa = (Owner == null ? Screen.PrimaryScreen! : Screen.FromControl(Owner)).WorkingArea;
            // Move onto the destination monitor first so DeviceDpi reflects that monitor.
            Location = new Point(wa.Right - Math.Min(S(W), wa.Width), wa.Bottom - Math.Min(S(H), wa.Height));
            int margin = S(Pad);
            ClientSize = new Size(Math.Min(S(W), Math.Max(1, wa.Width - 2 * margin)),
                                  Math.Min(S(H), Math.Max(1, wa.Height - 2 * margin)));
            Location = new Point(Math.Clamp(wa.Right - Width - margin, wa.Left, wa.Right - Width),
                                 Math.Clamp(wa.Bottom - Height - margin, wa.Top, wa.Bottom - Height));
            using var gp = RoundRect(ClientRectangle, Math.Min(S(Radius), Math.Min(Width, Height) / 2));
            var oldRegion = Region;
            Region = new Region(gp);
            oldRegion?.Dispose();
            _primary.Bounds = R(W - Pad - 132, H - Pad - 34, 132, 34);
            _later.Bounds = R(W - Pad - 132 - 8 - 72, H - Pad - 34, 72, 34);
            _skip.Bounds = R(Pad, H - Pad - 34, 110, 34);
            Invalidate(true);
        }
        finally { _positioning = false; }
    }

    void Later() { if (_c.Info?.IsRequired == true) return; _c.Later(); Close(); }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape) { Later(); return true; }
        if (keyData == Keys.Enter) { if (!Busy) _primary.PerformClick(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    void UpdateButtons()
    {
        _primary.Enabled = !Busy;
        _primary.Text = Busy ? "Bekleyin…" : _c.State == UpdateState.Failed ? "Tekrar dene" : "Şimdi güncelle";
        _primary.AccessibleName = _primary.Text;
        _later.Visible = _skip.Visible = !Busy && _c.Info?.IsRequired != true;
        Invalidate(true);
    }

    void OnProgress(double _) => Redraw();

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (_fadeStarted) return;
        _fadeStarted = true; _fade.Start();
    }

    void Redraw() { if (IsHandleCreated) BeginInvoke(Invalidate); }

    void OnState()
    {
        if (!IsHandleCreated) return;
        BeginInvoke(() =>
        {
            if (_c.State is UpdateState.Idle && _c.Info == null) Close();
            else { _scroll = 0; UpdateButtons(); }
        });
    }

    static GraphicsPath RoundRect(Rectangle r, int rad)
    {
        int d = rad * 2; var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure(); return p;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.Clear(_t.Bg);
        using (var gp = RoundRect(new Rectangle(0, 0, ClientSize.Width - 1, ClientSize.Height - 1), S(Radius)))
        using (var pen = new Pen(_t.Border, 1)) g.DrawPath(pen, gp);

        var info = _c.Info; var st = _c.State;
        TextRenderer.DrawText(g, "Güncelleme var", _title, P(Pad, Pad), _t.Text, TextFormatFlags.NoPadding);
        string sub = info == null ? "" : $"v{info.CurrentVersion.TrimStart('v')}  →  v{info.NewVersion.TrimStart('v')}";
        TextRenderer.DrawText(g, sub, _small, P(Pad, Pad + 28), _t.TextDim, TextFormatFlags.NoPadding);

        // notlar / notes
        _notesRect = R(Pad, 68, W - 2 * Pad, 104);
        using (var gp = RoundRect(_notesRect, S(8))) using (var b = new SolidBrush(_t.Surface)) g.FillPath(b, gp);
        var inner = Rectangle.Inflate(_notesRect, -S(10), -S(8));
        string notes = st == UpdateState.Failed ? "Hata: " + _c.Error : (info?.Notes ?? "");
        if (info?.IsRequired == true) notes = "Bu s\u00fcr\u00fcm art\u0131k desteklenmiyor; devam etmek i\u00e7in g\u00fcncelleyin.\n\n" + notes;
        var flags = TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.Left | TextFormatFlags.Top;
        var sz = TextRenderer.MeasureText(g, notes, _body, new Size(inner.Width, int.MaxValue), flags);
        _notesTotal = sz.Height;
        _scroll = Math.Clamp(_scroll, 0, Math.Max(0, _notesTotal - inner.Height));
        var oldClip = g.Clip; g.SetClip(inner);
        TextRenderer.DrawText(g, notes, _body, new Rectangle(inner.X, inner.Y - _scroll, inner.Width, _notesTotal + 4),
            st == UpdateState.Failed ? Color.FromArgb(0xE5, 0x6B, 0x6B) : _t.Text, flags);
        g.Clip = oldClip;
        if (_notesTotal > inner.Height) // kaydirma cubugu
        {
            float ratio = (float)inner.Height / _notesTotal;
            int bh = Math.Max(S(16), (int)(inner.Height * ratio));
            int by = inner.Y + (int)((inner.Height - bh) * ((float)_scroll / (_notesTotal - inner.Height)));
            using var b = new SolidBrush(_t.Border);
            g.FillRectangle(b, _notesRect.Right - S(6), by, S(3), bh);
        }

        // ilerleme / progress
        var pr = R(Pad, 184, W - 2 * Pad, 6);
        if (st is UpdateState.Downloading or UpdateState.Launching)
        {
            using (var gp = RoundRect(pr, S(3))) using (var b = new SolidBrush(_t.Border)) g.FillPath(b, gp);
            int fw = (int)(pr.Width * (st == UpdateState.Launching ? 1 : _c.Progress));
            if (fw > S(6)) using (var gp = RoundRect(new Rectangle(pr.X, pr.Y, fw, pr.Height), S(3))) using (var b = new SolidBrush(_t.Accent)) g.FillPath(b, gp);
            string pt = st == UpdateState.Launching ? "Yükleniyor…" : $"İndiriliyor… %{(int)(_c.Progress * 100)}";
            TextRenderer.DrawText(g, pt, _small, P(Pad, 194), _t.TextDim, TextFormatFlags.NoPadding);
        }

    }

    sealed class CardButton(UpdateCard card, bool filled, bool dim = false) : Button
    {
        bool _hover;
        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(card._t.Bg);
            var r = Rectangle.Inflate(ClientRectangle, -card.S(3), -card.S(3));
            using var gp = RoundRect(r, card.S(6));
            if (filled || _hover)
            {
                var color = filled ? (!Enabled ? Color.FromArgb(120, card._t.Accent) : _hover ? card._t.AccentHover : card._t.Accent) : card._t.Surface;
                using var brush = new SolidBrush(color);
                g.FillPath(brush, gp);
            }
            if (!filled && !dim)
            {
                using var border = new Pen(card._t.Border, card.S(1));
                g.DrawPath(border, gp);
            }
            if (Focused)
            {
                using var ring = RoundRect(Rectangle.Inflate(ClientRectangle, -card.S(1), -card.S(1)), card.S(8));
                using var pen = new Pen(card._t.Accent, card.S(2));
                g.DrawPath(pen, ring);
            }
            TextRenderer.DrawText(g, Text, card._small, r, filled ? card._t.OnAccent : dim ? card._t.TextDim : card._t.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (_notesRect.Contains(e.Location)) { _scroll -= Math.Sign(e.Delta) * S(24); Invalidate(); }
        base.OnMouseWheel(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _c.StateChanged -= OnState; _c.ProgressChanged -= OnProgress; _fade.Dispose(); _title.Dispose(); _body.Dispose(); _small.Dispose(); }
        base.Dispose(disposing);
    }
}
