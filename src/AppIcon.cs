using System;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Drawing = System.Drawing;

namespace NoteView
{
    internal static class AppIcon
    {
        private static string AssetPath(string name)
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "branding", name);
        }

        internal static ImageSource LoadWindowIcon()
        {
            // OnLoad releases the file immediately and preserves the ICO decoder's
            // multiple sizes for WPF's taskbar and Alt+Tab icon selection.
            ImageSource image = TryLoadImage(AssetPath("noteview.ico"))
                ?? TryLoadImage(AssetPath("noteview-256.png"));
            if (image != null) return image;
            try
            {
                using (Drawing.Icon icon = LoadTrayIcon())
                {
                    BitmapSource bitmap = Imaging.CreateBitmapSourceFromHIcon(icon.Handle,
                        Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    bitmap.Freeze();
                    return bitmap;
                }
            }
            catch { return null; } // Branding must never prevent the window opening.
        }

        private static ImageSource TryLoadImage(string path)
        {
            try
            {
                using (FileStream stream = File.OpenRead(path))
                {
                    BitmapFrame image = BitmapFrame.Create(stream,
                        BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    image.Freeze();
                    return image;
                }
            }
            catch { return null; } // Missing or damaged optional assets use the embedded icon.
        }

        // The caller owns the returned Icon and keeps it alive until NotifyIcon is disposed.
        internal static Drawing.Icon LoadTrayIcon()
        {
            try
            {
                Drawing.Size size = System.Windows.Forms.SystemInformation.SmallIconSize;
                using (Drawing.Icon icon = new Drawing.Icon(AssetPath("noteview.ico"),
                    Math.Max(16, size.Width), Math.Max(16, size.Height)))
                    return (Drawing.Icon)icon.Clone();
            }
            catch { }
            try
            {
                Drawing.Icon embedded = Drawing.Icon.ExtractAssociatedIcon(typeof(AppIcon).Assembly.Location);
                if (embedded != null) return embedded;
            }
            catch { }
            // SystemIcons are shared objects; clone before handing ownership to the window.
            return (Drawing.Icon)Drawing.SystemIcons.Application.Clone();
        }
    }
}
