using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Diagnostico
{
    // Base de todos os controles desenhados à mão: double buffer e redesenho no resize.
    public class DrawnControl : Control
    {
        public bool FixedBackColor;
        public DrawnControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            SuspendLayout();   // evita OnLayout das subclasses antes dos campos existirem
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.Body;
            ResumeLayout(false);
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if ((this is FlatButton || this is DarkSelect || this is DarkCheck || this is ToggleSwitch) &&
                (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space))
            {
                e.Handled = true; e.SuppressKeyPress = true; OnClick(EventArgs.Empty);
            }
            base.OnKeyDown(e);
        }

        protected Color ParentColor
        {
            get
            {
                for (var p = Parent; p != null; p = p.Parent)
                    if (p.BackColor != Color.Transparent) return p.BackColor;
                return Theme.Bg;
            }
        }
    }

    // Painel arredondado (os "cards" escuros do layout).
    public class RoundPanel : Panel
    {
        public float Radius = 14;
        public Color Fill = Theme.Panel;
        public Color BorderColor = Theme.Border;

        public RoundPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            BackColor = Theme.Panel;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Color outer = Parent != null ? Parent.BackColor : Theme.Bg;
            e.Graphics.Clear(outer);
            Theme.Smooth(e.Graphics);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Theme.FillRound(e.Graphics, Fill, r, Theme.S(Radius));
            Theme.DrawRound(e.Graphics, BorderColor, r, Theme.S(Radius));
        }
    }

    public class FlatButton : DrawnControl
    {
        public bool Primary;
        public float Radius = 8;
        public int TextOffset;
        bool hover, down;

        public FlatButton(string text, bool primary = false)
        {
            Text = text;
            Primary = primary;
            Cursor = Cursors.Hand;
            Font = Theme.Body;
            AccessibleRole = AccessibleRole.PushButton;
        }

        public int PreferredWidth
        {
            get { return TextRenderer.MeasureText(Text, Font).Width + Theme.S(36); }
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(ParentColor);
            Theme.Smooth(g);
            var r = new RectangleF(1, 1, Width - 2.5f, Height - 2.5f);
            Color text;
            if (Primary)
            {
                Color fill = down || hover ? (Theme.IsDark ? ControlPaint.Light(Theme.Accent, 0.1f) : ControlPaint.Dark(Theme.Accent, 0.1f)) : Theme.Accent;
                if (!Enabled) fill = Color.FromArgb(120, Theme.Accent);
                Theme.FillRound(g, fill, r, Theme.S(Radius));
                text = Theme.AccentText;
            }
            else
            {
                if (hover) Theme.FillRound(g, down ? Theme.Selected : Theme.PanelHover, r, Theme.S(Radius));
                Theme.DrawRound(g, Focused ? Theme.Accent : Theme.Border, r, Theme.S(Radius), 1.3f);
                text = Enabled ? Theme.Text : Theme.TextDim;
            }
            var tr = ClientRectangle; tr.X += TextOffset; tr.Width -= TextOffset;
            TextRenderer.DrawText(g, Text, Font, tr, text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            if (Focused && Primary) Theme.DrawRound(g, Theme.AccentText, new Rectangle(4, 4, Width - 9, Height - 9), Theme.S(Radius - 2));
        }
    }

    // Botão "← Menu"
    public class BackButton : FlatButton
    {
        public BackButton() : base("Menu") { TextOffset = Theme.S(22); }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            Theme.Smooth(g);
            int cy = Height / 2, x = Theme.S(22);
            using (var pen = new Pen(Theme.TextDim, Theme.S(2.2f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                g.DrawLine(pen, x, cy, x + Theme.S(14), cy);
                g.DrawLines(pen, new[] { new Point(x + Theme.S(6), cy - Theme.S(6)), new Point(x, cy), new Point(x + Theme.S(6), cy + Theme.S(6)) });
            }
        }

        public new int PreferredWidth { get { return TextRenderer.MeasureText(Text, Font).Width + Theme.S(72); } }
    }

    // Caixa de texto escura com cantos arredondados (TextBox nativo por dentro).
    public class DarkInput : DrawnControl
    {
        public readonly TextBox Box = new TextBox();

        public DarkInput(string text = "")
        {
            Box.BorderStyle = BorderStyle.None;
            Box.BackColor = Theme.Input;
            Box.ForeColor = Theme.Text;
            Box.Font = Theme.Body;
            Box.Text = text;
            Controls.Add(Box);
            TabStop = false;
            Box.GotFocus += (s, e) => Invalidate();
            Box.LostFocus += (s, e) => Invalidate();
            Cursor = Cursors.IBeam;
        }

        public override string Text { get { return Box == null ? "" : Box.Text; } set { if (Box != null) Box.Text = value; } }
        public bool ReadOnly { get { return Box.ReadOnly; } set { Box.ReadOnly = value; Box.BackColor = Theme.Input; } }
        public event EventHandler ValueChanged { add { Box.TextChanged += value; } remove { Box.TextChanged -= value; } }

        protected override void OnClick(EventArgs e) { Box.Focus(); base.OnClick(e); }

        protected override void OnLayout(LayoutEventArgs e)
        {
            int pad = Theme.S(12);
            Box.SetBounds(pad, (Height - Box.Height) / 2, Width - pad * 2, Box.Height);
            base.OnLayout(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(ParentColor);
            Theme.Smooth(e.Graphics);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Theme.FillRound(e.Graphics, Theme.Input, r, Theme.S(6));
            Theme.DrawRound(e.Graphics, Box.Focused ? Theme.Accent : Theme.Border, r, Theme.S(6));
        }
    }

    // Lista de opções que abre um menu ao clicar (parece um campo de texto).
    public class DarkSelect : DrawnControl
    {
        public string[] Options;
        string value = "";
        public event EventHandler ValueChanged;

        public DarkSelect(params string[] options) { Options = options; Cursor = Cursors.Hand; AccessibleRole = AccessibleRole.ComboBox; }

        public string Value
        {
            get { return value; }
            set { this.value = value ?? ""; Invalidate(); }
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            var menu = new ContextMenuStrip { Renderer = new DarkMenuRenderer(), ShowImageMargin = false, Font = Theme.Body };
            foreach (var o in Options)
            {
                string opt = o;
                var item = new ToolStripMenuItem(o) { ForeColor = Theme.Text, Checked = o == value };
                item.Click += (s, a) => { Value = opt; if (ValueChanged != null) ValueChanged(this, EventArgs.Empty); };
                menu.Items.Add(item);
            }
            menu.MinimumSize = new Size(Width, 0);
            UI.DisposeWhenClosed(menu);
            menu.Show(this, new Point(0, Height + 2));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(ParentColor);
            Theme.Smooth(g);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Theme.FillRound(g, Theme.Input, r, Theme.S(6));
            Theme.DrawRound(g, Focused ? Theme.Accent : Theme.Border, r, Theme.S(6));
            TextRenderer.DrawText(g, value, Font, new Rectangle(Theme.S(12), 0, Width - Theme.S(40), Height), Theme.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            int cx = Width - Theme.S(18), cy = Height / 2;
            using (var pen = new Pen(Theme.TextDim, Theme.S(1.8f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawLines(pen, new[] { new Point(cx - Theme.S(5), cy - Theme.S(2)), new Point(cx, cy + Theme.S(3)), new Point(cx + Theme.S(5), cy - Theme.S(2)) });
        }
    }

    public class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkColors()) { }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Theme.Text : Theme.TextDim;
            base.OnRenderItemText(e);
        }

        class DarkColors : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground { get { return Theme.Panel; } }
            public override Color MenuItemSelected { get { return Theme.Selected; } }
            public override Color MenuItemBorder { get { return Theme.Selected; } }
            public override Color MenuBorder { get { return Theme.Border; } }
            public override Color ImageMarginGradientBegin { get { return Theme.Panel; } }
            public override Color ImageMarginGradientMiddle { get { return Theme.Panel; } }
            public override Color ImageMarginGradientEnd { get { return Theme.Panel; } }
            public override Color SeparatorDark { get { return Theme.Border; } }
            public override Color SeparatorLight { get { return Theme.Border; } }
            public override Color CheckBackground { get { return Theme.Selected; } }
            public override Color CheckSelectedBackground { get { return Theme.Selected; } }
            public override Color CheckPressedBackground { get { return Theme.Selected; } }
        }
    }

    public class DarkCheck : DrawnControl
    {
        bool isChecked;
        public event EventHandler CheckedChanged;

        public DarkCheck(string text) { Text = text; Cursor = Cursors.Hand; AccessibleRole = AccessibleRole.CheckButton; }

        public bool Checked
        {
            get { return isChecked; }
            set { if (isChecked == value) return; isChecked = value; Invalidate(); if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty); }
        }

        public int PreferredWidth { get { return Theme.S(30) + TextRenderer.MeasureText(Text, Font).Width; } }

        protected override void OnClick(EventArgs e) { Checked = !Checked; base.OnClick(e); }

        public static void DrawBox(Graphics g, Rectangle box, bool on)
        {
            if (on)
            {
                Theme.FillRound(g, Theme.CheckBlue, box, Theme.S(4));
                using (var pen = new Pen(Color.White, Theme.S(2.4f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                    g.DrawLines(pen, new[] {
                        new PointF(box.X + box.Width * 0.24f, box.Y + box.Height * 0.52f),
                        new PointF(box.X + box.Width * 0.42f, box.Y + box.Height * 0.70f),
                        new PointF(box.X + box.Width * 0.78f, box.Y + box.Height * 0.30f) });
            }
            else
            {
                Theme.FillRound(g, Theme.Input, box, Theme.S(4));
                Theme.DrawRound(g, Theme.TextDim, Rectangle.Inflate(box, -1, -1), Theme.S(4));
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(ParentColor);
            Theme.Smooth(g);
            int s = Theme.S(22);
            DrawBox(g, new Rectangle(0, (Height - s) / 2, s, s), isChecked);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(s + Theme.S(6), 0, Width, Height), Theme.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            if (Focused) ControlPaint.DrawFocusRectangle(g, ClientRectangle, Theme.Text, ParentColor);
        }
    }

    // Chave liga/desliga (Toggle Switch) com rótulo de estado (Ativado / Desativado).
    public class ToggleSwitch : DrawnControl
    {
        bool isChecked;
        public string OnText = "Ativado";
        public string OffText = "Desativado";
        public event EventHandler CheckedChanged;

        public ToggleSwitch(string text, string onText = "Ativado", string offText = "Desativado")
        {
            Text = text;
            OnText = onText;
            OffText = offText;
            Cursor = Cursors.Hand;
            AccessibleRole = AccessibleRole.CheckButton;
        }

        public bool Checked
        {
            get { return isChecked; }
            set { if (isChecked == value) return; isChecked = value; Invalidate(); if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty); }
        }

        string DisplayLabel
        {
            get
            {
                string state = isChecked ? OnText : OffText;
                return string.IsNullOrEmpty(Text) ? state : Text + ": " + state;
            }
        }

        public int PreferredWidth
        {
            get
            {
                string longest = string.IsNullOrEmpty(Text)
                    ? (OnText.Length >= OffText.Length ? OnText : OffText)
                    : Text + ": " + (OnText.Length >= OffText.Length ? OnText : OffText);
                return Theme.S(54) + TextRenderer.MeasureText(longest, Font).Width;
            }
        }

        protected override void OnClick(EventArgs e) { Checked = !Checked; base.OnClick(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(ParentColor);
            Theme.Smooth(g);
            int tw = Theme.S(42), th = Theme.S(22);
            int ty = (Height - th) / 2;
            var track = new RectangleF(0, ty, tw, th);
            if (isChecked)
            {
                Theme.FillRound(g, Theme.CheckBlue, track, th / 2f);
            }
            else
            {
                Theme.FillRound(g, Theme.Input, track, th / 2f);
                Theme.DrawRound(g, Theme.Border, new RectangleF(0.5f, ty + 0.5f, tw - 1f, th - 1f), th / 2f, 1.2f);
            }
            int pad = Theme.S(3), d = th - pad * 2;
            float kx = isChecked ? tw - pad - d : pad;
            using (var b = new SolidBrush(isChecked ? Color.White : Theme.TextDim))
                g.FillEllipse(b, kx, ty + pad, d, d);

            TextRenderer.DrawText(g, DisplayLabel, Font, new Rectangle(tw + Theme.S(8), 0, Width - tw - Theme.S(8), Height),
                isChecked ? Theme.Text : Theme.TextDim, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            if (Focused) ControlPaint.DrawFocusRectangle(g, ClientRectangle, Theme.Text, ParentColor);
        }
    }

    // Sim / Não / Não Tem
    public class Segmented : DrawnControl
    {
        public string[] Options;
        int selected = -1, hover = -1;
        public event EventHandler SelectedChanged;

        public Segmented(params string[] options) { Options = options; Cursor = Cursors.Hand; }

        protected override bool IsInputKey(Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            return key == Keys.Left || key == Keys.Right || base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right || e.KeyCode == Keys.Space)
            {
                selected = e.KeyCode == Keys.Left ? Math.Max(0, selected - 1) : (selected + 1) % Options.Length;
                e.Handled = true; e.SuppressKeyPress = true;
                if (SelectedChanged != null) SelectedChanged(this, EventArgs.Empty);
                Invalidate();
            }
            base.OnKeyDown(e);
        }

        public int SelectedIndex
        {
            get { return selected; }
            set { selected = value; Invalidate(); }
        }

        Rectangle Seg(int i)
        {
            int w = (Width - 2) / Options.Length;
            return new Rectangle(1 + i * w, 1, i == Options.Length - 1 ? Width - 2 - i * w - 1 : w, Height - 3);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = Math.Min(Options.Length - 1, Math.Max(0, e.X * Options.Length / Math.Max(1, Width)));
            if (h != hover) { hover = h; Invalidate(); }
        }
        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            int i = Math.Min(Options.Length - 1, Math.Max(0, e.X * Options.Length / Math.Max(1, Width)));
            selected = selected == i ? -1 : i;
            Invalidate();
            if (SelectedChanged != null) SelectedChanged(this, EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(ParentColor);
            Theme.Smooth(g);
            float rad = Theme.S(6);
            var outer = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Theme.FillRound(g, Theme.Panel, outer, rad);
            using (var clip = Theme.Round(outer, rad))
            {
                g.SetClip(clip);
                for (int i = 0; i < Options.Length; i++)
                {
                    var r = Seg(i);
                    if (i == selected) { using (var brush = new SolidBrush(Theme.Accent)) g.FillRectangle(brush, r); }
                    else if (i == hover) { using (var brush = new SolidBrush(Theme.PanelHover)) g.FillRectangle(brush, r); }
                    if (i > 0) using (var pen = new Pen(Theme.Border)) g.DrawLine(pen, r.X, 0, r.X, Height);
                    TextRenderer.DrawText(g, Options[i], Font, r, i == selected ? Theme.AccentText : Theme.Text,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
                g.ResetClip();
            }
            Theme.DrawRound(g, Focused ? Theme.Accent : Theme.Border, outer, rad);
        }
    }

    // Barra fina de rolagem usada pelas listas desenhadas.
    public class ScrollHelper
    {
        readonly Control owner;
        public int Offset, Content;
        bool dragging; int dragStartY, dragStartOffset;

        public ScrollHelper(Control owner) { this.owner = owner; }

        public int Viewport { get; set; }
        public int Max { get { return Math.Max(0, Content - Viewport); } }

        public void Clamp() { Offset = Math.Max(0, Math.Min(Max, Offset)); }

        // multiplica antes de dividir: touchpads de precisão mandam deltas menores que 120
        public void Wheel(int delta) { Offset -= delta * Theme.S(60) / 120; Clamp(); owner.Invalidate(); }

        public void EnsureVisible(int top, int bottom)
        {
            if (top < Offset) Offset = top;
            else if (bottom > Offset + Viewport) Offset = bottom - Viewport;
            Clamp();
        }

        Rectangle Thumb(Rectangle track)
        {
            if (Max <= 0) return Rectangle.Empty;
            int h = Math.Max(Theme.S(30), track.Height * Viewport / Math.Max(1, Content));
            int y = track.Y + (int)((track.Height - h) * (Offset / (float)Max));
            return new Rectangle(track.X, y, track.Width, h);
        }

        public void Paint(Graphics g, Rectangle track)
        {
            var t = Thumb(track);
            if (t.IsEmpty) return;
            Theme.FillRound(g, Theme.TextDim, t, t.Width / 2f);
        }

        public bool MouseDown(MouseEventArgs e, Rectangle track)
        {
            var hit = new Rectangle(track.X - Theme.S(6), track.Y, track.Width + Theme.S(12), track.Height);
            if (Max <= 0 || !hit.Contains(e.Location)) return false;
            var t = Thumb(track);
            if (!t.Contains(new Point(t.X, e.Y)))
            {
                Offset = (int)((e.Y - track.Y) / (float)track.Height * Content - Viewport / 2f); Clamp();
                t = Thumb(track);
            }
            dragging = true; dragStartY = e.Y; dragStartOffset = Offset;
            owner.Capture = true;
            owner.Invalidate();
            return true;
        }

        public bool MouseMove(MouseEventArgs e, Rectangle track)
        {
            if (!dragging) return false;
            int h = Math.Max(Theme.S(30), track.Height * Viewport / Math.Max(1, Content));
            float ratio = Max / (float)Math.Max(1, track.Height - h);
            Offset = dragStartOffset + (int)((e.Y - dragStartY) * ratio); Clamp();
            owner.Invalidate();
            return true;
        }

        public bool MouseUp() { if (!dragging) return false; dragging = false; owner.Capture = false; return true; }
    }

    // Barra de progresso fina com contagem ("5/14").
    public class ProgressStrip : DrawnControl
    {
        int value, max;
        public void Set(int v, int m) { if (v == value && m == max) return; value = v; max = m; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(ParentColor);
            Theme.Smooth(g);
            string text = value + "/" + max;
            int tw = TextRenderer.MeasureText(g, "00/00", Theme.Small).Width;
            TextRenderer.DrawText(g, text, Theme.Small, new Rectangle(Width - tw, 0, tw, Height), max > 0 && value >= max ? Theme.PillGreen[1] : Theme.TextDim,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            int bh = Theme.S(6), bw = Width - tw - Theme.S(8);
            if (bw < Theme.S(20)) return;
            var track = new RectangleF(0, (Height - bh) / 2f, bw, bh);
            Theme.FillRound(g, Theme.Input, track, bh / 2f);
            if (max > 0 && value > 0)
                Theme.FillRound(g, value >= max ? Theme.Hex("#35b56a") : Theme.Accent,
                    new RectangleF(track.X, track.Y, Math.Max(bh, bw * Math.Min(1f, value / (float)max)), bh), bh / 2f);
        }
    }

    public class ListRow
    {
        public string Text, Sub, Right, Badge, KindBadge;
        public string Lead;          // número mostrado num círculo à esquerda (etapas)
        public bool LeadDone;        // círculo verde com ✓
        public bool Dimmed;          // etapa desativada
        public Color[] BadgeColors, KindColors;
        public object Tag;
    }

    // Lista com linhas, badges de status e rolagem (PROGRAMAS, PASTA DE OS...).
    public class RowList : DrawnControl
    {
        public readonly List<ListRow> Rows = new List<ListRow>();
        public int RowHeight = 54;
        public int SelectedIndex = -1;
        public bool Selectable = true;
        public bool Reorderable;
        public event EventHandler<int> RowClick, RowDoubleClick;
        public event EventHandler<int> RowRightClick;
        public event Action<int, int> RowMoved;
        readonly ScrollHelper scroll;
        int hover = -1;
        int dragIndex = -1, dropIndex = -1, mouseDownY;
        bool dragStarted;

        public RowList()
        {
            scroll = new ScrollHelper(this);
            BackColor = Theme.Panel;
        }

        int RH { get { return Theme.S(RowHeight); } }
        Rectangle Track { get { return new Rectangle(Width - Theme.S(6), Theme.S(4), Theme.S(4), Height - Theme.S(8)); } }

        public void SetRows(IEnumerable<ListRow> rows)
        {
            Rows.Clear(); Rows.AddRange(rows);
            if (SelectedIndex >= Rows.Count) SelectedIndex = -1;
            Invalidate();
        }

        public ListRow Selected { get { return SelectedIndex >= 0 && SelectedIndex < Rows.Count ? Rows[SelectedIndex] : null; } }

        int HitRow(Point p)
        {
            int i = (p.Y + scroll.Offset) / RH;
            return i >= 0 && i < Rows.Count ? i : -1;
        }

        protected override void OnMouseWheel(MouseEventArgs e) { scroll.Wheel(e.Delta); }
        protected override void OnMouseEnter(EventArgs e) { Focus(); base.OnMouseEnter(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (scroll.MouseDown(e, Track)) return;
            int i = HitRow(e.Location);
            if (i < 0) return;
            if (e.Button == MouseButtons.Left)
            {
                if (Selectable) SelectedIndex = i;
                if (Reorderable)
                {
                    dragIndex = i; dropIndex = i; mouseDownY = e.Y; dragStarted = false;
                    Capture = true;
                }
                Invalidate();
                if (RowClick != null) RowClick(this, i);
            }
            else if (e.Button == MouseButtons.Right)
            {
                if (Selectable) { SelectedIndex = i; Invalidate(); }
                if (RowRightClick != null) RowRightClick(this, i);
            }
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            int i = HitRow(e.Location);
            if (i >= 0 && e.Button == MouseButtons.Left && RowDoubleClick != null) RowDoubleClick(this, i);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (scroll.MouseMove(e, Track)) return;
            if (Reorderable && dragIndex >= 0)
            {
                if (!dragStarted && Math.Abs(e.Y - mouseDownY) < Theme.S(4)) return;
                dragStarted = true;
                Cursor = Cursors.SizeNS;
                int target = (int)Math.Round((e.Y + scroll.Offset) / (float)RH);
                dropIndex = Math.Max(0, Math.Min(Rows.Count, target));
                if (e.Y < Theme.S(20)) scroll.Wheel(60);
                if (e.Y > Height - Theme.S(20)) scroll.Wheel(-60);
                Invalidate();
                return;
            }
            int h = HitRow(e.Location);
            if (Reorderable) Cursor = h >= 0 && e.X < Theme.S(28) ? Cursors.SizeNS : Cursors.Default;
            if (h != hover) { hover = h; Invalidate(); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (scroll.MouseUp()) return;
            if (Reorderable && dragIndex >= 0)
            {
                Capture = false;
                Cursor = Cursors.Default;
                int from = dragIndex;
                bool moved = dragStarted && dropIndex != dragIndex && dropIndex != dragIndex + 1;
                int to = dropIndex > dragIndex ? dropIndex - 1 : dropIndex;
                dragIndex = -1; dropIndex = -1; dragStarted = false;
                if (moved && to >= 0 && to < Rows.Count)
                {
                    var item = Rows[from];
                    Rows.RemoveAt(from);
                    Rows.Insert(to, item);
                    if (Selectable) SelectedIndex = to;
                    Invalidate();
                    if (RowMoved != null) RowMoved(from, to);
                    return;
                }
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e) { hover = -1; if (dragIndex < 0) Cursor = Cursors.Default; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            Theme.Smooth(g);
            scroll.Viewport = Height;
            scroll.Content = Rows.Count * RH;
            scroll.Clamp();
            int pad = Theme.S(12);
            using (var line = new Pen(Theme.Line))
            using (var dot = new SolidBrush(Color.FromArgb(130, Theme.TextDim)))
            {
                for (int i = 0; i < Rows.Count; i++)
                {
                    int y = i * RH - scroll.Offset;
                    if (y + RH < 0 || y > Height) continue;
                    var row = Rows[i];
                    var r = new Rectangle(0, y, Width, RH);
                    if (i == SelectedIndex && Selectable) { using (var b = new SolidBrush(Theme.Selected)) g.FillRectangle(b, r); }
                    else if (i == hover) { using (var b = new SolidBrush(Theme.PanelHover)) g.FillRectangle(b, r); }
                    g.DrawLine(line, 0, y, Width, y);

                    int rightW = 0;
                    var inner = new Rectangle(pad, y, Width - pad * 2 - Theme.S(8), RH);
                    if (!string.IsNullOrEmpty(row.Badge))
                    {
                        var sz = TextRenderer.MeasureText(row.Badge, Theme.Body);
                        rightW = sz.Width + Theme.S(24);
                        Theme.Pill(g, row.Badge, row.BadgeColors ?? Theme.PillGray, inner, Theme.Body);
                    }
                    else if (!string.IsNullOrEmpty(row.Right))
                    {
                        var sz = TextRenderer.MeasureText(row.Right, Theme.Mono);
                        rightW = sz.Width + Theme.S(8);
                        TextRenderer.DrawText(g, row.Right, Theme.Mono, inner, Theme.TextDim,
                            TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
                    }
                    var textRect = new Rectangle(inner.X, y, inner.Width - rightW, RH);
                    if (Reorderable)
                    {
                        int hx = textRect.X, hy = y + RH / 2 - Theme.S(10);
                        for (int gr = 0; gr < 4; gr++)
                            for (int gc = 0; gc < 2; gc++)
                                g.FillEllipse(dot, hx + gc * Theme.S(5), hy + gr * Theme.S(6), Theme.S(3), Theme.S(3));
                        textRect.X += Theme.S(16); textRect.Width -= Theme.S(16);
                    }
                    if (row.Lead != null)
                    {
                        int d = Theme.S(28);
                        var c = new Rectangle(textRect.X, y + (RH - d) / 2, d, d);
                        if (row.LeadDone)
                        {
                            using (var b = new SolidBrush(Theme.Hex("#1f7a43"))) g.FillEllipse(b, c);
                            using (var pen = new Pen(Color.White, Theme.S(2.2f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                                g.DrawLines(pen, new[] { new PointF(c.X + d * 0.28f, c.Y + d * 0.52f), new PointF(c.X + d * 0.44f, c.Y + d * 0.68f), new PointF(c.X + d * 0.74f, c.Y + d * 0.34f) });
                        }
                        else
                        {
                            using (var pen = new Pen(Theme.Hex("#565d6b"), 1.4f)) g.DrawEllipse(pen, c);
                            TextRenderer.DrawText(g, row.Lead, Theme.Small, c, Theme.TextDim,
                                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                        }
                        textRect.X += d + Theme.S(12); textRect.Width -= d + Theme.S(12);
                    }
                    Color titleColor = row.Dimmed ? Theme.TextDim : Theme.Text;
                    if (string.IsNullOrEmpty(row.Sub) && string.IsNullOrEmpty(row.KindBadge))
                        TextRenderer.DrawText(g, row.Text, Theme.Body, textRect, titleColor,
                            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
                    else
                    {
                        int th = TextRenderer.MeasureText("Ag", Theme.Body).Height;
                        int sh = TextRenderer.MeasureText("Ag", Theme.Small).Height;
                        int top = y + (RH - th - sh - Theme.S(2)) / 2;
                        TextRenderer.DrawText(g, row.Text, Theme.Body, new Rectangle(textRect.X, top, textRect.Width, th), titleColor,
                            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
                        int subX = textRect.X;
                        if (!string.IsNullOrEmpty(row.KindBadge))
                        {
                            var ksz = TextRenderer.MeasureText(g, row.KindBadge, Theme.Small, Size.Empty, TextFormatFlags.NoPadding);
                            int kw = ksz.Width + Theme.S(14), kh = sh + Theme.S(2);
                            var kr = new Rectangle(subX, top + th + Theme.S(1), kw, kh);
                            var kc = row.KindColors ?? Theme.PillGray;
                            Theme.FillRound(g, kc[0], kr, kh / 2f);
                            TextRenderer.DrawText(g, row.KindBadge, Theme.Small, kr, kc[1],
                                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                            subX += kw + Theme.S(6);
                        }
                        if (!string.IsNullOrEmpty(row.Sub) && subX < textRect.Right)
                            TextRenderer.DrawText(g, row.Sub, Theme.Small, new Rectangle(subX, top + th + Theme.S(2), textRect.Right - subX, sh), Theme.TextDim,
                                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
                    }
                }
                if (Reorderable && dragStarted && dropIndex >= 0)
                {
                    int dy = Math.Max(1, Math.Min(Height - 2, dropIndex * RH - scroll.Offset));
                    using (var p = new Pen(Theme.Accent, Theme.S(2))) g.DrawLine(p, pad, dy, Width - pad, dy);
                }
            }
            scroll.Paint(g, Track);
        }
    }

    public class OrderItem
    {
        public string Path, Name;
        public bool Checked = true;
    }

    // Lista "Ordem das fotos": checkbox, número, nome e alça para arrastar.
    public class OrderList : DrawnControl
    {
        public readonly List<OrderItem> Items = new List<OrderItem>();
        public event EventHandler Changed;
        readonly ScrollHelper scroll;
        int dragIndex = -1, dropIndex = -1, mouseDownY;
        bool dragStarted;

        public OrderList() { scroll = new ScrollHelper(this); BackColor = Theme.Panel; }

        int RH { get { return Theme.S(54); } }
        Rectangle Track { get { return new Rectangle(Width - Theme.S(6), Theme.S(4), Theme.S(4), Height - Theme.S(8)); } }

        int Hit(int y) { int i = (y + scroll.Offset) / RH; return i >= 0 && i < Items.Count ? i : -1; }

        void Fire() { Invalidate(); if (Changed != null) Changed(this, EventArgs.Empty); }

        protected override void OnMouseWheel(MouseEventArgs e) { scroll.Wheel(e.Delta); }
        protected override void OnMouseEnter(EventArgs e) { Focus(); base.OnMouseEnter(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (scroll.MouseDown(e, Track)) return;
            int i = Hit(e.Y);
            if (i < 0) return;
            if (e.X < Theme.S(46)) { Items[i].Checked = !Items[i].Checked; Fire(); return; }
            dragIndex = i; dropIndex = i; mouseDownY = e.Y; dragStarted = false;
            Capture = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (scroll.MouseMove(e, Track)) return;
            Cursor = e.X > Width - Theme.S(50) || dragIndex >= 0 ? Cursors.SizeNS : Cursors.Default;
            if (dragIndex < 0) return;
            if (!dragStarted && Math.Abs(e.Y - mouseDownY) < Theme.S(4)) return;
            dragStarted = true;
            int target = (int)Math.Round((e.Y + scroll.Offset) / (float)RH);
            dropIndex = Math.Max(0, Math.Min(Items.Count, target));
            if (e.Y < Theme.S(20)) scroll.Wheel(60);
            if (e.Y > Height - Theme.S(20)) scroll.Wheel(-60);
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (scroll.MouseUp()) return;
            Capture = false;
            if (dragIndex >= 0 && dragStarted && dropIndex != dragIndex && dropIndex != dragIndex + 1)
            {
                var it = Items[dragIndex];
                Items.RemoveAt(dragIndex);
                int to = dropIndex > dragIndex ? dropIndex - 1 : dropIndex;
                Items.Insert(to, it);
                dragIndex = -1;
                Fire();
            }
            else if (dragIndex >= 0 && !dragStarted)
            {
                Items[dragIndex].Checked = !Items[dragIndex].Checked;
                dragIndex = -1;
                Fire();
            }
            dragIndex = -1; dropIndex = -1;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            Theme.Smooth(g);
            scroll.Viewport = Height; scroll.Content = Items.Count * RH; scroll.Clamp();
            int pad = Theme.S(12), box = Theme.S(22);
            using (var line = new Pen(Theme.Line))
            using (var dot = new SolidBrush(Color.FromArgb(120, 200, 205, 215)))
            {
                int n = 0;
                for (int i = 0; i < Items.Count; i++)
                {
                    var it = Items[i];
                    if (it.Checked) n++;
                    int y = i * RH - scroll.Offset;
                    if (y + RH < 0 || y > Height) continue;
                    if (i == dragIndex && dragStarted) { using (var b = new SolidBrush(Theme.Selected)) g.FillRectangle(b, 0, y, Width, RH); }
                    g.DrawLine(line, 0, y, Width, y);
                    DarkCheck.DrawBox(g, new Rectangle(pad, y + (RH - box) / 2, box, box), it.Checked);
                    TextRenderer.DrawText(g, (i + 1).ToString(), Theme.BodyBold, new Rectangle(pad + box + Theme.S(8), y, Theme.S(30), RH),
                        it.Checked ? Theme.Text : Theme.TextDim, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
                    TextRenderer.DrawText(g, it.Name, Theme.Body, new Rectangle(pad + box + Theme.S(42), y, Width - pad - box - Theme.S(100), RH),
                        it.Checked ? Theme.Text : Theme.TextDim, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                    int hx = Width - Theme.S(30), hy = y + RH / 2 - Theme.S(10);
                    for (int r = 0; r < 4; r++)
                        for (int c = 0; c < 2; c++)
                            g.FillEllipse(dot, hx + c * Theme.S(5), hy + r * Theme.S(6), Theme.S(3), Theme.S(3));
                }
                if (dragStarted && dropIndex >= 0)
                {
                    int y = dropIndex * RH - scroll.Offset;
                    using (var p = new Pen(Theme.Accent, Theme.S(2))) g.DrawLine(p, pad, y, Width - pad, y);
                }
            }
            scroll.Paint(g, Track);
        }
    }

    // Grade de miniaturas (FOTOS DA OS), com bloco opcional "+ Nova Foto".
    public class ThumbGrid : DrawnControl
    {
        public readonly List<string> Paths = new List<string>();
        public bool ShowAddTile, ShowNames = true, Placeholders;
        public int Columns = 4;
        public event EventHandler AddClicked;
        public event EventHandler<string> ItemOpen, ItemRightClick;
        readonly Dictionary<string, Image> cache = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);
        readonly HashSet<string> loading = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly ScrollHelper scroll;
        int hover = -1;

        public ThumbGrid() { scroll = new ScrollHelper(this); BackColor = Theme.Panel; }

        public void SetPaths(IEnumerable<string> paths)
        {
            Paths.Clear(); Paths.AddRange(paths);
            scroll.Offset = 0;
            Invalidate();
        }

        public void ClearCache()
        {
            foreach (var img in cache.Values) if (img != null) img.Dispose();
            cache.Clear();
            Invalidate();
        }

        int Count { get { return Paths.Count + (ShowAddTile ? 1 : 0); } }
        int Gap { get { return Theme.S(12); } }
        int CellW { get { return (Width - Theme.S(10) - Gap * (Columns - 1)) / Columns; } }
        int ImgH { get { return Placeholders ? CellW : (int)(CellW * 0.58f); } }
        int CellH { get { return ImgH + (ShowNames ? Theme.S(30) : Theme.S(12)); } }
        Rectangle Track { get { return new Rectangle(Width - Theme.S(5), 0, Theme.S(4), Height); } }

        Rectangle Cell(int i)
        {
            int c = i % Columns, r = i / Columns;
            return new Rectangle(c * (CellW + Gap), r * CellH - scroll.Offset, CellW, ImgH);
        }

        int Hit(Point p)
        {
            for (int i = 0; i < Count; i++) if (Cell(i).Contains(p)) return i;
            return -1;
        }

        protected override void OnMouseWheel(MouseEventArgs e) { scroll.Wheel(e.Delta); }
        protected override void OnMouseEnter(EventArgs e) { Focus(); base.OnMouseEnter(e); }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (scroll.MouseMove(e, Track)) return;
            int h = Hit(e.Location);
            Cursor = h >= 0 ? Cursors.Hand : Cursors.Default;
            if (h != hover) { hover = h; Invalidate(); }
        }
        protected override void OnMouseUp(MouseEventArgs e) { scroll.MouseUp(); }
        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (scroll.MouseDown(e, Track)) return;
            int i = Hit(e.Location);
            if (i < 0) return;
            if (i >= Paths.Count) { if (AddClicked != null) AddClicked(this, EventArgs.Empty); return; }
            if (e.Button == MouseButtons.Right && ItemRightClick != null) ItemRightClick(this, Paths[i]);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            int i = Hit(e.Location);
            if (i >= 0 && i < Paths.Count && ItemOpen != null) ItemOpen(this, Paths[i]);
        }

        Image Thumb(string path)
        {
            Image img;
            if (cache.TryGetValue(path, out img)) return img;
            if (loading.Contains(path)) return null;
            loading.Add(path);
            int maxW = Math.Max(Theme.S(200), CellW);
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                Image t = PhotoStore.LoadThumbnail(path, maxW);
                try
                {
                    BeginInvoke(new Action(() => { loading.Remove(path); cache[path] = t; Invalidate(); }));
                }
                catch { if (t != null) t.Dispose(); }
            });
            return null;
        }

        static void DrawPlaceholderIcon(Graphics g, Rectangle r)
        {
            int s = Math.Min(r.Width, r.Height) * 36 / 100;
            var box = new Rectangle(r.X + (r.Width - s) / 2, r.Y + (r.Height - s) / 2, s, s);
            using (var pen = new Pen(Color.FromArgb(185, 185, 185), Math.Max(2f, s / 14f)) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                using (var p = Theme.Round(box, s / 8f)) g.DrawPath(pen, p);
                g.DrawEllipse(pen, box.X + s * 0.22f, box.Y + s * 0.2f, s * 0.16f, s * 0.16f);
                g.DrawLines(pen, new[] {
                    new PointF(box.X + s * 0.1f, box.Bottom - s * 0.12f),
                    new PointF(box.X + s * 0.62f, box.Y + s * 0.45f),
                    new PointF(box.Right - s * 0.04f, box.Bottom - s * 0.16f) });
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            Theme.Smooth(g);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            int rows = (Count + Columns - 1) / Columns;
            scroll.Viewport = Height; scroll.Content = rows * CellH; scroll.Clamp();
            for (int i = 0; i < Count; i++)
            {
                var r = Cell(i);
                if (r.Bottom < 0 || r.Y > Height) continue;
                if (i >= Paths.Count)
                {
                    using (var pen = new Pen(Theme.Accent, Theme.S(1.6f)) { DashPattern = new float[] { 5, 4 } })
                        g.DrawRectangle(pen, r.X + 1, r.Y + 1, r.Width - 3, r.Height - 3);
                    TextRenderer.DrawText(g, "+  Nova Foto", Theme.BodyBold, r, Theme.Accent,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    continue;
                }
                string path = Paths[i];
                if (Placeholders) Theme.FillRound(g, Color.White, r, Theme.S(8));
                else { using (var b = new SolidBrush(Theme.Input)) g.FillRectangle(b, r); }
                var img = Thumb(path);
                if (img != null)
                {
                    var inner = Rectangle.Inflate(r, -Theme.S(4), -Theme.S(4));
                    float k = Math.Min(inner.Width / (float)img.Width, inner.Height / (float)img.Height);
                    int w = (int)(img.Width * k), h = (int)(img.Height * k);
                    g.DrawImage(img, inner.X + (inner.Width - w) / 2, inner.Y + (inner.Height - h) / 2, w, h);
                }
                else if (Placeholders) DrawPlaceholderIcon(g, r);
                else TextRenderer.DrawText(g, PhotoStore.Label(path), Theme.Body, r, Theme.TextDim,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                if (i == hover) Theme.DrawRound(g, Theme.Accent, r, Placeholders ? Theme.S(8) : 0, 2);
                if (ShowNames)
                    TextRenderer.DrawText(g, System.IO.Path.GetFileName(path), Theme.Body,
                        new Rectangle(r.X, r.Bottom + Theme.S(4), r.Width + Gap, Theme.S(24)), Theme.Text,
                        TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            }
            scroll.Paint(g, Track);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) ClearCache();
            base.Dispose(disposing);
        }
    }

    public static class UI
    {
        public static Label Label(string text, Font font = null, Color? color = null, Color? back = null)
        {
            return new Label
            {
                Text = text,
                Font = font ?? Theme.Label,
                ForeColor = color ?? Theme.TextDim,
                BackColor = back ?? Theme.Panel,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                UseMnemonic = false,
                AutoEllipsis = true,
            };
        }

        public static ContextMenuStrip Menu(params Tuple<string, Action>[] items)
        {
            var m = new ContextMenuStrip { Renderer = new DarkMenuRenderer(), ShowImageMargin = false, Font = Theme.Body };
            foreach (var it in items)
            {
                if (it == null) { m.Items.Add(new ToolStripSeparator()); continue; }
                var a = it.Item2;
                m.Items.Add(new ToolStripMenuItem(it.Item1, null, (s, e) => a()) { ForeColor = Theme.Text });
            }
            DisposeWhenClosed(m);
            return m;
        }

        // Descarta o menu só depois que o WinForms terminar de fechá-lo: descartar dentro do Closed
        // causa ObjectDisposedException no clique do item (e a opção escolhida não é aplicada).
        public static void DisposeWhenClosed(ContextMenuStrip menu)
        {
            menu.Closed += (s, e) =>
            {
                if (menu.IsDisposed || !menu.IsHandleCreated) return;
                try { menu.BeginInvoke(new Action(() => { if (!menu.IsDisposed) menu.Dispose(); })); } catch (InvalidOperationException) { }
            };
        }

        public static Tuple<string, Action> Item(string text, Action a) { return Tuple.Create(text, a); }
    }
}
