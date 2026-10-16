using System;
using System.Drawing;
using System.Windows.Forms;

namespace Diagnostico
{
    // Logo do programa, desenhado em código (sem arquivos de imagem): ícone de notebook na cor de destaque + nome.
    static class AppLogo
    {
        public const string Name = "Diagnóstico\nTécnico";

        // Ícone + nome em duas linhas, centralizados na altura de bounds.
        public static void Draw(Graphics g, Rectangle bounds)
        {
            int icon = Math.Min(bounds.Height, Theme.S(40));
            DrawIcon(g, new Rectangle(bounds.X, bounds.Y + (bounds.Height - icon) / 2, icon, icon), Theme.Accent);

            int x = bounds.X + icon + Theme.S(12), w = Math.Max(1, bounds.Right - x);
            var flags = TextFormatFlags.WordBreak | TextFormatFlags.NoPadding;
            var size = TextRenderer.MeasureText(g, Name, Theme.Heading, new Size(w, 0), flags);
            TextRenderer.DrawText(g, Name, Theme.Heading, new Rectangle(x, bounds.Y + (bounds.Height - size.Height) / 2, w, size.Height), Theme.Text, flags);
        }

        // Versão do menu recolhido: o ícone dentro de um quadrado arredondado na cor de destaque.
        public static void DrawBadge(Graphics g, Rectangle bounds)
        {
            Theme.FillRound(g, Theme.Accent, bounds, Theme.S(14));
            int pad = bounds.Width / 5;
            DrawIcon(g, Rectangle.Inflate(bounds, -pad, -pad), Theme.AccentText);
        }

        static void DrawIcon(Graphics g, Rectangle r, Color color)
        {
            var tint = Icons.Tint;
            Icons.Tint = color;
            Icons.Laptop(g, r);
            Icons.Tint = tint;
        }
    }
}
