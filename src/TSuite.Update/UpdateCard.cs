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
    string _hover = "";
    Rectangle _notesRect;
    readonly List<(string Id, Rectangle R)> _hits = new();
    bool _fadeStarted;

    public UpdateCard(UpdateController controller, UpdateTheme theme)
    {
        _c = controller; _t = theme;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false; TopMost = true;
        ClientSize = new Size(W, H);
        BackColor = theme.Bg;
        Opacity = 0;
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.ResizeRedraw, true);
        DoubleBuffered = true;
        var wa = Screen.PrimaryScreen!.WorkingArea;
        Location = new Point(wa.Right - W - 16, wa.Bottom - H - 16);
        using (var gp = RoundRect(new Rectangle(0, 0, W, H), Radius)) Region = new Region(gp);

        _c.StateChanged += OnState;
        _c.ProgressChanged += _ => Redraw();
        _fade.Tick += (_, _) =>
        {
            double p = Math.Min(1, (DateTime.UtcNow - _t0).TotalMilliseconds / 150.0);
            Opacity = p; if (p >= 1) _fade.Stop();
        };
    }

    protected override bool ShowWithoutActivation => true;

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
            else { _scroll = 0; Invalidate(); }
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
        using (var gp = RoundRect(new Rectangle(0, 0, W - 1, H - 1), Radius))
        using (var pen = new Pen(_t.Border, 1)) g.DrawPath(pen, gp);

        var info = _c.Info; var st = _c.State;
        _hits.Clear();
        TextRenderer.DrawText(g, "Güncelleme var", _title, new Point(Pad, Pad), _t.Text, TextFormatFlags.NoPadding);
        string sub = info == null ? "" : $"v{info.CurrentVersion.TrimStart('v')}  →  v{info.NewVersion.TrimStart('v')}";
        TextRenderer.DrawText(g, sub, _small, new Point(Pad, Pad + 28), _t.TextDim, TextFormatFlags.NoPadding);

        // notlar / notes
        _notesRect = new Rectangle(Pad, 68, W - 2 * Pad, 104);
        using (var gp = RoundRect(_notesRect, 8)) using (var b = new SolidBrush(_t.Surface)) g.FillPath(b, gp);
        var inner = Rectangle.Inflate(_notesRect, -10, -8);
        string notes = st == UpdateState.Failed ? "Hata: " + _c.Error : (info?.Notes ?? "");
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
            int bh = Math.Max(16, (int)(inner.Height * ratio));
            int by = inner.Y + (int)((inner.Height - bh) * ((float)_scroll / (_notesTotal - inner.Height)));
            using var b = new SolidBrush(_t.Border);
            g.FillRectangle(b, _notesRect.Right - 6, by, 3, bh);
        }

        // ilerleme / progress
        var pr = new Rectangle(Pad, 184, W - 2 * Pad, 6);
        if (st is UpdateState.Downloading or UpdateState.Launching)
        {
            using (var gp = RoundRect(pr, 3)) using (var b = new SolidBrush(_t.Border)) g.FillPath(b, gp);
            int fw = (int)(pr.Width * (st == UpdateState.Launching ? 1 : _c.Progress));
            if (fw > 6) using (var gp = RoundRect(new Rectangle(pr.X, pr.Y, fw, pr.Height), 3)) using (var b = new SolidBrush(_t.Accent)) g.FillPath(b, gp);
            string pt = st == UpdateState.Launching ? "Yükleniyor…" : $"İndiriliyor… %{(int)(_c.Progress * 100)}";
            TextRenderer.DrawText(g, pt, _small, new Point(Pad, 194), _t.TextDim, TextFormatFlags.NoPadding);
        }

        // butonlar / buttons
        int by2 = H - Pad - 34;
        bool busy = st is UpdateState.Downloading or UpdateState.Launching;
        string primary = st == UpdateState.Failed ? "Tekrar dene" : "Şimdi güncelle";
        var pRect = new Rectangle(W - Pad - 132, by2, 132, 34);
        var lRect = new Rectangle(pRect.X - 8 - 72, by2, 72, 34);
        var sRect = new Rectangle(Pad, by2, 110, 34);
        if (!busy)
        {
            DrawButton(g, pRect, primary, "primary", true);
            DrawButton(g, lRect, "Sonra", "later", false);
            DrawButton(g, sRect, "Bu sürümü atla", "skip", false, dim: true);
        }
        else DrawButton(g, pRect, "Bekleyin…", "busy", true, disabled: true);
    }

    void DrawButton(Graphics g, Rectangle r, string text, string id, bool filled, bool dim = false, bool disabled = false)
    {
        bool hov = _hover == id && !disabled;
        if (!disabled) _hits.Add((id, r));
        using (var gp = RoundRect(r, 8))
        {
            if (filled)
            {
                var col = disabled ? Color.FromArgb(120, _t.Accent) : hov ? _t.AccentHover : _t.Accent;
                using var b = new SolidBrush(col); g.FillPath(b, gp);
            }
            else if (hov) { using var b = new SolidBrush(_t.Surface); g.FillPath(b, gp); }
            if (!filled && !dim) { using var pen = new Pen(_t.Border, 1); g.DrawPath(pen, gp); }
        }
        var fc = filled ? _t.OnAccent : dim ? _t.TextDim : _t.Text;
        TextRenderer.DrawText(g, text, _small, r, fc,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    string HitTest(Point p)
    {
        foreach (var (id, r) in _hits) if (r.Contains(p)) return id;
        return "";
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var h = HitTest(e.Location);
        if (h != _hover) { _hover = h; Cursor = h == "" ? Cursors.Default : Cursors.Hand; Invalidate(); }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e) { _hover = ""; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (_notesRect.Contains(e.Location)) { _scroll -= Math.Sign(e.Delta) * 24; Invalidate(); }
        base.OnMouseWheel(e);
    }

    protected override async void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        switch (HitTest(e.Location))
        {
            case "primary": await _c.StartUpdateAsync(); break;
            case "later": _c.Later(); Close(); break;
            case "skip": _c.Skip(); Close(); break;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _fade.Dispose(); _title.Dispose(); _body.Dispose(); _small.Dispose(); }
        base.Dispose(disposing);
    }
}
