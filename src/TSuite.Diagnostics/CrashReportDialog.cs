using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using TSuite.Update;

namespace TSuite.Diagnostics;

/// <summary>Show on the UI thread, on the next launch, after Install. No network submission occurs here.</summary>
public sealed class CrashReportDialog : Form
{
    readonly UpdateTheme theme;
    readonly Font headingFont = new("Segoe UI Semibold", 13);
    readonly Label heading, explanation, status;
    readonly TextBox preview;
    readonly Button send, copy, close;
    readonly CheckBox automatic;
    readonly CrashReport report;
    bool positioning;
    const string TitleText = "Uygulama beklenmedik şekilde kapandı";

    public CrashReportDialog(CrashReport report, UpdateTheme? theme = null)
    {
        this.report = report; this.theme = theme ?? CrashReporter.Theme;
        Text = AccessibleName = TitleText;
        AutoScaleMode = AutoScaleMode.None;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.None;
        BackColor = this.theme.Bg; ForeColor = this.theme.Text;
        Font = new Font("Segoe UI", 9.5f);
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw, true);
        heading = new Label { Text = TitleText, AccessibleName = TitleText, Font = headingFont };
        explanation = new Label
        {
            Text = "İsterseniz bu hatayı bildirerek düzeltilmesine yardımcı olabilirsiniz. Rapor herkese açık bir GitHub sorunu olarak oluşturulur; kişisel bilgiler maskelenmiştir.",
            ForeColor = this.theme.TextDim
        };
        preview = new TextBox
        {
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false,
            Text = CrashReporter.GetReportText(report).ReplaceLineEndings(Environment.NewLine),
            BackColor = this.theme.Surface, ForeColor = this.theme.Text, BorderStyle = BorderStyle.FixedSingle,
            AccessibleName = "Maskelenmiş hata raporu", TabIndex = 0
        };
        automatic = new CheckBox
        {
            Text = "Hata raporlarını otomatik hazırla", AccessibleName = "Hata raporlarını otomatik hazırla",
            Checked = CrashReporter.AutomaticallyPrepareReports, TabIndex = 1
        };
        status = new Label { AccessibleName = "Rapor durumu", ForeColor = this.theme.TextDim };
        send = new PaintedButton(this, true) { Text = "Raporu gönder", AccessibleName = "Raporu gönder", TabIndex = 2 };
        copy = new PaintedButton(this, false) { Text = "Kopyala", AccessibleName = "Kopyala", TabIndex = 3 };
        close = new PaintedButton(this, false) { Text = "Kapat", AccessibleName = "Kapat", TabIndex = 4, DialogResult = DialogResult.Cancel };
        AcceptButton = send; CancelButton = close;
        send.Click += (_, _) => Run(() =>
        {
            Process.Start(new ProcessStartInfo(CrashReporter.BuildIssueUrl(report)) { UseShellExecute = true });
            CrashReporter.MarkReported(report);
            Close();
        });
        copy.Click += (_, _) => Run(() => { Clipboard.SetText(CrashReporter.GetReportText(report)); status.Text = "Rapor kopyalandı."; });
        close.Click += (_, _) => Close();
        automatic.CheckedChanged += (_, _) => Run(() => CrashReporter.AutomaticallyPrepareReports = automatic.Checked);
        Controls.AddRange([heading, explanation, preview, automatic, status, send, copy, close]);
        ClientSize = new Size(620, 530);
    }

    void Run(Action action)
    {
        try { action(); }
        catch (Exception ex) { Log.Warn(ex.Message); status.Text = "İşlem tamamlanamadı. Tekrar deneyebilirsiniz."; }
    }
    int S(int n) => (int)Math.Round(n * DeviceDpi / 96d);
    void Arrange()
    {
        if (positioning) return;
        positioning = true;
        try
        {
            var area = Screen.FromControl(Owner ?? this).WorkingArea;
            ClientSize = new Size(Math.Min(S(620), area.Width), Math.Min(S(530), area.Height));
            int pad = S(18), width = ClientSize.Width - pad * 2;
            heading.SetBounds(pad, pad, width, S(36));
            explanation.SetBounds(pad, S(59), width, S(66));
            int bottom = ClientSize.Height;
            preview.SetBounds(pad, S(131), width, Math.Max(S(32), bottom - S(265)));
            automatic.SetBounds(pad, bottom - S(124), width, S(28));
            status.SetBounds(pad, bottom - S(93), width, S(30));
            send.SetBounds(ClientSize.Width - pad - S(140), bottom - S(54), S(140), S(36));
            copy.SetBounds(pad + S(100), bottom - S(54), S(94), S(36));
            close.SetBounds(pad, bottom - S(54), S(94), S(36));
            Location = new Point(Math.Clamp(Left, area.Left, area.Right - Width), Math.Clamp(Top, area.Top, area.Bottom - Height));
            Invalidate(true);
        }
        finally { positioning = false; }
    }
    protected override void OnLoad(EventArgs e) { base.OnLoad(e); Arrange(); }
    protected override void OnDpiChanged(DpiChangedEventArgs e) { base.OnDpiChanged(e); Arrange(); }
    protected override void OnFormClosed(FormClosedEventArgs e) { CrashReporter.MarkDismissed(report); base.OnFormClosed(e); }
    static GraphicsPath Round(Rectangle rect, int radius)
    {
        int d = Math.Max(2, radius * 2);
        var p = new GraphicsPath();
        p.AddArc(rect.Left, rect.Top, d, d, 180, 90); p.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
        p.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90); p.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
        p.CloseFigure(); return p;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Round(new Rectangle(0, 0, Width - 1, Height - 1), S(12));
        using var pen = new Pen(theme.Border);
        e.Graphics.DrawPath(pen, path);
    }
    sealed class PaintedButton(CrashReportDialog dialog, bool primary) : Button
    {
        bool hover;
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var t = dialog.theme; var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(t.Bg);
            using var path = Round(Rectangle.Inflate(ClientRectangle, -dialog.S(2), -dialog.S(2)), dialog.S(6));
            using var brush = new SolidBrush(primary ? hover ? t.AccentHover : t.Accent : hover ? t.Surface : t.Bg);
            using var pen = new Pen(Focused ? t.Accent : t.Border, Focused ? dialog.S(2) : 1);
            g.FillPath(brush, path); g.DrawPath(pen, path);
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, primary ? t.OnAccent : t.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { headingFont.Dispose(); Font.Dispose(); }
        base.Dispose(disposing);
    }
}
