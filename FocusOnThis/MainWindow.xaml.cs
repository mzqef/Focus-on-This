using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
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
        
        // Win32 event hooks
        private IntPtr _foregroundHook = IntPtr.Zero;
        private IntPtr _locationChangeHook = IntPtr.Zero;
        private IntPtr _moveSizeStartHook = IntPtr.Zero;
        private IntPtr _moveSizeEndHook = IntPtr.Zero;
        private NativeMethods.WinEventDelegate? _foregroundDelegate;
        private NativeMethods.WinEventDelegate? _locationChangeDelegate;
        private NativeMethods.WinEventDelegate? _moveSizeStartDelegate;
        private NativeMethods.WinEventDelegate? _moveSizeEndDelegate;
        private bool _isResizing = false;
        
        // Debouncing for rapid foreground changes
        private DateTime _lastForegroundChangeTime = DateTime.MinValue;
        private const int FOREGROUND_DEBOUNCE_MS = 100;

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
            _isResizing = false;

            // Create and show overlay
            _overlay = new FocusOverlay();
            _overlay.Show();

            // Bring the selected window to the foreground first (auto focus)
            // This must be done BEFORE ClipCursor, as SetForegroundWindow can reset cursor clipping
            NativeMethods.SetForegroundWindow(selectedWindowHandle);

            // Now update to focus on the selected window and apply cursor clipping
            UpdateFocusedWindow(selectedWindowHandle, selectedWindowRect);

            // Setup Win32 event hooks for real-time tracking
            SetupWinEventHooks();

            // Start monitoring focused window as a fallback (for tracking window moves/resizes)
            _focusMonitorTimer = new DispatcherTimer();
            _focusMonitorTimer.Interval = TimeSpan.FromMilliseconds(100);
            _focusMonitorTimer.Tick += FocusMonitorTimer_Tick;
            _focusMonitorTimer.Start();
        }

        private void SetupWinEventHooks()
        {
            // Keep delegates alive to prevent garbage collection
            _foregroundDelegate = new NativeMethods.WinEventDelegate(OnForegroundWindowChanged);
            _locationChangeDelegate = new NativeMethods.WinEventDelegate(OnLocationChanged);
            _moveSizeStartDelegate = new NativeMethods.WinEventDelegate(OnMoveSizeStart);
            _moveSizeEndDelegate = new NativeMethods.WinEventDelegate(OnMoveSizeEnd);

            // Hook foreground window changes (Alt+Tab detection)
            _foregroundHook = NativeMethods.SetWinEventHook(
                NativeMethods.EVENT_SYSTEM_FOREGROUND,
                NativeMethods.EVENT_SYSTEM_FOREGROUND,
                IntPtr.Zero,
                _foregroundDelegate,
                0, 0,
                NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);

            // Hook window location/size changes for real-time tracking
            _locationChangeHook = NativeMethods.SetWinEventHook(
                NativeMethods.EVENT_OBJECT_LOCATIONCHANGE,
                NativeMethods.EVENT_OBJECT_LOCATIONCHANGE,
                IntPtr.Zero,
                _locationChangeDelegate,
                0, 0,
                NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);

            // Hook move/size start to detect when user starts resizing
            _moveSizeStartHook = NativeMethods.SetWinEventHook(
                NativeMethods.EVENT_SYSTEM_MOVESIZESTART,
                NativeMethods.EVENT_SYSTEM_MOVESIZESTART,
                IntPtr.Zero,
                _moveSizeStartDelegate,
                0, 0,
                NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);

            // Hook move/size end to detect when user finishes resizing
            _moveSizeEndHook = NativeMethods.SetWinEventHook(
                NativeMethods.EVENT_SYSTEM_MOVESIZEEND,
                NativeMethods.EVENT_SYSTEM_MOVESIZEEND,
                IntPtr.Zero,
                _moveSizeEndDelegate,
                0, 0,
                NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);
        }

        private void UnhookWinEvents()
        {
            if (_foregroundHook != IntPtr.Zero)
            {
                NativeMethods.UnhookWinEvent(_foregroundHook);
                _foregroundHook = IntPtr.Zero;
            }
            if (_locationChangeHook != IntPtr.Zero)
            {
                NativeMethods.UnhookWinEvent(_locationChangeHook);
                _locationChangeHook = IntPtr.Zero;
            }
            if (_moveSizeStartHook != IntPtr.Zero)
            {
                NativeMethods.UnhookWinEvent(_moveSizeStartHook);
                _moveSizeStartHook = IntPtr.Zero;
            }
            if (_moveSizeEndHook != IntPtr.Zero)
            {
                NativeMethods.UnhookWinEvent(_moveSizeEndHook);
                _moveSizeEndHook = IntPtr.Zero;
            }
            _foregroundDelegate = null;
            _locationChangeDelegate = null;
            _moveSizeStartDelegate = null;
            _moveSizeEndDelegate = null;
        }

        private void OnForegroundWindowChanged(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            // Only respond to foreground changes when focus mode is enabled
            if (!_isEnabled || hwnd == IntPtr.Zero)
                return;

            Dispatcher.BeginInvoke(DispatcherPriority.Normal, () =>
            {
                if (!_isEnabled || _isResizing)
                    return;

                // Debounce rapid foreground changes - but only for successfully processed windows
                // This check is inside the dispatcher to avoid filtering out legitimate windows
                // that fire shortly after task switcher windows get rejected
                var now = DateTime.UtcNow;
                if ((now - _lastForegroundChangeTime).TotalMilliseconds < FOREGROUND_DEBOUNCE_MS)
                {
                    // If this is the same window we just switched to, skip (actual debounce)
                    if (hwnd == _lastFocusedWindow)
                        return;
                    // Otherwise, allow processing - it's a different window
                }

                // Re-validate this is still the foreground window
                IntPtr currentForeground = NativeMethods.GetForegroundWindow();
                if (currentForeground != hwnd)
                    return;

                // Skip our own windows
                IntPtr overlayHandle = _overlay != null ? new System.Windows.Interop.WindowInteropHelper(_overlay).Handle : IntPtr.Zero;
                IntPtr mainHandle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                if (hwnd == overlayHandle || hwnd == mainHandle)
                    return;

                // Filter out task switcher and shell windows by class name
                var className = new StringBuilder(256);
                NativeMethods.GetClassName(hwnd, className, 256);
                string classStr = className.ToString();
                if (classStr == "XamlExplorerHostIslandWindow" ||  // Windows 11 Alt+Tab
                    classStr == "MultitaskingViewFrame" ||         // Windows 10 Alt+Tab
                    classStr == "TaskSwitcherWnd" ||               // Legacy task switcher
                    classStr == "Windows.UI.Core.CoreWindow" ||    // Various shell overlays
                    classStr == "Shell_TrayWnd" ||                 // Taskbar
                    classStr == "Progman" ||                       // Desktop
                    classStr == "WorkerW")                         // Desktop worker
                    return;

                // Validate window is valid and visible
                if (!NativeMethods.IsWindow(hwnd) || !NativeMethods.IsWindowVisible(hwnd))
                    return;

                // Check extended styles - skip tool windows
                int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
                if ((exStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0)
                    return;

                // Get window rect with retry logic
                if (!TryGetWindowRect(hwnd, out NativeMethods.RECT rect, 3))
                    return;

                // Validate the window has a reasonable size
                int width = rect.Right - rect.Left;
                int height = rect.Bottom - rect.Top;
                if (width < 50 || height < 50)
                    return;

                // Success - update debounce timestamp AFTER all validation passes
                _lastForegroundChangeTime = DateTime.UtcNow;

                // Switch tracking to the new foreground window
                _lastFocusedWindow = hwnd;
                _lastWindowRect = rect;
                
                // Update overlay immediately
                _overlay?.UpdateFocusedWindow(rect);

                // Delay cursor clip slightly to ensure window is fully active
                Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
                {
                    if (_isEnabled && !_isResizing && _lastFocusedWindow == hwnd)
                        ApplyCursorClip(rect);
                });
            });
        }

        private void OnLocationChanged(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            // Only track location changes of our tracked window
            if (!_isEnabled || hwnd != _lastFocusedWindow || idObject != 0)
                return;

            Dispatcher.BeginInvoke(() =>
            {
                if (_isEnabled && NativeMethods.GetWindowRect(_lastFocusedWindow, out NativeMethods.RECT rect))
                {
                    if (!RectsEqual(_lastWindowRect, rect))
                    {
                        _lastWindowRect = rect;
                        // Update overlay immediately
                        _overlay?.UpdateFocusedWindow(rect);
                        
                        // Only update cursor clip if not in resize mode
                        if (!_isResizing)
                        {
                            ApplyCursorClip(rect);
                        }
                    }
                }
            });
        }

        private void OnMoveSizeStart(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            // Only track our window
            if (!_isEnabled || hwnd != _lastFocusedWindow)
                return;

            Dispatcher.BeginInvoke(() =>
            {
                _isResizing = true;
                // Release cursor clip during resize/move to allow the operation
                NativeMethods.ClipCursor(IntPtr.Zero);
            });
        }

        private void OnMoveSizeEnd(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            // Only track our window
            if (!_isEnabled || hwnd != _lastFocusedWindow)
                return;

            Dispatcher.BeginInvoke(() =>
            {
                _isResizing = false;
                // Re-apply cursor clip with the new window bounds
                if (NativeMethods.GetWindowRect(_lastFocusedWindow, out NativeMethods.RECT rect))
                {
                    _lastWindowRect = rect;
                    UpdateFocusedWindow(_lastFocusedWindow, rect);
                }
            });
        }

        private void DisableFocusMode()
        {
            // Stop monitoring
            _focusMonitorTimer?.Stop();
            _focusMonitorTimer = null;

            // Unhook Win32 events
            UnhookWinEvents();

            // Release cursor clip
            NativeMethods.ClipCursor(IntPtr.Zero);

            // Close overlay
            _overlay?.Close();
            _overlay = null;
            
            _lastFocusedWindow = IntPtr.Zero;
            _isResizing = false;
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

        private bool TryGetWindowRect(IntPtr hwnd, out NativeMethods.RECT rect, int maxRetries = 3)
        {
            rect = default;
            for (int i = 0; i < maxRetries; i++)
            {
                if (NativeMethods.GetWindowRect(hwnd, out rect))
                    return true;
                Thread.Sleep(10);
            }
            return false;
        }

        private void UpdateFocusedWindow(IntPtr hwnd, NativeMethods.RECT rect)
        {
            // Update overlay to show hole for this window
            _overlay?.UpdateFocusedWindow(rect);

            // Apply cursor clipping
            ApplyCursorClip(rect);
        }

        private void ApplyCursorClip(NativeMethods.RECT rect)
        {
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

        // Win32 event constants
        public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
        public const uint EVENT_SYSTEM_MOVESIZESTART = 0x000A;
        public const uint EVENT_SYSTEM_MOVESIZEEND = 0x000B;
        public const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
        public const uint WINEVENT_OUTOFCONTEXT = 0x0000;
        public const uint WINEVENT_SKIPOWNPROCESS = 0x0002;

        // Delegate for WinEventHook callbacks
        public delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

        [DllImport("user32.dll")]
        public static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnhookWinEvent(IntPtr hWinEventHook);

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

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_TRANSPARENT = 0x00000020;
        public const int WS_EX_LAYERED = 0x00080000;
    }
}
