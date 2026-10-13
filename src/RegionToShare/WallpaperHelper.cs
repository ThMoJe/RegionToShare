using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using static RegionToShare.NativeMethods;

namespace RegionToShare;

/// <summary>
/// Renders the current desktop wallpaper (without icons) for a given screen, so it can be shown
/// by the separation layer in place of the noise background while sharing the live desktop.
/// </summary>
internal static class WallpaperHelper
{
    private const uint SPI_GETDESKWALLPAPER = 0x0073;
    private const int COLOR_DESKTOP = 1;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, StringBuilder pvParam, uint fWinIni);

    [DllImport("user32.dll")]
    private static extern int GetSysColor(int nIndex);

    /// <summary>
    /// Renders the wallpaper as it appears on a screen of the given size (in physical pixels).
    /// Returns a frozen bitmap, or null if rendering failed.
    /// </summary>
    public static BitmapSource? Render(int width, int height)
    {
        if (width <= 0 || height <= 0)
            return null;

        try
        {
            var sysColor = GetSysColor(COLOR_DESKTOP);
            var background = Color.FromArgb(sysColor & 0xFF, (sysColor >> 8) & 0xFF, (sysColor >> 16) & 0xFF);

            using var canvas = new Bitmap(width, height, PixelFormat.Format32bppRgb);
            using var graphics = Graphics.FromImage(canvas);

            graphics.Clear(background);
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;

            DrawWallpaper(graphics, width, height);

            var hBitmap = canvas.GetHbitmap();
            try
            {
                var source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(hBitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            finally
            {
                DeleteObject(hBitmap);
            }
        }
        catch
        {
            return null;
        }
    }

    private static void DrawWallpaper(Graphics graphics, int width, int height)
    {
        var path = new StringBuilder(520);
        if (!SystemParametersInfo(SPI_GETDESKWALLPAPER, (uint)path.Capacity, path, 0) || path.Length == 0)
            return; // solid color desktop

        var file = path.ToString();
        if (!System.IO.File.Exists(file))
            return;

        GetWallpaperStyle(out var style, out var tile);

        using var image = LoadWithoutLock(file);

        var iw = image.Width;
        var ih = image.Height;

        if (tile)
        {
            for (var y = 0; y < height; y += ih)
            {
                for (var x = 0; x < width; x += iw)
                {
                    graphics.DrawImage(image, x, y, iw, ih);
                }
            }

            return;
        }

        switch (style)
        {
            case 0: // Center
                graphics.DrawImage(image, (width - iw) / 2, (height - ih) / 2, iw, ih);
                break;
            case 2: // Stretch
                graphics.DrawImage(image, 0, 0, width, height);
                break;
            case 6: // Fit
                {
                    var scale = Math.Min((double)width / iw, (double)height / ih);
                    var w = (int)Math.Round(iw * scale);
                    var h = (int)Math.Round(ih * scale);
                    graphics.DrawImage(image, (width - w) / 2, (height - h) / 2, w, h);
                    break;
                }
            default: // Fill (10), Span (22) and unknown styles
                {
                    var scale = Math.Max((double)width / iw, (double)height / ih);
                    var w = (int)Math.Round(iw * scale);
                    var h = (int)Math.Round(ih * scale);
                    graphics.DrawImage(image, (width - w) / 2, (height - h) / 2, w, h);
                    break;
                }
        }
    }

    private static Image LoadWithoutLock(string file)
    {
        // Copy into memory so the wallpaper file is not locked.
        using var stream = new System.IO.FileStream(file, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite);
        using var original = Image.FromStream(stream);
        return new Bitmap(original);
    }

    private static void GetWallpaperStyle(out int style, out bool tile)
    {
        style = 10;
        tile = false;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
            if (key == null)
                return;

            if (int.TryParse(key.GetValue("WallpaperStyle") as string, out var parsedStyle))
                style = parsedStyle;

            tile = (key.GetValue("TileWallpaper") as string) == "1";
        }
        catch
        {
            // Use defaults.
        }
    }
}
