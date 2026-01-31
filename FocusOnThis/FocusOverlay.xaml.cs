using System;
using System.Windows;
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
            
            // Position to cover all screens
            this.Left = SystemParameters.VirtualScreenLeft;
            this.Top = SystemParameters.VirtualScreenTop;
            this.Width = SystemParameters.VirtualScreenWidth;
            this.Height = SystemParameters.VirtualScreenHeight;
            
            // Create the semi-transparent mask
            CreateMask();
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

                // Create a geometry that covers the entire screen with a hole for the focused window
                var fullScreenGeometry = new RectangleGeometry(
                    new Rect(0, 0, this.Width, this.Height));

                // Create a geometry for the focused window (relative to virtual screen)
                var windowGeometry = new RectangleGeometry(
                    new Rect(
                        windowRect.Left - SystemParameters.VirtualScreenLeft,
                        windowRect.Top - SystemParameters.VirtualScreenTop,
                        windowRect.Right - windowRect.Left,
                        windowRect.Bottom - windowRect.Top));

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
