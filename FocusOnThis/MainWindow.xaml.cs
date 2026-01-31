using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace FocusOnThis
{
    public partial class MainWindow : Window
    {
        private bool _isEnabled = false;
        private FocusOverlay? _overlay;
        private DispatcherTimer? _hideTimer;
        private DispatcherTimer? _focusMonitorTimer;
        private IntPtr _lastFocusedWindow = IntPtr.Zero;
        private bool _isMouseOver = false;

        public MainWindow()
        {
            InitializeComponent();
            
            // Position at top center
            this.Loaded += MainWindow_Loaded;
            
            // Setup auto-hide timer
            _hideTimer = new DispatcherTimer();
            _hideTimer.Interval = TimeSpan.FromSeconds(2);
            _hideTimer.Tick += HideTimer_Tick;
            _hideTimer.Start();
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Position at top center of screen
            var screenWidth = SystemParameters.PrimaryScreenWidth;
            this.Left = (screenWidth - this.Width) / 2;
            this.Top = 0;
        }

        private void ControlButton_Click(object sender, MouseButtonEventArgs e)
        {
            _isEnabled = !_isEnabled;
            
            if (_isEnabled)
            {
                EnableFocusMode();
                StatusIcon.Text = "🟢";
                StatusIcon.ToolTip = "Focus Mode ON - Click to disable";
            }
            else
            {
                DisableFocusMode();
                StatusIcon.Text = "⚫";
                StatusIcon.ToolTip = "Focus Mode OFF - Click to enable";
            }
        }

        private void ControlButton_MouseEnter(object sender, MouseEventArgs e)
        {
            _isMouseOver = true;
            this.Opacity = 1.0;
            _hideTimer?.Stop();
        }

        private void ControlButton_MouseLeave(object sender, MouseEventArgs e)
        {
            _isMouseOver = false;
            _hideTimer?.Start();
        }

        private void HideTimer_Tick(object? sender, EventArgs e)
        {
            if (!_isMouseOver)
            {
                this.Opacity = 0.3;
            }
        }

        private void EnableFocusMode()
        {
            // Create and show overlay
            _overlay = new FocusOverlay();
            _overlay.Show();

            // Start monitoring focused window
            _focusMonitorTimer = new DispatcherTimer();
            _focusMonitorTimer.Interval = TimeSpan.FromMilliseconds(100);
            _focusMonitorTimer.Tick += FocusMonitorTimer_Tick;
            _focusMonitorTimer.Start();
        }

        private void DisableFocusMode()
        {
            // Stop monitoring
            _focusMonitorTimer?.Stop();
            _focusMonitorTimer = null;

            // Release cursor clip
            NativeMethods.ClipCursor(IntPtr.Zero);

            // Close overlay
            _overlay?.Close();
            _overlay = null;
            
            _lastFocusedWindow = IntPtr.Zero;
        }

        private void FocusMonitorTimer_Tick(object? sender, EventArgs e)
        {
            try
            {
                // Use GetForegroundWindow as the primary method - it reliably returns
                // the top-level window handle for the active window
                var hwnd = NativeMethods.GetForegroundWindow();
                
                if (hwnd != IntPtr.Zero && !IsOurWindow(hwnd))
                {
                    // Only update if the focused window has changed
                    if (hwnd != _lastFocusedWindow)
                    {
                        _lastFocusedWindow = hwnd;
                        UpdateFocusedWindow(hwnd);
                    }
                }
            }
            catch
            {
                // Silently ignore any errors - the timer will retry on the next tick
            }
        }

        private bool IsOurWindow(IntPtr hwnd)
        {
            // Check if this is our main window or overlay
            var ourMainWindow = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (hwnd == ourMainWindow)
                return true;
                
            if (_overlay != null)
            {
                var overlayHandle = new System.Windows.Interop.WindowInteropHelper(_overlay).Handle;
                if (hwnd == overlayHandle)
                    return true;
            }
            
            return false;
        }

        private void UpdateFocusedWindow(IntPtr hwnd)
        {
            // Get window rectangle
            if (NativeMethods.GetWindowRect(hwnd, out NativeMethods.RECT rect))
            {
                // Update overlay to show hole for this window
                _overlay?.UpdateFocusedWindow(rect);

                // Clip cursor to the focused window bounds
                NativeMethods.RECT clipRect = rect;
                NativeMethods.ClipCursor(ref clipRect);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            DisableFocusMode();
            base.OnClosed(e);
        }
    }

    public static class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ClipCursor(ref RECT lpRect);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ClipCursor(IntPtr lpRect);
    }
}
