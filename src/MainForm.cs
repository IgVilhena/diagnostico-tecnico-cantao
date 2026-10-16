using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using System.Collections.Generic;

namespace Diagnostico
{
    public interface IPage
    {
        string Title { get; }
        void OnEnter();
        void OnLeave();
    }

    // Janela sem borda: barra de título fina + barra lateral de navegação + conteúdo.
    public class MainForm : Form
    {
        public const int PageHome = 0, PageAnalysis = 1, PageEdit = 2, PagePdf = 3, PageConfig = 4;

        readonly TitleBar titleBar;
        readonly NavRail rail;
        readonly Panel host = new Panel();
        readonly Control[] pages = new Control[5];
        Control current;
        bool hotkeysOk;

        public MainForm()
        {
            Text = "Diagnóstico Técnico" + (SystemInfo.IsWinPE ? " · WinPE" : "");
            FormBorderStyle = FormBorderStyle.None;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.Body;
            DoubleBuffered = true;
            StartPosition = FormStartPosition.CenterScreen;
            var wa = Screen.PrimaryScreen.WorkingArea;
            Size = new Size(Math.Min(wa.Width - 40, Theme.S(1420)), Math.Min(wa.Height - 40, Theme.S(880)));
            MinimumSize = new Size(Theme.S(1080), Theme.S(640));
            Padding = new Padding(Theme.S(4));
            KeyPreview = true;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            Session.MainWindow = this;

            titleBar = new TitleBar(this) { Dock = DockStyle.Top, Height = Theme.S(46) };
            rail = new NavRail(this) { Dock = DockStyle.Left };
            rail.Selected += i => Go(i);

            host.Dock = DockStyle.Fill;
            host.BackColor = Theme.Bg;

            Controls.Add(host);
            Controls.Add(rail);
            Controls.Add(titleBar);

            Go(PageHome);
            UpdateNavigation();
            try { Microsoft.Win32.SystemEvents.UserPreferenceChanged += SystemThemeChanged; }
            catch (Exception ex) { Log.Write("Tema automático indisponível: " + ex.Message); }
        }

        public void ApplyAppearance()
        {
            var map = Theme.Configure(Session.Cfg);
            var visited = new HashSet<Control>();
            Theme.Apply(this, map, visited);
            foreach (var page in pages) Theme.Apply(page, map, visited);
            foreach (Form form in Application.OpenForms) Theme.Apply(form, map, visited);
            Icons.Tint = Theme.Accent;
            Icons.Back = Theme.Panel;
            UpdateNavigation();
            titleBar.UpdateAppearance();
            Invalidate(true);
        }

        public void ToggleTheme()
        {
            Session.Cfg.Tema = Theme.IsDark ? "Claro" : "Escuro";
            Session.Cfg.Save();
            ApplyAppearance();
        }

        public void Customize()
        {
            using (var dialog = new AppearanceDialog(this)) dialog.ShowDialog(this);
        }

        void UpdateNavigation()
        {
            if (rail == null) return;
            rail.Compact = Session.Cfg.MenuCompacto || ClientSize.Width < Math.Max(Theme.S(1240), 1180);
            rail.Width = Theme.S(rail.Compact ? 96 : 220);
            rail.PerformLayout();
            rail.Invalidate(true);
        }

