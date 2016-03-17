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
using Rectangle = System.Windows.Shapes.Rectangle;

namespace Kollector
{
    public enum Mode
    {
        Rectangle,
        Lasso
    }

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private IKeyboardMouseEvents _globalHook;

        private Mode _mode = Mode.Rectangle;
        private bool _drawing = false;
        private bool _start = false;
        private double _xRatio = 0.0;
        private double _yRatio = 0.0;
        private Path _lastPath = new Path();
        private PathFigure _lastFigure = new PathFigure();
        private Point _startPoint;

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
            _start = false;
            BackgroundBrush.Opacity = 0;
        }

        private void _globalHook_MouseMove(object sender, MouseEventArgs e)
        {
            if (_drawing)
            {
                var point = new Point(e.X*_xRatio, e.Y*_yRatio);
                if (_mode == Mode.Lasso)
                {
                    _lastFigure.Segments.RemoveAt(_lastFigure.Segments.Count - 1);
                    _lastFigure.Segments.Add(new LineSegment {Point = point });
                    _lastFigure.Segments.Add(new LineSegment {Point = _startPoint });
                }
                else if(_mode == Mode.Rectangle)
                {
                    var rectangle = (System.Windows.Shapes.Rectangle)MainCanvas.Children[0];
                    var width = Math.Abs(point.X - _startPoint.X);
                    var height = Math.Abs(point.Y - _startPoint.Y);
                    rectangle.Width = width;
                    rectangle.Height = height;
                }
            }
        }

        private void _globalHook_GlobalHookOnMouseDown(object sender, MouseEventArgs e)
        {
            if (_start)
            {
                var point = new Point(e.X * _xRatio, e.Y * _yRatio);
                _startPoint = point;
                _drawing = true;

                if (_mode == Mode.Lasso)
                {
                    _lastPath = new Path {Stroke = new SolidColorBrush(Colors.Red), StrokeThickness = 5};
                    _lastFigure = new PathFigure {StartPoint = point};
                    _lastPath.Data = new PathGeometry(new List<PathFigure> {_lastFigure});
                    _lastFigure.Segments.Add(new LineSegment {Point = point});
                    MainCanvas.Children.Add(_lastPath);
                }
                else if(_mode == Mode.Rectangle)
                {

                    var rectangle = new System.Windows.Shapes.Rectangle
                    {
                        Stroke = new SolidColorBrush(Colors.Red),
                        StrokeThickness = 5,
                        //Fill = new SolidColorBrush(Colors.Black),
                        Width =0,
                        Height = 0
                    };
                    Canvas.SetLeft(rectangle, _startPoint.X);
                    Canvas.SetTop(rectangle, _startPoint.Y);
                    MainCanvas.Children.Add(rectangle);
                }

            }
        }

        private void GlobalHookOnKeyPress(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar == '1')
            {
                _start = true;
                BackgroundBrush.Opacity = 0.2;
                _mode = Mode.Rectangle;
            }
            else if (e.KeyChar == '2')
            {
                _start = true;
                BackgroundBrush.Opacity = 0.2;
                _mode = Mode.Lasso;
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

        #region Screenshoots
        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr hObject);

        private void FillWithScreenshot()
        {
            Bitmap screenshotBmp = new Bitmap((int)SystemParameters.VirtualScreenWidth, (int)SystemParameters.VirtualScreenHeight, PixelFormat.Format32bppArgb);

            using (var g = Graphics.FromImage(screenshotBmp))
            {
                g.CopyFromScreen(0, 0, 0, 0, screenshotBmp.Size);
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
        #endregion
    }
}
