using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Diagnostico
{
    class ConfigPage : DrawnControl, IPage
    {
        public string Title { get { return "Configuração"; } }

        readonly RoundPanel osPanel = new RoundPanel(), toolsPanel = new RoundPanel();
        readonly Label osLbl = UI.Label("PASTA DAS OS"), toolsLbl = UI.Label("PASTA DOS PROGRAMAS");
        readonly DarkInput osPath = new DarkInput(), toolsPath = new DarkInput();
        readonly FlatButton osChoose = new FlatButton("Escolher..."), toolsChoose = new FlatButton("Escolher...");

        readonly RoundPanel listPanel = new RoundPanel { Radius = 8 };
        readonly Label listLbl = UI.Label("ETAPAS", Theme.Body);
        readonly FlatButton findAll = new FlatButton("Procurar todos");
        readonly RowList list = new RowList { RowHeight = 54 };
        readonly FlatButton add = new FlatButton("Adicionar programa");

        readonly RoundPanel detail = new RoundPanel { AutoScroll = true };
        readonly TextBox name = new TextBox { BorderStyle = BorderStyle.None, BackColor = Theme.Panel, ForeColor = Theme.Text, Font = Theme.Heading };
        readonly Label exeLbl = UI.Label("Executável ou site (https://...)"), modeLbl = UI.Label("Modo"), winLbl = UI.Label("Janela"),
                       waitLbl = UI.Label("Espera print auto (s)"), descLbl = UI.Label("Descrição (aparece na lista)"),
                       argsLbl = UI.Label("Argumentos"), procLbl = UI.Label("Processo (se diferente do .exe)"),
                       fileLbl = UI.Label("Nome da evidência (arquivo)"), minLbl = UI.Label("Tempo mínimo (min)");
        readonly DarkInput exe = new DarkInput(), desc = new DarkInput(), wait = new DarkInput(), args = new DarkInput(), proc = new DarkInput(),
                           file = new DarkInput(), minTime = new DarkInput();
        readonly DarkSelect mode = new DarkSelect("manual", "automatico"), win = new DarkSelect("maximizada", "normal", "minimizada");
        readonly FlatButton locate = new FlatButton("Localizar"), open = new FlatButton("Abrir"), remove = new FlatButton("Remover");
        readonly DarkCheck maxBefore = new DarkCheck("Maximizar antes do print"), quick = new DarkCheck("Incluir no modo rápido"),
                           stress = new DarkCheck("Etapa de estresse"), periph = new DarkCheck("Etapa de periféricos"),
                           conditional = new DarkCheck("Condicional");

        ToolConfig current;
        bool loading;

        public ConfigPage()
        {
            osPanel.Controls.AddRange(new Control[] { osLbl, osPath, osChoose });
            toolsPanel.Controls.AddRange(new Control[] { toolsLbl, toolsPath, toolsChoose });
            listPanel.Controls.AddRange(new Control[] { listLbl, findAll, list, add, remove });
            detail.Controls.AddRange(new Control[] { name, exeLbl, exe, locate, open, modeLbl, mode, winLbl, win, waitLbl, wait,
                maxBefore, quick, stress, periph, conditional, descLbl, desc, argsLbl, args, procLbl, proc, fileLbl, file, minLbl, minTime });
            Controls.AddRange(new Control[] { osPanel, toolsPanel, listPanel, detail });
            foreach (Control c in detail.Controls) if (c is DarkCheck) c.BackColor = Theme.Panel;
            detail.Paint += (s, e) => { int lineY = Theme.S(76) + detail.AutoScrollPosition.Y; using (var p = new Pen(Theme.Line)) e.Graphics.DrawLine(p, Theme.S(20), lineY, detail.Width - Theme.S(20), lineY); };

            osChoose.Click += (s, e) => ChooseFolder(osPath, "Pasta onde as OS serão salvas", v => Session.Cfg.PastaOS = v);
            toolsChoose.Click += (s, e) => ChooseFolder(toolsPath, "Pasta onde estão os programas", v => Session.Cfg.PastaProgramas = v);
            osPath.Box.Leave += (s, e) => { if (osPath.Text.Trim() != "") { Session.Cfg.PastaOS = osPath.Text.Trim(); Session.Cfg.Save(); } };
            toolsPath.Box.Leave += (s, e) => { if (toolsPath.Text.Trim() != "") { Session.Cfg.PastaProgramas = toolsPath.Text.Trim(); Session.Cfg.Save(); } };

            list.RowClick += (s, i) => ShowTool(list.Rows[i].Tag as ToolConfig);
            findAll.Click += (s, e) => FindAll();
            add.Click += (s, e) => Add();
            remove.Click += (s, e) => Remove();
            locate.Click += (s, e) => Locate();
            open.Click += (s, e) => { if (current != null) Session.Launch(current); };

            name.TextChanged += (s, e) => Edit(t => t.Nome = name.Text, true);
            exe.ValueChanged += (s, e) => Edit(t => t.Exe = exe.Text.Trim(), true);
            desc.ValueChanged += (s, e) => Edit(t => t.Descricao = desc.Text);
            args.ValueChanged += (s, e) => Edit(t => t.Argumentos = args.Text);
            proc.ValueChanged += (s, e) => Edit(t => t.Processo = proc.Text.Trim());
            wait.ValueChanged += (s, e) => { int v; if (int.TryParse(wait.Text, out v)) Edit(t => t.Espera = Math.Max(1, v)); };
            mode.ValueChanged += (s, e) => Edit(t => t.Modo = mode.Value, true);
            win.ValueChanged += (s, e) => Edit(t => t.Janela = win.Value);
            maxBefore.CheckedChanged += (s, e) => Edit(t => t.MaximizarAntesPrint = maxBefore.Checked);
            quick.CheckedChanged += (s, e) => Edit(t => t.ModoRapido = quick.Checked);
            stress.CheckedChanged += (s, e) => Edit(t => t.SoEstresse = stress.Checked);
            periph.CheckedChanged += (s, e) => Edit(t => t.SoPerifericos = periph.Checked);
            conditional.CheckedChanged += (s, e) => Edit(t => t.Condicional = conditional.Checked);
            file.ValueChanged += (s, e) => Edit(t => t.Arquivo = file.Text.Trim());
            minTime.ValueChanged += (s, e) => { int v; if (int.TryParse(minTime.Text, out v)) Edit(t => t.MinutosMinimos = Math.Max(0, v)); };
        }

        public void OnEnter()
        {
            osPath.Text = Session.Cfg.PastaOS;
            toolsPath.Text = Session.Cfg.PastaProgramas;
            RefreshList();
            if (current == null && Session.Cfg.Programas.Count > 0) ShowTool(Session.Cfg.Programas[0]);
        }

        public void OnLeave() { Session.Cfg.Save(); }

        void RefreshList()
        {
            list.SetRows(Session.Cfg.Programas.Select(t => new ListRow
            {
                Text = string.IsNullOrWhiteSpace(t.Nome) ? "(sem nome)" : t.Nome,
                Badge = t.Exists ? "Encontrado" : "Não encontrado",
                BadgeColors = t.Exists ? Theme.PillGreen : Theme.PillRed,
                Tag = t,
            }));
            list.SelectedIndex = Session.Cfg.Programas.IndexOf(current);
            list.Invalidate();
        }

        void ShowTool(ToolConfig t)
        {
            current = t;
            loading = true;
            bool on = t != null;
            foreach (Control c in detail.Controls) c.Enabled = on;
            remove.Enabled = on;
            if (t != null)
            {
                name.Text = t.Nome; exe.Text = t.Exe; desc.Text = t.Descricao; args.Text = t.Argumentos; proc.Text = t.Processo;
                wait.Text = t.Espera.ToString(); mode.Value = t.Modo; win.Value = t.Janela;
                maxBefore.Checked = t.MaximizarAntesPrint; quick.Checked = t.ModoRapido; stress.Checked = t.SoEstresse; periph.Checked = t.SoPerifericos;
                conditional.Checked = t.Condicional; file.Text = t.Arquivo; minTime.Text = t.MinutosMinimos.ToString();
            }
            loading = false;
            list.SelectedIndex = Session.Cfg.Programas.IndexOf(t);
            list.Invalidate();
            PerformLayout();
        }

        void Edit(Action<ToolConfig> change, bool refreshList = false)
        {
            if (loading || current == null) return;
            change(current);
            Session.Cfg.Save();
            if (refreshList) RefreshList();
        }

        void ChooseFolder(DarkInput box, string title, Action<string> set)
        {
            using (var dlg = new FolderBrowserDialog { Description = title, SelectedPath = AppPaths.Resolve(box.Text), ShowNewFolderButton = true })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                string v = AppPaths.ToRelative(dlg.SelectedPath);
                box.Text = v;
                set(v);
                Session.Cfg.Save();
                RefreshList();
            }
        }

        void Locate()
        {
            if (current == null) return;
            using (var dlg = new OpenFileDialog { Filter = "Programas|*.exe;*.bat;*.cmd|Todos|*.*", Title = "Executável de " + current.Nome })
            {
                try { dlg.InitialDirectory = current.Exists ? Path.GetDirectoryName(current.FullPath) : Session.Cfg.ToolsRoot; } catch { }
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                exe.Text = AppPaths.ToRelative(dlg.FileName);
            }
        }

        // Procura cada executável (pelo nome do arquivo) dentro da pasta dos programas.
        async void FindAll()
        {
            Cursor = Cursors.WaitCursor;
            findAll.Enabled = false;
            try
            {
                var roots = ToolDiscovery.SearchRoots(Session.Cfg).ToList();
                var files = await System.Threading.Tasks.Task.Run(() => ToolDiscovery.Executables(roots));
                if (IsDisposed) return;
                int found = ToolDiscovery.FindMissing(Session.Cfg, files, Environment.Is64BitOperatingSystem);
                int missing = Session.Cfg.Programas.Count - found;
                Session.Cfg.Save();
                RefreshList();
                if (current != null) ShowTool(current);
                Toast.Show(found + " encontrado(s), " + missing + " não encontrado(s).", missing > 0);
            }
            catch (Exception ex) { Toast.Show("Erro na busca: " + ex.Message, true); }
            finally { if (!IsDisposed) { Cursor = Cursors.Default; findAll.Enabled = true; } }
        }

        void Add()
        {
            var t = new ToolConfig { Nome = "Novo programa", Exe = "", ModoRapido = false };
            Session.Cfg.Programas.Add(t);
            Session.Cfg.Save();
            RefreshList();
            ShowTool(t);
            Locate();
            if (string.IsNullOrWhiteSpace(t.Nome) || t.Nome == "Novo programa")
                if (!string.IsNullOrEmpty(t.Exe)) name.Text = Path.GetFileNameWithoutExtension(t.Exe);
            name.Focus(); name.SelectAll();
        }

        void Remove()
        {
            if (current == null) return;
            if (MessageBox.Show(this, "Remover " + current.Nome + " da lista?", "Remover programa", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            int i = Session.Cfg.Programas.IndexOf(current);
            Session.Cfg.Programas.Remove(current);
            Session.Cfg.Save();
            current = null;
            RefreshList();
            ShowTool(Session.Cfg.Programas.Count == 0 ? null : Session.Cfg.Programas[Math.Min(i, Session.Cfg.Programas.Count - 1)]);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (Width < 50) return;
            int pad = Theme.S(20), gap = Theme.S(22), ip = Theme.S(20);
            int half = (Width - pad * 2 - gap) / 2;
            int th = Theme.S(110), ih = Theme.S(48);
            int bottom = Height - pad;

            Action<RoundPanel, Label, DarkInput, FlatButton, int> top = (p, l, i, b, px) =>
            {
                p.SetBounds(px, pad, half, th);
                int bw = b.PreferredWidth;
                b.SetBounds(half - ip - bw, Theme.S(42), bw, ih);
                l.SetBounds(ip, Theme.S(14), half - ip * 2, Theme.S(26));
                i.SetBounds(ip, Theme.S(42), b.Left - ip * 2, ih);
            };
            top(osPanel, osLbl, osPath, osChoose, pad);
            top(toolsPanel, toolsLbl, toolsPath, toolsChoose, pad + half + gap);

            int y0 = pad + th + gap;
            int lw = Math.Max(Theme.S(340), (Width - pad * 2) * 30 / 100);
            listPanel.SetBounds(pad, y0, lw, bottom - y0);
            int hh = Theme.S(58), fw = findAll.PreferredWidth;
            listLbl.SetBounds(Theme.S(12), 2, lw - fw - Theme.S(30), hh);
            findAll.SetBounds(lw - fw - Theme.S(14), Theme.S(10), fw, hh - Theme.S(14));
            int aw = add.PreferredWidth;
            add.SetBounds(Theme.S(10), listPanel.Height - Theme.S(60), aw, Theme.S(48));
            int rmw = remove.PreferredWidth;
            remove.SetBounds(lw - rmw - Theme.S(14), listPanel.Height - Theme.S(60), rmw, Theme.S(48));
            list.SetBounds(2, hh + 4, lw - 4, listPanel.Height - hh - Theme.S(72));

            int dx = pad + lw + gap, dw = Width - dx - pad;
            detail.SetBounds(dx, y0, dw, bottom - y0);
            int scrollY = detail.AutoScrollPosition.Y;
            int x = ip, w = dw - ip * 2 - SystemInformation.VerticalScrollBarWidth, y = Theme.S(22) + scrollY;
            name.SetBounds(x, y + Theme.S(6), w, Theme.S(34)); y += Theme.S(56);
            y += Theme.S(8);

            int ow = open.PreferredWidth, lw2 = locate.PreferredWidth;
            exeLbl.SetBounds(x, y, w, Theme.S(26)); y += Theme.S(28);
            open.SetBounds(x + w - ow, y, ow, ih);
            locate.SetBounds(open.Left - Theme.S(16) - lw2, y, lw2, ih);
            exe.SetBounds(x, y, locate.Left - Theme.S(16) - x, ih); y += ih + Theme.S(10);

            int g = Theme.S(16), cw = (w - g * 3) / 4;
            modeLbl.SetBounds(x, y, cw, Theme.S(26)); winLbl.SetBounds(x + cw + g, y, cw, Theme.S(26));
            waitLbl.SetBounds(x + (cw + g) * 2, y, cw, Theme.S(26)); minLbl.SetBounds(x + (cw + g) * 3, y, cw, Theme.S(26)); y += Theme.S(28);
            mode.SetBounds(x, y, cw, ih); win.SetBounds(x + cw + g, y, cw, ih);
            wait.SetBounds(x + (cw + g) * 2, y, cw, ih); minTime.SetBounds(x + (cw + g) * 3, y, cw, ih); y += ih + Theme.S(14);

            // checkboxes em fluxo
            int cx = x, ch = Theme.S(34);
            foreach (var c in new[] { maxBefore, quick, stress, periph, conditional })
            {
                int pw = c.PreferredWidth + Theme.S(8);
                if (cx + pw > x + w && cx > x) { cx = x; y += ch + Theme.S(8); }
                c.SetBounds(cx, y, pw, ch);
                cx += pw + Theme.S(16);
            }
            y += ch + Theme.S(14);

            descLbl.SetBounds(x, y, w, Theme.S(26)); y += Theme.S(28);
            desc.SetBounds(x, y, w, ih); y += ih + Theme.S(10);
            int tw = (w - Theme.S(16) * 2) / 3;
            fileLbl.SetBounds(x, y, tw, Theme.S(26)); argsLbl.SetBounds(x + tw + Theme.S(16), y, tw, Theme.S(26));
            procLbl.SetBounds(x + (tw + Theme.S(16)) * 2, y, tw, Theme.S(26)); y += Theme.S(28);
            file.SetBounds(x, y, tw, ih); args.SetBounds(x + tw + Theme.S(16), y, tw, ih);
            proc.SetBounds(x + (tw + Theme.S(16)) * 2, y, tw, ih);
            detail.AutoScrollMinSize = new Size(0, y + ih + ip - scrollY);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.Clear(BackColor);
        }
    }
}
