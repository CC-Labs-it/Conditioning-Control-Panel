using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Versioning;

namespace ConditioningControlPanel.Avalonia.Platform
{
    /// <summary>
    /// A Windows .lnk through the shell's own COM writer: WPF StartupManager.CreateShortcut
    /// (ConditioningControlPanel/Services/StartupManager.cs:134), line for line. Shared by the launcher's
    /// desktop shortcuts and the Startup-folder entry.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal static class ShellLinkFile
    {
        internal static void Save(string path, string target, string workingDirectory, string description,
            string arguments, string? icon = null)
        {
            var link = (IShellLinkW)new ShellLink();
            try
            {
                link.SetPath(target);
                link.SetWorkingDirectory(workingDirectory);
                link.SetDescription(description);
                if (!string.IsNullOrEmpty(arguments)) link.SetArguments(arguments);
                if (!string.IsNullOrEmpty(icon)) link.SetIconLocation(icon, 0);
                var file = (IPersistFile)link;
                file.Save(path, false);
                Marshal.ReleaseComObject(file);
            }
            finally { Marshal.ReleaseComObject(link); }
        }

        /// <summary>The target and arguments a .lnk holds (tests, and the Startup entry's self-check).</summary>
        internal static (string Target, string Arguments) Read(string path)
        {
            var link = (IShellLinkW)new ShellLink();
            var buffer = Marshal.AllocCoTaskMem(2 * 1024 * 2);
            try
            {
                ((IPersistFile)link).Load(path, 0);
                link.GetPath(buffer, 1024, IntPtr.Zero, 0);
                var target = Marshal.PtrToStringUni(buffer) ?? "";
                link.GetArguments(buffer, 1024);
                return (target, Marshal.PtrToStringUni(buffer) ?? "");
            }
            finally
            {
                Marshal.FreeCoTaskMem(buffer);
                Marshal.ReleaseComObject(link);
            }
        }

        [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
        private class ShellLink { }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
        private interface IShellLinkW
        {
            void GetPath(IntPtr pszFile, int cch, IntPtr pfd, int fFlags);
            void GetIDList(out IntPtr ppidl);
            void SetIDList(IntPtr pidl);
            void GetDescription(IntPtr pszName, int cch);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetWorkingDirectory(IntPtr pszDir, int cch);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
            void GetArguments(IntPtr pszArgs, int cch);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
            void GetHotkey(out short pwHotkey);
            void SetHotkey(short wHotkey);
            void GetShowCmd(out int piShowCmd);
            void SetShowCmd(int iShowCmd);
            void GetIconLocation(IntPtr pszIconPath, int cch, out int piIcon);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
            void Resolve(IntPtr hwnd, int fFlags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        }
    }
}
