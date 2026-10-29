/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Runtime.InteropServices;

namespace FufuLauncher.Tray;

internal static class TrayNativeMethods
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct TrayRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct TrayPoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NotifyIconIdentifier
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public Guid guidItem;
    }

    [DllImport("shell32.dll", PreserveSig = true)]
    internal static extern int Shell_NotifyIconGetRect(ref NotifyIconIdentifier identifier, out TrayRect iconLocation);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetCursorPos(out TrayPoint point);
}
