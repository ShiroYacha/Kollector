using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using FontAwesome.WPF;
using Gma.System.MouseKeyHook;
using Brushes = System.Windows.Media.Brushes;
using MouseEventArgs = System.Windows.Forms.MouseEventArgs;
using Orientation = System.Windows.Controls.Orientation;
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
        private const double SELECTION_BACKGROUND_OPACITY = 0.7;
        private const double POST_SELECTION_BACKGROUND_OPACITY = 0.8;
        private IKeyboardMouseEvents _globalHook;

        private Mode _mode = Mode.Rectangle;
        private bool _drawing = false;
        private bool _start = false;
        private double _xRatio = 0.0;
        private double _yRatio = 0.0;
        private Point _startPoint;
        private Path _selectionForegroundPath;
        private Path _selectionBackgroundPath;
        private PathFigure _lassoSelectionForegroundGeometry;
        private RectangleGeometry _rectSelectionForegroundGeometry;
        private CombinedGeometry _selectionBackgroundGeometry;
        private bool _reseted = true;

        public MainWindow()
        {
            InitializeComponent();
        }

        private void Setup()
        {
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
            if (_drawing && _start)
            {
                _drawing = false;
                _start = false;
                _reseted = false;

                _selectionBackgroundPath.Opacity = POST_SELECTION_BACKGROUND_OPACITY;
                _selectionBackgroundPath.Fill = Brushes.Black;

                SetupNotebookIcons();
            }

        }

        private void SetupNotebookIcons()
        {
            // get right most position 
            var bounds = _selectionForegroundPath.Data.Bounds;
            var offsetHorizontal= 50;
            var offsetVertical = 65;
            // setup the 3 notebooks
            SetupNotebookIcon(FontAwesomeIcon.Book, "Technology", Brushes.Fuchsia, bounds.TopRight.X+ offsetHorizontal, bounds.TopRight.Y);
            SetupNotebookIcon(FontAwesomeIcon.Book, "Personal finance", Brushes.GreenYellow, bounds.TopRight.X + offsetHorizontal, bounds.TopRight.Y + offsetVertical);
            SetupNotebookIcon(FontAwesomeIcon.Book, "Project FinTech", Brushes.Crimson, bounds.TopRight.X + offsetHorizontal, bounds.TopRight.Y + offsetVertical*2);
        }

        private void _globalHook_MouseMove(object sender, MouseEventArgs e)
        {
            if (_drawing)
            {
                var point = new Point(e.X*_xRatio, e.Y*_yRatio);
                if (_mode == Mode.Lasso)
                {
                    _lassoSelectionForegroundGeometry.Segments.RemoveAt(_lassoSelectionForegroundGeometry.Segments.Count - 1);
                    _lassoSelectionForegroundGeometry.Segments.Add(new LineSegment {Point = point });
                    _lassoSelectionForegroundGeometry.Segments.Add(new LineSegment {Point = _startPoint });
                }
                else if(_mode == Mode.Rectangle)
                {
                    var width = Math.Abs(point.X - _startPoint.X);
                    var height = Math.Abs(point.Y - _startPoint.Y);
                    _rectSelectionForegroundGeometry.Rect = new Rect(_startPoint, new System.Windows.Size(width, height));
                }
            }
        }

        private void MainCanvas_OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!_drawing && !_start)
            {
                MainCanvas.Children.Clear();
                BackgroundBrush.Opacity = 0;
                _reseted = true;
            }
        }

        private void _globalHook_GlobalHookOnMouseDown(object sender, MouseEventArgs e)
        {
            if (_start)
            {
                BackgroundBrush.Opacity = 0;

                var point = new Point(e.X*_xRatio, e.Y*_yRatio);
                _startPoint = point;
                _drawing = true;

                SetupSelectionGeometry();
            }
        }

        private void SetupNotebookIcon(FontAwesomeIcon icon, string title, System.Windows.Media.Brush brush, double X, double Y)
        {
            // container
            var container = new StackPanel {Orientation = Orientation.Horizontal};

            // icon
            var iconBrush = new ImageBrush();
            var iconSource = ImageAwesome.CreateImageSource(icon, brush);
            iconBrush.ImageSource = iconSource;
            var notebookRectangle = new Rectangle
            {
                Fill = iconBrush,
                Width = 25,
                Height = 25,
                VerticalAlignment = VerticalAlignment.Center
            };
            container.Children.Add(notebookRectangle);

            // text
            var textBlock = new TextBlock
            {
                Text = title,
                Foreground = brush,
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI Light"),
                FontSize = 18,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10,0,0,0)
            };
            container.Children.Add(textBlock);

            // setup and run 
            Canvas.SetLeft(container, X);
            Canvas.SetTop(container, Y);
            MainCanvas.Children.Add(container);
            container.MouseDown += (sender, args) =>
            {
                args.Handled = true;
                var bounceAnimation = new DoubleAnimation
                {
                    From = 1.0,
                    To = 1.2,
                    Duration = new Duration(TimeSpan.FromSeconds(0.25)),
                    EasingFunction = new BackEase()
                    {
                        EasingMode = EasingMode.EaseIn, 
                    },
                    AutoReverse = true
                };
                bounceAnimation.Completed += (o, eventArgs) =>
                {
                    Task.Run(async () =>
                    {
                        await Task.Delay(200);
                        await Dispatcher.InvokeAsync(Reset);
                    });
                };
                var scaleTransform = new ScaleTransform() { ScaleX = 1.0, ScaleY = 1.0 };
                container.RenderTransform = scaleTransform;
                container.RenderTransformOrigin = new Point(0.5,0.5);
                scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, bounceAnimation);
                scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, bounceAnimation);
            };
            container.BeginAnimation(OpacityProperty, new DoubleAnimation
            {
                From = 0.0,
                To = 1.0,
                Duration = new Duration(TimeSpan.FromSeconds(0.5)),
                EasingFunction = new SineEase()
                {
                    EasingMode = EasingMode.EaseIn,
                }
            });
        }

        private void SetupSelectionGeometry()
        {
            _selectionForegroundPath = new Path { Stroke = new SolidColorBrush(Colors.Red), StrokeThickness = 5 };
            _selectionBackgroundPath = new Path { Fill = Brushes.White, Opacity = SELECTION_BACKGROUND_OPACITY };

            if (_mode == Mode.Lasso)
            {
                _lassoSelectionForegroundGeometry = new PathFigure { StartPoint = _startPoint };
                _selectionForegroundPath.Data = new PathGeometry(new List<PathFigure> { _lassoSelectionForegroundGeometry });
                _lassoSelectionForegroundGeometry.Segments.Add(new LineSegment { Point = _startPoint });

                _selectionBackgroundGeometry = new CombinedGeometry
                {
                    GeometryCombineMode = GeometryCombineMode.Xor,
                    Geometry1 = new RectangleGeometry(new Rect(0, 0, MainCanvas.ActualWidth, MainCanvas.ActualHeight)),
                    Geometry2 = _selectionForegroundPath.Data
                };
                _selectionBackgroundPath.Data = _selectionBackgroundGeometry;
            }
            else if (_mode == Mode.Rectangle)
            {
                _rectSelectionForegroundGeometry = new RectangleGeometry(new Rect(this._startPoint, new System.Windows.Size(0, 0)));
                _selectionForegroundPath.Data = _rectSelectionForegroundGeometry;

                _selectionBackgroundGeometry = new CombinedGeometry
                {
                    GeometryCombineMode = GeometryCombineMode.Xor,
                    Geometry1 = new RectangleGeometry(new Rect(0, 0, MainCanvas.ActualWidth, MainCanvas.ActualHeight)),
                    Geometry2 = _selectionForegroundPath.Data
                };
                _selectionBackgroundPath.Data = _selectionBackgroundGeometry;
            }


            MainCanvas.Children.Add(_selectionForegroundPath);
            MainCanvas.Children.Add(_selectionBackgroundPath);
        }

        private void Reset()
        {
            if (!_reseted)
            {
                BackgroundBrush.Opacity = 0;
                MainCanvas.Children.Clear();
                _drawing = false;
                _start = false;
                _reseted = true;
            }
        }

        private void GlobalHookOnKeyPress(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar == '\u001b')
            {
                // escape key => reset
                Reset();
                return;
            }

            if (_reseted)
            {
                if (e.KeyChar == '1')
                {
                    StartScreenClipping();
                    _mode = Mode.Rectangle;
                }
                else if (e.KeyChar == '2')
                {
                    StartScreenClipping();
                    _mode = Mode.Lasso;
                }
            }
        }

        private void StartScreenClipping()
        {
            BackgroundBrush.Opacity = SELECTION_BACKGROUND_OPACITY;
            _start = true;
            _reseted = false;
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
