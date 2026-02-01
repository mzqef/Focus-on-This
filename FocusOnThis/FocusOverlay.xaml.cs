using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;

namespace FocusOnThis
{
    public partial class FocusOverlay : Window
    {
        private Rectangle? _maskRectangle;

        public FocusOverlay()
        {
            InitializeComponent();
            
            // Make sure this window is completely transparent to mouse events
            this.IsHitTestVisible = false;
            
            // Position to cover all screens using WPF virtual screen coordinates
            this.Left = SystemParameters.VirtualScreenLeft;
            this.Top = SystemParameters.VirtualScreenTop;
            this.Width = SystemParameters.VirtualScreenWidth;
            this.Height = SystemParameters.VirtualScreenHeight;
            
            // Create the semi-transparent mask
            CreateMask();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            
            // Apply WS_EX_TRANSPARENT and WS_EX_TOOLWINDOW extended styles
            // This ensures the overlay window is completely click-through and
            // doesn't interfere with foreground window detection during Alt+Tab
            var helper = new WindowInteropHelper(this);
            int exStyle = NativeMethods.GetWindowLong(helper.Handle, NativeMethods.GWL_EXSTYLE);
            exStyle |= NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_TOOLWINDOW;
            NativeMethods.SetWindowLong(helper.Handle, NativeMethods.GWL_EXSTYLE, exStyle);
        }

        /// <summary>
        /// Gets the DPI scale factor to convert from device pixels to WPF units.
        /// </summary>
        private Matrix GetDpiScaleMatrix()
        {
            var source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget != null)
            {
                return source.CompositionTarget.TransformFromDevice;
            }
            // Fallback to identity matrix if PresentationSource is unavailable.
            // This may occur during window initialization before the window is fully rendered.
            // At 100% DPI scaling, identity matrix produces correct results.
            // For higher DPI settings, the first valid call will correct any temporary mismatch.
            return Matrix.Identity;
        }

        private void CreateMask()
        {
            // Create a semi-transparent black overlay
            _maskRectangle = new Rectangle
            {
                Fill = new SolidColorBrush(Color.FromArgb(180, 0, 0, 0)),
                Width = this.Width,
                Height = this.Height
            };
            
            OverlayCanvas.Children.Add(_maskRectangle);
        }

        public void UpdateFocusedWindow(NativeMethods.RECT windowRect)
        {
            Dispatcher.Invoke(() =>
            {
                // Clear existing overlay
                OverlayCanvas.Children.Clear();

                // Get the DPI scale matrix to convert from device pixels to WPF units
                var dpiMatrix = GetDpiScaleMatrix();

                // Convert window rect from device pixels to WPF units
                var topLeft = dpiMatrix.Transform(new Point(windowRect.Left, windowRect.Top));
                var bottomRight = dpiMatrix.Transform(new Point(windowRect.Right, windowRect.Bottom));

                // Calculate the window position relative to the virtual screen origin (in WPF units)
                double windowLeft = topLeft.X - SystemParameters.VirtualScreenLeft;
                double windowTop = topLeft.Y - SystemParameters.VirtualScreenTop;
                double windowWidth = bottomRight.X - topLeft.X;
                double windowHeight = bottomRight.Y - topLeft.Y;

                // Create a geometry that covers the entire screen with a hole for the focused window
                var fullScreenGeometry = new RectangleGeometry(
                    new Rect(0, 0, this.Width, this.Height));

                // Create a geometry for the focused window (in WPF units relative to virtual screen)
                var windowGeometry = new RectangleGeometry(
                    new Rect(windowLeft, windowTop, windowWidth, windowHeight));

                // Combine geometries: full screen minus the window
                var combinedGeometry = new CombinedGeometry(
                    GeometryCombineMode.Exclude,
                    fullScreenGeometry,
                    windowGeometry);

                // Create a path with the combined geometry
                var path = new System.Windows.Shapes.Path
                {
                    Fill = new SolidColorBrush(Color.FromArgb(180, 0, 0, 0)),
                    Data = combinedGeometry
                };

                OverlayCanvas.Children.Add(path);
            });
        }
    }
}
