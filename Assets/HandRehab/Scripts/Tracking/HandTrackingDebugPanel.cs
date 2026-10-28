using System.Text;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace HandRehab.Tracking
{
    /// <summary>
    /// Shows tracking status and live measurements in a corner of the screen.
    /// Keys: Z = zero the angles (hold the hand flat), X = clear the zero.
    /// </summary>
    public sealed class HandTrackingDebugPanel : MonoBehaviour
    {
        [SerializeField] private MediaPipeHandSource source;
        [SerializeField] private HandAngleTracker angles;
        [SerializeField] private Text label;
        [SerializeField] private float refreshSeconds = 0.1f;

        private readonly StringBuilder _sb = new StringBuilder();
        private float _timer;
        private float _renderFps;

        private void Awake()
        {
            // Scenes made with the first version have no HandAngleTracker yet: add one.
            if (angles == null)
            {
                angles = GetComponent<HandAngleTracker>();
                if (angles == null) angles = gameObject.AddComponent<HandAngleTracker>();
            }

            // The panel now has more lines than the first version: never cut text off.
            if (label != null)
            {
                label.horizontalOverflow = HorizontalWrapMode.Overflow;
                label.verticalOverflow = VerticalWrapMode.Overflow;
            }
        }

        private void Update()
        {
            HandleKeys();

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
                _sb.AppendLine($"Hand: {f.Handedness}   (mode {source.TrainingHand}; MediaPipe: {f.RawHandedness} {f.Confidence:0.00})");
                _sb.AppendLine($"Openness: {HandMetrics.Openness(f):0.00}   Pinch: {HandMetrics.PinchDistanceCm(f):0.0} cm");

                if (angles != null && angles.HasAngles)
                {
                    _sb.AppendLine($"Flexion (deg)  + bent / − overextended{(angles.IsZeroed ? "   [zeroed]" : "")}");
                    _sb.AppendLine("            MCP   PIP   DIP");
                    for (var finger = 0; finger < HandAngleTracker.FingerCount; finger++)
                    {
                        _sb.AppendLine($"  {HandMetrics.FingerNames[finger],-7} {angles.Angles[finger, 0],5:0} {angles.Angles[finger, 1],5:0} {angles.Angles[finger, 2],5:0}");
                    }
                }
            }
            else
            {
                _sb.AppendLine("Hand: not detected");
            }

            if (angles != null)
            {
                if (angles.IsZeroing) _sb.AppendLine($"Zeroing… {angles.ZeroingProgress * 100f:0}%  hold your hand flat");
                else if (!string.IsNullOrEmpty(angles.LastMessage)) _sb.AppendLine(angles.LastMessage);
            }
            _sb.AppendLine("Z = zero with hand flat   X = clear zero");

            label.text = _sb.ToString();
        }

        private void HandleKeys()
        {
            if (angles == null) return;
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.zKey.wasPressedThisFrame) angles.StartZeroing();
            if (kb.xKey.wasPressedThisFrame) angles.ClearZero();
#else
            if (Input.GetKeyDown(KeyCode.Z)) angles.StartZeroing();
            if (Input.GetKeyDown(KeyCode.X)) angles.ClearZero();
#endif
        }
    }
}
