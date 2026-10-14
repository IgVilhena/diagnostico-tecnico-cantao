using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Diagnostico
{
    class EditPage : DrawnControl, IPage
    {
        public string Title { get { return "Fotos e arquivos"; } }

        readonly RoundPanel listPanel = new RoundPanel { Radius = 8 };
        readonly Label listLbl = UI.Label("PASTA DE OS", Theme.BodyBold, Theme.TextDim);
        readonly FlatButton choose = new FlatButton("Escolher Pasta");
        readonly RowList osList = new RowList { RowHeight = 50 };

        readonly RoundPanel topPanel = new RoundPanel();
        readonly Label numLbl = UI.Label("Número da OS");
        readonly DarkInput number = new DarkInput();
        readonly FlatButton rename = new FlatButton("Renomear"), openFolder = new FlatButton("Abrir pasta");

        readonly RoundPanel photoPanel = new RoundPanel();
        readonly Label photosLbl = UI.Label("FOTOS DA OS", Theme.Body, Theme.Text);
        readonly Label subLbl = UI.Label("Textos e Documentos ficam nas subpastas", Theme.Body, Theme.Text);
        readonly ThumbGrid grid = new ThumbGrid { ShowAddTile = true };
        readonly Label empty = UI.Label("Selecione uma OS na lista ao lado\npara organizar suas fotos e capturas.", Theme.Body, Theme.TextDim);

        readonly FlatButton printWin = new FlatButton("Tirar print da janela (F9)");
        readonly FlatButton printScreen = new FlatButton("Tirar print da tela (F8)", true);

        string selected;
        readonly FileSystemWatcher watcher = new FileSystemWatcher();
        readonly Timer debounce = new Timer { Interval = 400 };

        public EditPage()
        {
            listPanel.Controls.AddRange(new Control[] { listLbl, choose, osList });
            topPanel.Controls.AddRange(new Control[] { numLbl, number, rename, openFolder });
            photoPanel.Controls.AddRange(new Control[] { photosLbl, subLbl, grid, empty });
            empty.TextAlign = ContentAlignment.MiddleCenter;
            Controls.AddRange(new Control[] { listPanel, topPanel, photoPanel, printWin, printScreen });
            subLbl.TextAlign = ContentAlignment.MiddleRight;

            choose.Click += (s, e) => ChooseRoot();
            osList.RowClick += (s, i) => Select(osList.Rows[i].Tag as string);
            osList.RowRightClick += (s, i) => FolderMenu(osList.Rows[i].Tag as string);
            rename.Click += (s, e) => Rename();
            number.Box.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Rename(); } };
            openFolder.Click += (s, e) => { if (selected != null) PhotoStore.OpenFolder(selected); };
            grid.AddClicked += (s, e) => AddMenu();
            grid.ItemOpen += (s, p) => PhotoStore.OpenInShell(p);
            grid.ItemRightClick += (s, p) => PhotoMenu(p);
            printWin.Click += async (s, e) => await Session.CaptureBehind(false);
            printScreen.Click += async (s, e) => await Session.CaptureBehind(true);

            Session.PhotoSaved += p => { if (Visible) Reload(); };
            debounce.Tick += (s, e) => { debounce.Stop(); Reload(); };
            watcher.IncludeSubdirectories = false;
            watcher.Changed += (s, e) => Kick();
            watcher.Created += (s, e) => Kick();
            watcher.Deleted += (s, e) => Kick();
            watcher.Renamed += (s, e) => Kick();
        }

        void Kick()
        {
            try { BeginInvoke(new Action(() => { debounce.Stop(); debounce.Start(); })); } catch { }
        }

        public void OnEnter()
        {
            if (Session.ActiveFolder != null && Directory.Exists(Session.ActiveFolder)) selected = Session.ActiveFolder;
            Reload();
        }

        public void OnLeave() { watcher.EnableRaisingEvents = false; }

        void Reload()
        {
            string root = Session.Cfg.OsRoot;
            var rows = new List<ListRow>();
            try
            {
                if (Directory.Exists(root))
                    foreach (var d in Directory.GetDirectories(root).OrderBy(x => Path.GetFileName(x), new NaturalComparer()))
                        rows.Add(new ListRow { Text = Path.GetFileName(d), Right = PhotoStore.PhotoCount(d) + " fotos", Tag = d });
            }
            catch { }
            osList.SetRows(rows);
            if (selected != null && !Directory.Exists(selected)) selected = null;
            osList.SelectedIndex = rows.FindIndex(r => string.Equals((string)r.Tag, selected, StringComparison.OrdinalIgnoreCase));
            ShowSelected(false);
            try
            {
                watcher.EnableRaisingEvents = false;
                if (Directory.Exists(root)) { watcher.Path = root; watcher.IncludeSubdirectories = true; watcher.EnableRaisingEvents = true; }
            }
            catch { }
        }

        void Select(string folder)
        {
            selected = folder;
            ShowSelected(true);
        }

        void ShowSelected(bool resetName)
        {
            empty.Visible = selected == null;
            rename.Enabled = openFolder.Enabled = selected != null;
            if (selected == null)
            {
                number.Text = ""; grid.SetPaths(new string[0]); photosLbl.Text = "FOTOS DA OS";
                grid.ShowAddTile = false; grid.Invalidate();
                return;
            }
            Session.ActiveFolder = selected;
            if (resetName || !number.Box.Focused) number.Text = Path.GetFileName(selected);
            var photos = PhotoStore.Photos(selected);
            grid.ShowAddTile = true;
            grid.ClearCache();
            grid.SetPaths(photos);
            photosLbl.Text = "FOTOS DA OS(" + photos.Count + ")";
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (Width < 50) return;
            int pad = Theme.S(20), gap = Theme.S(22), hh = Theme.S(58);
            int lw = Math.Max(Theme.S(300), Width * 26 / 100);
            int bottom = Height - pad;

            listPanel.SetBounds(pad, pad, lw, bottom - pad);
            int cw = choose.PreferredWidth;
            listLbl.SetBounds(Theme.S(12), 2, lw - cw - Theme.S(30), hh);
            choose.SetBounds(lw - cw - Theme.S(14), Theme.S(10), cw, hh - Theme.S(14));
            osList.SetBounds(2, hh + 4, lw - 4, listPanel.Height - hh - 8);

            int rx = pad + lw + gap, rw = Width - rx - pad;
            int th = Theme.S(110);
            topPanel.SetBounds(rx, pad, rw, th);
            int bw1 = openFolder.PreferredWidth, bw2 = rename.PreferredWidth, ip = Theme.S(20);
            openFolder.SetBounds(rw - ip - bw1, Theme.S(40), bw1, Theme.S(48));
            rename.SetBounds(openFolder.Left - Theme.S(20) - bw2, Theme.S(40), bw2, Theme.S(48));
            numLbl.SetBounds(ip, Theme.S(12), Theme.S(300), Theme.S(26));
            number.SetBounds(ip, Theme.S(40), rename.Left - ip * 2, Theme.S(48));

            int btnH = Theme.S(50);
            int w2 = printScreen.PreferredWidth + Theme.S(10), w1 = printWin.PreferredWidth + Theme.S(10);
            printScreen.SetBounds(Width - pad - w2, bottom - btnH, w2, btnH);
            printWin.SetBounds(printScreen.Left - Theme.S(14) - w1, bottom - btnH, w1, btnH);

            int py = pad + th + gap;
            photoPanel.SetBounds(rx, py, rw, bottom - btnH - Theme.S(20) - py);
            photosLbl.SetBounds(ip, Theme.S(14), rw / 2 - ip, Theme.S(40));
            subLbl.SetBounds(rw / 2, Theme.S(14), rw / 2 - ip, Theme.S(40));
            grid.SetBounds(ip, Theme.S(64), rw - ip * 2 + Theme.S(8), photoPanel.Height - Theme.S(80));
            empty.SetBounds(ip, Theme.S(100), rw - ip * 2, Math.Max(Theme.S(70), photoPanel.Height - Theme.S(130)));
        }

        // ---------- ações ----------

        void ChooseRoot()
        {
            using (var dlg = new FolderBrowserDialog { Description = "Escolha a pasta onde ficam as OS", SelectedPath = Session.Cfg.OsRoot, ShowNewFolderButton = true })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                Session.Cfg.PastaOS = AppPaths.ToRelative(dlg.SelectedPath);
                Session.Cfg.Save();
                selected = null;
                Reload();
            }
        }

        void Rename()
        {
            if (selected == null) return;
            // Sanitize devolve "Print" para texto vazio: sem este teste o campo em branco renomearia a OS para "Print"
            if (number.Text.Trim() == "") { number.Text = Path.GetFileName(selected); return; }
            string name = PhotoStore.Sanitize(number.Text.Trim());
            if (name == Path.GetFileName(selected)) return;
            string target = Path.Combine(Path.GetDirectoryName(selected), name);
            if (Directory.Exists(target)) { Toast.Show("Já existe uma OS com o número " + name + ".", true); return; }
            try
            {
                string oldName = Path.GetFileName(selected);
                grid.ClearCache();
                Directory.Move(selected, target);
                // os arquivos levam o número da OS no nome (OS_<numero>_...): renomeia junto
                foreach (var dir in new[] { target, Path.Combine(target, PhotoStore.DocDir), Path.Combine(target, PhotoStore.TextDir) })
                {
                    if (!Directory.Exists(dir)) continue;
                    foreach (var f in Directory.GetFiles(dir))
                    {
                        string fn = Path.GetFileName(f);
                        string nn = fn.Replace("OS_" + oldName + "_", "OS_" + name + "_").Replace("OS_" + oldName + ".", "OS_" + name + ".");
                        if (nn != fn) File.Move(f, Path.Combine(dir, nn));
                    }
                }
                if (File.Exists(OsJson.PathOf(target)))
                {
                    var data = OsJson.Load(target);
                    data["os"] = name;
                    OsJson.Save(target, data);
                }
                selected = target;
                Toast.Show("OS " + oldName + " renomeada para " + name + ".");
                Reload();
            }
            catch (Exception ex) { Toast.Show("Não foi possível renomear (algum arquivo aberto?): " + ex.Message, true); }
        }

        // "+ Nova Foto": escolhe qual foto da diretriz está sendo adicionada, para o arquivo já sair com o nome certo.
        void AddMenu()
        {
            if (selected == null) return;
            var items = new List<Tuple<string, Action>>();
            foreach (var f in Session.Cfg.Fotos)
            {
                var foto = f;
                bool has = Evidence.Has(selected, foto.Codigo, Session.Depois);
                items.Add(UI.Item((has ? "✔  " : "      ") + foto.Nome + "   (" + foto.Codigo + ")", () =>
                {
                    Session.ImportPhotos(this, selected, foto.Codigo, "Foto: " + foto.Nome);
                    Reload();
                }));
            }
            items.Add(null);
            items.Add(UI.Item("Outra foto (fora do padrão da diretriz)...", () =>
            {
                Session.ImportPhotos(this, selected, null, "Adicionar fotos à OS");
                Reload();
            }));
            UI.Menu(items.ToArray()).Show(Cursor.Position);
        }

        void PhotoMenu(string path)
        {
            UI.Menu(
                UI.Item("Abrir", () => PhotoStore.OpenInShell(path)),
                UI.Item("Renomear...", () =>
                {
                    string label = Prompt.Ask(this, "Renomear arquivo", "Novo nome (sem a extensão):", Path.GetFileNameWithoutExtension(path));
                    if (string.IsNullOrWhiteSpace(label)) return;
                    string dest = Path.Combine(Path.GetDirectoryName(path), PhotoStore.Sanitize(label) + Path.GetExtension(path));
                    if (string.Equals(dest, path, StringComparison.OrdinalIgnoreCase)) return;
                    if (File.Exists(dest)) { Toast.Show("Já existe um arquivo com o nome " + Path.GetFileName(dest) + ".", true); return; }
                    try { grid.ClearCache(); File.Move(path, dest); Reload(); } catch (Exception ex) { Toast.Show(ex.Message, true); }
                }),
                null,
                UI.Item("Excluir", () =>
                {
                    if (MessageBox.Show(this, "Excluir " + Path.GetFileName(path) + "?", "Excluir foto", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                    try { grid.ClearCache(); File.Delete(path); Reload(); } catch (Exception ex) { Toast.Show(ex.Message, true); }
                })
            ).Show(Cursor.Position);
        }

        void FolderMenu(string folder)
        {
            UI.Menu(
                UI.Item("Abrir pasta", () => PhotoStore.OpenFolder(folder)),
                UI.Item("Gerar PDF desta OS", () => { selected = folder; Session.ActiveFolder = folder; ((MainForm)Session.MainWindow).Go(MainForm.PagePdf); })
            ).Show(Cursor.Position);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { watcher.Dispose(); debounce.Dispose(); }
            base.Dispose(disposing);
        }
    }

    // Ordena "9" antes de "10".
    class NaturalComparer : IComparer<string>
    {
        public int Compare(string a, string b)
        {
            long x, y;
            if (long.TryParse(a, out x) && long.TryParse(b, out y)) return x.CompareTo(y);
            return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }

    static class Prompt
    {
        public static string Ask(IWin32Window owner, string title, string label, string value)
        {
            using (var f = new Form())
            {
                f.Text = title; f.FormBorderStyle = FormBorderStyle.FixedDialog; f.StartPosition = FormStartPosition.CenterParent;
                f.MinimizeBox = f.MaximizeBox = false; f.ShowInTaskbar = false;
                f.BackColor = Theme.Bg; f.ForeColor = Theme.Text; f.Font = Theme.Body;
                f.ClientSize = new Size(Theme.S(440), Theme.S(150));
                var l = UI.Label(label, Theme.Body, Theme.Text, Theme.Bg); l.SetBounds(Theme.S(16), Theme.S(10), Theme.S(400), Theme.S(28));
                var input = new DarkInput(value); input.SetBounds(Theme.S(16), Theme.S(42), Theme.S(408), Theme.S(42));
                var ok = new FlatButton("OK", true); ok.SetBounds(Theme.S(324), Theme.S(96), Theme.S(100), Theme.S(40));
                var cancel = new FlatButton("Cancelar"); cancel.SetBounds(Theme.S(210), Theme.S(96), Theme.S(104), Theme.S(40));
                ok.Click += (s, e) => { f.DialogResult = DialogResult.OK; };
                cancel.Click += (s, e) => { f.DialogResult = DialogResult.Cancel; };
                input.Box.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) f.DialogResult = DialogResult.OK; if (e.KeyCode == Keys.Escape) f.DialogResult = DialogResult.Cancel; };
                f.Controls.AddRange(new Control[] { l, input, ok, cancel });
                f.Shown += (s, e) => { input.Box.Focus(); input.Box.SelectAll(); };
                return f.ShowDialog(owner) == DialogResult.OK ? input.Text : null;
            }
        }
    }
}
