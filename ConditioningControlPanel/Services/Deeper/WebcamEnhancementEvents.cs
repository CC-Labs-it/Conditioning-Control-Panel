using System;
using System.Windows;

namespace ConditioningControlPanel.Services.Deeper
{
    /// <summary>
    /// Adapts <see cref="WebcamTrackingService"/> to the Core engine's
    /// <see cref="IEnhancementWebcam"/>. The tracker raises on its capture thread and this head
    /// installs no CoreDispatch provider, so the adapter does the marshal the engine used to do
    /// itself: inline on the UI thread, BeginInvoke otherwise, dropped once shutdown started.
    /// One per engine bind; Dispose unhooks the long-lived tracker.
    /// </summary>
    public sealed class WebcamEnhancementEvents : IEnhancementWebcam, IDisposable
    {
        private readonly WebcamTrackingService _webcam;

        public event Action? OnBlink;
        public event Action? OnMouthOpen;
        public event Action<double, double>? OnGazeMove;
        public event Action? OnFaceLost;
        public event Action? OnFaceFound;

        public WebcamEnhancementEvents(WebcamTrackingService webcam)
        {
            _webcam = webcam;
            _webcam.OnBlink += Blink;
            _webcam.OnMouthOpen += MouthOpen;
            _webcam.OnGazeMove += GazeMove;
            _webcam.OnFaceLost += FaceLost;
            _webcam.OnFaceFound += FaceFound;
        }

        private void Blink() => Raise(OnBlink);
        private void MouthOpen() => Raise(OnMouthOpen);
        private void FaceLost() => Raise(OnFaceLost);
        private void FaceFound() => Raise(OnFaceFound);

        private void GazeMove(Point p)
        {
            var h = OnGazeMove;
            if (h != null) MarshalToUi(() => h(p.X, p.Y));
        }

        private static void Raise(Action? h)
        {
            if (h != null) MarshalToUi(h);
        }

        private static void MarshalToUi(Action action)
        {
            var d = Application.Current?.Dispatcher;
            if (d == null || d.HasShutdownStarted) return;
            if (d.CheckAccess()) { action(); return; }
            try { d.BeginInvoke(action); } catch { /* dispatcher shutting down */ }
        }

        public void Dispose()
        {
            _webcam.OnBlink -= Blink;
            _webcam.OnMouthOpen -= MouthOpen;
            _webcam.OnGazeMove -= GazeMove;
            _webcam.OnFaceLost -= FaceLost;
            _webcam.OnFaceFound -= FaceFound;
        }
    }
}
