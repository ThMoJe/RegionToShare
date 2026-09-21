using System.ComponentModel;
using System.Configuration;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using System.Windows.Threading;
using RegionToShare.Properties;
using Throttle;
using TomsToolbox.Essentials;
using TomsToolbox.Wpf;
using TomsToolbox.Wpf.Styles;
using static RegionToShare.NativeMethods;
using static RegionToShare.ExtensionMethods;

namespace RegionToShare;

public partial class MainWindow
{
    private IntPtr _separationLayerHandle;

    private IntPtr _windowHandle;
    private RecordingWindow? _recordingWindow;

    private POINT _debugOffset;
    private bool _updatingThemeColor;

    public MainWindow()
    {
        InitializeComponent();

        DataContext = this;
        Resolutions = LoadResolutions();
        Resources.RegisterDefaultStyles();
        SetThemeColor();
        Settings.PropertyChanged += Settings_PropertyChanged;
    }

    public string Version => Assembly.GetExecutingAssembly().GetName().Version.ToString();

    public ICollection<string> Resolutions { get; }

    public static ICollection<int> SupportedFramesPerSecond { get; } = new[] { 5, 10, 15, 20, 30, 60 };

    internal Settings Settings => Settings.Default;

    public string? Extend
    {
        get => (string?)GetValue(ExtendProperty);
        set => SetValue(ExtendProperty, value);
    }
    public static readonly DependencyProperty ExtendProperty = DependencyProperty.Register(nameof(Extend), typeof(string), typeof(MainWindow),
        new FrameworkPropertyMetadata(default(string), FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            (d, args) => ((MainWindow)d).OnExtendChanged(args.NewValue as string)));

    public Brush BackgroundPattern
    {
        get => (Brush)GetValue(BackgroundPatternProperty);
        set => SetValue(BackgroundPatternProperty, value);
    }
    public static readonly DependencyProperty BackgroundPatternProperty = DependencyProperty.Register(
        nameof(BackgroundPattern), typeof(Brush), typeof(MainWindow), new PropertyMetadata(default(Brush)));

    public ImageSource? CustomImage
    {
        get => (ImageSource?)GetValue(CustomImageProperty);
        set => SetValue(CustomImageProperty, value);
    }
    public static readonly DependencyProperty CustomImageProperty = DependencyProperty.Register(
        nameof(CustomImage), typeof(ImageSource), typeof(MainWindow), new PropertyMetadata(null));

    private void OnExtendChanged(string? newValue)
    {
        if (newValue is null || !TryParseSize(newValue, out var size))
            return;

        size += GlassFrameThickness;
        SetWindowPos(_windowHandle, IntPtr.Zero, 0, 0, size.Width, size.Height, SWP_NOACTIVATE | SWP_NOZORDER | SWP_NOMOVE);
    }

    internal Thickness GlassFrameThickness => DwmGetExtendedFrameBounds(_windowHandle);

    internal RECT NativeWindowRect
    {
        get
        {
            GetWindowRect(_windowHandle, out var rect);
            return rect;
        }
        set
        {
            if (_windowHandle == IntPtr.Zero)
                return;

            SetWindowPos(_windowHandle, IntPtr.Zero, value.Left, value.Top, value.Width, value.Height, SWP_NOACTIVATE | SWP_NOZORDER);
        }
    }

    private ICollection<string> LoadResolutions()
    {
        var defaultResolutions = new[] { @"1024x782", @"1280x1024", @"1920x1080" };

        try
        {
            var userDataDirPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"RegionToShare");
            var resolutionsFilePath = Path.Combine(userDataDirPath, @"resolutions.txt");

            Directory.CreateDirectory(userDataDirPath);

            if (!File.Exists(resolutionsFilePath))
            {
                File.WriteAllLines(resolutionsFilePath, defaultResolutions);
                return defaultResolutions;
            }

            var resolutions = File.ReadAllLines(resolutionsFilePath)
                .Where(item => TryParseSize(item, out _))
                .ToArray();

            return resolutions.Any() ? resolutions : defaultResolutions;
        }
        catch
        {
            return defaultResolutions;
        }
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        _windowHandle = this.GetWindowHandle();

        var separationLayerWindow = new Window()
        {
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            Title = "Region to Share - Separation Layer",
            ShowInTaskbar = false,
            Top = Top,
            Left = Left,
            Width = 10,
            Height = 10
        };

        separationLayerWindow.MouseDown += SubLayer_MouseDown;
        BindingOperations.SetBinding(separationLayerWindow, BackgroundProperty, new Binding(nameof(BackgroundPattern)) { Source = this });

