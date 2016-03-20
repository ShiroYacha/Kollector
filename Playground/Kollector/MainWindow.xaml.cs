using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using FontAwesome.WPF;
using Gma.System.MouseKeyHook;
using LoadingIndicators.WPF;
using Markdown.Xaml;
using Tesseract;
using Application = System.Windows.Application;
using Binding = System.Windows.Data.Binding;
using Brushes = System.Windows.Media.Brushes;
using Cursors = System.Windows.Input.Cursors;
using Image = System.Windows.Controls.Image;
using ListView = System.Windows.Controls.ListView;
using MouseEventArgs = System.Windows.Forms.MouseEventArgs;
using Orientation = System.Windows.Controls.Orientation;
using Path = System.Windows.Shapes.Path;
using PixelFormat = System.Drawing.Imaging.PixelFormat;
using Point = System.Windows.Point;
using Rect = System.Windows.Rect;
using Size = System.Windows.Size;
using TextBox = System.Windows.Controls.TextBox;

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
        private const double POST_SELECTION_BACKGROUND_OPACITY = 0.85;
        private const double FONT_SIZE_NORMAL = 20;
        private const double FONT_SIZE_BIGGER = 25;
        private const double ICON_SIZE_NORMAL = 25;
        private const double ICON_SIZE_BIGGER = 30;
        private const double ICON_REMOVE_SIZE = 15;
        private const double OFFSET_VERTICAL = 60;
        private const double NOTEPAD_MIN_WIDTH = 500;
        private const bool DISMISS_ON_CLICK = false;
        private const int NOTEBOOK_SEARCH_TIME_MS = 2000;
        private const int TAG_EXTRACT_TIME_MS = 1800;
        private const int SCAN_TIME_MS = 1800;

        private IKeyboardMouseEvents _globalHook;

        private Mode _mode = Mode.Rectangle;
        private bool _drawing;
        private bool _start;
        private double _screenWidth;
        private double _screenHeight;
        private double _xRatio;
        private double _yRatio;
        private Point _startPoint;
        private Path _selectionForegroundPath;
        private Path _selectionBackgroundPath;
        private PathFigure _lassoSelectionForegroundGeometry;
        private RectangleGeometry _rectSelectionForegroundGeometry;
        private CombinedGeometry _selectionBackgroundGeometry;
        private bool _reseted = true;
        private Bitmap _fullScreenshotBitmap;
        private StatisticChecker _checker = new StatisticChecker("naive");

        public MainWindow()
        {
            _yRatio = 0.0;
            InitializeComponent();
        }

        #region Initialization
        private void Setup()
        {
            _globalHook = Hook.GlobalEvents();
            _globalHook.MouseDown += _globalHook_GlobalHookOnMouseDown;
            _globalHook.MouseUp += _globalHook_MouseUp;
            _globalHook.KeyUp += _globalHook_KeyUp; ;
            _globalHook.MouseMove += _globalHook_MouseMove;

            //screenWidth = SystemParameters.MaximizedPrimaryScreenWidth > 2000?3000:1920;
            //screenHeight = Math.Abs(screenWidth - 3000) < 0.5?2000:1080;
            _screenWidth = Screen.PrimaryScreen.Bounds.Width;
            _screenHeight = Screen.PrimaryScreen.Bounds.Height;
            
            _xRatio = MainCanvas.ActualWidth/_screenWidth;
            _yRatio = MainCanvas.ActualHeight/_screenHeight;

            SetupNotifyIcon();
        }

        private static void SetupNotifyIcon()
        {
            var notifyIcon = new System.Windows.Forms.NotifyIcon { Icon = new Icon("Icon.ico"), Visible = true };
            notifyIcon.ShowBalloonTip(1000, "Kollector", "Kollector is up and running", System.Windows.Forms.ToolTipIcon.Info);
            notifyIcon.ContextMenu = new System.Windows.Forms.ContextMenu(new System.Windows.Forms.MenuItem[]
            {
                new System.Windows.Forms.MenuItem("Close", (sender, args) =>
                {
                    Application.Current.Shutdown();
                    notifyIcon.Dispose();
                })
            });
        }

        private void MainWindow_OnDeactivated(object sender, EventArgs e)
        {
            Topmost = true;
        }

        private void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
        {
            Setup();
        }

        #endregion

        #region Scan

        private void Scan()
        {
            var scanTime = DateTime.Now;
            var bounds = _selectionForegroundPath.Data.Bounds;
            var targetBounds = new Rectangle((int)bounds.X, (int)bounds.Y, (int)bounds.Width, (int)bounds.Height);
            GraphicsPath graphicsPath;
            if (_lassoSelectionForegroundGeometry != null)
            {
                graphicsPath = new GraphicsPath();
                graphicsPath.AddLines(
                    _lassoSelectionForegroundGeometry.Segments.Cast<LineSegment>()
                        .Select(s => new PointF((float)(s.Point.X - bounds.X), (float)(s.Point.Y - bounds.Y)))
                        .Where(p => p.X > 0 && p.Y > 0 && p.X < bounds.Width && p.Y < bounds.Height)
                        .ToArray());
            }
            else
            {
                graphicsPath = new GraphicsPath();
                graphicsPath.AddRectangle(new RectangleF(0, 0, (float)bounds.Width, (float)bounds.Height));
            }
            var croppedScreenshot = CropBitmap(_fullScreenshotBitmap, targetBounds, graphicsPath);

            // test show cropped bitmap
            //ShowCroppedScreenshot(croppedScreenshot);

            // OCR
            var converter = new BitmapToPixConverter();
            var target = converter.Convert(croppedScreenshot);
            Task.Run(() =>
            {
                if (_reseted) return;

                // start watch
                var watch = new Stopwatch();
                watch.Start();

                // OCR
                double confidence;
                var result = Ocr(target, out confidence);
                if (_reseted) return;

                // stop watch 
                watch.Stop();

                // log and display
                var cleanText = string.Join(Environment.NewLine, result.Replace(Environment.NewLine, "").Replace("\t", "").Split(new string[] { "\n" }, StringSplitOptions.RemoveEmptyEntries));
                cleanText += Environment.NewLine;
                Dispatcher.Invoke(() =>
                {
                    // log
                    _checker.Log(confidence, cleanText, croppedScreenshot, watch.ElapsedMilliseconds, scanTime);

                    // display 
                    PrintScanTextOnScreen(cleanText);
                });
            });
        }

        private void ShowCroppedScreenshot(Bitmap croppedScreenshot)
        {
            var handle = croppedScreenshot.GetHbitmap();
            var imageBrush = new ImageBrush
            {
                ImageSource = Imaging.CreateBitmapSourceFromHBitmap(handle, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()),
                Stretch = Stretch.None
            };
            Grid test = new Grid();
            test.Background = imageBrush;
            test.Width = croppedScreenshot.Width;
            test.Height = croppedScreenshot.Height;
            Canvas.SetTop(test, 0);
            Canvas.SetLeft(test, 0);
            MainCanvas.Children.Add(test);
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        private void PrintScanTextOnScreen(string text)
        {
            var bounds = _selectionForegroundPath.Data.Bounds;
            var textBlock = new TextBlock
            {
                Text = text,
                Background = Brushes.Transparent,
                Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap,
                FontSize = FONT_SIZE_NORMAL
            };
            textBlock.Measure(new Size(bounds.Width, double.PositiveInfinity));
            Canvas.SetLeft(textBlock, bounds.TopLeft.X);
            MainCanvas.Children.Add(textBlock);
            UpdateLayout();
            Canvas.SetTop(textBlock, bounds.TopLeft.Y - textBlock.ActualHeight);
        }

        private Bitmap CropBitmap(Bitmap source, Rectangle bounds, GraphicsPath graphicsPath)
        {
            Bitmap target = new Bitmap(bounds.Width, bounds.Height);

            using (Graphics g = Graphics.FromImage(target))
            {
                g.FillRectangle(System.Drawing.Brushes.White, 0, 0, source.Width, source.Height);
                g.Clip = new Region(graphicsPath);
                g.DrawImage(source, new Rectangle(0, 0, target.Width, target.Height), bounds, GraphicsUnit.Pixel);
            }
            return target;
        }

        private string Ocr(Pix target, out double confidence)
        {
            try
            {
                var text = "";
                using (var engine = new TesseractEngine(@"./tessdata", "eng", EngineMode.Default))
                {

                    using (var page = engine.Process(target))
                    {
                        text = page.GetText();
                        confidence = page.GetMeanConfidence();
                    }
                    target.Dispose();
                }
                return text;
            }
            catch (Exception e)
            {
                Trace.TraceError(e.ToString());
                Debug.WriteLine("Unexpected Error: " + e.Message);
                Debug.WriteLine("Details: ");
                Debug.WriteLine(e.ToString());
            }
            confidence = 0;
            return "";
        }

        #endregion

        #region Notes

        private void SetupNotepad()
        {
            // compute boundaries
            var bounds = _selectionForegroundPath.Data.Bounds;
            var width = Math.Max(bounds.Width, NOTEPAD_MIN_WIDTH);
            var left = bounds.Left + bounds.Width / 2 - width / 2;
            var top = bounds.Bottom + 40;

            // create grid
            var grid = new Grid { Width = width, Height = 300 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // create text editor
            var editor = new TextBox
            {
                FontSize = FONT_SIZE_NORMAL,
                Foreground = Brushes.White,
                Background = Brushes.Transparent,
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true,
                CaretBrush = Brushes.DodgerBlue
            };
            Grid.SetColumn(editor, 0);

            // create text viewer
            var viewer = new FlowDocumentScrollViewer
            {
                FontSize = FONT_SIZE_NORMAL,
                Foreground = Brushes.White,
                VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                Background = Brushes.Transparent
            };
            var binding = new Binding()
            {
                Source = editor,
                Path = new PropertyPath("Text"),
                Converter = (TextToFlowDocumentConverter)FindResource("TextToFlowDocumentConverter")
            };
            viewer.SetBinding(FlowDocumentScrollViewer.DocumentProperty, binding);
            Grid.SetColumn(viewer, 2);

            // add to grid
            grid.Children.Add(editor);
            grid.Children.Add(viewer);

            // add to canvas
            Canvas.SetTop(grid, top);
            Canvas.SetLeft(grid, left);
            MainCanvas.Children.Add(grid);

            // set focus to editor
            editor.Focus();
        }
        #endregion

        #region Tags

        private void SearchTags()
        {
            var bounds = _selectionForegroundPath.Data.Bounds;
            StartSearchingTags("LoadingIndicatorDoubleBounceStyle", "extracting tags...", bounds.TopLeft.X - 250, bounds.TopLeft.Y);
        }

        private async void StartSearchingTags(string style, string text, double x, double y)
        {
            // loading container
            var container = new StackPanel { Orientation = Orientation.Horizontal };

            // setup loading indicator
            var loadingNotebookIcon = new LoadingIndicator
            {
                SpeedRatio = 1,
                IsActive = true,
                Width = ICON_SIZE_BIGGER,
                Height = ICON_SIZE_BIGGER
            };
            loadingNotebookIcon.SetResourceReference(StyleProperty, style);
            container.Children.Add(loadingNotebookIcon);

            // setup loading text
            var textBlock = new TextBlock
            {
                Text = text,
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI Light"),
                FontSize = FONT_SIZE_BIGGER,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0)
            };
            textBlock.SetResourceReference(ForegroundProperty, "AccentColorBrush");
            container.Children.Add(textBlock);

            // setup and run
            Canvas.SetLeft(container, x);
            Canvas.SetTop(container, y);
            MainCanvas.Children.Add(container);

            // wait a coupe of seconds and show notebooks
            await Task.Delay(TAG_EXTRACT_TIME_MS);
            await Dispatcher.InvokeAsync(() =>
            {
                if (!_reseted)
                {
                    MainCanvas.Children.Remove(container);

                    SetupTagIcons(x + 100, y);
                }
            });
        }

        private void SetupTagIcons(double x, double y)
        {
            var listView = new ListView();
            listView.Background = Brushes.Transparent;
            listView.BorderBrush = Brushes.Transparent;
            listView.BorderThickness = new Thickness(0);
            Canvas.SetLeft(listView, x - 100);
            Canvas.SetTop(listView, y);
            MainCanvas.Children.Add(listView);

            var tagNames = new List<string> { "Momo", "Mobile app", "Payment", "Vietnam" };
            for (var i = 0; i < tagNames.Count; ++i)
            {
                var item = SetupIcon(FontAwesomeIcon.Bookmark, tagNames[i], Brushes.White, 0, 0, true, listView);
                item.Margin = new Thickness(5, 10, 5, 10);
            }
        }

        #endregion

        #region Notebook
        private void SearchNotebooks()
        {
            var bounds = _selectionForegroundPath.Data.Bounds;
            StartSearchingNotebooks("LoadingIndicatorDoubleBounceStyle", "searching notebooks...", bounds.TopRight.X + 50, bounds.TopRight.Y);
        }

        private async void StartSearchingNotebooks(string style, string text, double x, double y)
        {
            // loading container
            var container = new StackPanel { Orientation = Orientation.Horizontal };

            // setup loading indicator
            var loadingNotebookIcon = new LoadingIndicator
            {
                SpeedRatio = 1,
                IsActive = true,
                Width = ICON_SIZE_BIGGER,
                Height = ICON_SIZE_BIGGER
            };
            loadingNotebookIcon.SetResourceReference(StyleProperty, style);
            container.Children.Add(loadingNotebookIcon);

            // setup loading text
            var textBlock = new TextBlock
            {
                Text = text,
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI Light"),
                FontSize = FONT_SIZE_BIGGER,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0)
            };
            textBlock.SetResourceReference(ForegroundProperty, "AccentColorBrush");
            container.Children.Add(textBlock);

            // setup and run
            Canvas.SetLeft(container, x);
            Canvas.SetTop(container, y);
            MainCanvas.Children.Add(container);

            // wait a coupe of seconds and show notebooks
            await Task.Delay(NOTEBOOK_SEARCH_TIME_MS);
            await Dispatcher.InvokeAsync(() =>
            {
                if (!_reseted)
                {
                    MainCanvas.Children.Remove(container);
                    SetupNotebookIcons(x, y);
                }
            });
        }

        private void SetupNotebookIcons(double x, double y)
        {
            // get right most position
            // setup add new notebook
            SetupIcon(FontAwesomeIcon.Plus, "New", Brushes.White, x, y);
            // setup the 3 notebooks
            SetupNotebookIcon(FontAwesomeIcon.Book, "Technology", Brushes.Fuchsia, x, y + OFFSET_VERTICAL);
            SetupNotebookIcon(FontAwesomeIcon.Book, "Personal finance", Brushes.GreenYellow, x, y + OFFSET_VERTICAL * 2);
            SetupNotebookIcon(FontAwesomeIcon.Book, "Project FinTech", Brushes.Crimson, x, y + OFFSET_VERTICAL * 3);
            // setup the more notebooks
            SetupIcon(FontAwesomeIcon.EllipsisH, "More", Brushes.White, x, y + OFFSET_VERTICAL * 4);
        }

        private void SetupNotebookIcon(FontAwesomeIcon icon, string text, System.Windows.Media.Brush brush,
            double x, double y)
        {
            var container = SetupIcon(icon, text, brush, x, y);
            HandleNotebookIconAction(container);
        }

        private void HandleNotebookIconAction(StackPanel container)
        {
            // setup anim
            container.MouseDown += (sender, args) =>
            {
                args.Handled = true;
                var disappearAnimation = new DoubleAnimation
                {
                    From = 1,
                    To = 0.2,
                    Duration = new Duration(TimeSpan.FromSeconds(0.30)),
                    EasingFunction = new PowerEase()
                    {
                        EasingMode = EasingMode.EaseOut,
                        Power = 1.5
                    },
                    AutoReverse = true
                };
                var translateAnimation = new DoubleAnimation
                {
                    From = 0,
                    To = 100,
                    Duration = new Duration(TimeSpan.FromSeconds(0.20)),
                    EasingFunction = new PowerEase()
                    {
                        EasingMode = EasingMode.EaseOut,
                        Power = 2
                    },
                };
                translateAnimation.Completed += (o, eventArgs) =>
                {
                    Task.Run(async () =>
                    {
                        await Task.Delay(150);
                        await Dispatcher.InvokeAsync(Reset);
                    });
                };
                var translateTransform = new TranslateTransform();
                MainCanvas.RenderTransform = translateTransform;
                translateTransform.BeginAnimation(TranslateTransform.XProperty, translateAnimation);
                MainCanvas.BeginAnimation(OpacityProperty, disappearAnimation);
            };
            //cursor
            ChangeMouseCursorToSelect(container);

            // run animation
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

        #endregion

        #region Screen-clipping

        private void Reset()
        {
            if (!_reseted)
            {
                BackgroundBrush.Opacity = 0;
                MainCanvas.Children.Clear();
                MainCanvas.Opacity = 1;
                MainCanvas.Background = Brushes.Transparent;
                MainCanvas.RenderTransform = null;
                _lassoSelectionForegroundGeometry = null;
                _rectSelectionForegroundGeometry = null;
                _selectionForegroundPath = null;
                _selectionBackgroundPath = null;
                Mouse.OverrideCursor = null;
                OverlayMask.Opacity = 0;
                _drawing = false;
                _start = false;
                _reseted = true;
            }
        }

        private void SetupSelectionGeometry()
        {
            _selectionForegroundPath = new Path { StrokeThickness = 5, };
            _selectionForegroundPath.SetResourceReference(Path.StrokeProperty, "AccentColorBrush");
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

        private void StartScreenClipping()
        {
            Reset();

            FillWithScreenshot();
            OverlayMask.Opacity = SELECTION_BACKGROUND_OPACITY;

            Mouse.OverrideCursor = Cursors.Cross;

            _start = true;
            _reseted = false;

        }



        private void _globalHook_KeyUp(object sender, System.Windows.Forms.KeyEventArgs e)
        {
            if (e.KeyCode == System.Windows.Forms.Keys.Escape)
            {
                // escape key => reset
                Reset();
                return;
            }

            if (_reseted)
            {
                if (e.KeyCode == System.Windows.Forms.Keys.D1)
                {
                    StartScreenClipping();
                    _mode = Mode.Rectangle;
                }
                else if (e.KeyCode == System.Windows.Forms.Keys.D2)
                {
                    StartScreenClipping();
                    _mode = Mode.Lasso;
                }
            }
        }

        private void MainCanvas_OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!_drawing && !_start && DISMISS_ON_CLICK)
            {
                MainCanvas.Children.Clear();
                BackgroundBrush.Opacity = 0;
                _reseted = true;
            }
        }

        private void _globalHook_MouseMove(object sender, MouseEventArgs e)
        {
            if (_drawing)
            {
                var point = new Point(e.X * _xRatio, e.Y * _yRatio);
                if (_mode == Mode.Lasso)
                {
                    _lassoSelectionForegroundGeometry.Segments.RemoveAt(_lassoSelectionForegroundGeometry.Segments.Count - 1);
                    _lassoSelectionForegroundGeometry.Segments.Add(new LineSegment { Point = point });
                    _lassoSelectionForegroundGeometry.Segments.Add(new LineSegment { Point = _startPoint });
                }
                else if (_mode == Mode.Rectangle)
                {
                    var width = Math.Abs(point.X - _startPoint.X);
                    var height = Math.Abs(point.Y - _startPoint.Y);
                    _rectSelectionForegroundGeometry.Rect = new Rect(_startPoint, new System.Windows.Size(width, height));
                }
            }
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

                Mouse.OverrideCursor = null;

                SetupNotepad();
                Scan();
                SearchTags();
                SearchNotebooks();
            }

        }

        private void _globalHook_GlobalHookOnMouseDown(object sender, MouseEventArgs e)
        {
            if (_start)
            {
                //BackgroundBrush.Opacity = 0;
                OverlayMask.Opacity = 0;

                var point = new Point(e.X * _xRatio, e.Y * _yRatio);
                _startPoint = point;
                _drawing = true;

                SetupSelectionGeometry();

                Mouse.OverrideCursor = Cursors.Cross;
            }
        }
        #endregion

        #region Screenshoots
        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr hObject);

        private void FillWithScreenshot()
        {
            _fullScreenshotBitmap = new Bitmap((int)_screenWidth, (int)_screenHeight, PixelFormat.Format24bppRgb);

            using (var g = Graphics.FromImage(_fullScreenshotBitmap))
            {
                g.CopyFromScreen(Screen.PrimaryScreen.Bounds.X, Screen.PrimaryScreen.Bounds.Y, 0, 0, _fullScreenshotBitmap.Size, CopyPixelOperation.SourceCopy);
            }

            IntPtr handle = IntPtr.Zero;
            try
            {
                handle = _fullScreenshotBitmap.GetHbitmap();
                var imageBrush = new ImageBrush
                {
                    ImageSource = Imaging.CreateBitmapSourceFromHBitmap(handle, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()),
                    Stretch = Stretch.None
                };
                MainCanvas.Background = imageBrush;
            }
            finally
            {
                DeleteObject(handle);
            }
        }
        #endregion

        private StackPanel SetupIcon(FontAwesomeIcon icon, string text, System.Windows.Media.Brush brush, double x, double y, bool canBeDeleted = false, ListView listViewContainer = null)
        {
            // container
            var container = new StackPanel { Orientation = Orientation.Horizontal };

            // icon
            var iconBlock = new ImageAwesome { Icon = icon, Foreground = brush, Width = ICON_SIZE_NORMAL, Height = ICON_SIZE_NORMAL, VerticalAlignment = VerticalAlignment.Center };
            container.Children.Add(iconBlock);

            // text
            var textBlock = new TextBlock
            {
                Text = text,
                Foreground = brush,
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI Light"),
                FontSize = FONT_SIZE_NORMAL,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0)
            };
            container.Children.Add(textBlock);

            // delete button (if needed)
            if (canBeDeleted)
            {
                var deleteButton = new ImageAwesome
                {
                    Icon = FontAwesomeIcon.Remove,
                    Foreground = Brushes.Red,
                    Width = ICON_REMOVE_SIZE,
                    Height = ICON_REMOVE_SIZE,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(5, 0, 0, 0)
                };
                ChangeMouseCursorToSelect(deleteButton);
                deleteButton.MouseUp += (sender, args) =>
                {
                    if (listViewContainer == null)
                        MainCanvas.Children.Remove(container);
                    else
                        listViewContainer.Items.Remove(container);
                };
                container.Children.Add(deleteButton);
            }

            // setup
            if (listViewContainer == null)
            {
                Canvas.SetLeft(container, x);
                Canvas.SetTop(container, y);
                MainCanvas.Children.Add(container);
            }
            else
            {
                // put in listview
                listViewContainer.Items.Add(container);
            }

            return container;
        }

        private static void ChangeMouseCursorToSelect(UIElement element)
        {
            element.MouseEnter += (sender, args) => { Mouse.OverrideCursor = Cursors.Hand; };
            element.MouseLeave += (sender, args) => { Mouse.OverrideCursor = null; };
        }


    }
}
