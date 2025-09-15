using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using SharpHook;
using SharpHook.Native;

namespace Autodraw;

public static class Input
{
    //// Variables

    // Private
    private static readonly EventSimulator eventSim = new();

    // Public
    private static readonly TaskPoolGlobalHook _taskHook = new();
    private static Vector2 _mousePos;
    public static bool ForceUio { get; set; } = false;
    public static event EventHandler? MousePosUpdate;
    public static PixelPoint PrimaryScreenBounds { get; private set; }

    public static TaskPoolGlobalHook taskHook => _taskHook;
    public static Vector2 mousePos { get => _mousePos; private set => _mousePos = value; }

    //// Functions

    // Core

    // Removed unused isUio()

    public static void Start()
    {
        if (_taskHook.IsRunning) return;
        if (_taskHook.IsDisposed) return; // Avalonia Preview Fix.
        PrimaryScreenBounds = MainWindow.CurrentMainWindow.Screens.Primary.Bounds.TopLeft; // updates if main screen orientation changes

        _taskHook.MouseMoved += (sender, e) =>
        {
            mousePos = new Vector2(e.Data.X, e.Data.Y);
            
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) // || RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                mousePos = new Vector2(mousePos.X + PrimaryScreenBounds.X, mousePos.Y + PrimaryScreenBounds.Y);
            MousePosUpdate?.Invoke(null, EventArgs.Empty);
        };

        _taskHook.RunAsync();
    }

    public static void Stop()
    {
        // Never really need to call this UNLESS, we are closing the software.
        _taskHook.Dispose();
    }

    // Movement

    public static void MoveTo(short x, short y)
    {
        eventSim.SimulateMouseMovement(x, y);
        mousePos = new Vector2(x, y);
    }

    public static void MoveBy(short xOffset, short yOffset)
    {
        eventSim.SimulateMouseMovementRelative(xOffset, yOffset);
        mousePos = new Vector2(xOffset + (short)mousePos.X, yOffset + (short)mousePos.Y);
    }

    // Click Handling

    public static void SendClick(byte mouseType)
    {
        var button = mouseType == MouseTypes.MouseLeft ? MouseButton.Button1 : MouseButton.Button2;
        eventSim.SimulateMousePress(button);
        eventSim.SimulateMouseRelease(button);
    }

    public static void SendClickDown(byte mouseType)
    {
        var button = mouseType == MouseTypes.MouseLeft ? MouseButton.Button1 : MouseButton.Button2;
        eventSim.SimulateMousePress(button);
    }

    public static void SendClickUp(byte mouseType)
    {
        var button = mouseType == MouseTypes.MouseLeft ? MouseButton.Button1 : MouseButton.Button2;
        eventSim.SimulateMouseRelease(button);
    }

    public static void SendKeyDown(KeyCode keyCode)
    {
        eventSim.SimulateKeyPress(keyCode);
    }
    public static void SendKeyUp(KeyCode keyCode)
    {
        eventSim.SimulateKeyRelease(keyCode);
    }
    public static void SendText(string text)
    {
        eventSim.SimulateTextEntry(text);
    }

    public static class MouseTypes
    {
        public const byte MouseLeft = 1;
        public const byte MouseRight = 2;
    }
}