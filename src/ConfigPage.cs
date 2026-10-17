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
        readonly RowList list = new RowList { RowHeight = 54, Reorderable = true };
        readonly FlatButton add = new FlatButton("Adicionar programa");

        readonly RoundPanel detail = new RoundPanel { AutoScroll = true };
        readonly Label nameLbl = UI.Label("Nome da etapa");
        readonly DarkInput name = new DarkInput();
        readonly Label exeLbl = UI.Label("Executável ou site (https://...)"), modeLbl = UI.Label("Modo"), winLbl = UI.Label("Janela"),
                       waitLbl = UI.Label("Espera print auto (s)"), descLbl = UI.Label("Descrição (aparece na lista)"),
                       argsLbl = UI.Label("Argumentos"), procLbl = UI.Label("Processo (se diferente do .exe)"),
                       fileLbl = UI.Label("Nome da evidência (arquivo)"), minLbl = UI.Label("Tempo mínimo (min)"),
                       behaviorLbl = UI.Label("Comportamento da etapa"), modesLbl = UI.Label("Modos de análise em que entra");
        readonly DarkInput exe = new DarkInput(), desc = new DarkInput(), wait = new DarkInput(), args = new DarkInput(), proc = new DarkInput(),
                           file = new DarkInput(), minTime = new DarkInput();
        readonly DarkSelect mode = new DarkSelect("manual", "automatico"), win = new DarkSelect("maximizada", "normal", "minimizada");
        readonly FlatButton locate = new FlatButton("Localizar"), addUrl = new FlatButton("+ Adicionar URL"), open = new FlatButton("Abrir"), remove = new FlatButton("Remover");
        readonly ToggleSwitch enabledCheck = new ToggleSwitch("Etapa ativa", "Ativado", "Desativado");
        readonly DarkCheck maxBefore = new DarkCheck("Maximizar antes do print"), quick = new DarkCheck("Modo rápido"),
                           stress = new DarkCheck("Etapa de estresse"), periph = new DarkCheck("Etapa de periféricos"),
                           conditional = new DarkCheck("Condicional (opcional)");
        readonly List<Tuple<DarkInput, FlatButton>> extraUrls = new List<Tuple<DarkInput, FlatButton>>();

        ToolConfig current;
        bool loading;
        bool isSiteMode;
        string autoName = "";
        string autoFile = "";

        public ConfigPage()
        {
            osPanel.Controls.AddRange(new Control[] { osLbl, osPath, osChoose });
            toolsPanel.Controls.AddRange(new Control[] { toolsLbl, toolsPath, toolsChoose });
            listPanel.Controls.AddRange(new Control[] { listLbl, findAll, list, add, remove });
            detail.Controls.AddRange(new Control[] { nameLbl, name, exeLbl, exe, locate, addUrl, open, modeLbl, mode, winLbl, win, waitLbl, wait,
                behaviorLbl, enabledCheck, conditional, maxBefore, modesLbl, quick, stress, periph,
                descLbl, desc, argsLbl, args, procLbl, proc, fileLbl, file, minLbl, minTime });
            Controls.AddRange(new Control[] { osPanel, toolsPanel, listPanel, detail });
            foreach (Control c in detail.Controls) if (c is DarkCheck || c is ToggleSwitch) c.BackColor = Theme.Panel;
            detail.Paint += (s, e) =>
            {
                int scrollY = detail.AutoScrollPosition.Y;
                int lineY = Theme.S(102) + scrollY;
                using (var p = new Pen(Theme.Line)) e.Graphics.DrawLine(p, Theme.S(20), lineY, detail.Width - Theme.S(20), lineY);
                if (current != null)
                {
                    Theme.Smooth(e.Graphics);
                    string badge = current.IsInternal ? "Teste interno" : (isSiteMode || current.IsUrl) ? "Site de teste" : "Teste externo";
                    var colors = current.IsInternal ? Theme.PillBlue : (isSiteMode || current.IsUrl) ? Theme.PillGreen : Theme.PillPurple;
                    int right = detail.Width - Theme.S(20) - SystemInformation.VerticalScrollBarWidth;
                    Theme.Pill(e.Graphics, badge, colors, new Rectangle(Theme.S(20), Theme.S(10) + scrollY, right - Theme.S(20), Theme.S(30)), Theme.Small, true);
                }
            };

            osChoose.Click += (s, e) => ChooseFolder(osPath, "Pasta onde as OS serão salvas", v => Session.Cfg.PastaOS = v);
            toolsChoose.Click += (s, e) => ChooseFolder(toolsPath, "Pasta onde estão os programas", v => Session.Cfg.PastaProgramas = v);
            osPath.Box.Leave += (s, e) => { if (osPath.Text.Trim() != "") { Session.Cfg.PastaOS = osPath.Text.Trim(); Session.Cfg.Save(); } };
            toolsPath.Box.Leave += (s, e) => { if (toolsPath.Text.Trim() != "") { Session.Cfg.PastaProgramas = toolsPath.Text.Trim(); Session.Cfg.Save(); } };

            list.RowClick += (s, i) => ShowTool(list.Rows[i].Tag as ToolConfig);
            list.RowMoved += (from, to) =>
            {
                var item = Session.Cfg.Programas[from];
                Session.Cfg.Programas.RemoveAt(from);
                Session.Cfg.Programas.Insert(to, item);
                Session.Cfg.Save();
                RefreshList();
            };
            findAll.Click += (s, e) => FindAll();
            add.Click += (s, e) => Add();
            remove.Click += (s, e) => Remove();
            locate.Click += (s, e) => Locate();
            addUrl.Click += (s, e) => AddExtraUrlRow("", true);
            open.Click += (s, e) => { if (current != null) Session.Launch(current); };

            name.ValueChanged += (s, e) =>
            {
                if (loading || current == null) return;
                current.Nome = name.Text;
                ApplyAutoFile(false);
                Session.Cfg.Save();
                RefreshList();
            };
            name.Box.Leave += (s, e) =>
            {
                if (current != null && string.IsNullOrWhiteSpace(name.Text))
                {
                    ApplyAutoName(true);
                    Session.Cfg.Save();
                    RefreshList();
                }
            };
            exe.ValueChanged += (s, e) =>
            {
                if (loading || current == null) return;
                current.Exe = exe.Text.Trim();
                bool looksUrl = !current.IsInternal && ToolConfig.LooksLikeUrl(current.Exe);
                if (looksUrl != isSiteMode && (looksUrl || !string.IsNullOrEmpty(current.Exe)))
                {
                    isSiteMode = looksUrl;
                    if (!isSiteMode) ClearExtraUrlRows();
                    UpdateSiteControls();
                    PerformLayout();
                    detail.Invalidate();
                }
                ApplyAutoName(false);
                Session.Cfg.Save();
                RefreshList();
            };
            exe.Box.Leave += (s, e) =>
            {
                if (loading || current == null || current.IsInternal) return;
                if ((isSiteMode || current.IsUrl) && ToolConfig.HasValidUrl(exe.Text))
                {
                    string norm = ToolConfig.NormalizeUrl(exe.Text);
                    if (norm != exe.Text) { loading = true; exe.Text = norm; loading = false; current.Exe = norm; Session.Cfg.Save(); RefreshList(); }
                }
            };
            desc.ValueChanged += (s, e) => Edit(t => t.Descricao = desc.Text);
            args.ValueChanged += (s, e) => Edit(t => t.Argumentos = args.Text);
            proc.ValueChanged += (s, e) => Edit(t => t.Processo = proc.Text.Trim());
            wait.ValueChanged += (s, e) => { int v; if (int.TryParse(wait.Text, out v)) Edit(t => t.Espera = Math.Max(1, v)); };
            mode.ValueChanged += (s, e) => Edit(t => t.Modo = mode.Value, true);
            win.ValueChanged += (s, e) => Edit(t => t.Janela = win.Value);
            enabledCheck.CheckedChanged += (s, e) => { Edit(t => t.Desativado = !enabledCheck.Checked, true); UpdateActionButtons(); };
            maxBefore.CheckedChanged += (s, e) => Edit(t => t.MaximizarAntesPrint = maxBefore.Checked);
            quick.CheckedChanged += (s, e) => Edit(t => t.ModoRapido = quick.Checked);
            stress.CheckedChanged += (s, e) => Edit(t => t.SoEstresse = stress.Checked);
            periph.CheckedChanged += (s, e) => Edit(t => t.SoPerifericos = periph.Checked);
            conditional.CheckedChanged += (s, e) => Edit(t => t.Condicional = conditional.Checked);
            file.ValueChanged += (s, e) => Edit(t => t.Arquivo = file.Text.Trim());
            file.Box.Leave += (s, e) =>
            {
                if (current != null && string.IsNullOrWhiteSpace(file.Text))
                {
                    ApplyAutoFile(true);
                    Session.Cfg.Save();
                }
            };
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

        List<string> CurrentExtraUrls()
        {
            return extraUrls.Select(r => r.Item1.Text.Trim()).Where(s => s != "").ToList();
        }

        void ApplyAutoName(bool forceIfEmpty)
        {
            if (current == null || current.IsInternal) return;
            string suggested = ToolConfig.AutoName(current.Exe, isSiteMode ? CurrentExtraUrls() : current.ExtraUrls());
            if (string.IsNullOrWhiteSpace(suggested)) return;
            string cur = (current.Nome ?? "").Trim();
            bool canAuto = forceIfEmpty || cur == "" || cur == "Novo programa" || cur == "Novo site" ||
                           (!string.IsNullOrEmpty(autoName) && string.Equals(cur, autoName, StringComparison.OrdinalIgnoreCase));
            autoName = suggested;
            if (canAuto && cur != suggested)
            {
                current.Nome = suggested;
                bool prev = loading;
                loading = true;
                name.Text = suggested;
                loading = prev;
                ApplyAutoFile(false);
            }
        }

        void ApplyAutoFile(bool forceIfEmpty)
        {
            if (current == null || current.IsInternal) return;
            string suggested = ToolConfig.AutoFileCode(current.Nome);
            if (string.IsNullOrWhiteSpace(suggested)) return;
            string cur = (current.Arquivo ?? "").Trim();
            bool canAuto = forceIfEmpty || cur == "" ||
                           (!string.IsNullOrEmpty(autoFile) && string.Equals(cur, autoFile, StringComparison.OrdinalIgnoreCase));
            autoFile = suggested;
            if (canAuto && cur != suggested)
            {
                current.Arquivo = suggested;
                bool prev = loading;
                loading = true;
                file.Text = suggested;
                loading = prev;
            }
        }

        void ClearExtraUrlRows()
        {
            foreach (var row in extraUrls)
            {
                detail.Controls.Remove(row.Item1);
                detail.Controls.Remove(row.Item2);
                row.Item1.Dispose();
                row.Item2.Dispose();
            }
            extraUrls.Clear();
        }

        void AddExtraUrlRow(string initialUrl, bool focusNew)
        {
            if (current == null || current.IsInternal) return;
            if (!isSiteMode)
            {
                isSiteMode = true;
                UpdateSiteControls();
            }
            var input = new DarkInput(initialUrl ?? "");
            var rmBtn = new FlatButton("Remover");
            Tuple<DarkInput, FlatButton> row = null;
            row = Tuple.Create(input, rmBtn);
            input.ValueChanged += (s, e) =>
            {
                if (loading || current == null) return;
                SyncExtraUrlsToArgs();
                ApplyAutoName(false);
                Session.Cfg.Save();
                RefreshList();
            };
            input.Box.Leave += (s, e) =>
            {
                if (loading || current == null) return;
                if (ToolConfig.HasValidUrl(input.Text))
                {
                    string norm = ToolConfig.NormalizeUrl(input.Text);
                    if (norm != input.Text) { loading = true; input.Text = norm; loading = false; SyncExtraUrlsToArgs(); Session.Cfg.Save(); RefreshList(); }
                }
            };
            rmBtn.Click += (s, e) =>
            {
                extraUrls.Remove(row);
                detail.Controls.Remove(input);
                detail.Controls.Remove(rmBtn);
                input.Dispose();
                rmBtn.Dispose();
                SyncExtraUrlsToArgs();
                ApplyAutoName(false);
                Session.Cfg.Save();
                RefreshList();
                PerformLayout();
            };
            extraUrls.Add(row);
            detail.Controls.Add(input);
            detail.Controls.Add(rmBtn);
            if (!loading)
            {
                PerformLayout();
                if (focusNew)
                {
                    if (string.IsNullOrEmpty(initialUrl)) { input.Text = "https://"; input.Box.Focus(); input.Box.SelectAll(); }
                    else input.Box.Focus();
                }
            }
        }

        void SyncExtraUrlsToArgs()
        {
            if (current == null || !isSiteMode) return;
            var urls = extraUrls
                .Select(r => r.Item1.Text.Trim())
                .Where(ToolConfig.HasValidUrl)
                .Select(ToolConfig.NormalizeUrl)
                .ToList();
            current.Argumentos = string.Join(" ", urls);
            bool prev = loading;
            loading = true;
            args.Text = current.Argumentos;
            loading = prev;
        }

        bool IsExternalExe
        {
            get { return current != null && !current.IsInternal && !isSiteMode && !current.IsUrl; }
        }

        void UpdateSiteControls()
        {
            bool on = current != null;
            bool internalTool = on && current.IsInternal;
            bool extExe = IsExternalExe;
            exe.ReadOnly = internalTool;
            locate.Visible = extExe;
            locate.Enabled = on && extExe;
            addUrl.Visible = isSiteMode && !internalTool;
            addUrl.Enabled = on && isSiteMode && !internalTool;
            exeLbl.Text = internalTool ? "Recurso interno (fixo do sistema)"
                        : isSiteMode ? "URL do site (https://...)"
                        : "Executável (.exe, .bat) ou site (https://...)";

            foreach (Control c in new Control[] { modeLbl, mode, winLbl, win, waitLbl, wait, minLbl, minTime, maxBefore, argsLbl, args, procLbl, proc })
                c.Visible = extExe;
        }

        void RefreshList()
        {
            list.SetRows(Session.Cfg.Programas.Select(t =>
            {
                int urlCount = t.IsUrl ? t.AllUrls().Count : 0;
                string siteSub = urlCount > 1 ? "Site (online · " + urlCount + " URLs)" : "Site (online)";
                return new ListRow
                {
                    Text = (string.IsNullOrWhiteSpace(t.Nome) ? "(sem nome)" : t.Nome) + (t.Modo == "automatico" ? "  ⚡" : ""),
                    KindBadge = t.IsInternal ? "Interno" : t.IsUrl ? "Site" : "Externo",
                    KindColors = t.IsInternal ? Theme.PillBlue : t.IsUrl ? Theme.PillGreen : Theme.PillPurple,
                    Sub = t.IsInternal ? "Embutido no programa" : t.IsUrl ? siteSub : string.IsNullOrWhiteSpace(t.Exe) ? "Sem executável" : ToolDiscovery.FileNameOf(t.Exe),
                    Dimmed = t.Desativado,
                    Badge = t.Desativado ? "Desativado" : t.IsInternal ? "Ativo" : t.Exists ? "Encontrado" : "Não encontrado",
                    BadgeColors = t.Desativado ? Theme.PillGray : t.Exists ? Theme.PillGreen : Theme.PillRed,
                    Tag = t,
                };
            }));
            list.SelectedIndex = Session.Cfg.Programas.IndexOf(current);
            list.Invalidate();
        }

        void LayoutListButtons()
        {
            int lw = listPanel.Width, bh = Theme.S(48), by = listPanel.Height - Theme.S(60);
            int rmw = remove.PreferredWidth;
            remove.SetBounds(lw - rmw - Theme.S(12), by, rmw, bh);
            int aw = Math.Min(add.PreferredWidth, Math.Max(Theme.S(120), remove.Left - Theme.S(20)));
            add.SetBounds(Theme.S(12), by, aw, bh);
        }

        void UpdateActionButtons()
        {
            bool on = current != null;
            remove.Enabled = on;
            remove.Text = on && current.IsInternal ? (current.Desativado ? "Ativar" : "Desativar") : "Remover";
            if (listPanel.Width > 50) LayoutListButtons();
            remove.Invalidate();
        }

        void ShowTool(ToolConfig t, bool forceSiteMode = false)
        {
            current = t;
            loading = true;
            isSiteMode = t != null && !t.IsInternal && (forceSiteMode || t.IsUrl);
            ClearExtraUrlRows();
            bool on = t != null;
            foreach (Control c in detail.Controls) c.Enabled = on;
            if (t != null)
            {
                autoName = ToolConfig.AutoName(t.Exe, t.ExtraUrls());
                autoFile = ToolConfig.AutoFileCode(t.Nome);
                if (string.IsNullOrWhiteSpace(t.Arquivo) && !string.IsNullOrEmpty(autoFile))
                    t.Arquivo = autoFile;
                name.Text = t.Nome; exe.Text = t.Exe; desc.Text = t.Descricao; args.Text = t.Argumentos; proc.Text = t.Processo;
                wait.Text = t.Espera.ToString(); mode.Value = t.Modo; win.Value = t.Janela;
                enabledCheck.Checked = !t.Desativado;
                maxBefore.Checked = t.MaximizarAntesPrint; quick.Checked = t.ModoRapido; stress.Checked = t.SoEstresse; periph.Checked = t.SoPerifericos;
                conditional.Checked = t.Condicional; file.Text = t.Arquivo; minTime.Text = t.MinutosMinimos.ToString();
                if (isSiteMode)
                    foreach (var u in t.ExtraUrls())
                        AddExtraUrlRow(u, false);
            }
            else { autoName = ""; autoFile = ""; }
            UpdateSiteControls();
            loading = false;
            UpdateActionButtons();
            list.SelectedIndex = Session.Cfg.Programas.IndexOf(t);
            list.Invalidate();
            detail.Invalidate();
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
            if (current == null || current.IsInternal) return;
            using (var dlg = new OpenFileDialog { Filter = "Programas|*.exe;*.bat;*.cmd|Todos|*.*", Title = "Executável de " + current.Nome })
            {
                try { dlg.InitialDirectory = current.Exists && !current.IsUrl ? Path.GetDirectoryName(current.FullPath) : Session.Cfg.ToolsRoot; } catch { }
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                string rel = AppPaths.ToRelative(dlg.FileName);
                var preset = ToolDiscovery.MatchPreset(dlg.FileName, Session.Cfg.Programas, current);
                if (preset != null && (current.Nome == "Novo programa" || string.IsNullOrWhiteSpace(current.Arquivo) || string.Equals(current.Arquivo, preset.Arquivo, StringComparison.OrdinalIgnoreCase)))
                {
                    AppConfig.CopyPresetTo(preset, current, rel);
                    Session.Cfg.Save();
                    RefreshList();
                    ShowTool(current);
                    Toast.Show("Pré-configuração aplicada: " + current.Nome + (current.Modo == "automatico" ? " (modo automático ⚡)" : ""));
                    return;
                }
                isSiteMode = false;
                ClearExtraUrlRows();
                UpdateSiteControls();
                exe.Text = rel;
                ApplyAutoName(false);
                Session.Cfg.Save();
                RefreshList();
                PerformLayout();
            }
        }

        // Procura cada executável (pelo nome do arquivo) dentro da pasta dos programas e adiciona pré-configurados encontrados.
        async void FindAll()
        {
            Cursor = Cursors.WaitCursor;
            findAll.Enabled = false;
            try
            {
                var roots = ToolDiscovery.SearchRoots(Session.Cfg).ToList();
                var files = await System.Threading.Tasks.Task.Run(() => ToolDiscovery.Executables(roots));
                if (IsDisposed) return;
                int added = ToolDiscovery.DiscoverPresets(Session.Cfg, files, Environment.Is64BitOperatingSystem);
                int found = ToolDiscovery.FindMissing(Session.Cfg, files, Environment.Is64BitOperatingSystem);
                int missing = Session.Cfg.Programas.Count - found;
                Session.Cfg.Save();
                RefreshList();
                if (current != null) ShowTool(current);
                else if (Session.Cfg.Programas.Count > 0) ShowTool(Session.Cfg.Programas[0]);
                string extra = added > 0 ? " (" + added + " pré-configurado(s) adicionado(s))" : "";
                Toast.Show(found + " encontrado(s), " + missing + " não encontrado(s)" + extra + ".", missing > 0);
            }
            catch (Exception ex) { Toast.Show("Erro na busca: " + ex.Message, true); }
            finally { if (!IsDisposed) { Cursor = Cursors.Default; findAll.Enabled = true; } }
        }

        void Add()
        {
            var items = new List<Tuple<string, Action>>
            {
                UI.Item("Programa (.exe, .bat)...", AddCustomProgram),
                UI.Item("Site de teste (URL)...", AddCustomSite),
                null,
            };
            foreach (var p in AppConfig.PresetTools())
            {
                var preset = p;
                string label = preset.Nome + (preset.Modo == "automatico" ? "  (automático ⚡)" : "");
                items.Add(UI.Item(label, () => AddPreset(preset)));
            }
            var menu = UI.Menu(items.ToArray());
            menu.Show(add, new Point(0, -menu.PreferredSize.Height - 2));
        }

        void AddPreset(ToolConfig preset)
        {
            var t = new ToolConfig();
            AppConfig.CopyPresetTo(preset, t);
            try
            {
                var files = ToolDiscovery.Executables(ToolDiscovery.SearchRoots(Session.Cfg));
                var names = ToolDiscovery.FamilyOf(t.Exe);
                bool is64 = Environment.Is64BitOperatingSystem;
                string hit = files.Where(f => names.Contains(ToolDiscovery.FileNameOf(f), StringComparer.OrdinalIgnoreCase) && ToolDiscovery.Compatible(f, is64))
                    .OrderBy(f => is64 && Path.GetFileNameWithoutExtension(ToolDiscovery.FileNameOf(f)).EndsWith("64", StringComparison.OrdinalIgnoreCase) ? 0 : 1).FirstOrDefault();
                if (hit != null) t.Exe = AppPaths.ToRelative(hit);
            }
            catch { }
            Session.Cfg.Programas.Add(t);
            Session.Cfg.Save();
            RefreshList();
            ShowTool(t);
            if (!t.Exists) Locate();
        }

        void AddCustomProgram()
        {
            var t = new ToolConfig { Nome = "Novo programa", Exe = "", ModoRapido = false };
            Session.Cfg.Programas.Add(t);
            Session.Cfg.Save();
            RefreshList();
            ShowTool(t, false);
            Locate();
            ApplyAutoName(false);
            Session.Cfg.Save();
            RefreshList();
            name.Box.Focus();
            name.Box.SelectAll();
        }

        void AddCustomSite()
        {
            var t = new ToolConfig { Nome = "Novo site", Exe = "https://", Modo = "manual", SoPerifericos = true };
            Session.Cfg.Programas.Add(t);
            Session.Cfg.Save();
            RefreshList();
            ShowTool(t, true);
            exe.Box.Focus();
            exe.Box.SelectAll();
        }

        void Remove()
        {
            if (current == null) return;
            if (current.IsInternal)
            {
                current.Desativado = !current.Desativado;
                Session.Cfg.Save();
                RefreshList();
                ShowTool(current);
                Toast.Show(current.Nome + (current.Desativado ? " desativado." : " ativado."));
                return;
            }
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
            LayoutListButtons();
            list.SetBounds(2, hh + 4, lw - 4, listPanel.Height - hh - Theme.S(72));

            int dx = pad + lw + gap, dw = Width - dx - pad;
            detail.SetBounds(dx, y0, dw, bottom - y0);
            int scrollY = detail.AutoScrollPosition.Y;
            int x = ip, w = dw - ip * 2 - SystemInformation.VerticalScrollBarWidth, y = Theme.S(12) + scrollY;
            nameLbl.SetBounds(x, y, Math.Max(Theme.S(120), w - Theme.S(150)), Theme.S(26)); y += Theme.S(28);
            name.SetBounds(x, y, w, ih); y += ih + Theme.S(24);

            int ow = open.PreferredWidth;
            exeLbl.SetBounds(x, y, w, Theme.S(26)); y += Theme.S(28);
            open.SetBounds(x + w - ow, y, ow, ih);
            FlatButton actionBtn = isSiteMode ? addUrl : (IsExternalExe ? locate : null);
            if (actionBtn != null && actionBtn.Visible)
            {
                int aw2 = actionBtn.PreferredWidth;
                actionBtn.SetBounds(open.Left - Theme.S(12) - aw2, y, aw2, ih);
                exe.SetBounds(x, y, actionBtn.Left - Theme.S(12) - x, ih);
            }
            else
            {
                exe.SetBounds(x, y, open.Left - Theme.S(12) - x, ih);
            }
            y += ih + Theme.S(12);

            foreach (var row in extraUrls)
            {
                int rw = row.Item2.PreferredWidth;
                row.Item2.SetBounds(x + w - rw, y, rw, ih);
                row.Item1.SetBounds(x, y, row.Item2.Left - Theme.S(12) - x, ih);
                y += ih + Theme.S(10);
            }

            bool extExe = IsExternalExe;
            if (extExe)
            {
                int g = Theme.S(16), cw = (w - g * 3) / 4;
                modeLbl.SetBounds(x, y, cw, Theme.S(26)); winLbl.SetBounds(x + cw + g, y, cw, Theme.S(26));
                waitLbl.SetBounds(x + (cw + g) * 2, y, cw, Theme.S(26)); minLbl.SetBounds(x + (cw + g) * 3, y, cw, Theme.S(26)); y += Theme.S(28);
                mode.SetBounds(x, y, cw, ih); win.SetBounds(x + cw + g, y, cw, ih);
                wait.SetBounds(x + (cw + g) * 2, y, cw, ih); minTime.SetBounds(x + (cw + g) * 3, y, cw, ih); y += ih + Theme.S(14);
            }

            // Grupo 1: Comportamento da etapa (ToggleSwitch Ativado/Desativado + Condicional + Maximizar antes do print)
            int ch = Theme.S(34);
            behaviorLbl.SetBounds(x, y, w, Theme.S(24)); y += Theme.S(26);
            int cx = x;
            foreach (var c in new Control[] { enabledCheck, conditional, maxBefore })
            {
                if (!c.Visible) continue;
                int pw = (c is ToggleSwitch ? ((ToggleSwitch)c).PreferredWidth : ((DarkCheck)c).PreferredWidth) + Theme.S(8);
                if (cx + pw > x + w && cx > x) { cx = x; y += ch + Theme.S(8); }
                c.SetBounds(cx, y, pw, ch);
                cx += pw + Theme.S(20);
            }
            y += ch + Theme.S(14);

            // Grupo 2: Modos de análise em que entra
            modesLbl.SetBounds(x, y, w, Theme.S(24)); y += Theme.S(26);
            cx = x;
            foreach (var c in new[] { quick, stress, periph })
            {
                int pw = c.PreferredWidth + Theme.S(8);
                if (cx + pw > x + w && cx > x) { cx = x; y += ch + Theme.S(8); }
                c.SetBounds(cx, y, pw, ch);
                cx += pw + Theme.S(20);
            }
            y += ch + Theme.S(14);

            descLbl.SetBounds(x, y, w, Theme.S(26)); y += Theme.S(28);
            desc.SetBounds(x, y, w, ih); y += ih + Theme.S(12);
            if (extExe)
            {
                int tw = (w - Theme.S(16) * 2) / 3;
                fileLbl.SetBounds(x, y, tw, Theme.S(26)); argsLbl.SetBounds(x + tw + Theme.S(16), y, tw, Theme.S(26));
                procLbl.SetBounds(x + (tw + Theme.S(16)) * 2, y, tw, Theme.S(26)); y += Theme.S(28);
                file.SetBounds(x, y, tw, ih); args.SetBounds(x + tw + Theme.S(16), y, tw, ih);
                proc.SetBounds(x + (tw + Theme.S(16)) * 2, y, tw, ih);
            }
            else
            {
                fileLbl.SetBounds(x, y, w, Theme.S(26)); y += Theme.S(28);
                file.SetBounds(x, y, w, ih);
            }
            detail.AutoScrollMinSize = new Size(0, y + ih + ip - scrollY);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.Clear(BackColor);
        }
    }
}
