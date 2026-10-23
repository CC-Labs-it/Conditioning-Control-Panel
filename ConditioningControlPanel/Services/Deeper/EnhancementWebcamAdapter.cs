using System;

namespace ConditioningControlPanel.Services.Deeper
{
    /// <summary>
    /// The tracker as the Core engine sees it: the five events it listens to, with the gaze point
    /// unpacked from System.Windows.Point. One per tracker (App caches it), so the single gaze
    /// forwarder below is never added twice.
    /// </summary>
    internal sealed class EnhancementWebcamAdapter : IEnhancementWebcam
    {
        private readonly WebcamTrackingService _webcam;

        public EnhancementWebcamAdapter(WebcamTrackingService webcam)
        {
            _webcam = webcam;
            _webcam.OnGazeMove += p => OnGazeMove?.Invoke(p.X, p.Y);
        }

        public event Action<double, double>? OnGazeMove;
        public event Action? OnBlink { add => _webcam.OnBlink += value; remove => _webcam.OnBlink -= value; }
        public event Action? OnMouthOpen { add => _webcam.OnMouthOpen += value; remove => _webcam.OnMouthOpen -= value; }
        public event Action? OnFaceLost { add => _webcam.OnFaceLost += value; remove => _webcam.OnFaceLost -= value; }
        public event Action? OnFaceFound { add => _webcam.OnFaceFound += value; remove => _webcam.OnFaceFound -= value; }
    }
}
