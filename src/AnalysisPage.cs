using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Diagnostico
{
    class AnalysisPage : DrawnControl, IPage
    {
        public string Title { get { return "Fazer análise"; } }

        static MachineInfo machine;          // lido uma vez por execução (botão Atualizar relê)
        static Task<MachineInfo> machineTask;

        readonly RoundPanel osPanel = new RoundPanel();
        readonly DarkInput osNumber = new DarkInput(), tecnico = new DarkInput(), cliente = new DarkInput();
        readonly Label osTitle = UI.Label("ORDEM DE SERVIÇO", Theme.Big, Theme.Text);
        readonly Label osLbl = UI.Label("Número da OS"), tecLbl = UI.Label("Técnico"), cliLbl = UI.Label("Cliente"), phaseLbl = UI.Label("Fase da manutenção");
        readonly Segmented phase = new Segmented("Antes", "Depois") { BackColor = Theme.Panel };

        readonly RoundPanel progPanel = new RoundPanel { Radius = 8 };
        readonly Label progLbl = UI.Label("ETAPAS", Theme.Body);
        readonly DarkSelect mode = new DarkSelect("Completo", "Rápido", "Estresse", "Periféricos", "Todos");
        readonly FlatButton openAll = new FlatButton("Abrir todos");
        // Testes de periféricos: pelo próprio programa (sem internet), por sites, ou os dois
        readonly Segmented tipo = new Segmented("Testes offline", "Testes online", "Os dois") { BackColor = Theme.Panel };
        static readonly string[] Tipos = { "Offline", "Online", "Ambos" };
        readonly RowList progList = new RowList { RowHeight = 56 };
        readonly ProgressStrip progress = new ProgressStrip();
        int rowH = 40;

        readonly RoundPanel testPanel = new RoundPanel { Radius = 8 };
        readonly Label testLbl = UI.Label("TESTES", Theme.Body);
        readonly Panel testRows = new Panel { AutoScroll = true, BackColor = Theme.Panel };
        readonly List<Tuple<Label, Segmented>> tests = new List<Tuple<Label, Segmented>>();
        // respostas por fase: [0] = antes, [1] = depois
        readonly Dictionary<string, int>[] answers = { new Dictionary<string, int>(), new Dictionary<string, int>() };

        readonly RoundPanel infoPanel = new RoundPanel { Radius = 8 };
        readonly InfoView info = new InfoView();
        readonly FlatButton refresh = new FlatButton("Atualizar");

        readonly FlatButton checklistBtn = new FlatButton("Checklist");
        readonly FlatButton compareBtn = new FlatButton("Antes x Depois");
        readonly FlatButton printBtn = new FlatButton("Print do resumo");
        readonly FlatButton exportBtn = new FlatButton("Exportar JSON", true);

        readonly Timer statusTimer = new Timer { Interval = 1000 };
        readonly Timer loadTimer = new Timer { Interval = 700 };
        static readonly string[] Answers = { "sim", "nao", "nao_tem" };

        public AnalysisPage()
        {
            osPanel.Controls.AddRange(new Control[] { osLbl, osNumber, cliLbl, cliente, tecLbl, tecnico, phaseLbl, phase });
            tecnico.Text = Session.Cfg.Tecnico ?? "";
            progPanel.Controls.AddRange(new Control[] { progLbl, progress, mode, openAll, tipo, progList });
            testPanel.Controls.AddRange(new Control[] { testLbl, testRows });
            infoPanel.Controls.AddRange(new Control[] { info, refresh });
            Controls.AddRange(new Control[] { osPanel, progPanel, testPanel, infoPanel, checklistBtn, compareBtn, printBtn, exportBtn });

            phase.SelectedIndex = Session.Depois ? 1 : 0;
            phase.SelectedChanged += (s, e) =>
            {
                if (phase.SelectedIndex < 0) phase.SelectedIndex = Session.Depois ? 1 : 0;   // sempre uma fase marcada
                bool depois = phase.SelectedIndex == 1;
                if (depois == Session.Depois) return;
                StoreAnswers();
                Session.Depois = depois;
                ShowAnswers();
                RefreshPrograms();
            };

            mode.Value = Session.Cfg.ModoAnalise ?? "Completo";
            mode.ValueChanged += (s, e) => { Session.Cfg.ModoAnalise = mode.Value; Session.Cfg.Save(); RefreshPrograms(); };
            tipo.AccessibleName = "Fazer os testes de periféricos offline, online ou os dois";
            tipo.SelectedIndex = TipoIndex();
            tipo.SelectedChanged += (s, e) =>
            {
                if (tipo.SelectedIndex < 0) tipo.SelectedIndex = TipoIndex();   // sempre uma opção marcada
                string v = Tipos[tipo.SelectedIndex];
                if (v == Session.Cfg.TipoTestes) return;
                Session.Cfg.TipoTestes = v; Session.Cfg.Save(); RefreshPrograms();
            };
            openAll.Click += async (s, e) => await OpenAll();
            progList.RowClick += (s, i) => { var t = progList.Rows[i].Tag as ToolConfig; if (t != null) { Session.Launch(t); RefreshPrograms(); } };
            progList.RowRightClick += (s, i) => ToolMenu(progList.Rows[i].Tag as ToolConfig);

            osNumber.ValueChanged += (s, e) => { Session.ActiveFolder = Session.FolderForOs(osNumber.Text); loadTimer.Stop(); loadTimer.Start(); RefreshPrograms(); };
            loadTimer.Tick += (s, e) => { loadTimer.Stop(); LoadExisting(); };
            statusTimer.Tick += (s, e) => RefreshPrograms();

            refresh.Click += async (s, e) => { machineTask = null; await LoadMachine(); };
            printBtn.Click += async (s, e) => await Session.Capture(Session.MainWindow.Handle, null, "RESUMO_Diagnostico", false, Session.Cfg.PrintTelaInteira);
            exportBtn.Click += (s, e) => Export();
            checklistBtn.Click += (s, e) => { if (NeedFolder()) using (var d = new ChecklistDialog(Session.ActiveFolder)) d.ShowDialog(FindForm()); RefreshPrograms(); };
            compareBtn.Click += (s, e) => { if (NeedFolder()) using (var d = new CompareDialog(Session.ActiveFolder, machine)) d.ShowDialog(FindForm()); };
            Session.PhotoSaved += p => { if (Visible) RefreshPrograms(); };
            Session.TestAnswered += (name, answer) =>
            {
                foreach (var t in tests)
                    if (LaudoExport.Normalize(t.Item1.Text) == LaudoExport.Normalize(name)) t.Item2.SelectedIndex = answer;
                StoreAnswers();
            };

            BuildTests();
            try { Native.SetWindowTheme(testRows.Handle, Theme.IsDark ? "DarkMode_Explorer" : "Explorer", null); } catch { }
        }

        static int TipoIndex()
        {
            int i = Array.IndexOf(Tipos, Session.Cfg.TipoTestes);
            return i < 0 ? 2 : i;   // config antigo ou valor desconhecido: os dois
        }

        bool NeedFolder()
        {
            if (!string.IsNullOrEmpty(Session.ActiveFolder)) return true;
            Toast.Show("Informe o número da OS primeiro.", true);
            osNumber.Box.Focus();
            return false;
        }

        void BuildTests()
        {
            foreach (var name in Session.Cfg.Testes)
            {
                var lbl = UI.Label(name, Theme.Body, Theme.Text);
                var seg = new Segmented("Sim", "Não", "Não Tem") { BackColor = Theme.Panel };
                tests.Add(Tuple.Create(lbl, seg));
                testRows.Controls.Add(lbl);
                testRows.Controls.Add(seg);
            }
            testRows.Paint += (s, e) =>
            {
                using (var p = new Pen(Theme.Line))
                    for (int i = 0; i < tests.Count; i++)
                    {
                        int y = i * rowH + testRows.AutoScrollPosition.Y;
                        e.Graphics.DrawLine(p, 0, y, testRows.Width, y);
                    }
            };
        }

        void StoreAnswers()
        {
            var d = answers[Session.Depois ? 1 : 0];
            d.Clear();
            foreach (var t in tests) if (t.Item2.SelectedIndex >= 0) d[t.Item1.Text] = t.Item2.SelectedIndex;
        }

        void ShowAnswers()
        {
            var d = answers[Session.Depois ? 1 : 0];
            foreach (var t in tests)
            {
                int v;
                t.Item2.SelectedIndex = d.TryGetValue(t.Item1.Text, out v) ? v : -1;
            }
            testLbl.Text = "TESTES  ·  " + (Session.Depois ? "depois da manutenção" : "antes da manutenção");
        }

        public void OnEnter()
        {
            Session.ActiveFolder = Session.FolderForOs(osNumber.Text);
            phase.SelectedIndex = Session.Depois ? 1 : 0;
            ShowAnswers();
            RefreshPrograms();
            statusTimer.Start();
            if (machine == null) { var _ = LoadMachine(); }
            else info.Set(machine);
            BeginInvoke(new Action(() => osNumber.Box.Focus()));
        }

        public void OnLeave() { statusTimer.Stop(); StoreAnswers(); }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (Width < 50) return;
            int pad = Theme.S(20), gap = Theme.S(16);
            int bottom = Height - pad, w = Width - pad * 2;

            // Faixa da OS: Número | Cliente | Técnico | Fase
            int osH = Theme.S(98);
            osPanel.SetBounds(pad, pad, w, osH);
            int ip = Theme.S(18), g3 = Theme.S(14), inner = w - ip * 2 - g3 * 3;
            int[] widths = { inner * 17 / 100, inner * 37 / 100, inner * 22 / 100, 0 };
            widths[3] = inner - widths[0] - widths[1] - widths[2];
            Control[] labels = { osLbl, cliLbl, tecLbl, phaseLbl };
            Control[] fields = { osNumber, cliente, tecnico, phase };
            int fx = ip;
            for (int i = 0; i < 4; i++)
            {
                labels[i].SetBounds(fx, Theme.S(10), widths[i], Theme.S(26));
                fields[i].SetBounds(fx, Theme.S(38), widths[i], Theme.S(46));
                fx += widths[i] + g3;
            }

            // Três colunas: etapas | testes | especificação + ações
            int top = pad + osH + gap, colH = bottom - top;
            int wA = (w - gap * 2) * 40 / 100, wB = (w - gap * 2) * 30 / 100, wC = w - gap * 2 - wA - wB;
            int xA = pad, xB = xA + wA + gap, xC = xB + wB + gap;
            int hh = Theme.S(140);

            progPanel.SetBounds(xA, top, wA, colH);
            progLbl.SetBounds(Theme.S(14), 2, Theme.S(78), Theme.S(42));
            int bw = openAll.PreferredWidth;
            openAll.SetBounds(wA - bw - Theme.S(12), Theme.S(48), bw, Theme.S(36));
            mode.SetBounds(Theme.S(14), Theme.S(48), openAll.Left - Theme.S(26), Theme.S(36));
            tipo.SetBounds(Theme.S(14), Theme.S(94), wA - Theme.S(26), Theme.S(36));
            progress.SetBounds(progLbl.Right + Theme.S(12), Theme.S(8), wA - progLbl.Right - Theme.S(28), Theme.S(30));
            progList.SetBounds(2, hh + 2, wA - 4, colH - hh - 6);

            testPanel.SetBounds(xB, top, wB, colH);
            hh = Theme.S(52);
            testLbl.SetBounds(Theme.S(14), 2, wB - Theme.S(28), hh);
            testRows.SetBounds(2, hh, wB - 4, colH - hh - 4);
            int n = Math.Max(1, tests.Count);
            int readableWidth = TextRenderer.MeasureText("Armazenamento", Theme.Body).Width +
                3 * TextRenderer.MeasureText("Não Tem", Theme.Body).Width + Theme.S(34);
            bool stacked = wB < Math.Max(Theme.S(320), readableWidth);
            rowH = stacked ? Theme.S(74) : Math.Max(Theme.S(42), Math.Min(Theme.S(50), (testRows.Height - 2) / n));
            int sb = rowH * n > testRows.Height ? SystemInformation.VerticalScrollBarWidth : 0;
            int segW = Math.Min(Theme.S(230), wB * 55 / 100);
            for (int i = 0; i < tests.Count; i++)
            {
                int y = i * rowH + testRows.AutoScrollPosition.Y;
                if (stacked)
                {
                    tests[i].Item1.SetBounds(Theme.S(12), y + Theme.S(4), wB - Theme.S(30) - sb, Theme.S(26));
                    tests[i].Item2.SetBounds(Theme.S(12), y + Theme.S(34), wB - Theme.S(30) - sb, Theme.S(32));
                }
                else
                {
                    tests[i].Item1.SetBounds(Theme.S(12), y + 1, wB - segW - Theme.S(28) - sb, rowH - 2);
                    tests[i].Item2.SetBounds(wB - segW - Theme.S(12) - sb, y + Theme.S(5), segW, rowH - Theme.S(10));
                }
            }
            testRows.Invalidate();

            int btnH = Theme.S(46), bg = Theme.S(10), block = btnH * 2 + bg;
            infoPanel.SetBounds(xC, top, wC, colH - block - gap);
            info.SetBounds(2, 2, wC - 4, infoPanel.Height - 4);
            int rw = refresh.PreferredWidth;
            refresh.SetBounds(wC - rw - Theme.S(12), Theme.S(9), rw, Theme.S(34));
            int by = bottom - block, half = (wC - bg) / 2;
            checklistBtn.SetBounds(xC, by, half, btnH);
            compareBtn.SetBounds(xC + half + bg, by, wC - half - bg, btnH);
            printBtn.SetBounds(xC, by + btnH + bg, half, btnH);
            exportBtn.SetBounds(xC + half + bg, by + btnH + bg, wC - half - bg, btnH);
        }

        // ---------- etapas ----------

        void RefreshPrograms()
        {
            var folder = Session.ActiveFolder;
            var rows = new List<ListRow>();
            int selected = -1, done = 0, total = 0;
            foreach (var t in Session.ToolsForMode(mode.Value))
            {
                var st = Session.StatusOf(t, folder);
                bool cond = Session.IsConditional(t);
                string badge; Color[] colors;
                switch (st)
                {
                    case ToolStatus.FotoTirada: badge = "Foto tirada"; colors = Theme.PillGreen; break;
                    case ToolStatus.SegundoPlano: badge = "Em segundo plano"; colors = Theme.PillYellow; break;
                    case ToolStatus.Aberto: badge = "Aberto"; colors = Theme.PillBlue; break;
                    case ToolStatus.NaoEncontrado: badge = "Não encontrado"; colors = Theme.PillRed; break;
                    default: badge = cond ? "Condicional" : "Pendente"; colors = cond ? Theme.PillPurple : Theme.PillYellow; break;
                }
                if (t == Session.CurrentStep) selected = rows.Count;
                if (!cond || st == ToolStatus.FotoTirada) { total++; if (st == ToolStatus.FotoTirada) done++; }
                rows.Add(new ListRow
                {
                    Lead = (rows.Count + 1).ToString(), LeadDone = st == ToolStatus.FotoTirada,
                    Text = t.Nome + (t.Modo == "automatico" ? "  ⚡" : ""),
                    Sub = t.Codigo + (string.IsNullOrEmpty(t.Descricao) ? "" : "  ·  " + t.Descricao),
                    Badge = badge, BadgeColors = colors, Tag = t,
                });
            }
            progList.SetRows(rows);
            progress.Set(done, total);
            progList.SelectedIndex = selected;
            progList.Invalidate();
        }

        async Task OpenAll()
        {
            if (!NeedFolder()) return;
            var list = Session.ToolsForMode(mode.Value).Where(t => t.Exists && !t.IsUrl && !t.IsInternal).ToList();
            if (list.Count == 0) { Toast.Show("Nenhum programa encontrado para o modo " + mode.Value + ".", true); return; }
            openAll.Enabled = false;
            var keep = Session.CurrentStep;
            foreach (var t in list)
            {
                Session.Launch(t);
                await Task.Delay(1500);
            }
            Session.CurrentStep = keep;
            openAll.Enabled = true;
        }

        void ToolMenu(ToolConfig t)
        {
            if (t == null) return;
            UI.Menu(
                UI.Item("Abrir / trazer para frente", () => { Session.Launch(t); RefreshPrograms(); }),
                UI.Item("Marcar como etapa atual (próximo print)", () => { Session.CurrentStep = t; RefreshPrograms(); }),
                UI.Item("Tirar print agora", async () =>
                {
                    var h = Session.MainWindowOf(t);
                    if (h == IntPtr.Zero) { Toast.Show(t.Nome + " não está aberto.", true); return; }
                    Session.CurrentStep = t;
                    Native.ForceForeground(h);
                    await Task.Delay(500);
                    await Session.Capture(h, t, t.Codigo, t.MaximizarAntesPrint, Session.Cfg.PrintTelaInteira);
                })
            ).Show(Cursor.Position);
        }

        // ---------- hardware ----------

        async Task LoadMachine()
        {
            info.Loading = true;
            refresh.Enabled = false;
            if (machineTask == null) machineTask = Task.Run(() => SystemInfo.Collect());
            try { machine = await machineTask; info.Set(machine); }
            catch (Exception ex) { info.Error = ex.Message; }
            refresh.Enabled = true;
        }

        // ---------- JSON da OS ----------

        bool answersFromFile;

        void LoadExisting()
        {
            var folder = Session.ActiveFolder;
            bool exists = folder != null && File.Exists(OsJson.PathOf(folder));
            // Só descarta as respostas quando elas vieram de outra OS já gravada (ou vão ser substituídas pelas desta);
            // o que o técnico marcou antes de digitar o número de uma OS nova é mantido.
            if (exists || answersFromFile) { answers[0].Clear(); answers[1].Clear(); }
            else StoreAnswers();
            answersFromFile = exists;
            if (exists)
            {
                var d = OsJson.Load(folder);
                if (OsJson.Str(d, "tecnico") != "") tecnico.Text = OsJson.Str(d, "tecnico");
                cliente.Text = OsJson.Str(d, "cliente");
                string[] keys = { "testes", "testes_depois" };
                for (int ph = 0; ph < 2; ph++)
                {
                    var td = OsJson.Section(d, keys[ph]);
                    if (td == null) continue;
                    foreach (var kv in td)
                    {
                        int idx = Array.IndexOf(Answers, kv.Value as string);
                        if (idx >= 0) answers[ph][kv.Key] = idx;
                    }
                }
                Toast.Show("OS " + Path.GetFileName(folder) + " já existe: dados anteriores carregados.");
            }
            ShowAnswers();
        }

        Dictionary<string, object> AnswersJson(int ph)
        {
            var td = new Dictionary<string, object>();
            foreach (var t in tests)
            {
                int v;
                td[t.Item1.Text] = answers[ph].TryGetValue(t.Item1.Text, out v) ? Answers[v] : null;
            }
            return td;
        }

        void Export()
        {
            string folder = Session.EnsureActiveFolder();
            if (folder == null) { osNumber.Box.Focus(); return; }
            try
            {
                StoreAnswers();
                var m = machine ?? new MachineInfo();
                var d = OsJson.Load(folder);       // preserva comparação e observações gravadas pela outra tela
                d["os"] = Path.GetFileName(folder);
                d["cliente"] = cliente.Text.Trim();
                d["tecnico"] = tecnico.Text.Trim();
                d["data"] = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");
                d["fase"] = Session.Depois ? "depois" : "antes";
                d["modo_analise"] = mode.Value;
                var eq = new Dictionary<string, object>
                {
                    { "fabricante", m.Fabricante }, { "modelo", m.Modelo }, { "serial", m.Serial }, { "tipo", m.Tipo },
                    { "processador", m.Processador }, { "memoria", m.Memoria }, { "sistema", m.Sistema },
                    { "tela", m.TelaValor }, { "tela_largura", m.TelaLargura }, { "tela_altura", m.TelaAltura }, { "tela_hz", m.TelaHz },
                };
                var saude = new Dictionary<string, object>
                {
                    { "discos", m.Discos.Select(x => new Dictionary<string, object> {
                        { "titulo", x.Title }, { "modelo", x.Model }, { "tipo", x.Kind }, { "marca", x.Brand }, { "tamanho", x.Size },
                        { "status", x.Status }, { "percentual", x.Percent >= 0 ? (object)x.Percent : null }, { "valor", x.Value } }).ToList() },
                    { "fonte_discos", m.FonteDiscos },
                    { "bateria", m.BateriaValor },
                    { "bateria_percentual", m.BateriaPercent >= 0 ? (object)m.BateriaPercent : null },
                };
                if (machine != null)
                {
                    d["equipamento"] = eq;
                    d[Session.Depois ? "saude_depois" : "saude"] = saude;
                }
                d["testes"] = AnswersJson(0);
                d["testes_depois"] = AnswersJson(1);
                d["evidencias"] = Evidence.All().Select(e => new Dictionary<string, object> {
                    { "codigo", e.Codigo }, { "nome", e.Nome }, { "tipo", e.IsPhoto ? "foto" : "print" }, { "condicional", e.Condicional },
                    { "antes", Evidence.FilesOf(folder, e.Codigo, false).Select(Path.GetFileName).ToList() },
                    { "depois", Evidence.FilesOf(folder, e.Codigo, true).Select(Path.GetFileName).ToList() } }).ToList();
                d["fotos"] = PhotoStore.Photos(folder).Select(Path.GetFileName).ToList();
                if (!d.ContainsKey("observacoes")) d["observacoes"] = "";
                d.Remove("programas");
                OsJson.Save(folder, d);

                if (m.DiskInfoTxt != null)
                {
                    string txtDir = Path.Combine(folder, PhotoStore.TextDir);
                    Directory.CreateDirectory(txtDir);
                    File.WriteAllText(Path.Combine(txtDir, "DiskInfo" + (Session.Depois ? "_Depois" : "") + ".txt"), m.DiskInfoTxt, Encoding.UTF8);
                }

                // JSON no formato do laudo web: Antes -> coluna TÉCNICO; Depois -> coluna TESTES + checklist
                bool final = Session.Depois || answers[1].Count > 0;
                var laudo = LaudoExport.Write(folder, cliente.Text.Trim(), tecnico.Text.Trim(), machine, Session.Cfg.Testes,
                                              answers[0], answers[1], final, OsJson.Str(d, "observacoes"));
                if (tecnico.Text.Trim() != "" && tecnico.Text.Trim() != Session.Cfg.Tecnico) { Session.Cfg.Tecnico = tecnico.Text.Trim(); Session.Cfg.Save(); }

                var missing = Evidence.Missing(folder, Session.Depois);
                string msg = "Salvo em Documentos:\n" + string.Join("\n", laudo) + "\n(importe no laudo web pelo botão Importar)";
                if (missing.Count > 0) Toast.Show(msg + "\nAinda faltam " + missing.Count + " evidências obrigatórias (veja o Checklist).", true);
                else Toast.Show(msg);
            }
            catch (Exception ex) { Log.Write("Erro ao exportar: " + ex); Toast.Show("Erro ao exportar: " + ex.Message, true); }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { statusTimer.Dispose(); loadTimer.Dispose(); }
            base.Dispose(disposing);
        }
    }

    // Quadro "Saúde dos Componentes" + "Especificação", em uma coluna com rolagem.
    class InfoView : DrawnControl
    {
        class Entry { public string Label, Value; public bool Header; public Color Dot = Color.Empty; }

        readonly List<Entry> entries = new List<Entry>();
        bool loading;
        string error;
        readonly ScrollHelper scroll;

        public InfoView() { BackColor = Theme.Panel; scroll = new ScrollHelper(this); }

        public bool Loading { set { loading = value; error = null; Invalidate(); } }
        public string Error { set { error = value; loading = false; Invalidate(); } }

        static Color DotFor(string status)
        {
            string s = (status ?? "").ToLowerInvariant();
            if (s.StartsWith("bom") || s.Contains("boa")) return Theme.Hex("#35b56a");
            if (s.StartsWith("aten") || s.Contains("ok") || s.Contains("desgastada")) return Theme.Hex("#facc15");
            if (s.StartsWith("ruim") || s.Contains("ruim")) return Theme.Hex("#f87171");
            return Color.Empty;
        }

        void Add(string label, string value, Color? dot = null)
        {
            entries.Add(new Entry { Label = label, Value = string.IsNullOrEmpty(value) ? "—" : value, Dot = dot ?? Color.Empty });
        }

        public void Set(MachineInfo m)
        {
            loading = false; error = null;
            entries.Clear();
            entries.Add(new Entry { Label = "SAÚDE DOS COMPONENTES", Header = true });
            foreach (var d in m.Discos) Add(d.Title, d.Value, DotFor(d.Status));
            if (m.Discos.Count == 0) Add("Discos", "Nenhum disco encontrado");
            Add("Saúde da bateria", m.BateriaValor, m.BateriaPercent >= 0 ? DotFor(m.BateriaTexto) : Color.Empty);
            entries.Add(new Entry { Label = "ESPECIFICAÇÃO", Header = true });
            Add("Modelo", (m.Fabricante + " " + m.Modelo).Trim());
            Add("Serial", m.Serial);
            Add("Tipo", m.Tipo);
            Add("Processador", m.Processador);
            Add("Memória RAM", m.Memoria);
            Add("Tela", m.TelaValor);
            Add("Sistema", m.Sistema);
            Invalidate();
        }

        Rectangle Track { get { return new Rectangle(Width - Theme.S(6), Theme.S(52), Theme.S(4), Height - Theme.S(60)); } }
        protected override void OnMouseWheel(MouseEventArgs e) { scroll.Wheel(e.Delta); }
        protected override void OnMouseEnter(EventArgs e) { Focus(); base.OnMouseEnter(e); }
        protected override void OnMouseDown(MouseEventArgs e) { scroll.MouseDown(e, Track); }
        protected override void OnMouseMove(MouseEventArgs e) { scroll.MouseMove(e, Track); }
        protected override void OnMouseUp(MouseEventArgs e) { scroll.MouseUp(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            Theme.Smooth(g);
            int pad = Theme.S(14), top = Theme.S(52);
            TextRenderer.DrawText(g, "EQUIPAMENTO", Theme.Body, new Rectangle(pad, 0, Width - pad * 2, top), Theme.TextDim,
                TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            using (var pen = new Pen(Theme.Line)) g.DrawLine(pen, 0, top, Width, top);
            if (loading || error != null)
            {
                TextRenderer.DrawText(g, loading ? "Lendo o hardware...\n(CrystalDiskInfo / Windows)" : "Erro: " + error, Theme.Body,
                    new Rectangle(pad, top + Theme.S(14), Width - pad * 2, Theme.S(80)), Theme.TextDim, TextFormatFlags.WordBreak);
                return;
            }

            int headerH = Theme.S(40), itemH = Theme.S(52);
            int content = entries.Sum(x => x.Header ? headerH : itemH) + Theme.S(8);
            scroll.Viewport = Height - top - 1;
            scroll.Content = content;
            scroll.Clamp();
            g.SetClip(new Rectangle(0, top + 1, Width, Height - top - 1));
            int y = top + 1 - scroll.Offset, w = Width - pad * 2 - Theme.S(8);
            foreach (var en in entries)
            {
                if (en.Header)
                {
                    TextRenderer.DrawText(g, en.Label, Theme.Small, new Rectangle(pad, y + Theme.S(14), w, Theme.S(22)), Theme.Accent,
                        TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
                    y += headerH;
                    continue;
                }
                TextRenderer.DrawText(g, en.Label, Theme.Small, new Rectangle(pad, y + Theme.S(2), w, Theme.S(22)), Theme.TextDim,
                    TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
                int vx = pad;
                if (en.Dot != Color.Empty)
                {
                    int d = Theme.S(9);
                    using (var b = new SolidBrush(en.Dot)) g.FillEllipse(b, pad, y + Theme.S(24) + (Theme.S(22) - d) / 2, d, d);
                    vx += d + Theme.S(8);
                }
                TextRenderer.DrawText(g, en.Value, Theme.BodyBold, new Rectangle(vx, y + Theme.S(23), w - (vx - pad), Theme.S(24)), Theme.Text,
                    TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
                y += itemH;
            }
            g.ResetClip();
            scroll.Paint(g, Track);
        }
    }
}
