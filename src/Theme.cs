using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Collections.Generic;
using System.Windows.Forms;

namespace Diagnostico
{
    static class Theme
    {
        public static float Scale = 1f;
        public static int S(float v) { return (int)Math.Round(v * Scale); }

        public static Color Bg, TitleBar, Panel, PanelHover, Border, Line, Input, Text, TextDim;
        public static Color Accent, AccentText, Selected, CheckBlue, DotRed, DotYellow, DotGreen;
        // Fundo fixo do banner da tela inicial (não muda com o tema)
        public static readonly Color Brand = Hex("#1F2937");
        public static bool IsDark { get; private set; }
        // Mantém as referências: linhas já abertas também recebem as novas cores.
        public static readonly Color[] PillGreen = new Color[2], PillYellow = new Color[2],
            PillBlue = new Color[2], PillGray = new Color[2], PillRed = new Color[2], PillPurple = new Color[2];

        static Theme() { SetPalette(false, false); }

        static void Pair(Color[] pair, string bg, string fg) { pair[0] = Hex(bg); pair[1] = Hex(fg); }

        // Paleta grafite + verde. Cada cor é única dentro do modo (Apply troca as cores pelo valor),
        // por isso o texto sobre o destaque no claro é #FEFFFF e não #FFFFFF, que é o painel.
        static void SetPalette(bool dark, bool emerald)
        {
            IsDark = dark;
            Bg = Hex(dark ? "#121417" : "#F4F5F7");
            TitleBar = Hex(dark ? "#16191D" : "#ECEEF1");
            Panel = Hex(dark ? "#1C1F24" : "#FFFFFF");
            PanelHover = Hex(dark ? "#262A30" : "#EEF1F3");
            Border = Hex(dark ? "#33383F" : "#D9DCE1");
            Line = Hex(dark ? "#2A2E34" : "#E6E8EB");
            Input = Hex(dark ? "#17191D" : "#F9FAFB");
            Text = Hex(dark ? "#E9ECEF" : "#1F2328");
            TextDim = Hex(dark ? "#A3ABB5" : "#5B636E");
            Accent = Hex(dark ? (emerald ? "#34D399" : "#4ADE80") : (emerald ? "#047857" : "#15803D"));
            AccentText = Hex(dark ? "#0B2A16" : "#FEFFFF");
            Selected = Hex(dark ? "#1F3A2A" : "#E3F1E8");
            CheckBlue = Hex(dark ? "#22A355" : "#1F7A45");
            DotRed = Hex(dark ? "#F59A9A" : "#B42318");
            DotYellow = Hex(dark ? "#E8C36A" : "#8A5A00");
            DotGreen = Hex(dark ? "#6EDC9A" : "#166F3A");
            Pair(PillGreen, dark ? "#1D3B2A" : "#DCF2E3", dark ? "#86E0A8" : "#166534");
            Pair(PillYellow, dark ? "#3F3622" : "#FFF1CC", dark ? "#EDCB7E" : "#7A5200");
            Pair(PillBlue, dark ? "#1F3147" : "#E3EDFB", dark ? "#A3C6F2" : "#2B5A91");
            Pair(PillGray, dark ? "#2B2F35" : "#ECEEF0", dark ? "#C3C9D0" : "#525A64");
            Pair(PillRed, dark ? "#43262A" : "#FBE4E4", dark ? "#FFADB3" : "#A3262D");
            Pair(PillPurple, dark ? "#33264A" : "#EFE7FA", dark ? "#D2B5F3" : "#6B3FA0");
        }

        static Color[] Palette()
        {
            return new[] { Bg, TitleBar, Panel, PanelHover, Border, Line, Input, Text, TextDim,
                Accent, AccentText, Selected, CheckBlue, DotRed, DotYellow, DotGreen,
                PillGreen[0], PillGreen[1], PillYellow[0], PillYellow[1], PillBlue[0], PillBlue[1],
                PillGray[0], PillGray[1], PillRed[0], PillRed[1], PillPurple[0], PillPurple[1] };
        }

