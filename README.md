# Focus-on-This

A Windows application that helps you focus on specific windows by masking the rest of the screen and preventing mouse movement outside the focused window.

## Features

- **Screen Masking**: Automatically masks all parts of the screen except the currently focused window
- **Mouse Confinement**: Prevents mouse cursor from moving outside the focused window boundaries
- **Windows UI Automation Compatible**: Uses Windows UI Automation (UIA) for reliable window detection
- **Auto-Hide Control**: Small control button at the top of the screen that auto-hides when not in use
- **Works with Any Window Size**: Functions correctly even when windows are not maximized

## Requirements

- Windows 10 or Windows 11
- .NET 8.0 or later

## Building the Application

1. Install the .NET 8.0 SDK or later
2. Clone this repository
3. Build the solution:
   ```
   dotnet build FocusOnThis.sln
   ```
4. Run the application:
   ```
   dotnet run --project FocusOnThis/FocusOnThis.csproj
   ```

## Usage

1. Launch the application - a small control button will appear at the top center of your screen
2. Click the control button to toggle Focus Mode ON/OFF
   - ⚪ (White): Focus Mode is OFF
   - 🟢 (Green): Focus Mode is ON
3. When Focus Mode is ON:
   - The currently focused window will be highlighted (clear)
   - All other areas of the screen will be masked with a semi-transparent dark overlay
   - Your mouse cursor will be confined to the focused window
   - Moving, resizing, or reshaping the focused window automatically updates the mask and cursor confinement
4. Switch between windows normally - the focus mask will automatically follow the active window
5. Click the control button again to disable Focus Mode
6. Right-click the control button for additional options (including Quit)

## How It Works

The application uses:
- **Windows UI Automation (UIA)** to detect the currently focused window
- **WPF Overlays** with geometry combinations to create a screen mask with a transparent hole
- **Windows API (ClipCursor)** to confine the mouse cursor to the focused window boundaries
- **DispatcherTimer** for periodic monitoring of window focus changes

## Technical Details

- Built with C# and WPF (.NET 8.0)
- Uses `System.Windows.Automation` for UIA integration
- Uses native Windows API calls for cursor confinement
- Supports multi-monitor setups via `SystemParameters.VirtualScreen*` properties
- DPI-aware for modern high-DPI displays
