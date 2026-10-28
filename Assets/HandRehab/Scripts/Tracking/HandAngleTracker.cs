using UnityEngine;

namespace HandRehab.Tracking
{
    /// <summary>
    /// Computes the flexion angles of the 4 fingers (MCP, PIP, DIP) for every hand frame,
    /// with an optional "zero" calibration taken while the hand is held flat.
    /// Games and the debug panel read the angles from here.
    /// </summary>
    public sealed class HandAngleTracker : MonoBehaviour
    {
        public const int FingerCount = 4;
        public const int JointCount = 3;

        [SerializeField] private MonoBehaviour handSourceBehaviour; // must implement IHandSource
        [Tooltip("Tick if a closed fist shows NEGATIVE angles on your setup.")]
        [SerializeField] private bool invertSign = false;
        [Tooltip("How long to average when zeroing with a flat hand.")]
        [SerializeField] private float zeroingSeconds = 1.5f;

        /// <summary>Angles in degrees [finger, joint]: raw, minus the zero offsets when zeroed.</summary>
        public float[,] Angles { get; } = new float[FingerCount, JointCount];

        /// <summary>Angles before the zero offsets are applied.</summary>
        public float[,] RawAngles { get; } = new float[FingerCount, JointCount];

        public bool HasAngles { get; private set; }
        public bool IsZeroed { get; private set; }
        public bool IsZeroing { get; private set; }
        public float ZeroingProgress => IsZeroing ? Mathf.Clamp01((Time.unscaledTime - _zeroingStart) / zeroingSeconds) : 0f;
        public string LastMessage { get; private set; } = "";

        private IHandSource _source;
        private readonly float[,] _offsets = new float[FingerCount, JointCount];
        private readonly float[,] _sums = new float[FingerCount, JointCount];
        private int _samples;
        private float _zeroingStart;

        private void Awake()
        {
            _source = handSourceBehaviour as IHandSource ?? GetComponent<IHandSource>();
            if (_source == null)
            {
                Debug.LogError("[HandRehab] HandAngleTracker needs a component that implements IHandSource.");
                enabled = false;
            }
        }

        private void OnEnable()
        {
            if (_source == null) return;
            _source.FrameReady += OnFrame;
            _source.HandLost += OnLost;
        }

        private void OnDisable()
        {
            if (_source == null) return;
            _source.FrameReady -= OnFrame;
            _source.HandLost -= OnLost;
        }

        /// <summary>Start averaging the current pose as the new zero. Hold the hand flat and still.</summary>
        public void StartZeroing()
        {
            if (!HasAngles)
            {
                LastMessage = "Show your hand first, then zero.";
                return;
            }
            System.Array.Clear(_sums, 0, _sums.Length);
            _samples = 0;
            _zeroingStart = Time.unscaledTime;
            IsZeroing = true;
            LastMessage = "Zeroing… hold your hand flat and still.";
        }

        public void ClearZero()
        {
            System.Array.Clear(_offsets, 0, _offsets.Length);
            IsZeroed = false;
            IsZeroing = false;
            LastMessage = "Zero cleared.";
        }

        private void OnFrame(HandFrame frame)
        {
            if (!frame.IsValid) return;
            var sign = invertSign ? -1f : 1f;

            for (var f = 0; f < FingerCount; f++)
            {
                for (var j = 0; j < JointCount; j++)
                {
                    var raw = sign * HandMetrics.Flexion(frame, f, (FingerJoint)j);
                    RawAngles[f, j] = raw;
                    Angles[f, j] = IsZeroed ? raw - _offsets[f, j] : raw;
                    if (IsZeroing) _sums[f, j] += raw;
                }
            }
            HasAngles = true;

            if (IsZeroing)
            {
                _samples++;
                if (Time.unscaledTime - _zeroingStart >= zeroingSeconds && _samples >= 10)
                {
                    for (var f = 0; f < FingerCount; f++)
                        for (var j = 0; j < JointCount; j++)
                            _offsets[f, j] = _sums[f, j] / _samples;
                    IsZeroing = false;
                    IsZeroed = true;
                    LastMessage = $"Zeroed from {_samples} frames.";
                }
            }
        }

        private void OnLost()
        {
            HasAngles = false;
            if (IsZeroing)
            {
                IsZeroing = false;
                LastMessage = "Zeroing cancelled: hand lost.";
            }
        }
    }
}
