using System.Drawing;
using System.Reflection;

namespace Diagnostico
{
    static class BrandAssets
    {
        static readonly Image logo = Load("Cantao.Logo"), banner = Load("Cantao.Fundo");

        static Image Load(string resource)
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
            {
                if (stream == null) return null;
                using (var source = Image.FromStream(stream)) return new Bitmap(source);
            }
        }

        public static void Logo(Graphics g, Rectangle bounds)
        {
            Theme.FillRound(g, Color.White, bounds, Theme.S(12));
            if (logo == null) return;
            var area = Rectangle.Inflate(bounds, -Theme.S(10), -Theme.S(10));
            // Recorte apenas na exibição; o arquivo original continua intacto.
            var source = new RectangleF(10, 110, 420, 215);
            float fit = System.Math.Min(area.Width / source.Width, area.Height / source.Height);
            var target = new RectangleF(area.X + (area.Width - source.Width * fit) / 2,
                area.Y + (area.Height - source.Height * fit) / 2, source.Width * fit, source.Height * fit);
            g.DrawImage(logo, target, source, GraphicsUnit.Pixel);
        }

        public static void Banner(Graphics g, Rectangle bounds)
        {
            if (banner == null) return;
            g.DrawImage(banner, bounds);
        }
    }
}
