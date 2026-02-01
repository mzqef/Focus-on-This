using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Input;

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
                        Rect = rect
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
