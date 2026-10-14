using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Diagnostico
{
    class HomePage : DrawnControl, IPage
    {
        public string Title { get { return "Visão geral"; } }
        readonly Card[] cards;
        readonly Welcome hero = new Welcome();
        readonly FlatButton start = new FlatButton("Fazer análise  →", true);
        readonly RoundPanel recentPanel = new RoundPanel();
        readonly Label recentLabel = UI.Label("Ordens de serviço recentes", Theme.Heading, Theme.Text);
        readonly Label searchLabel = UI.Label("Buscar nas recentes", Theme.Small);
        readonly DarkInput search = new DarkInput();
        readonly Label empty = UI.Label("Sua próxima análise começa aqui.\nClique em Fazer análise para iniciar uma OS.", Theme.Body, Theme.TextDim);
        readonly RowList recent = new RowList { RowHeight = 60, Selectable = false };
        readonly FlatButton folder = new FlatButton("Abrir pasta");
        readonly FlatButton customize = new FlatButton("Personalizar") { Font = Theme.Small };
        readonly List<ListRow> allRecent = new List<ListRow>();
        string emptyMessage;

        public HomePage(MainForm form)
        {
            cards = new[]
            {
                new Card("Fotos e arquivos", "Organize as evidências de cada OS.", Icons.Folder),
                new Card("Relatório PDF", "Reúna o diagnóstico e as fotos no laudo.", Icons.FilePlus),
                new Card("Configurações", "Ajuste pastas, programas e etapas.", Icons.Wrench)
            };
            start.Click += (s, e) => form.Go(MainForm.PageAnalysis);
            cards[0].Click += (s, e) => form.Go(MainForm.PageEdit);
            cards[1].Click += (s, e) => form.Go(MainForm.PagePdf);
            cards[2].Click += (s, e) => form.Go(MainForm.PageConfig);
            customize.Click += (s, e) => form.Customize();
            hero.Controls.Add(start);
            Controls.AddRange(new Control[] { hero, recentPanel, customize });
            Controls.AddRange(cards);
            recentPanel.Controls.AddRange(new Control[] { recentLabel, searchLabel, search, folder, recent, empty });
            empty.TextAlign = ContentAlignment.MiddleCenter;
            search.Box.AccessibleName = "Buscar OS recente pelo número ou cliente";
            search.ValueChanged += (s, e) => FilterRecent();
            folder.Click += (s, e) => PhotoStore.OpenFolder(Session.Cfg.OsRoot);
            recent.RowClick += (s, i) => { Session.ActiveFolder = recent.Rows[i].Tag as string; form.Go(MainForm.PageEdit); };
        }

        public void OnEnter() { LoadRecent(); }
        public void OnLeave() { }

        void LoadRecent()
        {
            allRecent.Clear();
            emptyMessage = "Sua próxima análise começa aqui.\nClique em Fazer análise para iniciar uma OS.";
            try
            {
                string root = Session.Cfg.OsRoot;
                if (Directory.Exists(root))
                {
                    int required = Evidence.All().Count(e => Evidence.Required(e, false));
                    foreach (var d in new DirectoryInfo(root).GetDirectories().OrderByDescending(x => x.LastWriteTime).Take(12))
                    {
                        int missing = Evidence.Missing(d.FullName, false).Count, done = required - missing;
                        string client = OsJson.Str(File.Exists(OsJson.PathOf(d.FullName)) ? OsJson.Load(d.FullName) : null, "cliente");
                        allRecent.Add(new ListRow
                        {
                            Text = "OS " + d.Name + (client != "" ? "   ·   " + client : ""),
                            Sub = PhotoStore.PhotoCount(d.FullName) + " arquivos   ·   " + d.LastWriteTime.ToString("dd/MM/yyyy HH:mm"),
                            Badge = missing == 0 ? "Evidências completas" : done + " de " + required + " evidências",
                            BadgeColors = missing == 0 ? Theme.PillGreen : done == 0 ? Theme.PillGray : Theme.PillYellow,
                            Tag = d.FullName
                        });
                    }
                }
                else emptyMessage = "A pasta das OS ainda não está disponível.\nConecte a unidade ou ajuste o caminho em Configurações.";
            }
            catch (Exception ex)
            {
                emptyMessage = "Não foi possível carregar as OS.\nVerifique a pasta em Configurações.";
                Log.Write("Erro listando OS recentes: " + ex.Message);
            }
            FilterRecent();
        }

        void FilterRecent()
        {
            string term = search.Text.Trim();
            var rows = allRecent.Where(r => r.Text.IndexOf(term, StringComparison.CurrentCultureIgnoreCase) >= 0).ToList();
            recent.SetRows(rows);
            recentLabel.Text = "Ordens recentes  ·  " + rows.Count;
            empty.Text = allRecent.Count > 0 && rows.Count == 0 ? "Nenhuma OS recente corresponde à busca.\nTente outro número ou nome do cliente." : emptyMessage;
            empty.Visible = rows.Count == 0;
            recent.Visible = rows.Count > 0;
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (Width < 50 || cards == null) return;
            int pad = Theme.S(26), gap = Theme.S(16), content = Width - pad * 2;
            customize.SetBounds(Width - pad - Theme.S(142), Theme.S(24), Theme.S(142), Theme.S(36));
            hero.SetBounds(pad, Theme.S(84), content, Theme.S(172));
            start.SetBounds(Theme.S(24), Theme.S(113), Theme.S(184), Theme.S(40));
            int cardWidth = (content - gap * 2) / 3;
            for (int i = 0; i < cards.Length; i++) cards[i].SetBounds(pad + i * (cardWidth + gap), hero.Bottom + gap, cardWidth, Theme.S(122));
            int ry = cards[0].Bottom + gap;
            recentPanel.SetBounds(pad, ry, content, Math.Max(Theme.S(138), Height - ry - pad));
            int header = Theme.S(84), bw = folder.PreferredWidth, sw = Theme.S(232);
            folder.SetBounds(content - bw - Theme.S(16), Theme.S(28), bw, Theme.S(38));
            search.SetBounds(folder.Left - sw - gap, Theme.S(31), sw, Theme.S(36));
            searchLabel.SetBounds(search.Left, Theme.S(8), sw, Theme.S(22));
            recentLabel.SetBounds(Theme.S(20), Theme.S(18), Math.Max(0, search.Left - Theme.S(36)), Theme.S(46));
            recent.SetBounds(2, header, content - 4, recentPanel.Height - header - Theme.S(6));
            empty.SetBounds(Theme.S(16), header - Theme.S(8), content - Theme.S(32), recentPanel.Height - header);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            TextRenderer.DrawText(e.Graphics, "Sua bancada, organizada.", Theme.Big,
                new Rectangle(Theme.S(26), Theme.S(16), Width - Theme.S(230), Theme.S(38)), Theme.Text, TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(e.Graphics, "Da primeira análise à entrega do equipamento.", Theme.Small,
                new Rectangle(Theme.S(28), Theme.S(54), Width - Theme.S(230), Theme.S(24)), Theme.TextDim, TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        }

        class Welcome : DrawnControl
        {
            public Welcome() { BackColor = Theme.Brand; FixedBackColor = true; TabStop = false; }
            protected override void OnPaintBackground(PaintEventArgs e)
            {
                e.Graphics.Clear(ParentColor);
                Theme.Smooth(e.Graphics);
                Theme.FillRound(e.Graphics, Theme.Brand, new Rectangle(0, 0, Width - 1, Height - 1), Theme.S(16));
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                int imageWidth = Math.Min(Width * 36 / 100, Theme.S(350));
                var state = g.Save();
                using (var clip = Theme.Round(new Rectangle(0, 0, Width - 1, Height - 1), Theme.S(16))) g.SetClip(clip);
                BrandAssets.Banner(g, new Rectangle(Width - imageWidth, 0, imageWidth, Height));
                g.Restore(state);
                int textWidth = Width - imageWidth - Theme.S(44);
                TextRenderer.DrawText(g, "DIAGNÓSTICO TÉCNICO", Theme.Small, new Rectangle(Theme.S(24), Theme.S(20), textWidth, Theme.S(24)), Theme.Hex("#B8E7E4"), TextFormatFlags.Left);
                TextRenderer.DrawText(g, "Tudo pronto para a próxima OS.", Theme.Heading, new Rectangle(Theme.S(24), Theme.S(48), textWidth, Theme.S(32)), Color.White, TextFormatFlags.EndEllipsis);
                TextRenderer.DrawText(g, "Analise, registre as evidências e gere o laudo.", Theme.Small, new Rectangle(Theme.S(24), Theme.S(83), textWidth, Theme.S(24)), Theme.Hex("#D7EEEB"), TextFormatFlags.EndEllipsis);
                if (Session.Cfg.MostrarDicas && textWidth > Theme.S(590))
                    TextRenderer.DrawText(g, "F9 · capturar print", Theme.Small, new Rectangle(Theme.S(228), Theme.S(124), textWidth - Theme.S(228), Theme.S(24)), Color.White, TextFormatFlags.Left);
            }
        }

        class Card : FlatButton
        {
            readonly string description;
            readonly Action<Graphics, Rectangle> icon;
            bool hover;
            public Card(string title, string description, Action<Graphics, Rectangle> icon) : base(title)
            { this.description = description; this.icon = icon; AccessibleDescription = description; }
            protected override void OnMouseEnter(EventArgs e) { hover = true; base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { hover = false; base.OnMouseLeave(e); }
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics; g.Clear(ParentColor); Theme.Smooth(g);
                var r = new Rectangle(1, 1, Width - 3, Height - 3);
                Theme.FillRound(g, hover ? Theme.PanelHover : Theme.Panel, r, Theme.S(12));
                Theme.DrawRound(g, hover || Focused ? Theme.Accent : Theme.Border, r, Theme.S(12));
                Icons.Tint = Theme.Accent; Icons.Back = hover ? Theme.PanelHover : Theme.Panel;
                icon(g, new Rectangle(Theme.S(18), Theme.S(18), Theme.S(28), Theme.S(28)));
                Icons.Back = Theme.Panel;
                TextRenderer.DrawText(g, Text, Theme.BodyBold, new Rectangle(Theme.S(57), Theme.S(19), Width - Theme.S(75), Theme.S(30)), Theme.Text, TextFormatFlags.EndEllipsis);
                TextRenderer.DrawText(g, description, Theme.Small, new Rectangle(Theme.S(18), Theme.S(61), Width - Theme.S(36), Theme.S(48)), Theme.TextDim, TextFormatFlags.WordBreak);
            }
        }
    }
}
