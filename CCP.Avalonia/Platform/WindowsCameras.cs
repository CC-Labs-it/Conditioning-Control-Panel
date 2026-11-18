using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using System.Runtime.InteropServices.ComTypes;
using Serilog;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Platform
{
    /// <summary>
    /// Enumerates connected video-capture devices via the DirectShow
    /// SystemDeviceEnum / VideoInputDeviceCategory COM interfaces. Returns the
    /// FriendlyName string for each registered camera, in the same order
    /// DirectShow exposes them.
    ///
    /// DSHOW capture uses this enumeration order. No device is opened.
    /// </summary>
    internal static class WindowsCameras
    {
        internal static string DeviceName(string? name) =>
            string.IsNullOrWhiteSpace(name) ? Loc.Get("webcam_unnamed_device") : name;

        public readonly record struct WebcamDevice(int Index, string Name);

        private static readonly Guid CLSID_SystemDeviceEnum = new("62BE5D10-60EB-11D0-BD3B-00A0C911CE86");
        private static readonly Guid CLSID_VideoInputDeviceCategory = new("860BB310-5D01-11D0-BD3B-00A0C911CE86");
        private static readonly Guid IID_IPropertyBag = new("55272A00-42CB-11CE-8135-00AA004BB851");

        [ComImport, Guid("29840822-5B84-11D0-BD3B-00A0C911CE86"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ICreateDevEnum
        {
            [PreserveSig]
            int CreateClassEnumerator(ref Guid pType, out IEnumMoniker ppEnumMoniker, int dwFlags);
        }

        [ComImport, Guid("55272A00-42CB-11CE-8135-00AA004BB851"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPropertyBag
        {
            [PreserveSig]
            int Read([MarshalAs(UnmanagedType.LPWStr)] string pszPropName,
                     [In, Out, MarshalAs(UnmanagedType.Struct)] ref object pVar,
                     IntPtr pErrorLog);
            [PreserveSig]
            int Write([MarshalAs(UnmanagedType.LPWStr)] string pszPropName, ref object pVar);
        }

        /// <summary>
        /// The forgiving form, and the one the webcam tracking feature uses: anything that goes
        /// wrong is logged and read as "no cameras", because a device picker that throws is worse
        /// than an empty one. Callers that must be able to tell "there is no camera" apart from
        /// "the camera could not be counted" want <see cref="EnumerateStrict"/>.
        /// </summary>
        public static IReadOnlyList<WebcamDevice> Enumerate()
        {
            if (!OperatingSystem.IsWindows()) return Array.Empty<WebcamDevice>();
            return EnumerateWithFallback(EnumerateStrict, EnumerateMediaFoundationStrict);
        }

        internal static IReadOnlyList<WebcamDevice> EnumerateOrEmpty(Func<IReadOnlyList<WebcamDevice>> enumerate)
        {
            try
            {
                return enumerate();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "WindowsCameras: enumeration threw");
                return new List<WebcamDevice>();
            }
        }

        internal static IReadOnlyList<WebcamDevice> EnumerateWithFallback(
            Func<IReadOnlyList<WebcamDevice>> directShow, Func<IReadOnlyList<WebcamDevice>> mediaFoundation)
        {
            var devices = EnumerateOrEmpty(directShow);
            return devices.Count > 0 ? devices : EnumerateOrEmpty(mediaFoundation);
        }

        internal static bool CameraPresent(bool windows, Func<IReadOnlyList<WebcamDevice>> directShow,
            Func<IReadOnlyList<WebcamDevice>> mediaFoundation, Func<bool> linuxProbe) =>
            windows ? directShow().Count > 0 || mediaFoundation().Count > 0 : linuxProbe();

        private static readonly Guid SourceType = new("c60ac5fe-252a-478f-a0ef-bc8fa5f7cad3");
        private static readonly Guid VideoCapture = new("8ac3587a-4ae7-42d8-99e0-0a6013eef90f");
        private static readonly Guid FriendlyName = new("60d0e559-52f8-4fa2-bbce-acdb34a8ec01");

        [DllImport("mfplat.dll", ExactSpelling = true)]
        private static extern int MFStartup(int version, int flags);
        [DllImport("mfplat.dll", ExactSpelling = true)]
        private static extern int MFShutdown();
        [DllImport("mfplat.dll", ExactSpelling = true)]
        private static extern int MFCreateAttributes(out IntPtr attributes, int initialSize);
        [DllImport("mf.dll", ExactSpelling = true)]
        private static extern int MFEnumDeviceSources(IntPtr attributes, out IntPtr devices, out int count);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int SetGuid(IntPtr self, ref Guid key, ref Guid value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetAllocatedString(IntPtr self, ref Guid key, out IntPtr value, out int length);

        private static T Method<T>(IntPtr instance, int slot) where T : Delegate =>
            Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * IntPtr.Size));

        internal static IReadOnlyList<WebcamDevice> EnumerateMediaFoundationStrict()
        {
            if (!OperatingSystem.IsWindows()) return Array.Empty<WebcamDevice>();
            return Task.Run(EnumerateMediaFoundation).WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
        }

        [SupportedOSPlatform("windows")]
        private static IReadOnlyList<WebcamDevice> EnumerateMediaFoundation()
        {
            Marshal.ThrowExceptionForHR(MFStartup(0x20070, 0));
            IntPtr attributes = IntPtr.Zero, sources = IntPtr.Zero;
            int count = 0;
            try
            {
                Marshal.ThrowExceptionForHR(MFCreateAttributes(out attributes, 1));
                var key = SourceType;
                var value = VideoCapture;
                Marshal.ThrowExceptionForHR(Method<SetGuid>(attributes, 24)(attributes, ref key, ref value));
                Marshal.ThrowExceptionForHR(MFEnumDeviceSources(attributes, out sources, out count));
                var result = new List<WebcamDevice>(count);
                for (int i = 0; i < count; i++)
                {
                    var source = Marshal.ReadIntPtr(sources, i * IntPtr.Size);
                    var nameKey = FriendlyName;
                    IntPtr name = IntPtr.Zero;
                    try
                    {
                        int hr = Method<GetAllocatedString>(source, 13)(source, ref nameKey, out name, out _);
                        result.Add(new WebcamDevice(i, DeviceName(hr >= 0 ? Marshal.PtrToStringUni(name) : null)));
                    }
                    finally { if (name != IntPtr.Zero) Marshal.FreeCoTaskMem(name); }
                }
                return result;
            }
            finally
            {
                if (sources != IntPtr.Zero)
                {
                    for (int i = 0; i < count; i++)
                    {
                        var source = Marshal.ReadIntPtr(sources, i * IntPtr.Size);
                        if (source != IntPtr.Zero) Marshal.Release(source);
                    }
                    Marshal.FreeCoTaskMem(sources);
                }
                if (attributes != IntPtr.Zero) Marshal.Release(attributes);
                MFShutdown();
            }
        }

        /// <summary>
        /// The same enumeration with the swallow taken off: a broken COM registration, a wedged
        /// driver or a privacy block THROWS instead of returning an empty list. Only a clean run
        /// that genuinely found nothing returns empty. The quest hardware gate needs that
        /// distinction - an error read as "absent" is an answer it would then trust forever - and
        /// nothing else should care.
        /// </summary>
        public static IReadOnlyList<WebcamDevice> EnumerateStrict()
        {
            if (!OperatingSystem.IsWindows()) return Array.Empty<WebcamDevice>();
            return EnumerateDirectShow();
        }

        [SupportedOSPlatform("windows")]
        private static IReadOnlyList<WebcamDevice> EnumerateDirectShow()
        {
            var devices = new List<WebcamDevice>();
            ICreateDevEnum? devEnum = null;
            IEnumMoniker? enumMoniker = null;

            try
            {
                var devEnumType = Type.GetTypeFromCLSID(CLSID_SystemDeviceEnum);
                if (devEnumType == null)
                    throw new InvalidOperationException("SystemDeviceEnum CLSID could not be resolved");

                devEnum = Activator.CreateInstance(devEnumType) as ICreateDevEnum;
                if (devEnum == null)
                    throw new InvalidOperationException("ICreateDevEnum activation returned null");

                Guid videoCat = CLSID_VideoInputDeviceCategory;
                int hr = devEnum.CreateClassEnumerator(ref videoCat, out enumMoniker, 0);
                // hr == 0 (S_OK) → enumMoniker valid; hr == 1 (S_FALSE) → no devices.
                // A negative hr is a FAILURE, not an empty machine, so it throws.
                if (hr < 0)
                    throw Marshal.GetExceptionForHR(hr)
                        ?? new InvalidOperationException($"CreateClassEnumerator failed (0x{hr:X8})");
                if (hr != 0 || enumMoniker == null)
                {
                    return devices;
                }

                var monikers = new IMoniker[1];
                IntPtr fetched = IntPtr.Zero;
                int idx = 0;
                while (enumMoniker.Next(1, monikers, fetched) == 0)
                {
                    var moniker = monikers[0];
                    if (moniker == null) continue;

                    string name = DeviceName(null);
                    object? propBagObj = null;
                    try
                    {
                        Guid propBagIid = IID_IPropertyBag;
                        moniker.BindToStorage(null, null, ref propBagIid, out propBagObj);
                        if (propBagObj is IPropertyBag pb)
                        {
                            object value = string.Empty;
                            if (pb.Read("FriendlyName", ref value, IntPtr.Zero) == 0 && value is string s && !string.IsNullOrWhiteSpace(s))
                            {
                                name = s;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Debug(ex, "WindowsCameras: failed to read FriendlyName for device {Index}", idx);
                    }
                    finally
                    {
                        if (propBagObj != null)
                        {
                            try { Marshal.ReleaseComObject(propBagObj); } catch { }
                        }
                        try { Marshal.ReleaseComObject(moniker); } catch { }
                    }

                    devices.Add(new WebcamDevice(idx, name));
                    idx++;
                }
            }
            finally
            {
                if (enumMoniker != null)
                {
                    try { Marshal.ReleaseComObject(enumMoniker); } catch { }
                }
                if (devEnum != null)
                {
                    try { Marshal.ReleaseComObject(devEnum); } catch { }
                }
            }

            return devices;
        }
    }
}