        public static Dictionary<int, Color> Configure(AppConfig cfg)
        {
            var before = Palette();
            bool dark = cfg.Tema == "Escuro";
            if (cfg.Tema == "Sistema")
                try
                {
                    object value = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1);
                    dark = Convert.ToInt32(value) == 0;
                }
                catch { dark = false; }
            SetPalette(dark, cfg.CorDestaque == "Esmeralda");
            var after = Palette();
            var map = new Dictionary<int, Color>();
            for (int i = 0; i < before.Length; i++) map[before[i].ToArgb()] = after[i];
            return map;
        }

        static Color Remap(Color color, Dictionary<int, Color> map)
        {
            Color mapped;
            return map.TryGetValue(color.ToArgb(), out mapped) ? mapped : color;
        }

        public static void Apply(Control control, Dictionary<int, Color> map, HashSet<Control> visited)
        {
            if (control == null || control.IsDisposed || !visited.Add(control)) return;
            var drawn = control as DrawnControl;
            if (drawn == null || !drawn.FixedBackColor) control.BackColor = Remap(control.BackColor, map);
            control.ForeColor = Remap(control.ForeColor, map);
            var panel = control as RoundPanel;
            if (panel != null) { panel.Fill = Remap(panel.Fill, map); panel.BorderColor = Remap(panel.BorderColor, map); }
            if (control.IsHandleCreated)
            {
                try
                {
                    Native.SetWindowTheme(control.Handle, IsDark ? "DarkMode_Explorer" : "Explorer", null);
                    if (control is Form) { int value = IsDark ? 1 : 0; Native.DwmSetWindowAttribute(control.Handle, 20, ref value, 4); }
                }
                catch { }
            }
            foreach (Control child in control.Controls) Apply(child, map, visited);
            control.Invalidate(true);
        }

        static string family;
        public static string Family
        {
            get
            {
                if (family != null) return family;
                family = "Segoe UI";
                try
                {
                    using (var fonts = new InstalledFontCollection())
                    {
                        foreach (var f in fonts.Families)
                            if (f.Name == "Inter") { family = "Inter"; break; }
                    }
                }
                catch { }
                return family;
            }
        }

        public static Font F(float size, FontStyle style = FontStyle.Regular)
        {
            return new Font(Family, size, style);
        }

        public static readonly Font Body = F(10.5f);
        public static readonly Font BodyBold = F(10.5f, FontStyle.Bold);
        public static readonly Font Small = F(9f);
        public static readonly Font Label = F(9.5f);
        public static readonly Font Heading = F(12f, FontStyle.Bold);
        public static readonly Font PageTitle = F(16f, FontStyle.Bold);
        public static readonly Font Big = F(18f, FontStyle.Bold);
        public static readonly Font Mono = new Font("Consolas", 10f);

        public static Color Hex(string h)
        {
            h = h.TrimStart('#');
            return Color.FromArgb(Convert.ToInt32(h.Substring(0, 2), 16),
                                  Convert.ToInt32(h.Substring(2, 2), 16),
                                  Convert.ToInt32(h.Substring(4, 2), 16));
        }

        public static GraphicsPath Round(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 1) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void Smooth(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        }

        public static void FillRound(Graphics g, Color c, RectangleF r, float radius)
        {
            using (var p = Round(r, radius)) using (var b = new SolidBrush(c)) g.FillPath(b, p);
        }

        public static void DrawRound(Graphics g, Color c, RectangleF r, float radius, float width = 1)
        {
            using (var p = Round(r, radius)) using (var pen = new Pen(c, width)) g.DrawPath(pen, p);
        }

        public static void Pill(Graphics g, string text, Color[] colors, Rectangle area, Font font, bool alignRight = true)
        {
            var size = System.Windows.Forms.TextRenderer.MeasureText(g, text, font, Size.Empty,
                System.Windows.Forms.TextFormatFlags.NoPadding);
            int w = size.Width + S(20), h = size.Height + S(8);
            int x = alignRight ? area.Right - w : area.X;
            int y = area.Y + (area.Height - h) / 2;
            var r = new Rectangle(x, y, w, h);
            FillRound(g, colors[0], r, h / 2f);
            System.Windows.Forms.TextRenderer.DrawText(g, text, font, r, colors[1],
                System.Windows.Forms.TextFormatFlags.HorizontalCenter | System.Windows.Forms.TextFormatFlags.VerticalCenter |
                System.Windows.Forms.TextFormatFlags.NoPadding);
        }
    }
}