        void SystemThemeChanged(object sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
        {
            if (Session.Cfg.Tema != "Sistema" || IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke(new Action(() => { if (!IsDisposed) ApplyAppearance(); })); } catch (InvalidOperationException) { }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateNavigation();
        }

        Control CreatePage(int index)
        {
            switch (index)
            {
                case PageAnalysis: return new AnalysisPage();
                case PageEdit: return new EditPage();
                case PagePdf: return new PdfPage();
                case PageConfig: return new ConfigPage();
                default: return new HomePage(this);
            }
        }

        // Troca de tela (as telas são criadas na primeira vez e mantêm o que foi digitado).
        public void Go(int index)
        {
            if (pages[index] == null) pages[index] = CreatePage(index);
            var page = pages[index];
            rail.Active = index;
            if (current == page) return;
            var old = current as IPage;
            if (old != null) old.OnLeave();
            SuspendLayout();
            host.Controls.Clear();
            page.Dock = DockStyle.Fill;
            host.Controls.Add(page);
            current = page;
            var p = page as IPage;
            titleBar.PageTitle = p != null ? p.Title : "";
            if (p != null) p.OnEnter();
            ResumeLayout(true);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            hotkeysOk = !Native.IsWindows || (Native.RegisterHotKey(Handle, 1, 0, Native.VK_F9) & Native.RegisterHotKey(Handle, 2, 0, Native.VK_F8));
            titleBar.Hint = hotkeysOk ? "F9 maximiza e tira o print  ·  F8 print da tela"
                                      : "Atalhos F8/F9 indisponíveis (usados por outro programa)";
            try { int round = 2; Native.DwmSetWindowAttribute(Handle, 33, ref round, 4); } catch { }
            try { int dark = Theme.IsDark ? 1 : 0; Native.DwmSetWindowAttribute(Handle, 20, ref dark, 4); } catch { }
        }

        bool closeByButton;

        // Chamado somente pelo botão X da barra de título.
        public void CloseFromButton()
        {
            closeByButton = true;
            Close();
        }

        // Bloqueia Alt+F4, "Fechar janela" da barra de tarefas e WM_CLOSE de outros programas.
        // Desligar/reiniciar o Windows e o Gerenciador de Tarefas continuam funcionando.
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!closeByButton && (e.CloseReason == CloseReason.UserClosing || e.CloseReason == CloseReason.None))
            {
                e.Cancel = true;
                Log.Write("Fechamento bloqueado (motivo: " + e.CloseReason + ")");
                return;
            }
            Log.Write("Programa fechado (motivo: " + (closeByButton ? "botão X" : e.CloseReason.ToString()) + ")");
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            try { Microsoft.Win32.SystemEvents.UserPreferenceChanged -= SystemThemeChanged; } catch { }
            Native.UnregisterHotKey(Handle, 1);
            Native.UnregisterHotKey(Handle, 2);
            var p = current as IPage;
            if (p != null) p.OnLeave();
            base.OnFormClosed(e);
        }

