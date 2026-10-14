using System;
using System.Drawing;
using System.Windows.Forms;

namespace Diagnostico
{
    class AppearanceDialog : Form
    {
        readonly MainForm main;
        readonly DarkSelect theme = new DarkSelect("Claro", "Escuro", "Sistema");
        readonly DarkSelect accent = new DarkSelect("Turquesa", "Petróleo");
        readonly DarkSelect navigation = new DarkSelect("Completo", "Compacto");
        readonly DarkCheck hints = new DarkCheck("Mostrar dicas e atalhos");
        readonly Label status = UI.Label("As alterações são aplicadas na hora.", Theme.Small, Theme.TextDim, Theme.Bg);
        readonly Preview preview = new Preview();

        public AppearanceDialog(MainForm main)
        {
            this.main = main;
            Text = "Aparência · Cantão";
            BackColor = Theme.Bg; ForeColor = Theme.Text; Font = Theme.Body;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(Theme.S(620), Theme.S(620));
            var title = UI.Label("Do seu jeito", Theme.Big, Theme.Text, Theme.Bg);
            var subtitle = UI.Label("A identidade da Cantão, no seu espaço de trabalho.", Theme.Body, Theme.TextDim, Theme.Bg);
            Place(title, 28, 20, 560, 36); Place(subtitle, 28, 60, 560, 28);
            Place(preview, 28, 108, 564, 138);
            AddLabel("Tema", 28, 268, 268);
            AddLabel("Cor de destaque", 324, 268, 268);
            Place(theme, 28, 300, 268, 44); Place(accent, 324, 300, 268, 44);
            AddLabel("Menu lateral", 28, 365, 268);
            Place(navigation, 28, 395, 268, 44);
            var note = UI.Label("O menu se adapta automaticamente\na janelas menores.", Theme.Small, Theme.TextDim, Theme.Bg);
            Place(note, 324, 391, 268, 52);
            Place(hints, 28, 461, 540, 32); Place(status, 28, 508, 564, 28);
            var reset = new FlatButton("Restaurar aparência");
            var done = new FlatButton("Concluir", true);
            Place(reset, 28, 555, 218, 42); Place(done, 444, 555, 148, 42);
            done.Click += (s, e) => Close();
            reset.Click += (s, e) =>
            {
                Session.Cfg.Tema = "Claro"; Session.Cfg.CorDestaque = "Turquesa";
                Session.Cfg.MenuCompacto = false; Session.Cfg.MostrarDicas = true;
                Sync(); Apply();
            };
            Sync();
            theme.ValueChanged += (s, e) => { Session.Cfg.Tema = theme.Value; Apply(); };
            accent.ValueChanged += (s, e) => { Session.Cfg.CorDestaque = accent.Value; Apply(); };
            navigation.ValueChanged += (s, e) => { Session.Cfg.MenuCompacto = navigation.Value == "Compacto"; Apply(); };
            hints.CheckedChanged += (s, e) => { Session.Cfg.MostrarDicas = hints.Checked; Apply(); };
        }

        void Sync()
        {
            theme.Value = Session.Cfg.Tema == "Escuro" || Session.Cfg.Tema == "Sistema" ? Session.Cfg.Tema : "Claro";
            accent.Value = Session.Cfg.CorDestaque == "Petróleo" ? "Petróleo" : "Turquesa";
            navigation.Value = Session.Cfg.MenuCompacto ? "Compacto" : "Completo";
            hints.Checked = Session.Cfg.MostrarDicas;
        }

        void Apply()
        {
            bool saved = Session.Cfg.TrySave();
            main.ApplyAppearance();
            status.Text = saved ? "Preferências salvas para a próxima abertura." : "Aplicado. Não foi possível salvar o config.json nesta pasta.";
            preview.Invalidate();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try { int dark = Theme.IsDark ? 1 : 0; Native.DwmSetWindowAttribute(Handle, 20, ref dark, 4); } catch { }
        }

        void AddLabel(string text, int x, int y, int w) { Place(UI.Label(text, Theme.BodyBold, Theme.Text, Theme.Bg), x, y, w, 26); }
        void Place(Control control, int x, int y, int w, int h)
        {
            control.SetBounds(Theme.S(x), Theme.S(y), Theme.S(w), Theme.S(h));
            Controls.Add(control);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { Close(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        class Preview : DrawnControl
        {
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics; g.Clear(ParentColor); Theme.Smooth(g);
                Theme.FillRound(g, Theme.Panel, new Rectangle(1, 1, Width - 3, Height - 3), Theme.S(14));
                Theme.DrawRound(g, Theme.Border, new Rectangle(1, 1, Width - 3, Height - 3), Theme.S(14));
                BrandAssets.Logo(g, new Rectangle(Theme.S(16), Theme.S(24), Theme.S(160), Theme.S(86)));
                TextRenderer.DrawText(g, "Diagnóstico técnico", Theme.Heading, new Rectangle(Theme.S(198), Theme.S(20), Width - Theme.S(218), Theme.S(30)), Theme.Text);
                TextRenderer.DrawText(g, "Uma prévia da sua aparência", Theme.Small, new Rectangle(Theme.S(198), Theme.S(54), Width - Theme.S(218), Theme.S(26)), Theme.TextDim);
                var button = new Rectangle(Theme.S(200), Theme.S(90), Theme.S(138), Theme.S(30));
                Theme.FillRound(g, Theme.Accent, button, Theme.S(7));
                TextRenderer.DrawText(g, "Fazer análise", Theme.Small, button, Theme.AccentText, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                Theme.Pill(g, "Concluído", Theme.PillGreen, new Rectangle(Theme.S(358), Theme.S(90), Theme.S(170), Theme.S(30)), Theme.Small, false);
            }
        }
    }
}
