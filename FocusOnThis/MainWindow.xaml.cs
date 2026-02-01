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
        private NativeMethods.RECT _lastWindowRect = new NativeMethods.RECT { Left = -1, Top = -1, Right = -1, Bottom = -1 };
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
            if (!_isEnabled)
            {
                // Show window selector dialog
                var selector = new WindowSelector();
                if (selector.ShowDialog() == true && selector.SelectedWindowHandle != IntPtr.Zero)
                {
                    _isEnabled = true;
                    EnableFocusMode(selector.SelectedWindowHandle, selector.SelectedWindowRect);
                    StatusIcon.Text = "🟢";
                    StatusIcon.ToolTip = "Focus Mode ON - Click to disable";
                }
                // If user cancels, don't enable focus mode
            }
            else
            {
                SetFocusModeOff();
            }
        }

        private void SetFocusModeOff()
        {
            _isEnabled = false;
            DisableFocusMode();
            StatusIcon.Text = "⚪";
            StatusIcon.ToolTip = "Focus Mode OFF - Click to enable";
        }

        private void Quit_Click(object sender, RoutedEventArgs e)
        {
            DisableFocusMode();
            Application.Current.Shutdown();
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

        private void EnableFocusMode(IntPtr selectedWindowHandle, NativeMethods.RECT selectedWindowRect)
        {
            // Store the selected window as the initial target
            _lastFocusedWindow = selectedWindowHandle;
            _lastWindowRect = selectedWindowRect;

            // Create and show overlay
            _overlay = new FocusOverlay();
            _overlay.Show();

            // Bring the selected window to the foreground first (auto focus)
            // This must be done BEFORE ClipCursor, as SetForegroundWindow can reset cursor clipping
            NativeMethods.SetForegroundWindow(selectedWindowHandle);

            // Now update to focus on the selected window and apply cursor clipping
            UpdateFocusedWindow(selectedWindowHandle, selectedWindowRect);

            // Start monitoring focused window (for tracking window moves/resizes)
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
                // Track the selected window's position changes (for moving/resizing)
                // instead of following focus changes
                if (_lastFocusedWindow != IntPtr.Zero)
                {
                    // Get the current window rectangle
                    if (NativeMethods.GetWindowRect(_lastFocusedWindow, out NativeMethods.RECT currentRect))
                    {
                        // Update if the window position/size has changed
                        if (!RectsEqual(_lastWindowRect, currentRect))
                        {
                            _lastWindowRect = currentRect;
                            UpdateFocusedWindow(_lastFocusedWindow, currentRect);
                        }
                    }
                    else
                    {
                        // Window no longer exists - disable focus mode
                        SetFocusModeOff();
                    }
                }
            }
            catch
            {
                // Silently ignore any errors - the timer will retry on the next tick
            }
        }

        private bool RectsEqual(NativeMethods.RECT a, NativeMethods.RECT b)
        {
            return a.Left == b.Left && a.Top == b.Top && a.Right == b.Right && a.Bottom == b.Bottom;
        }

        private void UpdateFocusedWindow(IntPtr hwnd, NativeMethods.RECT rect)
        {
            // Update overlay to show hole for this window
            _overlay?.UpdateFocusedWindow(rect);

            // Clip cursor to the focused window bounds with an inset of 8 pixels
            // A larger inset (8 pixels) is needed because:
            // 1. Window borders and resize handles can be 5-8 pixels wide
            // 2. Clicking on the boundary triggers system resize operations (two-arrows cursor mode)
            //    which can release cursor clipping and allow the mouse to escape
            // 3. On high-DPI displays, the effective border area is scaled even larger
            // 4. An 8-pixel inset ensures the cursor stays well inside the window content area
            NativeMethods.RECT clipRect = new NativeMethods.RECT
            {
                Left = rect.Left + 8,
                Top = rect.Top + 8,
                Right = rect.Right - 8,
                Bottom = rect.Bottom - 8
            };
            NativeMethods.ClipCursor(ref clipRect);
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

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);
    }
}