        static async void PrintFromHotkey(bool screen) { await Session.HotkeyPrint(screen); }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY)
            {
                bool screen = m.WParam.ToInt32() == 2;
                base.WndProc(ref m);
                PrintFromHotkey(screen);
                return;
            }
            if (m.Msg == Native.WM_NCHITTEST && WindowState == FormWindowState.Normal)
            {
                base.WndProc(ref m);
                // coordenadas com sinal (monitores à esquerda/acima do principal).
                // ToInt64: em 64 bits o ToInt32 estoura (OverflowException) com coordenadas negativas.
                int lp = unchecked((int)m.LParam.ToInt64());
                var pt = PointToClient(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)));
                int g = Theme.S(6);
                bool l = pt.X < g, r = pt.X >= Width - g, t = pt.Y < g, b = pt.Y >= Height - g;
                if (t && l) m.Result = (IntPtr)Native.HTTOPLEFT;
                else if (t && r) m.Result = (IntPtr)Native.HTTOPRIGHT;
                else if (b && l) m.Result = (IntPtr)Native.HTBOTTOMLEFT;
                else if (b && r) m.Result = (IntPtr)Native.HTBOTTOMRIGHT;
                else if (l) m.Result = (IntPtr)Native.HTLEFT;
                else if (r) m.Result = (IntPtr)Native.HTRIGHT;
                else if (t) m.Result = (IntPtr)Native.HTTOP;
                else if (b) m.Result = (IntPtr)Native.HTBOTTOM;
                return;
            }
            base.WndProc(ref m);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.Style |= 0x00020000; // WS_MINIMIZEBOX: permite minimizar pela barra de tarefas
                return cp;
            }
        }

        public void ToggleMaximize()
        {
            if (WindowState == FormWindowState.Maximized) WindowState = FormWindowState.Normal;
            else { MaximizedBounds = Screen.FromHandle(Handle).WorkingArea; WindowState = FormWindowState.Maximized; }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var p = new Pen(Theme.Border)) e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
        }
    }

    class TitleBar : DrawnControl
    {
        readonly MainForm form;
        int hoverBtn = -1;
        bool pressed;
        int downBtn = -1;
        string pageTitle = "", hint = "";
        readonly FlatButton themeButton;

        public string PageTitle { get { return pageTitle; } set { pageTitle = value ?? ""; Invalidate(); } }
        public string Hint { get { return hint; } set { hint = value ?? ""; Invalidate(); } }

        public TitleBar(MainForm form)
        {
            this.form = form;
            BackColor = Theme.TitleBar;
            themeButton = new FlatButton("") { Font = Theme.Small, AccessibleName = "Alternar entre modo claro e escuro" };
            themeButton.Click += (s, e) => form.ToggleTheme();
            Controls.Add(themeButton);
            UpdateAppearance();
            form.Resize += (s, e) => Invalidate();   // troca o ícone maximizar/restaurar
        }

        public void UpdateAppearance() { themeButton.Text = Theme.IsDark ? "Modo claro" : "Modo escuro"; Invalidate(); }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (themeButton != null) themeButton.SetBounds(Width - Theme.S(288), Theme.S(7), Theme.S(120), Height - Theme.S(14));
        }

        // Botões no estilo do Windows: 0 = minimizar, 1 = maximizar/restaurar, 2 = fechar
        Rectangle Btn(int i)
        {
            int w = Theme.S(52);
            return new Rectangle(Width - (3 - i) * w, 0, w, Height);
        }

        int HitBtn(Point p) { for (int i = 0; i < 3; i++) if (Btn(i).Contains(p)) return i; return -1; }

        bool dragging;
        Point dragStart, formStart;

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (dragging)
            {
                var p = Cursor.Position;
                form.Location = new Point(formStart.X + p.X - dragStart.X, formStart.Y + p.Y - dragStart.Y);
                return;
            }
            int h = HitBtn(e.Location);
            if (h != hoverBtn) { hoverBtn = h; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e) { hoverBtn = -1; pressed = false; Invalidate(); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            downBtn = -1;
            if (e.Button != MouseButtons.Left) return;
            int b = HitBtn(e.Location);
            if (b >= 0) { if (e.Clicks == 1) { downBtn = b; pressed = true; Invalidate(); } return; }
            if (e.Clicks == 2) { form.ToggleMaximize(); return; }
            if (Native.IsWindows)
            {
                Native.ReleaseCapture();
                Native.SendMessage(form.Handle, Native.WM_NCLBUTTONDOWN, (IntPtr)Native.HTCAPTION, IntPtr.Zero);
            }
            else if (form.WindowState == FormWindowState.Normal)
            {
                dragging = true;
                dragStart = Cursor.Position;
                formStart = form.Location;
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            dragging = false;
            int down = downBtn;
            downBtn = -1;
            pressed = false;
            Invalidate();
            if (e.Button != MouseButtons.Left || down < 0 || HitBtn(e.Location) != down) return;
            switch (down)
            {
                case 0: form.WindowState = FormWindowState.Minimized; break;
                case 1: form.ToggleMaximize(); break;
                case 2: form.CloseFromButton(); break;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            Theme.Smooth(g);
            Icons.Back = BackColor;
            Icons.Laptop(g, new Rectangle(Theme.S(16), (Height - Theme.S(24)) / 2, Theme.S(24), Theme.S(24)));
            Icons.Back = Theme.Panel;
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis;
            int x = Theme.S(50);
            string app = "Diagnóstico Técnico";
            int aw = TextRenderer.MeasureText(g, app, Theme.BodyBold, Size.Empty, TextFormatFlags.NoPadding).Width;
            TextRenderer.DrawText(g, app, Theme.BodyBold, new Rectangle(x, 0, aw + 4, Height), Theme.Text, flags);
            x += aw + Theme.S(10);
            if (pageTitle != "")
            {
                TextRenderer.DrawText(g, "›", Theme.Body, new Rectangle(x, 0, Theme.S(14), Height), Theme.TextDim, flags);
                x += Theme.S(18);
                TextRenderer.DrawText(g, pageTitle, Theme.BodyBold, new Rectangle(x, 0, Theme.S(320), Height), Theme.Text, flags);
            }
            int right = themeButton.Left - Theme.S(16);
            if (Session.Cfg.MostrarDicas && hint != "" && right - Theme.S(520) > x + Theme.S(200))
                TextRenderer.DrawText(g, hint, Theme.Small, new Rectangle(right - Theme.S(520), 0, Theme.S(520), Height), Theme.TextDim,
                    flags | TextFormatFlags.Right);

            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
            for (int i = 0; i < 3; i++)
            {
                var r = Btn(i);
                bool hot = hoverBtn == i;
                if (hot)
                {
                    Color bg = i == 2 ? (pressed ? Theme.Hex("#94141c") : Theme.Hex("#c42b1c"))
                                      : (pressed ? Theme.Selected : Theme.PanelHover);
                    using (var b = new SolidBrush(bg)) g.FillRectangle(b, r);
                }
                Color fg = hot && i == 2 ? Color.White : Theme.Text;
                int s = Theme.S(11), cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
                using (var pen = new Pen(fg, Math.Max(1f, Theme.Scale)))
                {
                    if (i == 0)
                        g.DrawLine(pen, cx - s / 2, cy, cx + s / 2, cy);
                    else if (i == 1)
                    {
                        if (form.WindowState == FormWindowState.Maximized)
                        {
                            int o = Math.Max(2, s / 5);
                            g.DrawRectangle(pen, cx - s / 2, cy - s / 2 + o, s - o, s - o);
                            g.DrawLines(pen, new[] { new Point(cx - s / 2 + o, cy - s / 2 + o), new Point(cx - s / 2 + o, cy - s / 2),
                                new Point(cx + s / 2, cy - s / 2), new Point(cx + s / 2, cy + s / 2 - o), new Point(cx + s / 2 - o, cy + s / 2 - o) });
                        }
                        else g.DrawRectangle(pen, cx - s / 2, cy - s / 2, s, s);
                    }
                    else
                    {
                        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                        g.DrawLine(pen, cx - s / 2, cy - s / 2, cx + s / 2, cy + s / 2);
                        g.DrawLine(pen, cx - s / 2, cy + s / 2, cx + s / 2, cy - s / 2);
                        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
                    }
                }
            }
        }
    }

    // Barra lateral: troca de tela com um clique.
    class NavRail : DrawnControl
    {
        static readonly string[] Labels = { "Visão geral", "Fazer análise", "Fotos e arquivos", "Relatório PDF", "Configurações" };
        static readonly string[] ShortLabels = { "Início", "Análise", "Fotos", "PDF", "Ajustes" };
        static readonly Action<Graphics, Rectangle>[] Glyphs = { Icons.Home, Icons.Clipboard, Icons.Folder, Icons.FilePlus, Icons.Wrench };
        public event Action<int> Selected;
        readonly NavButton[] buttons = new NavButton[5];
        readonly FlatButton customize, collapse;
        int active;
        public bool Compact;

        public NavRail(MainForm form)
        {
            BackColor = Theme.TitleBar;
            TabStop = false;
            for (int i = 0; i < buttons.Length; i++)
            {
                int index = i;
                buttons[i] = new NavButton(this, i) { AccessibleName = Labels[i] };
                buttons[i].Click += (s, e) => { if (Selected != null) Selected(index); };
                Controls.Add(buttons[i]);
            }
            customize = new FlatButton("Aparência") { Font = Theme.Small };
            customize.Click += (s, e) => form.Customize();
            collapse = new FlatButton("Recolher menu") { Font = Theme.Small };
            collapse.Click += (s, e) => { Session.Cfg.MenuCompacto = !Session.Cfg.MenuCompacto; Session.Cfg.Save(); form.ApplyAppearance(); };
            Controls.AddRange(new Control[] { customize, collapse });
        }

        public int Active { get { return active; } set { active = value; Invalidate(true); } }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (customize == null) return;
            int pad = Theme.S(Compact ? 8 : 14), top = Theme.S(Compact ? 94 : 150);
            for (int i = 0; i < buttons.Length; i++)
                buttons[i].SetBounds(pad, top + i * Theme.S(Compact ? 68 : 56), Width - pad * 2, Theme.S(Compact ? 62 : 48));
            customize.SetBounds(pad, Height - Theme.S(100), Width - pad * 2, Theme.S(38));
            collapse.SetBounds(pad, Height - Theme.S(54), Width - pad * 2, Theme.S(34));
            collapse.Text = Compact ? "Expandir" : "Recolher menu";
            // Em telas menores a versão compacta mantém espaço para os formulários.
            collapse.Enabled = Session.Cfg.MenuCompacto || FindForm() == null || FindForm().ClientSize.Width >= Math.Max(Theme.S(1240), 1180);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            Theme.Smooth(g);
            using (var pen = new Pen(Theme.Line)) g.DrawLine(pen, Width - 1, 0, Width - 1, Height);
            if (Compact)
            {
                AppLogo.DrawBadge(g, new Rectangle(Theme.S(24), Theme.S(18), Theme.S(48), Theme.S(48)));
            }
            else
            {
                AppLogo.Draw(g, new Rectangle(Theme.S(20), Theme.S(36), Width - Theme.S(36), Theme.S(70)));
                TextRenderer.DrawText(g, "ÁREA TÉCNICA", Theme.Small, new Rectangle(Theme.S(22), Theme.S(122), Width - Theme.S(44), Theme.S(22)), Theme.TextDim);
            }
        }

        class NavButton : FlatButton
        {
            readonly NavRail rail;
            readonly int index;
            bool hover;
            public NavButton(NavRail rail, int index) : base(Labels[index]) { this.rail = rail; this.index = index; }
            protected override void OnMouseEnter(EventArgs e) { hover = true; base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { hover = false; base.OnMouseLeave(e); }
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.Clear(ParentColor);
                Theme.Smooth(g);
                bool selected = rail.active == index;
                Color bg = selected ? Theme.Selected : hover ? Theme.PanelHover : ParentColor;
                Theme.FillRound(g, bg, new Rectangle(1, 1, Width - 3, Height - 3), Theme.S(10));
                if (selected) Theme.FillRound(g, Theme.Accent, new Rectangle(1, Theme.S(12), Theme.S(3), Height - Theme.S(24)), 2);
                Icons.Tint = selected ? Theme.Accent : Theme.TextDim; Icons.Back = bg;
                int size = Theme.S(23);
                Glyphs[index](g, new Rectangle(rail.Compact ? (Width - size) / 2 : Theme.S(14), rail.Compact ? Theme.S(7) : (Height - size) / 2, size, size));
                Icons.Tint = Theme.Accent; Icons.Back = Theme.Panel;
                var textBounds = rail.Compact ? new Rectangle(0, Theme.S(36), Width, Theme.S(22)) : new Rectangle(Theme.S(50), 0, Width - Theme.S(56), Height);
                TextRenderer.DrawText(g, rail.Compact ? ShortLabels[index] : Labels[index], rail.Compact ? Theme.Small : Theme.Body,
                    textBounds, selected ? Theme.Text : Theme.TextDim, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                    (rail.Compact ? TextFormatFlags.HorizontalCenter : TextFormatFlags.Left));
                if (Focused) Theme.DrawRound(g, Theme.Accent, new Rectangle(2, 2, Width - 5, Height - 5), Theme.S(10));
            }
        }
    }

    // Ícones em traço, desenhados em uma grade 64x64. Tint = cor do traço; Back = cor de fundo onde o ícone está.
    static class Icons
    {
        public static Color Tint = Theme.Accent, Back = Theme.Panel;

        public static void Home(Graphics g, Rectangle r)
        {
            Draw(g, r, p =>
            {
                g.DrawLines(p, new[] { new PointF(6, 31), new PointF(32, 8), new PointF(58, 31) });
                g.DrawLines(p, new[] { new PointF(14, 26), new PointF(14, 56), new PointF(50, 56), new PointF(50, 26) });
                g.DrawLines(p, new[] { new PointF(26, 56), new PointF(26, 40), new PointF(38, 40), new PointF(38, 56) });
            });
        }

        static Pen StrokePen(Rectangle r, float w = 5f)
        {
            return new Pen(Theme.Accent, w * r.Width / 64f) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
        }

        static GraphicsState Begin(Graphics g, Rectangle r)
        {
            var st = g.Save();
            g.TranslateTransform(r.X, r.Y);
            g.ScaleTransform(r.Width / 64f, r.Height / 64f);
            return st;
        }

        static void Draw(Graphics g, Rectangle r, Action<Pen> paint, float w = 5f)
        {
            var st = Begin(g, r);
            using (var pen = new Pen(Tint, w) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round })
                paint(pen);
            g.Restore(st);
        }

        public static void Clipboard(Graphics g, Rectangle r)
        {
            Draw(g, r, p =>
            {
                using (var b = Theme.Round(new RectangleF(13, 10, 38, 50), 6)) g.DrawPath(p, b);
                using (var c = Theme.Round(new RectangleF(22, 4, 20, 11), 3)) { using (var b2 = new SolidBrush(Back)) g.FillPath(b2, c); g.DrawPath(p, c); }
                g.DrawLines(p, new[] { new PointF(22, 36), new PointF(29, 43), new PointF(42, 29) });
            });
        }

        public static void Folder(Graphics g, Rectangle r)
        {
            Draw(g, r, p =>
            {
                using (var path = new GraphicsPath())
                {
                    path.AddLines(new[] { new PointF(6, 50), new PointF(6, 14), new PointF(24, 14), new PointF(30, 21), new PointF(58, 21), new PointF(58, 50) });
                    path.CloseFigure();
                    g.DrawPath(p, path);
                }
            });
        }

        public static void FilePlus(Graphics g, Rectangle r)
        {
            Draw(g, r, p =>
            {
                using (var path = new GraphicsPath())
                {
                    path.AddLines(new[] { new PointF(14, 4), new PointF(38, 4), new PointF(52, 18), new PointF(52, 60), new PointF(14, 60) });
                    path.CloseFigure();
                    g.DrawPath(p, path);
                }
                g.DrawLines(p, new[] { new PointF(38, 4), new PointF(38, 18), new PointF(52, 18) });
                g.DrawLine(p, 33, 30, 33, 48);
                g.DrawLine(p, 24, 39, 42, 39);
            });
        }

        public static void Wrench(Graphics g, Rectangle r)
        {
            Draw(g, r, p =>
            {
                // cabeça (anel aberto) + cabo em cápsula
                g.DrawArc(p, 32, 6, 26, 26, 345, 290);
                var st = g.Save();
                g.TranslateTransform(36, 28);
                g.RotateTransform(135);
                using (var cap = Theme.Round(new RectangleF(2, -6, 30, 12), 6)) g.DrawPath(p, cap);
                g.Restore(st);
            });
        }

        public static void Laptop(Graphics g, Rectangle r)
        {
            Draw(g, r, p =>
            {
                using (var s = Theme.Round(new RectangleF(10, 12, 44, 30), 4)) g.DrawPath(p, s);
                g.DrawLine(p, 4, 50, 60, 50);
                using (var w = new Pen(Tint, 5) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    g.DrawLine(w, 40, 40, 52, 56);
                    g.DrawEllipse(w, 33, 30, 12, 12);
                }
            }, 5.5f);
        }
    }
}
