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
        public static readonly Color Brand = Hex("#075B5E"), Turquoise = Hex("#2DA8AD");
        public static bool IsDark { get; private set; }
        // Mantém as referências: linhas já abertas também recebem as novas cores.
        public static readonly Color[] PillGreen = new Color[2], PillYellow = new Color[2],
            PillBlue = new Color[2], PillGray = new Color[2], PillRed = new Color[2], PillPurple = new Color[2];

        static Theme() { SetPalette(false, false); }

        static void Pair(Color[] pair, string bg, string fg) { pair[0] = Hex(bg); pair[1] = Hex(fg); }

        static void SetPalette(bool dark, bool petroleum)
        {
            IsDark = dark;
            Bg = Hex(dark ? "#101E23" : "#F2F7F6");
            TitleBar = Hex(dark ? "#13272B" : "#EAF3F1");
            Panel = Hex(dark ? "#1A2E34" : "#FFFFFF");
            PanelHover = Hex(dark ? "#243F46" : "#E8F4F2");
            Border = Hex(dark ? "#355159" : "#CADDD9");
            Line = Hex(dark ? "#2D464E" : "#E1EBE8");
            Input = Hex(dark ? "#14262C" : "#F8FBFA");
            Text = Hex(dark ? "#ECF5F4" : "#173C40");
            TextDim = Hex(dark ? "#ADC4C7" : "#526E72");
            Accent = Hex(dark ? (petroleum ? "#85CAC5" : "#46C3C6") : (petroleum ? "#075B5E" : "#087D83"));
            AccentText = Hex(dark ? "#063C40" : "#FEFFFF");
            Selected = Hex(dark ? "#244C52" : "#DCEFEA");
            CheckBlue = Hex(dark ? "#248B91" : "#14777D");
            DotRed = Hex(dark ? "#F49494" : "#B73340");
            DotYellow = Hex(dark ? "#EDC66F" : "#926013");
            DotGreen = Hex(dark ? "#72D3A7" : "#18744D");
            Pair(PillGreen, dark ? "#1E4238" : "#DEF3E8", dark ? "#8AE0B6" : "#19603E");
            Pair(PillYellow, dark ? "#453C26" : "#FFF0CF", dark ? "#F2CF83" : "#80540C");
            Pair(PillBlue, dark ? "#223C51" : "#E4EEF9", dark ? "#A5CEFA" : "#315D8C");
            Pair(PillGray, dark ? "#2B4148" : "#EAF0EE", dark ? "#C0D3D5" : "#526765");
            Pair(PillRed, dark ? "#482E35" : "#FBE6E7", dark ? "#FFB0B5" : "#A32F3B");
            Pair(PillPurple, dark ? "#3A294F" : "#F0E8FA", dark ? "#D5B4F5" : "#7141A3");
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
            SetPalette(dark, cfg.CorDestaque == "Petróleo");
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
