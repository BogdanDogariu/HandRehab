using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace HandRehab.Tracking
{
    /// <summary>Shows tracking status and live measurements in a corner of the screen.</summary>
    public sealed class HandTrackingDebugPanel : MonoBehaviour
    {
        [SerializeField] private MediaPipeHandSource source;
        [SerializeField] private Text label;
        [SerializeField] private float refreshSeconds = 0.1f;

        private readonly StringBuilder _sb = new StringBuilder();
        private float _timer;
        private float _renderFps;

        private void Update()
        {
            _renderFps = Mathf.Lerp(_renderFps, 1f / Mathf.Max(Time.unscaledDeltaTime, 1e-4f), 0.1f);
            _timer += Time.unscaledDeltaTime;
            if (_timer < refreshSeconds || source == null || label == null) return;
            _timer = 0f;

            _sb.Clear();
            _sb.AppendLine($"Status: {source.Status}");
            _sb.AppendLine($"Camera: {source.CameraResolution.x}x{source.CameraResolution.y}");
            _sb.AppendLine($"Render: {_renderFps:0} fps   Tracking: {source.ResultsPerSecond:0} results/s");

            if (source.IsTracking && source.LatestFrame.IsValid)
            {
                var f = source.LatestFrame;
                _sb.AppendLine($"Hand: {f.Handedness} ({f.Confidence:0.00})");
                _sb.AppendLine($"Openness: {HandMetrics.Openness(f):0.00}   Pinch: {HandMetrics.PinchDistanceCm(f):0.0} cm");
                _sb.AppendLine("Flexion MCP / PIP / DIP (deg)");
                Finger("Index ", f, HandLandmark.IndexMcp);
                Finger("Middle", f, HandLandmark.MiddleMcp);
                Finger("Ring  ", f, HandLandmark.RingMcp);
                Finger("Little", f, HandLandmark.PinkyMcp);
            }
            else
            {
                _sb.AppendLine("Hand: not detected");
            }

            label.text = _sb.ToString();
        }

        private void Finger(string name, in HandFrame f, int mcp)
        {
            var mcpAngle = HandMetrics.FlexionDegrees(f, HandLandmark.Wrist, mcp, mcp + 1);
            var pip = HandMetrics.FlexionDegrees(f, mcp, mcp + 1, mcp + 2);
            var dip = HandMetrics.FlexionDegrees(f, mcp + 1, mcp + 2, mcp + 3);
            _sb.AppendLine($"  {name}  {mcpAngle,4:0} {pip,4:0} {dip,4:0}");
        }
    }
}
