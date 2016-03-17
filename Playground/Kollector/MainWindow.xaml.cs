using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Gma.System.MouseKeyHook;
using MouseEventArgs = System.Windows.Forms.MouseEventArgs;
using PixelFormat = System.Drawing.Imaging.PixelFormat;
using Point = System.Windows.Point;

namespace Kollector
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private IKeyboardMouseEvents _globalHook;

        //[System.Runtime.InteropServices.DllImport("gdi32.dll")]
        //public static extern bool DeleteObject(IntPtr hObject);

        public MainWindow()
        {
            InitializeComponent();
        }

        private void Setup()
        {
            BackgroundBrush.Opacity = 0;

            _globalHook = Hook.GlobalEvents();
            _globalHook.MouseDown += _globalHook_GlobalHookOnMouseDown;
            _globalHook.MouseUp += _globalHook_MouseUp;
            _globalHook.KeyPress += GlobalHookOnKeyPress;
            _globalHook.MouseMove += _globalHook_MouseMove;

            _xRatio = MainCanvas.ActualWidth / SystemParameters.MaximizedPrimaryScreenWidth;
            _yRatio = MainCanvas.ActualHeight / SystemParameters.MaximizedPrimaryScreenHeight;
        }

        private void _globalHook_MouseUp(object sender, MouseEventArgs e)
        {
            MainCanvas.Children.Clear();
            _drawing = false;
            BackgroundBrush.Opacity = 0;
        }

        private void _globalHook_MouseMove(object sender, MouseEventArgs e)
        {
            if (_drawing)
            {
                _lastFigure.Segments.Add(new LineSegment { Point = new Point(e.X * _xRatio, e.Y * _yRatio) });
            }
        }

        private bool _drawing = false;
        private double _xRatio = 0.0;
        private double _yRatio = 0.0;
        private Path _lastPath = new Path();
        private PathFigure _lastFigure = new PathFigure();
        private void _globalHook_GlobalHookOnMouseDown(object sender, MouseEventArgs e)
        {
            if (_drawing)
            {
                var point = new Point(e.X * _xRatio, e.Y * _yRatio);
                _lastPath = new Path { Stroke = new SolidColorBrush(Colors.Red), StrokeThickness = 10 };
                _lastFigure = new PathFigure { StartPoint = point };
                _lastPath.Data = new PathGeometry(new List<PathFigure> { _lastFigure });
                _lastFigure.Segments.Add(new LineSegment { Point = point });
                MainCanvas.Children.Add(_lastPath);

                //var circle = new Ellipse { Width = 20, Height = 20, Fill = new SolidColorBrush(Colors.Red) };
                //Canvas.SetLeft(circle, point.X);
                //Canvas.SetTop(circle, point.Y);
                //MainCanvas.Children.Add(circle);
            }
        }

        private void FillWithScreenshot()
        {
            Bitmap screenshotBmp = new Bitmap((int)SystemParameters.VirtualScreenWidth, (int)SystemParameters.VirtualScreenHeight, PixelFormat.Format32bppArgb);

            using (var g = Graphics.FromImage(screenshotBmp))
            {
                g.CopyFromScreen(0,0,0,0, screenshotBmp.Size);
            }

            IntPtr handle = IntPtr.Zero;
            try
            {
                handle = screenshotBmp.GetHbitmap();

                var imageBrush = new ImageBrush();
                imageBrush.ImageSource = Imaging.CreateBitmapSourceFromHBitmap(handle, IntPtr.Zero, Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                imageBrush.Stretch = Stretch.UniformToFill;
                MainCanvas.Background = imageBrush;
            }
            finally
            {
                //DeleteObject(handle);
            }
        }

        private void GlobalHookOnKeyPress(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar == '`')
            {
                _drawing = true;
                BackgroundBrush.Opacity = 0.2;
            }
        }

        private void MainWindow_OnDeactivated(object sender, EventArgs e)
        {
            Topmost = true;
        }

        private void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
        {
            Setup();
        }
    }
}
