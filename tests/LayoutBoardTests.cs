using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NoteView
{
    public static class LayoutBoardTests
    {
        private static int checks;
        private static object Field(object o, string name) { return o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(o); }
        private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
        [STAThread] public static int Main()
        {
            try
            {
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                var main = new MainWindow(true);
                main.Measure(new Size(1120, 780)); main.Arrange(new Rect(0, 0, 1120, 780)); main.UpdateLayout();
                var board = (LayoutBoard)Field(main, "layoutBoard");
                main.SetScoreEditing(true);
                var boxes = (Border[])Field(board, "boxes");
                Check(boxes.All(b => b.Visibility == Visibility.Visible), "All three edit boxes visible");
                Check((string)((Button)Field(main, "adjustScoreButton")).Content == "\u5b8c\u6210\u8c03\u6574", "Whole-layout button");
                for (int i = 0; i < 3; i++)
                {
                    Rect[] before = Enumerable.Range(0, 3).Select(board.Bounds).ToArray();
                    var panel = (Grid)boxes[i].Child;
                    var resize = (Thumb)panel.Children[2];
                    resize.RaiseEvent(new DragDeltaEventArgs(-before[i].Width * .3, -before[i].Height * .3) { RoutedEvent = Thumb.DragDeltaEvent });
                    Check(board.Bounds(i).Width < before[i].Width, "Actual resize handle works " + i);
                    var drag = (Thumb)panel.Children[0];
                    double dy = i == 0 ? 90 : -250;
                    Rect shrunk = board.Bounds(i);
                    drag.RaiseEvent(new DragDeltaEventArgs(70, dy) { RoutedEvent = Thumb.DragDeltaEvent });
                    Check(board.Bounds(i).Y != shrunk.Y, "Actual drag handle moves across rows " + i);
                    for (int other = 0; other < 3; other++) if (other != i) Check(before[other] == board.Bounds(other), "Other components stay fixed");
                }
                var settings = main.Settings;
                var state = new NoteState(); state.Process(0x90, 60, 110); state.Process(0x90, 64, 75); state.Process(0x90, 67, 95);
                var renderer = new ObsFrameRenderer();
                byte[] frame = renderer.Render(settings.Snapshot(), state.GetActiveNotes(), "C", "C major", "", false);
                var obs = (LayoutBoard)Field(renderer, "board");
                for (int i = 0; i < 3; i++) Check(board.Bounds(i) == obs.Bounds(i), "OBS uses desktop bounds " + i);
                File.WriteAllBytes("artifacts/layout-obs.png", frame);
                board.Measure(new Size(1120, 640)); board.Arrange(new Rect(0, 0, 1120, 640)); board.UpdateLayout();
                var bitmap = new RenderTargetBitmap(1120, 640, 96, 96, PixelFormats.Pbgra32); bitmap.Render(board);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var output = File.Create("artifacts/layout-editor.png")) encoder.Save(output);
                for (int i = 0; i < 3; i++)
                {
                    board.Move(i, -5000, 5000); Rect r = board.Bounds(i);
                    Check(r.X >= 0 && r.Bottom <= 640, "Canvas boundary clamp");
                    board.Resize(i, 5000); Check(board.Bounds(i).Right <= 1120 && board.Bounds(i).Bottom <= 640, "Resize boundary clamp");
                }
                main.SetScoreEditing(false);
                Check(boxes.All(b => b.Visibility == Visibility.Collapsed), "Clean performance mode");
                Console.WriteLine("PASS: " + checks + " independent layout and OBS checks");
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        }
    }
}
