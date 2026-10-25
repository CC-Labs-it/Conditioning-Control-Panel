// PORTED from ConditioningControlPanel/Services/Possession/TransformLease.cs (WPF TransformLease) and
// Services/Possession/PossessionOffLimitsTree.cs + Possession.cs (the Exclude attached property).
//
// The lease: an effect never assigns RenderTransform itself. It takes a lease, which records the
// control's own RenderTransform and RenderTransformOrigin (and whether each was a local value),
// installs one TransformGroup (prior, Scale, Skew, Rotate, Translate - WPF order) and hands the
// effect those four parts. Release puts the originals back exactly, so panic restores every touched
// control with nothing left behind. Ref-counted like WPF: two effects on one control share a lease.
//
// P82: nothing here animates. The runtime sets the four parts from frame timestamps, so there is no
// Animation.RunAsync on a Transform and no looping Animation to leak.

using System;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using ConditioningControlPanel.Services.Possession;

namespace ConditioningControlPanel.Avalonia.Possession
{
    internal sealed class PossessionLease
    {
        private static readonly ConditionalWeakTable<Control, PossessionLease> Table = new();

        private readonly Control _el;
        private readonly TransformGroup _group = new();
        private readonly ITransform? _prior;
        private readonly bool _priorLocal;
        private readonly RelativePoint _priorOrigin;
        private readonly bool _originLocal;
        private readonly Transitions? _priorTransitions;
        private readonly bool _transitionsLocal, _transitionsSwapped;
        private int _refCount = 1;

        public TranslateTransform Translate { get; } = new();
        public RotateTransform Rotate { get; } = new();
        public SkewTransform Skew { get; } = new();
        public ScaleTransform Scale { get; } = new(1, 1);
        public Control Element => _el;
        public bool IsReleased { get; private set; }

        private PossessionLease(Control el)
        {
            _el = el;
            _priorLocal = IsLocal(el, Visual.RenderTransformProperty);
            _prior = el.RenderTransform;
            _originLocal = IsLocal(el, Visual.RenderTransformOriginProperty);
            _priorOrigin = el.RenderTransformOrigin;
            // A themed control (every Fluent Button) carries a RenderTransform transition: it would take
            // the group at Animation priority and fade it in and out. Suspend just that one while leased.
            _transitionsLocal = IsLocal(el, Animatable.TransitionsProperty);
            _priorTransitions = el.Transitions;
            if (_priorTransitions != null && _priorTransitions.Any(IsRenderTransformTransition))
            {
                var kept = new Transitions();
                foreach (var tr in _priorTransitions) if (!IsRenderTransformTransition(tr)) kept.Add(tr);
                el.Transitions = kept;
                _transitionsSwapped = true;
            }
            el.RenderTransform = null;
            if (_prior is Transform t) _group.Children.Add(t);
            _group.Children.Add(Scale);
            _group.Children.Add(Skew);
            _group.Children.Add(Rotate);
            _group.Children.Add(Translate);
            el.RenderTransform = _group;
            // Origin is left alone (a prior transform keeps rendering as before); scale/rotate effects
            // call SetOrigin(0.5, 0.5) as on WPF, translate-only ones do not care (P71).
        }

        public static PossessionLease? Take(Control? el)
        {
            if (el == null) return null;
            if (Table.TryGetValue(el, out var existing) && !existing.IsReleased)
            {
                existing._refCount++;
                return existing;
            }
            var lease = new PossessionLease(el);
            Table.AddOrUpdate(el, lease);
            return lease;
        }

        public static bool IsLeased(Control? el) => el != null && Table.TryGetValue(el, out var l) && !l.IsReleased;

        public void SetOrigin(double x, double y) => _el.RenderTransformOrigin = new RelativePoint(x, y, RelativeUnit.Relative);

