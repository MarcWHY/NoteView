using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace NoteView
{
    public static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            try
            {
                bool preview = args.Contains("--preview");
                var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
                app.DispatcherUnhandledException += delegate(object sender, DispatcherUnhandledExceptionEventArgs e)
                {
                    Directory.CreateDirectory(AppSettings.DirectoryPath);
                    File.AppendAllText(Path.Combine(AppSettings.DirectoryPath, "error.log"), DateTime.Now + " " + e.Exception + Environment.NewLine);
                    MessageBox.Show("NoteView 遇到错误，详情已写入 %APPDATA%\\NoteView\\error.log。\n" + e.Exception.Message, "NoteView");
                    e.Handled = true;
                    app.Shutdown(1);
                };
                var window = new MainWindow(preview);
                if (preview)
                {
                    window.ShowInTaskbar = false;
                    window.ShowActivated = false;
                    window.Left = -15000;
                    window.Top = -15000;
                    window.Loaded += delegate
                    {
                        window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(delegate
                        {
                            string output = args.Length > 1 ? Path.GetFullPath(args[1]) : Path.GetFullPath("preview.png");
                            window.SetPreviewChord();
                            window.UpdateLayout();
                            SavePreview(window, output);
                            window.SetPreviewTransparent();
                            window.UpdateLayout();
                            SavePreview(window, Path.Combine(Path.GetDirectoryName(output), "preview-transparent.png"));
                            window.Close();
                        }));
                    };
                }
                return app.Run(window);
            }
            catch (Exception ex)
            {
                Directory.CreateDirectory(AppSettings.DirectoryPath);
                File.AppendAllText(Path.Combine(AppSettings.DirectoryPath, "error.log"), ex + Environment.NewLine);
                if (!args.Contains("--preview")) MessageBox.Show(ex.Message, "NoteView 启动失败");
                return 1;
            }
        }
        private static void SavePreview(Window window, string output)
        {
            var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(window);
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(output)) encoder.Save(stream);
        }
    }
}
