using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Diagnostico
{
    class DarkDialog : Form
    {
        public DarkDialog(string title, int w, int h)
        {
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = MaximizeBox = false;
            ShowInTaskbar = false;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.Body;
            var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            ClientSize = new Size(Math.Min(Theme.S(w), wa.Width - 60), Math.Min(Theme.S(h), wa.Height - 80));
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try { int dark = Theme.IsDark ? 1 : 0; Native.DwmSetWindowAttribute(Handle, 20, ref dark, 4); } catch { }
        }
    }

    // Checklist das evidências exigidas pela diretriz (fotos físicas + prints), na fase atual.
    class ChecklistDialog : DarkDialog
    {
        readonly string folder;
        readonly RowList list = new RowList { RowHeight = 56, Selectable = false, BackColor = Theme.Panel };
        readonly Label summary = UI.Label("", Theme.BodyBold, Theme.Text, Theme.Bg);
        readonly Label hint = UI.Label("Clique em uma foto para importar o arquivo, ou em um print para abrir o programa.", Theme.Small, Theme.TextDim, Theme.Bg);
        readonly FlatButton close = new FlatButton("Fechar", true);

        public ChecklistDialog(string folder) : base("Checklist de evidências", 760, 760)
        {
            this.folder = folder;
            int pad = Theme.S(16);
            summary.SetBounds(pad, Theme.S(10), ClientSize.Width - pad * 2, Theme.S(30));
            hint.SetBounds(pad, Theme.S(38), ClientSize.Width - pad * 2, Theme.S(24));
            list.SetBounds(pad, Theme.S(70), ClientSize.Width - pad * 2, ClientSize.Height - Theme.S(140));
            close.SetBounds(ClientSize.Width - pad - Theme.S(120), ClientSize.Height - Theme.S(56), Theme.S(120), Theme.S(42));
            Controls.AddRange(new Control[] { summary, hint, list, close });
            close.Click += (s, e) => Close();
            list.RowClick += (s, i) => Clicked(list.Rows[i].Tag as EvidenceItem);
            Reload();
        }

        void Reload()
        {
            bool depois = Session.Depois;
            int required = 0, done = 0;
            var rows = new List<ListRow>();
            foreach (var e in Evidence.All())
            {
                int count = Evidence.FilesOf(folder, e.Codigo, depois).Count;
                bool req = Evidence.Required(e, depois);
                if (req) { required++; if (count > 0) done++; }
                string badge; Color[] colors;
                if (count > 0) { badge = count > 1 ? "OK (" + count + ")" : "OK"; colors = Theme.PillGreen; }
                else if (e.Condicional) { badge = "Condicional"; colors = Theme.PillPurple; }
                else if (!req) { badge = "Opcional"; colors = Theme.PillGray; }
                else { badge = "Falta"; colors = Theme.PillRed; }
                rows.Add(new ListRow
                {
                    Text = (e.IsPhoto ? "Foto  ·  " : "Print  ·  ") + e.Nome,
                    Sub = Evidence.BaseName(folder, e.Codigo, depois) + (e.IsPhoto ? ".jpg" : ".png"),
                    Badge = badge, BadgeColors = colors, Tag = e,
                });
            }
            list.SetRows(rows);
            summary.Text = "OS " + Evidence.OsOf(folder) + "  ·  Fase " + (depois ? "DEPOIS" : "ANTES") + " da manutenção  ·  "
                         + done + " de " + required + " evidências obrigatórias";
        }

        void Clicked(EvidenceItem e)
        {
            if (e == null) return;
            if (e.IsPhoto)
            {
                Session.ImportPhotos(this, folder, e.Codigo, "Foto: " + e.Nome);
                Reload();
            }
            else
            {
                Close();
                Session.Launch(e.Tool);
            }
        }
    }

    // Validação pós-manutenção (tabela Antes x Depois) + observações do laudo. Grava direto no JSON da OS.
    class CompareDialog : DarkDialog
    {
        readonly string folder;
        readonly List<Tuple<string, DarkInput, DarkInput>> rows = new List<Tuple<string, DarkInput, DarkInput>>();
        readonly TextBox notes = new TextBox { Multiline = true, BorderStyle = BorderStyle.None, BackColor = Theme.Input, ForeColor = Theme.Text, Font = Theme.Body, ScrollBars = ScrollBars.Vertical };

        public CompareDialog(string folder, MachineInfo machine) : base("Antes x Depois da manutenção", 860, 780)
        {
            this.folder = folder;
            int pad = Theme.S(16), rowH = Theme.S(44), labelW = Theme.S(210);
            int colW = (ClientSize.Width - pad * 2 - labelW - Theme.S(12)) / 2;
            int y = Theme.S(12);

            Controls.Add(Place(UI.Label("Item", Theme.BodyBold, Theme.TextDim, Theme.Bg), pad, y, labelW, Theme.S(26)));
            Controls.Add(Place(UI.Label("Antes", Theme.BodyBold, Theme.TextDim, Theme.Bg), pad + labelW, y, colW, Theme.S(26)));
            Controls.Add(Place(UI.Label("Depois", Theme.BodyBold, Theme.TextDim, Theme.Bg), pad + labelW + colW + Theme.S(12), y, colW, Theme.S(26)));
            y += Theme.S(30);

            var data = OsJson.Load(folder);
            var comp = OsJson.Section(data, "comparacao");
            foreach (var item in Session.Cfg.Comparacao)
            {
                var a = new DarkInput(); var d = new DarkInput();
                var saved = OsJson.Section(comp, item);
                a.Text = OsJson.Str(saved, "antes");
                d.Text = OsJson.Str(saved, "depois");
                Controls.Add(Place(UI.Label(item, Theme.Body, Theme.Text, Theme.Bg), pad, y, labelW, rowH - Theme.S(6)));
                Controls.Add(Place(a, pad + labelW, y, colW, rowH - Theme.S(6)));
                Controls.Add(Place(d, pad + labelW + colW + Theme.S(12), y, colW, rowH - Theme.S(6)));
                rows.Add(Tuple.Create(item, a, d));
                y += rowH;
            }
            Prefill(machine);

            Controls.Add(Place(UI.Label("Observações / laudo técnico", Theme.BodyBold, Theme.TextDim, Theme.Bg), pad, y + Theme.S(4), Theme.S(400), Theme.S(26)));
            y += Theme.S(34);
            int btnY = ClientSize.Height - Theme.S(56);
            var box = new RoundPanel { Fill = Theme.Input, BackColor = Theme.Input, Radius = 6 };
            Place(box, pad, y, ClientSize.Width - pad * 2, Math.Max(Theme.S(70), btnY - y - Theme.S(12)));
            notes.SetBounds(Theme.S(10), Theme.S(8), box.Width - Theme.S(20), box.Height - Theme.S(16));
            notes.Text = OsJson.Str(data, "observacoes").Replace("\r\n", "\n").Replace("\n", "\r\n");
            box.Controls.Add(notes);
            try { Native.SetWindowTheme(notes.Handle, Theme.IsDark ? "DarkMode_Explorer" : "Explorer", null); } catch { }
            Controls.Add(box);

            var save = new FlatButton("Salvar", true);
            var cancel = new FlatButton("Cancelar");
            Place(save, ClientSize.Width - pad - Theme.S(120), btnY, Theme.S(120), Theme.S(42));
            Place(cancel, save.Left - Theme.S(12) - Theme.S(120), btnY, Theme.S(120), Theme.S(42));
            save.Click += (s, e) => Save();
            cancel.Click += (s, e) => Close();
            Controls.Add(save); Controls.Add(cancel);
        }

        static Control Place(Control c, int x, int y, int w, int h) { c.SetBounds(x, y, w, h); return c; }

        // Preenche sozinho o que o programa já sabe (discos e bateria), na coluna da fase atual.
        void Prefill(MachineInfo m)
        {
            if (m == null) return;
            foreach (var r in rows)
            {
                var target = Session.Depois ? r.Item3 : r.Item2;
                if (target.Text.Trim() != "") continue;
                if (r.Item1.StartsWith("Saúde SSD", StringComparison.OrdinalIgnoreCase) && m.Discos.Count > 0)
                    target.Text = string.Join("; ", m.Discos.Select(d => d.Kind + " " + d.Value));
                else if (r.Item1.StartsWith("Bateria", StringComparison.OrdinalIgnoreCase) && m.BateriaPercent >= 0)
                    target.Text = m.BateriaValor;
            }
        }

        void Save()
        {
            try
            {
                var data = OsJson.Load(folder);
                var comp = new Dictionary<string, object>();
                foreach (var r in rows)
                    comp[r.Item1] = new Dictionary<string, object> { { "antes", r.Item2.Text.Trim() }, { "depois", r.Item3.Text.Trim() } };
                if (!data.ContainsKey("os")) data["os"] = Evidence.OsOf(folder);
                data["comparacao"] = comp;
                data["observacoes"] = notes.Text.Trim();
                Directory.CreateDirectory(folder);
                OsJson.Save(folder, data);
                Toast.Show("Comparação e laudo salvos no JSON da OS.");
                Close();
            }
            catch (Exception ex) { Toast.Show("Erro ao salvar: " + ex.Message, true); }
        }
    }
}