        separationLayerWindow.SourceInitialized += (_, _) =>
        {
            _separationLayerHandle = separationLayerWindow.GetWindowHandle();

            this.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
            {
                if (Keyboard.Modifiers != (ModifierKeys.Alt | ModifierKeys.Control))
                {
                    var placement = _windowHandle.GetWindowPlacement();

                    placement.NormalPosition.DeserializeFrom(Settings.WindowPlacement);

                    placement.NormalPosition += GlassFrameThickness;
                    _windowHandle.SetWindowPlacement(ref placement);
                    // need to set it twice, if the first call has moved the window to another screen with a different dpi, the size might be incorrect.
                    _windowHandle.SetWindowPlacement(ref placement);
                }

                UpdateSizeAndPos();

                if (Settings.StartActivated)
                {
                    SetActive();
                }
                else
                {
                    this.BeginInvoke(BringToFront);
                }
            });
        };

        separationLayerWindow.Show();
    }

    private void SetActive()
    {
        OnMouseLeftButtonDown();

        var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle, Dispatcher.CurrentDispatcher);

        void TimerTick(object sender, EventArgs e)
        {
            if (_recordingWindow != null)
            {
                SendToBack();
            }
            timer.Stop();
        }

        timer.Interval = TimeSpan.FromSeconds(1);
        timer.Tick += TimerTick;
        timer.Start();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        OnMouseLeftButtonDown();
    }

    private void OnMouseLeftButtonDown()
    {
        _debugOffset = Keyboard.Modifiers == (ModifierKeys.Alt | ModifierKeys.Control | ModifierKeys.Shift) ? new POINT(600, 300) : new POINT();

        if (_recordingWindow != null)
            return;

        InfoArea.Visibility = Visibility.Collapsed;
        RenderTarget.Visibility = Visibility.Visible;

        ValidateSettings();

        _recordingWindow = new RecordingWindow(RenderTarget, Settings.DrawShadowCursor, Settings.FramesPerSecond, _debugOffset);

        NativeWindowRect -= GlassFrameThickness;

        _recordingWindow.SourceInitialized += (_, _) =>
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
        };

        _recordingWindow.Closed += (_, _) =>
        {
            InfoArea.Visibility = Visibility.Visible;
            RenderTarget.Visibility = Visibility.Hidden;
            WindowStyle = WindowStyle.ThreeDBorderWindow;
            ResizeMode = ResizeMode.CanResize;

            _recordingWindow = null;

            NativeWindowRect += GlassFrameThickness;

            BringToFront();
        };

        _recordingWindow.Show();

        this.BeginInvoke(DispatcherPriority.Background, SendToBack);
    }

    public static bool ValidateSettings()
    {
        try
        {
            var settings = Settings.Default;

            settings.FramesPerSecond = SupportedFramesPerSecond.Contains(settings.FramesPerSecond) ? settings.FramesPerSecond : 15;

            if (TryResolveImagePath(settings.ThemeColor) == null)
            {
                var normalized = TryNormalizeHexColor(settings.ThemeColor);
                if (normalized != null)
                {
                    settings.ThemeColor = normalized;
                }
                else
                {
                    try
                    {
                        ColorConverter.ConvertFromString(settings.ThemeColor);
                    }
                    catch
                    {
                        settings.ThemeColor = nameof(Colors.SteelBlue);
                    }
                }
            }

            return true;
        }
        catch (ConfigurationException ex)
        {
            var inner = ex.ExceptionChain().OfType<ConfigurationException>().FirstOrDefault(item => !item.Filename.IsNullOrEmpty());
            if (inner == null)
                throw;

            var message = $"The settings file '{inner.Filename}' is corrupt. It will be reset to default values.";
            MessageBox.Show(message, "Error", MessageBoxButton.OK, MessageBoxImage.Error, MessageBoxResult.OK, MessageBoxOptions.ServiceNotification);
            File.Delete(inner.Filename);
        }

        return false;
    }

    private void Settings_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Settings.ThemeColor))
        {
            if (!_updatingThemeColor)
            {
                var normalized = TryNormalizeHexColor(Settings.ThemeColor);
                if (normalized != null && normalized != Settings.ThemeColor)
                {
                    _updatingThemeColor = true;
                    Settings.ThemeColor = normalized; // triggers re-entry; SetThemeColor() runs there
                    _updatingThemeColor = false;
                    return;
                }
            }
            SetThemeColor();
        }
    }

    /// <summary>
    /// Returns the resolved absolute path if <paramref name="value"/> is a supported image
    /// file (.jpg, .jpeg, .png) that exists on disk. Accepts both bare filenames (resolved
    /// relative to the app directory) and absolute paths (e.g. from the file picker).
    /// Returns null if the value is not a recognised image or the file does not exist.
    /// </summary>
    private static string? TryResolveImagePath(string? value)
    {
        if (value is null)
            return null;

        var ext = Path.GetExtension(value);
        if (!ext.Equals(".jpg",  StringComparison.OrdinalIgnoreCase) &&
            !ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) &&
            !ext.Equals(".png",  StringComparison.OrdinalIgnoreCase))
            return null;

        // Absolute path (e.g. selected via file dialog)
        if (Path.IsPathRooted(value))
            return File.Exists(value) ? value : null;

        // Bare filename — resolve relative to app directory
        var fullPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, value);
        return File.Exists(fullPath) ? fullPath : null;
    }

    /// <summary>
    /// Returns the normalized form "#XXXXXX" (uppercase) if <paramref name="value"/> is a
    /// valid 6-digit hex color (with or without a leading '#'). Returns null otherwise.
    /// </summary>
    private static string? TryNormalizeHexColor(string? value)
    {
        if (value is null)
            return null;

        var hex = value.Trim();
        if (hex.StartsWith("#"))
            hex = hex.Substring(1);

        if (hex.Length != 6)
            return null;

        hex = hex.ToUpperInvariant();

        foreach (var c in hex)
        {
            if (!((c >= '0' && c <= '9') || (c >= 'A' && c <= 'F')))
                return null;
        }

        return "#" + hex;
    }

    private void SetThemeColor()
    {
        // Priority 1: Image file (JPEG or PNG, relative filename or absolute path)
        var imagePath = TryResolveImagePath(Settings.ThemeColor);
        if (imagePath != null)
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(imagePath, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();
                CustomImage = bitmap;
                BackgroundPattern = GenerateRandomBrush(Colors.SteelBlue);
                Application.Current.Resources["ThemeColor"] = Colors.SteelBlue;
            }
            catch
            {
                // Corrupt or unreadable image — fall back to default.
                CustomImage = null;
                Application.Current.Resources["ThemeColor"] = Colors.SteelBlue;
                BackgroundPattern = GenerateRandomBrush(Colors.SteelBlue);
            }

            return;
        }

        CustomImage = null;

        // Priority 2: Exact 6-digit hex color — solid color background.
        // This intentionally produces a flat SolidColorBrush, unlike named colors
        // (Priority 3) which use the GenerateRandomBrush dot pattern. The visual
        // distinction lets users choose between a clean solid fill and the textured look.
        var hexColor = TryNormalizeHexColor(Settings.ThemeColor);
        if (hexColor != null)
        {
            var color = (Color)ColorConverter.ConvertFromString(hexColor);
            Application.Current.Resources["ThemeColor"] = color;
            BackgroundPattern = new SolidColorBrush(color);
            return;
        }

        // Priority 3: Named color (e.g. "SteelBlue") — dot pattern brush
        try
        {
            var themeColor = (Color)ColorConverter.ConvertFromString(Settings.ThemeColor);
            Application.Current.Resources["ThemeColor"] = themeColor;
            BackgroundPattern = GenerateRandomBrush(themeColor);
        }
        catch
        {
            // Invalid color, ignore.
        }
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (_windowHandle == IntPtr.Zero)
            return;

        if (e.Property != LeftProperty
            && e.Property != TopProperty
            && e.Property != ActualWidthProperty
            && e.Property != ActualHeightProperty
            && e.Property != WindowStateProperty)
            return;

        UpdateSizeAndPos();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);

        var normalPosition = _windowHandle.GetWindowPlacement().NormalPosition - GlassFrameThickness;
        Settings.WindowPlacement = normalPosition.Serialize();
        Settings.Save();
    }

    [Throttled(typeof(DispatcherThrottle), (int)DispatcherPriority.Normal)]
    private void UpdateSizeAndPos()
    {
        if (WindowState == WindowState.Minimized)
            return;

        _recordingWindow?.UpdateSizeAndPos(NativeWindowRect);

        var rect = NativeWindowRect - GlassFrameThickness;
        Extend = rect.Width + "x" + rect.Height;

        SetSeparationLayerPos(SWP_NOACTIVATE | SWP_NOZORDER);
    }

    private void SubLayer_MouseDown(object sender, MouseButtonEventArgs e)
    {
        this.BeginInvoke(DispatcherPriority.Background, SendToBack);
    }

    public void BringToFront()
    {
        SetSeparationLayerPos(SWP_HIDEWINDOW);
        SetWindowPos(_windowHandle, HWND_TOP, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);
    }

    public void SendToBack()
    {
        SetSeparationLayerPos(SWP_NOACTIVATE | SWP_SHOWWINDOW);
        SetWindowPos(_windowHandle, _separationLayerHandle, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    private void SetSeparationLayerPos(uint flags)
    {
        if (_separationLayerHandle == IntPtr.Zero)
            return;

        var rect = NativeWindowRect - _debugOffset;

        SetWindowPos(_separationLayerHandle, HWND_BOTTOM, rect.Left, rect.Top, rect.Width, rect.Height, flags);
    }

    private bool TryParseSize(string value, out SIZE size)
    {
        size = Size.Empty;

        try
        {
            var parts = value.Split('x');

            if (parts.Length != 2
                || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var width)
                || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var height))
                return false;

            size = new SIZE(width, height);

            return size.Width >= MinWidth && size.Height >= MinHeight;
        }
        catch
        {
            return false;
        }
    }

    private void ThemeColorBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select Background Image",
            Filter = "Image Files (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png",
            InitialDirectory = AppDomain.CurrentDomain.BaseDirectory
        };

        if (dialog.ShowDialog() == true)
        {
            Settings.ThemeColor = dialog.FileName;
        }
    }
}