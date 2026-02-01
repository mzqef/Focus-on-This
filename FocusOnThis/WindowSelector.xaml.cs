using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FocusOnThis
{
    public partial class WindowSelector : Window
    {
        public IntPtr SelectedWindowHandle { get; private set; } = IntPtr.Zero;
        public NativeMethods.RECT SelectedWindowRect { get; private set; }

        private List<WindowInfo> _windows = new List<WindowInfo>();

        public WindowSelector()
        {
            InitializeComponent();
            RefreshWindowList();
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Allow dragging the window by the header
            DragMove();
        }

        private void RefreshWindowList()
        {
            _windows.Clear();

            // Enumerate all visible windows
            WindowEnumerator.EnumWindows((hWnd, lParam) =>
            {
                // Only include visible windows
                if (!WindowEnumerator.IsWindowVisible(hWnd))
                    return true;

                // Skip windows with no title
                string title = WindowEnumerator.GetWindowTitle(hWnd);
                if (string.IsNullOrWhiteSpace(title))
                    return true;

                // Skip our own windows
                if (IsOurWindow(hWnd))
                    return true;

                // Skip certain system windows
                if (IsSystemWindow(title))
                    return true;

                // Get window rectangle
                if (NativeMethods.GetWindowRect(hWnd, out NativeMethods.RECT rect))
                {
                    // Skip windows with zero size
                    if (rect.Right - rect.Left <= 0 || rect.Bottom - rect.Top <= 0)
                        return true;

                    _windows.Add(new WindowInfo
                    {
                        Handle = hWnd,
                        Title = title,
                        Rect = rect,
                        Icon = IconExtractor.GetWindowIcon(hWnd)
                    });
                }

                return true; // Continue enumeration
            }, IntPtr.Zero);

            WindowListBox.ItemsSource = null;
            WindowListBox.ItemsSource = _windows;

            if (_windows.Count > 0)
            {
                WindowListBox.SelectedIndex = 0;
            }
        }

        private bool IsOurWindow(IntPtr hWnd)
        {
            string title = WindowEnumerator.GetWindowTitle(hWnd);
            return title == "Focus on This - Control" || 
                   title == "FocusOverlay" ||
                   title == "Select Window to Focus";
        }

        private bool IsSystemWindow(string title)
        {
            // Skip some known system windows that shouldn't be focused
            return title == "Program Manager" ||
                   title == "Windows Input Experience" ||
                   title == "Microsoft Text Input Application";
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshWindowList();
        }

        private void SelectButton_Click(object sender, RoutedEventArgs e)
        {
            SelectCurrentWindow();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void WindowListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            SelectCurrentWindow();
        }

        private void SelectCurrentWindow()
        {
            if (WindowListBox.SelectedItem is WindowInfo selectedWindow)
            {
                SelectedWindowHandle = selectedWindow.Handle;
                SelectedWindowRect = selectedWindow.Rect;
                DialogResult = true;
                Close();
            }
        }
    }

    public class WindowInfo
    {
        public IntPtr Handle { get; set; }
        public string Title { get; set; } = string.Empty;
        public NativeMethods.RECT Rect { get; set; }
        public ImageSource? Icon { get; set; }
    }

    public static class IconExtractor
    {
        private const int GCL_HICON = -14;
        private const int GCL_HICONSM = -34;
        private const int ICON_SMALL = 0;
        private const int ICON_BIG = 1;
        private const int ICON_SMALL2 = 2;
        private const uint WM_GETICON = 0x007F;
        private const uint SMTO_ABORTIFHUNG = 0x0002;

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

        [DllImport("user32.dll", EntryPoint = "GetClassLongPtr")]
        private static extern IntPtr GetClassLongPtr64(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "GetClassLong")]
        private static extern IntPtr GetClassLongPtr32(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("psapi.dll", CharSet = CharSet.Unicode)]
        private static extern uint GetModuleFileNameEx(IntPtr hProcess, IntPtr hModule, StringBuilder lpFilename, uint nSize);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr ExtractIcon(IntPtr hInst, string lpszExeFileName, int nIconIndex);

        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        private static IntPtr GetClassLongPtr(IntPtr hWnd, int nIndex)
        {
            return IntPtr.Size == 8 ? GetClassLongPtr64(hWnd, nIndex) : GetClassLongPtr32(hWnd, nIndex);
        }

        public static ImageSource? GetWindowIcon(IntPtr hwnd)
        {
            IntPtr hIcon = IntPtr.Zero;
            bool needsDestroy = false;

            try
            {
                // Try WM_GETICON with small icon first (better quality for our use case)
                SendMessageTimeout(hwnd, WM_GETICON, (IntPtr)ICON_SMALL2, IntPtr.Zero, SMTO_ABORTIFHUNG, 100, out hIcon);
                
                if (hIcon == IntPtr.Zero)
                    SendMessageTimeout(hwnd, WM_GETICON, (IntPtr)ICON_SMALL, IntPtr.Zero, SMTO_ABORTIFHUNG, 100, out hIcon);

                if (hIcon == IntPtr.Zero)
                    SendMessageTimeout(hwnd, WM_GETICON, (IntPtr)ICON_BIG, IntPtr.Zero, SMTO_ABORTIFHUNG, 100, out hIcon);

                // Try class icon
                if (hIcon == IntPtr.Zero)
                    hIcon = GetClassLongPtr(hwnd, GCL_HICONSM);

                if (hIcon == IntPtr.Zero)
                    hIcon = GetClassLongPtr(hwnd, GCL_HICON);

                // Try to get icon from executable
                if (hIcon == IntPtr.Zero)
                {
                    hIcon = GetIconFromProcess(hwnd);
                    needsDestroy = hIcon != IntPtr.Zero;
                }

                if (hIcon != IntPtr.Zero)
                {
                    var iconSource = Imaging.CreateBitmapSourceFromHIcon(
                        hIcon,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                    iconSource.Freeze(); // Make it thread-safe
                    return iconSource;
                }
            }
            catch
            {
                // Silently ignore icon extraction failures
            }
            finally
            {
                if (needsDestroy && hIcon != IntPtr.Zero)
                    DestroyIcon(hIcon);
            }

            return null;
        }

        private static IntPtr GetIconFromProcess(IntPtr hwnd)
        {
            try
            {
                GetWindowThreadProcessId(hwnd, out uint processId);
                IntPtr hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
                if (hProcess == IntPtr.Zero)
                    return IntPtr.Zero;

                try
                {
                    var sb = new StringBuilder(1024);
                    if (GetModuleFileNameEx(hProcess, IntPtr.Zero, sb, (uint)sb.Capacity) > 0)
                    {
                        string exePath = sb.ToString();
                        IntPtr hIcon = ExtractIcon(IntPtr.Zero, exePath, 0);
                        if (hIcon != IntPtr.Zero && hIcon.ToInt64() != 1) // ExtractIcon returns 1 on failure
                            return hIcon;
                    }
                }
                finally
                {
                    CloseHandle(hProcess);
                }
            }
            catch
            {
                // Silently ignore
            }

            return IntPtr.Zero;
        }
    }

    public static class WindowEnumerator
    {
        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        // Reusable StringBuilder to reduce allocations during window enumeration
        [ThreadStatic]
        private static StringBuilder? _titleBuilder;
        private const int MaxTitleLength = 256;

        public static string GetWindowTitle(IntPtr hWnd)
        {
            int length = GetWindowTextLength(hWnd);
            if (length == 0)
                return string.Empty;

            // Reuse StringBuilder to reduce allocations
            if (_titleBuilder == null || _titleBuilder.Capacity < length + 1)
            {
                _titleBuilder = new StringBuilder(Math.Max(length + 1, MaxTitleLength));
            }
            else
            {
                _titleBuilder.Clear();
            }

            GetWindowText(hWnd, _titleBuilder, _titleBuilder.Capacity);
            return _titleBuilder.ToString();
        }
    }
}
