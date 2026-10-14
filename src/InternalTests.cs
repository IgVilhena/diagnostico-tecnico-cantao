using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Media;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace Diagnostico
{
    // Testes embutidos no programa (funcionam sem internet e no WinPE).
    // Uma etapa usa um deles quando o campo "Executável" é  interno:teclado  ou  interno:audio.
    static class InternalTests
    {
        public const string Prefix = "interno:";

        public static void Open(ToolConfig step)
        {
            string kind = (step.Exe ?? "").Trim().Substring(Prefix.Length).ToLowerInvariant();
            Form f;
            if (kind == "teclado") f = new KeyboardTestForm(step);
            else if (kind == "audio") f = new AudioTestDialog(step);
            else { Toast.Show("Teste interno desconhecido: " + step.Exe + "  (use interno:teclado ou interno:audio)", true); return; }
            using (f) f.ShowDialog(Session.MainWindow);
        }
    }

    // ======================================================================================
    // TECLADO: layout ABNT2; a tecla fica verde quando é pressionada.
    // Usa a posição física; Shift esq./dir. e teclas especiais também usam o código virtual
    // para não depender da sinalização de tecla estendida enviada no evento.
    // ======================================================================================
    class KeyboardTestForm : Form
    {
        class Key
        {
            public string Label;
            public string CompactLabel;   // rótulo no notebook compacto (ex.: "Q /"), quando muda
            public int Scan, Vk;
            public bool Ext, Numpad, Pressed, Down, Excluded;
            public bool FullOnly;         // tecla que o notebook compacto não tem
            public float X, Y, W = 1, H = 1;
        }

        readonly ToolConfig step;
        readonly List<Key> keys = new List<Key>();
        readonly Native.HookProc hookProc;      // mantém o delegate vivo enquanto o hook existir
        IntPtr hook = IntPtr.Zero;
        bool numpadOff;
        string lastKey = "";
        // false = ABNT2 completo; true = notebook compacto (ABNT2 sem a tecla "/ ?": elas ficam no Q e no W com AltGr)
        bool compact;

        readonly FlatButton layoutBtn = new FlatButton("");
        readonly FlatButton numpadBtn = new FlatButton("Sem teclado numérico");
        readonly FlatButton resetBtn = new FlatButton("Zerar");
        readonly FlatButton printBtn = new FlatButton("Salvar print");
        readonly FlatButton okBtn = new FlatButton("Teclado OK", true);
        readonly FlatButton badBtn = new FlatButton("Com defeito");
        readonly FlatButton closeBtn = new FlatButton("Fechar");

        public KeyboardTestForm(ToolConfig step)
        {
            this.step = step;
            Text = "Teste de teclado";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.Body;
            DoubleBuffered = true;
            // Ocupa a área de trabalho, deixando a barra de tarefas (data/hora) visível para o print.
            Bounds = Screen.FromHandle(Session.MainWindow.Handle).WorkingArea;
            BuildLayout();

            Controls.AddRange(new Control[] { layoutBtn, numpadBtn, resetBtn, printBtn, badBtn, okBtn, closeBtn });
            compact = Session.Cfg != null && Session.Cfg.LayoutTeclado == "Compacto";
            numpadOff = compact;
            ApplyLayout();
            layoutBtn.Click += (s, e) =>
            {
                compact = !compact;
                numpadOff = compact;
                Session.Cfg.LayoutTeclado = compact ? "Compacto" : "ABNT2";
                Session.Cfg.Save();
                ApplyLayout(); LayoutButtons(); Invalidate();
            };
            numpadBtn.Click += (s, e) => { numpadOff = !numpadOff; foreach (var k in keys) if (k.Numpad) k.Excluded = numpadOff; numpadBtn.Text = numpadOff ? "Com teclado numérico" : "Sem teclado numérico"; LayoutButtons(); Invalidate(); };
            resetBtn.Click += (s, e) => { foreach (var k in keys) { k.Pressed = false; k.Down = false; } lastKey = ""; Invalidate(); };
            printBtn.Click += async (s, e) => await Session.Capture(Handle, step, step.Codigo, false, true);
            okBtn.Click += (s, e) => Finish(0);
            badBtn.Click += (s, e) => Finish(1);
            closeBtn.Click += (s, e) => Close();
            hookProc = HookCallback;
        }

        void Finish(int answer)
        {
            var missing = keys.Where(k => !k.Excluded && !k.Pressed).ToList();
            if (answer == 0 && missing.Count > 0)
            {
                var r = MessageBox.Show(this, "Ainda há " + missing.Count + " tecla(s) não testada(s).\n\nMarcar o teclado como OK mesmo assim?",
                    "Teste de teclado", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (r != DialogResult.Yes) return;
            }
            Session.AnswerTest("Teclado", answer);
            Close();
        }

        // ---------- layout ABNT2 (unidades de tecla) ----------

        void Add(string label, int scan, bool ext, float x, float y, float w = 1, float h = 1, int vk = 0, bool numpad = false)
        {
            keys.Add(new Key { Label = label, Scan = scan, Ext = ext, X = x, Y = y, W = w, H = h, Vk = vk, Numpad = numpad });
        }

        void Row(float x, float y, string labels, params int[] scans)
        {
            var parts = labels.Split(' ');
            for (int i = 0; i < scans.Length; i++) Add(parts[i], scans[i], false, x + i, y);
        }

        void BuildLayout()
        {
            // linha de funções
            Add("Esc", 0x01, false, 0, 0);
            Row(2, 0, "F1 F2 F3 F4", 0x3B, 0x3C, 0x3D, 0x3E);
            Row(6.5f, 0, "F5 F6 F7 F8", 0x3F, 0x40, 0x41, 0x42);
            Row(11, 0, "F9 F10 F11 F12", 0x43, 0x44, 0x57, 0x58);
            Add("PrtSc", 0x37, true, 15.5f, 0, 1, 1, 0x2C);
            Add("ScrLk", 0x46, false, 16.5f, 0);
            Add("Pause", 0x45, false, 17.5f, 0, 1, 1, 0x13);

            // números
            Row(0, 1.5f, "' 1 2 3 4 5 6 7 8 9 0 - =", 0x29, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D);
            Add("Backspace", 0x0E, false, 13, 1.5f, 2);
            // QWERTY
            Add("Tab", 0x0F, false, 0, 2.5f, 1.5f);
            Row(1.5f, 2.5f, "Q W E R T Y U I O P ´ [", 0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18, 0x19, 0x1A, 0x1B);
            Add("Enter", 0x1C, false, 13.5f, 2.5f, 1.5f, 2);
            // ASDF
            Add("Caps", 0x3A, false, 0, 3.5f, 1.75f);
            Row(1.75f, 3.5f, "A S D F G H J K L Ç ~", 0x1E, 0x1F, 0x20, 0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x28);
            Add("]", 0x2B, false, 12.75f, 3.5f, 0.75f);
            // ZXCV
            Add("Shift esq.", 0x2A, false, 0, 4.5f, 1.25f, vk: (int)Keys.LShiftKey);
            Row(1.25f, 4.5f, "\\ Z X C V B N M , . ; /", 0x56, 0x2C, 0x2D, 0x2E, 0x2F, 0x30, 0x31, 0x32, 0x33, 0x34, 0x35, 0x73);
            Add("Shift dir.", 0x36, false, 13.25f, 4.5f, 1.75f, vk: (int)Keys.RShiftKey);
            // barra de espaço
            Add("Ctrl", 0x1D, false, 0, 5.5f, 1.25f);
            Add("Win", 0x5B, true, 1.25f, 5.5f, 1.25f);
            Add("Alt", 0x38, false, 2.5f, 5.5f, 1.25f);
            Add("Espaço", 0x39, false, 3.75f, 5.5f, 6.25f);
            Add("AltGr", 0x38, true, 10, 5.5f, 1.25f);
            Add("Win", 0x5C, true, 11.25f, 5.5f, 1.25f);
            Add("Menu", 0x5D, true, 12.5f, 5.5f, 1.25f);
            Add("Ctrl", 0x1D, true, 13.75f, 5.5f, 1.25f);

            // navegação
            Add("Ins", 0x52, true, 15.5f, 1.5f); Add("Home", 0x47, true, 16.5f, 1.5f); Add("PgUp", 0x49, true, 17.5f, 1.5f);
            Add("Del", 0x53, true, 15.5f, 2.5f); Add("End", 0x4F, true, 16.5f, 2.5f); Add("PgDn", 0x51, true, 17.5f, 2.5f);
            Add("↑", 0x48, true, 16.5f, 4.5f);
            Add("←", 0x4B, true, 15.5f, 5.5f); Add("↓", 0x50, true, 16.5f, 5.5f); Add("→", 0x4D, true, 17.5f, 5.5f);

            // teclado numérico
            Add("Num", 0x45, true, 19, 1.5f, 1, 1, 0x90, true); Add("/", 0x35, true, 20, 1.5f, 1, 1, 0, true);
            Add("*", 0x37, false, 21, 1.5f, 1, 1, 0, true); Add("-", 0x4A, false, 22, 1.5f, 1, 1, 0, true);
            Add("7", 0x47, false, 19, 2.5f, 1, 1, 0, true); Add("8", 0x48, false, 20, 2.5f, 1, 1, 0, true);
            Add("9", 0x49, false, 21, 2.5f, 1, 1, 0, true); Add("+", 0x4E, false, 22, 2.5f, 1, 1, 0, true);
            Add("4", 0x4B, false, 19, 3.5f, 1, 1, 0, true); Add("5", 0x4C, false, 20, 3.5f, 1, 1, 0, true);
            Add("6", 0x4D, false, 21, 3.5f, 1, 1, 0, true); Add(".", 0x7E, false, 22, 3.5f, 1, 1, 0, true);
            Add("1", 0x4F, false, 19, 4.5f, 1, 1, 0, true); Add("2", 0x50, false, 20, 4.5f, 1, 1, 0, true);
            Add("3", 0x51, false, 21, 4.5f, 1, 1, 0, true); Add("Ent", 0x1C, true, 22, 4.5f, 1, 2, 0, true);
            Add("0", 0x52, false, 19, 5.5f, 2, 1, 0, true); Add(",", 0x53, false, 21, 5.5f, 1, 1, 0, true);

            // Notebook compacto: sem "/ ?" dedicada, ScrLk, Pause, PrtSc, Menu e Win direito;
            // Home/End/PgUp/PgDn ficam nas setas (com Fn). "/" e "?" são AltGr+Q e AltGr+W.
            foreach (var k in keys)
            {
                if (k.Numpad) continue;
                k.FullOnly = k.Scan == 0x73 || k.Scan == 0x46 || k.Vk == 0x13 || k.Vk == 0x2C ||
                    (k.Ext && (k.Scan == 0x5C || k.Scan == 0x5D || k.Scan == 0x47 || k.Scan == 0x4F || k.Scan == 0x49 || k.Scan == 0x51));
                if (k.Scan == 0x10 && !k.Ext) k.CompactLabel = "Q /";
                if (k.Scan == 0x11 && !k.Ext) k.CompactLabel = "W ?";
            }
        }

        string Name(Key k) { return compact && k.CompactLabel != null ? k.CompactLabel : k.Label; }

        // Tira da contagem as teclas que o layout escolhido não tem (uma tecla pressionada volta sozinha).
        void ApplyLayout()
        {
            foreach (var k in keys) k.Excluded = k.Numpad ? numpadOff : compact && k.FullOnly;
            layoutBtn.Text = compact ? "Layout: notebook compacto" : "Layout: ABNT2 completo";
            numpadBtn.Text = numpadOff ? "Com teclado numérico" : "Sem teclado numérico";
        }

        Key Find(int vk, int scan, bool ext)
        {
            // VK_LSHIFT/VK_RSHIFT já informam o lado. LLKHF_EXTENDED não deve
            // impedir esse reconhecimento. Para VK_SHIFT genérico, usa a posição.
            if (vk == (int)Keys.ShiftKey)
            {
                if (scan == 0x2A) vk = (int)Keys.LShiftKey;
                else if (scan == 0x36) vk = (int)Keys.RShiftKey;
                else return null; // não adivinha o lado sem informação suficiente
            }
            foreach (var k in keys) if (k.Vk != 0 && k.Vk == vk) return k;
            foreach (var k in keys) if (k.Vk == 0 && k.Scan == scan && k.Ext == ext) return k;
            return null;
        }

        void ProcessKey(int vk, int scan, bool ext, bool down)
        {
            var key = Find(vk, scan, ext);
            if (key != null)
            {
                key.Down = down;
                key.Pressed = true; // conta também quando só chega a soltura (PrtSc)
                if (key.Excluded && !key.Numpad) key.Excluded = false;
                lastKey = Name(key);
            }
            else if (down)
                lastKey = "VK " + vk.ToString("X2") + " / SC " + scan.ToString("X2") +
                    (ext ? " (ext)" : "") + " — tecla fora do layout";
            Invalidate();
        }

        // ---------- captura das teclas (hook de baixo nível: pega Win, Alt, PrtSc, F-keys...) ----------

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            LayoutButtons();
            hook = Native.SetWindowsHookEx(13 /* WH_KEYBOARD_LL */, hookProc, Native.GetModuleHandle(null), 0);
            if (hook == IntPtr.Zero) Toast.Show("Não foi possível capturar o teclado (hook). O teste pode não registrar todas as teclas.", true);
            Activate();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (hook != IntPtr.Zero) { Native.UnhookWindowsHookEx(hook); hook = IntPtr.Zero; }
            base.OnFormClosed(e);
        }

        IntPtr HookCallback(int code, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                // Só captura enquanto esta janela está na frente; senão o teclado segue normal.
                if (code >= 0 && Native.GetForegroundWindow() == Handle)
                {
                    var info = (Native.KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.KBDLLHOOKSTRUCT));
                    int msg = wParam.ToInt32();
                    bool down = msg == 0x100 || msg == 0x104;
                    ProcessKey(info.vkCode, info.scanCode, (info.flags & 1) != 0, down);
                    return (IntPtr)1;            // não repassa: Win não abre o menu, Alt+F4 não fecha, F8/F9 não tiram print
                }
            }
            catch { }
            return Native.CallNextHookEx(hook, code, wParam, lParam);
        }

        // ---------- desenho ----------

        const float TotalW = 23, TotalH = 6.5f;

        float Unit { get { return Math.Min((ClientSize.Width - Theme.S(80)) / TotalW, (ClientSize.Height - Theme.S(330)) / TotalH); } }

        RectangleF KeyRect(Key k)
        {
            float u = Unit, pad = Math.Max(2, u * 0.05f);
            float ox = (ClientSize.Width - TotalW * u) / 2, oy = Theme.S(170);
            return new RectangleF(ox + k.X * u + pad, oy + k.Y * u + pad, k.W * u - pad * 2, k.H * u - pad * 2);
        }

        void LayoutButtons()
        {
            int h = Theme.S(50), y = ClientSize.Height - h - Theme.S(26), x = ClientSize.Width - Theme.S(40), g = Theme.S(12);
            foreach (var b in new[] { closeBtn, okBtn, badBtn, printBtn })
            {
                int w = b.PreferredWidth + Theme.S(10);
                b.SetBounds(x - w, y, w, h); x -= w + g;
            }
            int lx = Theme.S(40);
            foreach (var b in new[] { layoutBtn, numpadBtn, resetBtn })
            {
                int w = b.PreferredWidth + Theme.S(10);
                b.SetBounds(lx, y, w, h); lx += w + g;
            }
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); if (IsHandleCreated) LayoutButtons(); Invalidate(); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            // Clique numa tecla que o equipamento não tem: ela sai da contagem.
            foreach (var k in keys)
                if (KeyRect(k).Contains(e.Location)) { k.Excluded = !k.Excluded; if (k.Excluded) k.Pressed = false; Invalidate(); break; }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            Theme.Smooth(g);
            int px = Theme.S(40);
            int total = keys.Count(k => !k.Excluded), done = keys.Count(k => !k.Excluded && k.Pressed);

            TextRenderer.DrawText(g, "Teste de teclado", Theme.Big, new Point(px, Theme.S(22)), Theme.Text);
            string info = "OS " + (Session.ActiveOs == "" ? "—" : Session.ActiveOs) + "   ·   " + DateTime.Now.ToString("dd/MM/yyyy HH:mm") +
                          "   ·   " + (Session.Depois ? "depois da manutenção" : "antes da manutenção");
            TextRenderer.DrawText(g, info, Theme.Body, new Point(px + Theme.S(2), Theme.S(70)), Theme.TextDim);
            TextRenderer.DrawText(g, "Pressione todas as teclas. Clique com o mouse nas teclas que este equipamento não tem para tirá-las da contagem.",
                Theme.Body, new Point(px + Theme.S(2), Theme.S(100)), Theme.TextDim);

            // progresso
            string prog = done + " de " + total + " teclas";
            var progColor = done == total && total > 0 ? Theme.PillGreen : Theme.PillYellow;
            Theme.Pill(g, done == total && total > 0 ? "TODAS AS TECLAS OK  ·  " + prog : prog, progColor,
                new Rectangle(ClientSize.Width - px - Theme.S(520), Theme.S(22), Theme.S(520), Theme.S(50)), Theme.Heading);
            if (lastKey != "")
                TextRenderer.DrawText(g, "Última tecla: " + lastKey, Theme.Body,
                    new Rectangle(ClientSize.Width - px - Theme.S(620), Theme.S(74), Theme.S(620), Theme.S(28)), Theme.TextDim, TextFormatFlags.Right);

            float u = Unit;
            using (var small = new Font(Theme.Family, Math.Max(6.5f, u * 0.17f * 72f / g.DpiY)))
            using (var normal = new Font(Theme.Family, Math.Max(7.5f, u * 0.24f * 72f / g.DpiY), FontStyle.Bold))
            {
                foreach (var k in keys)
                {
                    var r = KeyRect(k);
                    Color fill, text, border;
                    if (k.Excluded) { fill = Theme.TitleBar; text = Color.FromArgb(90, 95, 105); border = Color.FromArgb(50, 54, 62); }
                    else if (k.Down) { fill = Theme.Accent; text = Theme.AccentText; border = Theme.Accent; }
                    else if (k.Pressed) { fill = Theme.Hex("#1f7a43"); text = Color.White; border = Theme.Hex("#35b56a"); }
                    else { fill = Theme.Panel; text = Theme.Text; border = Theme.Hex("#565d6b"); }
                    Theme.FillRound(g, fill, r, u * 0.12f);
                    Theme.DrawRound(g, border, r, u * 0.12f, 1.4f);
                    string label = Name(k);
                    TextRenderer.DrawText(g, label, label.Length > 2 ? small : normal, Rectangle.Round(r), text,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
                    if (k.Excluded)
                        using (var pen = new Pen(Color.FromArgb(70, 75, 85), 1.5f)) g.DrawLine(pen, r.Left + 4, r.Bottom - 4, r.Right - 4, r.Top + 4);
                }
            }

            // o que falta
            var missing = keys.Where(k => !k.Excluded && !k.Pressed).Select(k => Name(k)).ToList();
            int my = (int)(Theme.S(170) + TotalH * u + Theme.S(14));
            string txt = missing.Count == 0 ? "Nenhuma tecla pendente." : "Faltam (" + missing.Count + "): " + string.Join("  ", missing.Take(60));
            TextRenderer.DrawText(g, txt, Theme.Body, new Rectangle(px, my, ClientSize.Width - px * 2, Theme.S(56)),
                missing.Count == 0 ? Theme.PillGreen[1] : Theme.PillRed[1], TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);

            using (var pen = new Pen(Theme.Border)) g.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }
    }

    // ======================================================================================
    // ÁUDIO: tom de teste por canal (esquerdo/direito) e gravação do microfone com reprodução.
    // ======================================================================================
    class AudioTestDialog : DarkDialog
    {
        readonly ToolConfig step;
        readonly SoundPlayer player = new SoundPlayer();
        readonly Timer recTimer = new Timer { Interval = 100 };
        readonly string recFile = Path.Combine(Path.GetTempPath(), "diagnostico_mic_teste.wav");
        const string Alias = "dtmic";
        const int RecordMs = 5000;
        DateTime recStart;
        bool recording, hasRecording;
        int level, peak;
        string micStatus = "Clique em \"Gravar 5 segundos\" e fale perto do microfone.";
        string speakerStatus = "Escolha um canal para ouvir o tom de teste.";

        readonly FlatButton leftBtn = new FlatButton("Esquerdo"), rightBtn = new FlatButton("Direito"),
                            bothBtn = new FlatButton("Os dois"), stopBtn = new FlatButton("Parar");
        readonly FlatButton recBtn = new FlatButton("Gravar 5 segundos"), playBtn = new FlatButton("Ouvir gravação");
        readonly FlatButton printBtn = new FlatButton("Salvar print"), doneBtn = new FlatButton("Concluir", true), cancelBtn = new FlatButton("Fechar");
        readonly string[] tests = { "Áudio interno", "Microfone", "Conector P2" };
        readonly Segmented[] answers = new Segmented[3];

        public AudioTestDialog(ToolConfig step) : base("Teste de áudio e microfone", 820, 700)
        {
            this.step = step;
            DoubleBuffered = true;
            int pad = Theme.S(24), bh = Theme.S(46), g = Theme.S(12);

            int x = pad, y = Theme.S(118);
            foreach (var b in new[] { leftBtn, rightBtn, bothBtn, stopBtn })
            {
                int w = b.PreferredWidth + Theme.S(16);
                b.SetBounds(x, y, w, bh); x += w + g; Controls.Add(b);
            }
            leftBtn.Click += (s, e) => PlayTone(true, false, "Tocando no canal ESQUERDO...");
            rightBtn.Click += (s, e) => PlayTone(false, true, "Tocando no canal DIREITO...");
            bothBtn.Click += (s, e) => PlayTone(true, true, "Tocando nos DOIS canais...");
            stopBtn.Click += (s, e) => { try { player.Stop(); } catch { } speakerStatus = "Parado."; Invalidate(); };

            y = Theme.S(268);
            recBtn.SetBounds(pad, y, recBtn.PreferredWidth + Theme.S(16), bh);
            playBtn.SetBounds(recBtn.Right + g, y, playBtn.PreferredWidth + Theme.S(16), bh);
            playBtn.Enabled = false;
            Controls.Add(recBtn); Controls.Add(playBtn);
            recBtn.Click += (s, e) => StartRecording();
            playBtn.Click += (s, e) => PlayRecording();
            recTimer.Tick += (s, e) => RecordingTick();

            y = Theme.S(440);
            for (int i = 0; i < tests.Length; i++)
            {
                var lbl = UI.Label(tests[i], Theme.Body, Theme.Text, Theme.Bg);
                lbl.SetBounds(pad, y + i * Theme.S(48), Theme.S(200), Theme.S(40));
                answers[i] = new Segmented("Sim", "Não", "Não Tem") { BackColor = Theme.Bg };
                answers[i].SetBounds(pad + Theme.S(210), y + i * Theme.S(48), Theme.S(300), Theme.S(40));
                Controls.Add(lbl); Controls.Add(answers[i]);
            }

            int by = ClientSize.Height - Theme.S(60);
            int rx = ClientSize.Width - pad;
            foreach (var b in new[] { doneBtn, cancelBtn, printBtn })
            {
                int w = b.PreferredWidth + Theme.S(16);
                b.SetBounds(rx - w, by, w, bh); rx -= w + g; Controls.Add(b);
            }
            printBtn.Click += async (s, e) => await Session.Capture(Handle, step, step.Codigo, false, true);
            cancelBtn.Click += (s, e) => Close();
            doneBtn.Click += (s, e) =>
            {
                for (int i = 0; i < tests.Length; i++)
                    if (answers[i].SelectedIndex >= 0) Session.AnswerTest(tests[i], answers[i].SelectedIndex);
                Close();
            };
        }

        // ---------- alto-falantes ----------

        // WAV 44,1 kHz, 16 bits, estéreo: tom com um canal mudo para conferir esquerdo/direito.
        static MemoryStream Tone(bool left, bool right, double seconds = 2.0)
        {
            const int rate = 44100;
            int samples = (int)(rate * seconds);
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + samples * 4); w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
            w.Write(16); w.Write((short)1); w.Write((short)2); w.Write(rate); w.Write(rate * 4); w.Write((short)4); w.Write((short)16);
            w.Write(Encoding.ASCII.GetBytes("data")); w.Write(samples * 4);
            for (int i = 0; i < samples; i++)
            {
                double t = i / (double)rate;
                // três notas em sequência (mais fácil de perceber que um tom fixo), com fade para não estalar
                double freq = t < seconds / 3 ? 523.25 : t < 2 * seconds / 3 ? 659.25 : 783.99;
                double seg = (t % (seconds / 3)) / (seconds / 3);
                double env = Math.Min(1, Math.Min(seg, 1 - seg) * 12);
                short v = (short)(Math.Sin(2 * Math.PI * freq * t) * 11000 * env);
                w.Write(left ? v : (short)0);
                w.Write(right ? v : (short)0);
            }
            w.Flush();
            ms.Position = 0;
            return ms;
        }

        void PlayTone(bool left, bool right, string status)
        {
            try
            {
                player.Stop();
                player.Stream = Tone(left, right);
                player.Play();
                speakerStatus = status;
            }
            catch (Exception ex) { speakerStatus = "Não foi possível tocar: " + ex.Message; }
            Invalidate();
        }

        // ---------- microfone (MCI: não depende de nenhuma biblioteca) ----------

        static string Mci(string cmd, out int err)
        {
            var sb = new StringBuilder(256);
            err = Native.mciSendString(cmd, sb, sb.Capacity, IntPtr.Zero);
            return sb.ToString();
        }

        static string MciError(int err)
        {
            var sb = new StringBuilder(256);
            return Native.mciGetErrorString(err, sb, sb.Capacity) ? sb.ToString() : "erro " + err;
        }

        void StartRecording()
        {
            if (recording) return;
            int err;
            try { player.Stop(); } catch { }
            Mci("close " + Alias, out err);
            Mci("open new type waveaudio alias " + Alias, out err);
            if (err != 0) { micStatus = "Nenhum microfone disponível: " + MciError(err); Invalidate(); return; }
            Mci("set " + Alias + " time format ms bitspersample 16 channels 1 samplespersec 44100 bytespersec 88200 alignment 2", out err);
            Mci("record " + Alias, out err);
            if (err != 0) { micStatus = "Não foi possível gravar: " + MciError(err); Mci("close " + Alias, out err); Invalidate(); return; }
            recording = true; hasRecording = false; peak = 0; level = 0;
            recStart = DateTime.Now;
            recBtn.Enabled = false; playBtn.Enabled = false;
            micStatus = "Gravando... fale agora.";
            recTimer.Start();
            Invalidate();
        }

        void RecordingTick()
        {
            int err, v;
            string s = Mci("status " + Alias + " level", out err);
            if (err == 0 && int.TryParse(s.Trim(), out v)) { level = Math.Min(100, v * 300 / 32768); peak = Math.Max(peak, level); }
            double ms = (DateTime.Now - recStart).TotalMilliseconds;
            micStatus = "Gravando... fale agora.  (" + Math.Max(0, Math.Ceiling((RecordMs - ms) / 1000)) + " s)";
            if (ms >= RecordMs) StopRecording();
            Invalidate();
        }

        void StopRecording()
        {
            recTimer.Stop();
            int err;
            Mci("stop " + Alias, out err);
            try { if (File.Exists(recFile)) File.Delete(recFile); } catch { }
            Mci("save " + Alias + " \"" + recFile + "\"", out err);
            int saveErr = err;
            Mci("close " + Alias, out err);
            recording = false; level = 0;
            recBtn.Enabled = true;
            hasRecording = saveErr == 0 && File.Exists(recFile);
            playBtn.Enabled = hasRecording;
            if (!hasRecording) micStatus = "Falha ao salvar a gravação: " + MciError(saveErr);
            else if (peak < 3) micStatus = "Gravado, mas o microfone praticamente não captou som (pico " + peak + "%). Ouça para confirmar.";
            else micStatus = "Gravado (pico " + peak + "%). Clique em \"Ouvir gravação\" para conferir.";
        }

        void PlayRecording()
        {
            try
            {
                player.Stop();
                player.Stream = new MemoryStream(File.ReadAllBytes(recFile));
                player.Play();
                micStatus = "Reproduzindo a gravação (pico " + peak + "%)...";
            }
            catch (Exception ex) { micStatus = "Não foi possível reproduzir: " + ex.Message; }
            Invalidate();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            int err;
            recTimer.Stop(); recTimer.Dispose();
            if (recording) { Mci("stop " + Alias, out err); }
            Mci("close " + Alias, out err);
            try { player.Stop(); player.Dispose(); } catch { }
            try { if (File.Exists(recFile)) File.Delete(recFile); } catch { }
            base.OnFormClosed(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            Theme.Smooth(g);
            int pad = Theme.S(24), w = ClientSize.Width - pad * 2;
            string info = "OS " + (Session.ActiveOs == "" ? "—" : Session.ActiveOs) + "   ·   " + DateTime.Now.ToString("dd/MM/yyyy HH:mm") +
                          "   ·   " + (Session.Depois ? "depois da manutenção" : "antes da manutenção");
            TextRenderer.DrawText(g, "Teste de áudio e microfone", Theme.Heading, new Point(pad, Theme.S(14)), Theme.Text);
            TextRenderer.DrawText(g, info, Theme.Small, new Point(pad, Theme.S(46)), Theme.TextDim);

            TextRenderer.DrawText(g, "ALTO-FALANTES", Theme.BodyBold, new Point(pad, Theme.S(84)), Theme.TextDim);
            TextRenderer.DrawText(g, speakerStatus, Theme.Body, new Rectangle(pad, Theme.S(172), w, Theme.S(28)), Theme.Text, TextFormatFlags.EndEllipsis);

            using (var pen = new Pen(Theme.Line)) { g.DrawLine(pen, pad, Theme.S(214), pad + w, Theme.S(214)); g.DrawLine(pen, pad, Theme.S(396), pad + w, Theme.S(396)); }

            TextRenderer.DrawText(g, "MICROFONE", Theme.BodyBold, new Point(pad, Theme.S(232)), Theme.TextDim);
            // medidor de volume
            var bar = new Rectangle(pad, Theme.S(328), w, Theme.S(18));
            Theme.FillRound(g, Theme.Input, bar, bar.Height / 2f);
            if (level > 0) Theme.FillRound(g, Theme.Hex("#35b56a"), new RectangleF(bar.X, bar.Y, Math.Max(bar.Height, bar.Width * level / 100f), bar.Height), bar.Height / 2f);
            if (peak > 0) using (var b = new SolidBrush(Theme.Accent)) g.FillRectangle(b, bar.X + bar.Width * peak / 100 - 2, bar.Y - 2, 3, bar.Height + 4);
            TextRenderer.DrawText(g, micStatus, Theme.Body, new Rectangle(pad, Theme.S(354), w, Theme.S(30)), Theme.Text, TextFormatFlags.EndEllipsis);

            TextRenderer.DrawText(g, "RESULTADO (vai para os testes da OS ao concluir)", Theme.BodyBold, new Point(pad, Theme.S(404) - Theme.S(4)), Theme.TextDim);
        }
    }
}
