/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Runtime.InteropServices;

namespace FufuLauncher.Services.Yae;

internal static class YaeNative
{
    public const uint CreateSuspended = 0x4;
    public const uint DontResolveDllReferences = 0x1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct StartupInfoW
    {
        public uint cb;
        public nint lpReserved;
        public nint lpDesktop;
        public nint lpTitle;
        public uint dwX;
        public uint dwY;
        public uint dwXSize;
        public uint dwYSize;
        public uint dwXCountChars;
        public uint dwYCountChars;
        public uint dwFillAttribute;
        public uint dwFlags;
        public ushort wShowWindow;
        public ushort cbReserved2;
        public nint lpReserved2;
        public nint hStdInput;
        public nint hStdOutput;
        public nint hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ProcessInformation
    {
        public nint hProcess;
        public nint hThread;
        public uint dwProcessId;
        public uint dwThreadId;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CreateProcessW(
        string? lpApplicationName, string lpCommandLine,
        nint lpProcessAttributes, nint lpThreadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandles,
        uint dwCreationFlags, nint lpEnvironment, string? lpCurrentDirectory,
        ref StartupInfoW lpStartupInfo, out ProcessInformation lpProcessInformation);

    [DllImport("kernel32.dll")]
    public static extern uint ResumeThread(nint hThread);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool TerminateProcess(nint hProcess, uint uExitCode);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseHandle(nint hObject);

    [DllImport("kernel32.dll")]
    public static extern nint VirtualAllocEx(nint hProcess, nint lpAddress, nuint dwSize, uint flAllocationType,
        uint flProtect);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool VirtualFreeEx(nint hProcess, nint lpAddress, nuint dwSize, uint dwFreeType);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool WriteProcessMemory(nint hProcess, nint lpBaseAddress, byte[] lpBuffer, nuint nSize,
        out nuint lpNumberOfBytesWritten);

    [DllImport("kernel32.dll")]
    public static extern nint CreateRemoteThread(nint hProcess, nint lpThreadAttributes, nuint dwStackSize,
        nint lpStartAddress, nint lpParameter, uint dwCreationFlags, out uint lpThreadId);

    [DllImport("kernel32.dll")]
    public static extern uint WaitForSingleObject(nint hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetExitCodeThread(nint hThread, out uint lpExitCode);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern nint GetModuleHandleW(string lpModuleName);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
    public static extern nint GetProcAddress(nint hModule, string lpProcName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern nint LoadLibraryEx(string lpFileName, nint hFile, uint dwFlags);

    public const uint TH32CS_SNAPMODULE = 0x00000008;
    public const uint TH32CS_SNAPMODULE32 = 0x00000010;

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern nint CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool Module32First(nint hSnapshot, ref ModuleEntry32 lpme);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool Module32Next(nint hSnapshot, ref ModuleEntry32 lpme);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct ModuleEntry32
    {
        public uint dwSize;
        public uint th32ModuleID;
        public uint th32ProcessID;
        public uint GlblcntUsage;
        public uint ProccntUsage;
        public nint modBaseAddr;
        public uint modBaseSize;
        public nint hModule;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szModule;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExePath;
    }
}