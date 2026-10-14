using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Diagnostico
{
    class PdfPage : DrawnControl, IPage
    {
        public static PdfPage Instance;
        public string Title { get { return "Relatório PDF"; } }

        readonly RoundPanel topPanel = new RoundPanel();
        readonly Label numLbl = UI.Label("Número da OS");
        readonly DarkInput path = new DarkInput();
        readonly FlatButton choose = new FlatButton("Escolher...");

        readonly RoundPanel orderPanel = new RoundPanel { Radius = 8 };
        readonly Label orderLbl = UI.Label("Ordem das fotos", Theme.Body);
        readonly Label orderHint = UI.Label("arraste para reordenar", Theme.Small);
        readonly OrderList order = new OrderList();

        readonly RoundPanel previewPanel = new RoundPanel();
        readonly Label photosLbl = UI.Label("FOTOS DA OS", Theme.Body, Theme.Text);
        readonly Label subLbl = UI.Label("Textos e Documentos ficam nas subpastas", Theme.Body, Theme.Text);
        readonly ThumbGrid preview = new ThumbGrid { Columns = 3, ShowNames = false, Placeholders = true };
        readonly Label empty = UI.Label("Escolha uma OS para montar o relatório.\nAs fotos selecionadas aparecerão aqui.", Theme.Body, Theme.TextDim);

        readonly FlatButton generate = new FlatButton("Gerar PDF", true);
        string folder;

        public PdfPage()
        {
            Instance = this;
            topPanel.Controls.AddRange(new Control[] { numLbl, path, choose });
            orderPanel.Controls.AddRange(new Control[] { orderLbl, orderHint, order });
            previewPanel.Controls.AddRange(new Control[] { photosLbl, subLbl, preview, empty });
            empty.TextAlign = ContentAlignment.MiddleCenter;
            generate.Enabled = false;
            Controls.AddRange(new Control[] { topPanel, orderPanel, previewPanel, generate });
            subLbl.TextAlign = ContentAlignment.MiddleRight;
            orderHint.TextAlign = ContentAlignment.MiddleRight;

            choose.Click += (s, e) => Choose();
            path.Box.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Load(ResolveInput(path.Text)); } };
            path.Box.Leave += (s, e) => { var f = ResolveInput(path.Text); if (f != folder) Load(f); };
            order.Changed += (s, e) => UpdatePreview();
            preview.ItemOpen += (s, p) => PhotoStore.OpenInShell(p);
            generate.Click += async (s, e) => await Generate();
        }

        // Aceita "12341", ".\OS\12341" ou um caminho completo.
        string ResolveInput(string text)
        {
            text = (text ?? "").Trim();
            if (text == "") return null;
            if (!text.Contains("\\") && !text.Contains("/")) return Session.FolderForOs(text);
            return AppPaths.Resolve(text);
        }

        public void OnEnter()
        {
            string f = Session.ActiveFolder;
            if (f != null && Directory.Exists(f) && f != folder) Load(f);
            else if (folder != null) Load(folder);
        }

        public void OnLeave() { }

        void Load(string f)
        {
            folder = f;
            path.Text = f == null ? "" : AppPaths.ToRelative(f);
            order.Items.Clear();
            if (f != null && Directory.Exists(f))
                foreach (var p in PhotoStore.Photos(f))
                    order.Items.Add(new OrderItem { Path = p, Name = Path.GetFileName(p) });
            else if (f != null) Toast.Show("Pasta da OS não encontrada: " + f, true);
            order.Invalidate();
            preview.ClearCache();
            UpdatePreview();
        }

        void UpdatePreview()
        {
            var sel = order.Items.Where(i => i.Checked).Select(i => i.Path).ToList();
            preview.SetPaths(sel);
            photosLbl.Text = "FOTOS DA OS(" + sel.Count + ")";
            generate.Enabled = sel.Count > 0;
            empty.Visible = sel.Count == 0;
            empty.Text = folder == null ? "Escolha uma OS para montar o relatório.\nAs fotos selecionadas aparecerão aqui." :
                order.Items.Count == 0 ? "Esta OS ainda não tem fotos ou capturas.\nAdicione evidências em Fotos e arquivos." :
                "Selecione as fotos na lista ao lado\npara incluí-las no relatório.";
        }

        void Choose()
        {
            using (var dlg = new FolderBrowserDialog { Description = "Escolha a pasta da OS", SelectedPath = folder ?? Session.Cfg.OsRoot })
                if (dlg.ShowDialog(this) == DialogResult.OK) Load(dlg.SelectedPath);
        }

        async Task Generate()
        {
            var photos = order.Items.Where(i => i.Checked).Select(i => i.Path).ToList();
            if (folder == null || photos.Count == 0) return;
            var missing = Evidence.Missing(folder, false);
            if (missing.Count > 0)
            {
                string names = string.Join("\n", missing.Take(12).Select(m => "  -  " + (m.IsPhoto ? "Foto: " : "Print: ") + m.Nome));
                if (missing.Count > 12) names += "\n  ...";
                var answer = MessageBox.Show(FindForm(), "Faltam " + missing.Count + " evidências obrigatórias da diretriz:\n\n" + names +
                    "\n\nGerar o PDF mesmo assim?", "Evidências pendentes", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (answer != DialogResult.Yes) return;
            }
            generate.Enabled = false;
            generate.Text = "Gerando...";
            string f = folder;
            try
            {
                string outPath = await Task.Run(() => PdfReport.Generate(f, photos));
                Toast.Show("PDF gerado: " + Path.GetFileName(outPath));
                PhotoStore.OpenInShell(outPath);
            }
            catch (Exception ex) { Toast.Show("Erro ao gerar o PDF: " + ex.Message, true); }
            generate.Text = "Gerar PDF";
            generate.Enabled = true;
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (Width < 50) return;
            int pad = Theme.S(20), gap = Theme.S(22), ip = Theme.S(20);
            int lw = (Width - pad * 2 - gap) * 42 / 100;
            int bottom = Height - pad;

            int th = Theme.S(110);
            topPanel.SetBounds(pad, pad, lw, th);
            int cw = choose.PreferredWidth;
            choose.SetBounds(lw - ip - cw, Theme.S(42), cw, Theme.S(48));
            numLbl.SetBounds(ip, Theme.S(14), Theme.S(300), Theme.S(26));
            path.SetBounds(ip, Theme.S(42), choose.Left - ip * 2, Theme.S(48));

            int oy = pad + th + gap;
            orderPanel.SetBounds(pad, oy, lw, bottom - oy);
            orderLbl.SetBounds(Theme.S(12), 2, lw / 2, Theme.S(48));
            orderHint.SetBounds(lw / 2, 2, lw / 2 - Theme.S(16), Theme.S(48));
            order.SetBounds(2, Theme.S(48), lw - 4, orderPanel.Height - Theme.S(52));

            int rx = pad + lw + gap, rw = Width - rx - pad;
            int btnH = Theme.S(50), gw = generate.PreferredWidth + Theme.S(16);
            generate.SetBounds(Width - pad - gw, bottom - btnH, gw, btnH);
            previewPanel.SetBounds(rx, pad, rw, bottom - btnH - Theme.S(20) - pad);
            photosLbl.SetBounds(ip, Theme.S(14), rw / 2 - ip, Theme.S(40));
            subLbl.SetBounds(rw / 2, Theme.S(14), rw / 2 - ip, Theme.S(40));
            preview.SetBounds(ip, Theme.S(64), rw - ip * 2 + Theme.S(8), previewPanel.Height - Theme.S(80));
            empty.SetBounds(ip, Theme.S(100), rw - ip * 2, Math.Max(Theme.S(70), previewPanel.Height - Theme.S(130)));
        }
    }
}
