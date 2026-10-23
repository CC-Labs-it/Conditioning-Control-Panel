using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;

namespace ConditioningControlPanel.Services.Video.Floating
{
    /// <summary>What the pointer router needs from a floating frame.</summary>
    internal interface IFloatingPointerTarget
    {
        IntPtr Hwnd { get; }
        /// <summary>Left button went down on this window (device px). True = it wants the moves.</summary>
        bool PointerDown(Point px);
        void PointerMove(Point px);
        void PointerUp(Point px);
    }

    /// <summary>
    /// ONE low-level mouse hook for every floating video window. This is how a press reaches the
    /// frame when the pointer is over the video itself: the default engine paints the clip in a
    /// WebView2 child HWND (another process), so WPF never sees that mouse; the hook does, and
    /// asks Windows which top-level window sits under the point. The same route serves the plain
    /// WPF window of the LibVLC path, so both engines drag the same way.
    ///
    /// The hook only LISTENS: it never swallows a press, so the page still gets its click (the
    /// attention targets live in there) and so does the WPF window. Moves are wired only while a
    /// window is held. Everything here runs on the UI thread, which is the thread that installs
    /// the hook and therefore the thread Windows calls it on.
    /// </summary>
    internal static class FloatingVideoPointer
    {
        private static readonly List<IFloatingPointerTarget> Targets = new();
        private static GlobalMouseHook? _hook;
        private static IFloatingPointerTarget? _held;

        public static void Register(IFloatingPointerTarget target)
        {
            if (Targets.Contains(target)) return;
            Targets.Add(target);
            if (_hook != null) return;
            _hook = new GlobalMouseHook { LeftDown = OnDown, LeftUp = OnUp };
            _hook.Start();
        }

        public static void Unregister(IFloatingPointerTarget target)
        {
            Targets.Remove(target);
            if (ReferenceEquals(_held, target)) Let();
            if (Targets.Count > 0 || _hook == null) return;
            var hook = _hook;
            _hook = null;
            try { hook.Dispose(); } catch (Exception ex) { Diag.Swallowed(ex); }
        }

        /// <summary>The held window let go by itself (lost button-up, a stop).</summary>
        public static void Released(IFloatingPointerTarget target)
        {
            if (ReferenceEquals(_held, target)) Let();
        }

        private static void Let()
        {
            _held = null;
            if (_hook != null) _hook.MouseMove = null;
        }

        private static bool OnDown(Point px)
        {
            try
            {
                if (Targets.Count == 0) return false;
                var root = GetAncestor(WindowFromPoint(new POINT { X = (int)px.X, Y = (int)px.Y }), GA_ROOT);
                if (root == IntPtr.Zero) return false;
                foreach (var t in Targets.ToArray())
                {
                    if (t.Hwnd != root) continue;
                    if (t.PointerDown(px))
                    {
                        _held = t;
                        if (_hook != null) _hook.MouseMove = OnMove;
                    }
                    break;
                }
            }
            catch (Exception ex) { Diag.Swallowed(ex); }
            return false;   // never swallowed: the page and the window still get their click
        }

        private static void OnMove(Point px)
        {
            try { _held?.PointerMove(px); }
            catch (Exception ex) { Diag.Swallowed(ex); }
        }

        private static void OnUp(Point px)
        {
            var held = _held;
            if (held == null) return;
            Let();
            try { held.PointerUp(px); }
            catch (Exception ex) { Diag.Swallowed(ex); }
        }

        private const uint GA_ROOT = 2;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(POINT pt);

        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    }
}
