/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using FufuLauncher.Helpers;

namespace FufuLauncher.Services.Yae;

internal sealed class YaeGameProcess : IDisposable
{
    public uint Id
    {
        get;
    }

    public nint MainThreadHandle
    {
        get;
    }

    private readonly nint _processHandle;
    private readonly nint _mainThreadHandle;
    private readonly nint _entryThreadHandle;
    private bool _disposed;

    public YaeGameProcess(string gameExePath, string dllPath)
    {
        var startupInfo = new YaeNative.StartupInfoW
        {
            cb = (uint)Marshal.SizeOf<YaeNative.StartupInfoW>(),
        };
        var workDir = Path.GetDirectoryName(gameExePath) ?? AppContext.BaseDirectory;
        var commandLine = $"\"{gameExePath}\"";

        if (!YaeNative.CreateProcessW(
                gameExePath, commandLine, 0, 0, false, YaeNative.CreateSuspended, 0, workDir,
                ref startupInfo, out var processInfo))
        {
            throw new YaeGameCreateException(string.Format("Yae_ErrCreateProcessFailed_Format".GetLocalized(), Marshal.GetLastPInvokeErrorMessage()));
        }

        _processHandle = processInfo.hProcess;
        _mainThreadHandle = processInfo.hThread;
        Id = processInfo.dwProcessId;

        try
        {
            var targetBase = InjectDllAndGetBase(processInfo.hProcess, processInfo.dwProcessId, dllPath);
            var entryRva = ResolveYaeMainRva(dllPath);
            _entryThreadHandle = StartRemoteThread(processInfo.hProcess, targetBase + entryRva);
        }
        catch (Exception ex)
        {
            YaeNative.TerminateProcess(_processHandle, 1);
            CloseHandles();
            throw new YaeInjectionException(string.Format("Yae_ErrInjectFailed_Format".GetLocalized(), ex.Message), ex);
        }
    }


    public bool IsRunning => YaeNative.WaitForSingleObject(_processHandle, 0) == 0x102;


    public void ResumeMainThread() => YaeNative.ResumeThread(_mainThreadHandle);


    public void Kill() => YaeNative.TerminateProcess(_processHandle, 0);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CloseHandles();
    }

    private void CloseHandles()
    {
        YaeNative.CloseHandle(_processHandle);
        YaeNative.CloseHandle(_mainThreadHandle);
        if (_entryThreadHandle != 0) YaeNative.CloseHandle(_entryThreadHandle);
    }

    private static nint InjectDllAndGetBase(nint hProcess, uint processId, string dllPath)
    {
        var libPathBytes = Encoding.Unicode.GetBytes(dllPath + "\0");
        var libPathLen = (uint)libPathBytes.Length;
        var remotePath = YaeNative.VirtualAllocEx(hProcess, 0, libPathLen, 0x3000, 0x04);
        if (remotePath == 0)
        {
            throw new Win32Exception("VirtualAllocEx failed.");
        }

        try
        {
            if (!YaeNative.WriteProcessMemory(hProcess, remotePath, libPathBytes, libPathLen, out _))
            {
                throw new Win32Exception("WriteProcessMemory failed.");
            }

            var kernel32 = YaeNative.GetModuleHandleW("kernel32.dll");
            var loadLibraryW = YaeNative.GetProcAddress(kernel32, "LoadLibraryW");
            if (loadLibraryW == 0)
            {
                throw new Win32Exception("GetProcAddress(LoadLibraryW) failed.");
            }

            var loadThread = YaeNative.CreateRemoteThread(hProcess, 0, 0, loadLibraryW, remotePath, 0, out _);
            if (loadThread == 0)
            {
                throw new Win32Exception("CreateRemoteThread(LoadLibraryW) failed.");
            }

            try
            {
                if (YaeNative.WaitForSingleObject(loadThread, 10000) != 0)
                {
                    throw new Win32Exception(string.Format("Yae_ErrDllLoadTimeout_Format".GetLocalized(), dllPath));
                }
            }
            finally
            {
                YaeNative.CloseHandle(loadThread);
            }
        }
        finally
        {
            YaeNative.VirtualFreeEx(hProcess, remotePath, 0, 0x8000);
        }


        var moduleBase = FindRemoteModuleBase(processId, dllPath);
        if (moduleBase == 0)
        {
            throw new Win32Exception(string.Format("Yae_ErrModuleNotFound_Format".GetLocalized(), dllPath));
        }

        return moduleBase;
    }

    private static nint FindRemoteModuleBase(uint processId, string dllPath)
    {
        const uint TH32CS_SNAPMODULE = 0x00000008;
        const uint TH32CS_SNAPMODULE32 = 0x00000010;
        const uint ERROR_BAD_LENGTH = 24;

        var targetFullPath = SafeGetFullPath(dllPath);
        var targetFileName = Path.GetFileName(targetFullPath);

        nint snapshot;
        do
        {
            snapshot = YaeNative.CreateToolhelp32Snapshot(
                TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32, processId);


            if (Marshal.GetLastSystemError() is not (0 or (int)ERROR_BAD_LENGTH))
            {
                break;
            }
        } while (snapshot == -1);

        if (snapshot == -1)
        {
            return 0;
        }

        try
        {
            var entry = new YaeNative.ModuleEntry32
            {
                dwSize = (uint)Marshal.SizeOf<YaeNative.ModuleEntry32>(),
            };

            if (!YaeNative.Module32First(snapshot, ref entry))
            {
                return 0;
            }

            do
            {
                var modulePath = entry.szExePath;
                if (string.IsNullOrEmpty(modulePath))
                {
                    continue;
                }

                var moduleFullPath = SafeGetFullPath(modulePath);


                if (string.Equals(moduleFullPath, targetFullPath, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(Path.GetFileName(moduleFullPath), targetFileName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return entry.modBaseAddr;
                }
            } while (YaeNative.Module32Next(snapshot, ref entry));
        }
        finally
        {
            YaeNative.CloseHandle(snapshot);
        }

        return 0;
    }

    private static string SafeGetFullPath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return path;
        }
    }

    private static nint ResolveYaeMainRva(string dllPath)
    {
        var localHandle = YaeNative.LoadLibraryEx(dllPath, 0, YaeNative.DontResolveDllReferences);
        if (localHandle == 0)
        {
            throw new Win32Exception("LoadLibraryEx(DONT_RESOLVE_DLL_REFERENCES) failed.");
        }

        var mainProc = YaeNative.GetProcAddress(localHandle, "YaeMain");
        if (mainProc == 0)
        {
            throw new Win32Exception("GetProcAddress(YaeMain) failed.");
        }

        return mainProc - localHandle;
    }

    private static nint StartRemoteThread(nint hProcess, nint startAddress)
    {
        var thread = YaeNative.CreateRemoteThread(hProcess, 0, 0, startAddress, 0, 0, out _);
        if (thread == 0)
        {
            throw new Win32Exception("CreateRemoteThread(YaeMain) failed.");
        }

        return thread;
    }
}