        /// <summary>WPF ReleaseImmediate: zero the parts and give the control its own values back now.</summary>
        public void ReleaseImmediate()
        {
            if (IsReleased) return;
            if (--_refCount > 0) return;
            IsReleased = true;
            Translate.X = Translate.Y = 0;
            Rotate.Angle = 0;
            Skew.AngleX = Skew.AngleY = 0;
            Scale.ScaleX = Scale.ScaleY = 1;
            if (ReferenceEquals(_el.RenderTransform, _group))
            {
                if (_prior is Transform t) _group.Children.Remove(t);
                if (_priorLocal) _el.RenderTransform = _prior;
                else _el.ClearValue(Visual.RenderTransformProperty);
            }
            if (_originLocal) _el.RenderTransformOrigin = _priorOrigin;
            else _el.ClearValue(Visual.RenderTransformOriginProperty);
            if (_transitionsSwapped)
            {
                if (_transitionsLocal) _el.Transitions = _priorTransitions;
                else _el.ClearValue(Animatable.TransitionsProperty);
            }
            if (Table.TryGetValue(_el, out var cur) && ReferenceEquals(cur, this)) Table.Remove(_el);
        }

        /// <summary>WPF ReadLocalValue != UnsetValue. IsSet is not it: it is also true for a style value,
        /// and putting a style value back as a local one would pin it (a Button's pressed scale dies).</summary>
        private static bool IsLocal(Control el, AvaloniaProperty p) =>
            global::Avalonia.Diagnostics.AvaloniaObjectExtensions.GetDiagnostic(el, p).Priority == global::Avalonia.Data.BindingPriority.LocalValue;

        private static bool IsRenderTransformTransition(ITransition t) =>
            t is TransitionBase b && b.Property == Visual.RenderTransformProperty;
    }

    /// <summary>WPF poss:Possession.Exclude plus PossessionOffLimitsTree.IsOffLimits over the Avalonia
    /// visual tree. The name half of the rule is Core's <see cref="PossessionOffLimits.IsReservedName"/>.</summary>
    public sealed class PossessionTree
    {
        private PossessionTree() { }

        public const int MaxDepth = 16;

        public static readonly AttachedProperty<PossessionRole> RoleProperty =
            AvaloniaProperty.RegisterAttached<PossessionTree, Control, PossessionRole>("Role");
        public static PossessionRole GetRole(Control c) => c.GetValue(RoleProperty);
        public static void SetRole(Control c, PossessionRole v) => c.SetValue(RoleProperty, v);

        public static readonly AttachedProperty<string?> NameProperty =
            AvaloniaProperty.RegisterAttached<PossessionTree, Control, string?>("Name");
        public static string? GetName(Control c) => c.GetValue(NameProperty);
        public static void SetName(Control c, string? v) => c.SetValue(NameProperty, v);

        /// <summary>Keeps a control and its subtree out of the haunt (WPF Possession.Exclude).</summary>
        public static readonly AttachedProperty<bool> ExcludeProperty =
            AvaloniaProperty.RegisterAttached<PossessionTree, Control, bool>("Exclude");

        public static bool GetExclude(Control c) => c.GetValue(ExcludeProperty);
        public static void SetExclude(Control c, bool v) => c.SetValue(ExcludeProperty, v);

        /// <summary>Off limits when the control IS excluded, CONTAINS anything excluded, or it or any
        /// ancestor is named for a room the user must be able to leave (lockdown/emergency/secret).</summary>
        public static bool IsOffLimits(Control? el)
        {
            if (el == null) return true;
            try
            {
                if (ContainsExcluded(el, 0)) return true;
                for (Visual? node = el; node != null; node = node.GetVisualParent())
                {
                    if (node is not Control c) continue;
                    if (GetExclude(c)) return true;
                    if (PossessionOffLimits.IsReservedName(c.Name)) return true;
                }
            }
            catch { return true; }
            return false;
        }

        private static bool ContainsExcluded(Visual node, int depth)
        {
            if (depth > MaxDepth) return false;
            if (depth > 0 && node is Control c && GetExclude(c)) return true;
            foreach (var child in node.GetVisualChildren())
                if (ContainsExcluded(child, depth + 1)) return true;
            return false;
        }
    }
}
