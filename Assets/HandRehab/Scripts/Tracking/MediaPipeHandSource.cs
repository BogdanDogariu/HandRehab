using System;
using System.Collections;
using Mediapipe.Tasks.Vision.HandLandmarker;
using UnityEngine;
using UnityEngine.UI;
using RunningMode = Mediapipe.Tasks.Vision.Core.RunningMode;
using BaseOptions = Mediapipe.Tasks.Core.BaseOptions;
using TextureFrame = Mediapipe.Unity.Experimental.TextureFrame;
using MpImage = Mediapipe.Image;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace HandRehab.Tracking
{
    /// <summary>
    /// Opens the webcam, shows it (mirrored, like a selfie view) on a RawImage and runs the
    /// MediaPipe Hand Landmarker on every new camera frame in LIVE_STREAM mode.
    /// Results arrive on a MediaPipe thread; they are copied, then smoothed and handed out on the main thread.
    /// </summary>
    public sealed class MediaPipeHandSource : MonoBehaviour, IHandSource
    {
        public enum TrainingHandMode { Auto, Left, Right }

        [Header("Camera")]
        [Tooltip("Leave empty to use the first webcam found.")]
        [SerializeField] private string cameraName = "";
        [SerializeField] private int requestedWidth = 1280;
        [SerializeField] private int requestedHeight = 720;
        [SerializeField] private int requestedFps = 30;
        [Tooltip("Mirror the image so the patient sees themselves like in a mirror. MediaPipe's left/right labels assume a mirrored image.")]
        [SerializeField] private bool mirror = true;

        [Header("Display")]
        [SerializeField] private RawImage screen;
        [SerializeField] private AspectRatioFitter aspectFitter;

        [Header("MediaPipe")]
        [Tooltip("hand_landmarker.bytes from Packages/MediaPipe Unity Plugin/PackageResources/MediaPipe")]
        [SerializeField] private TextAsset modelAsset;
        [SerializeField, Range(1, 2)] private int maxHands = 1;
        [SerializeField, Range(0f, 1f)] private float minDetectionConfidence = 0.5f;
        [SerializeField, Range(0f, 1f)] private float minPresenceConfidence = 0.5f;
        [SerializeField, Range(0f, 1f)] private float minTrackingConfidence = 0.5f;

        [Header("Which hand")]
        [Tooltip("Left/Right: the patient's training hand. Any hand seen is treated as that hand, so the label never flickers. " +
                 "Auto: use MediaPipe's label, smoothed over time.")]
        [SerializeField] private TrainingHandMode trainingHand = TrainingHandMode.Auto;
        [Tooltip("Auto mode only: how quickly the label follows MediaPipe (0..1). Lower = steadier.")]
        [SerializeField, Range(0.02f, 1f)] private float handednessSmoothing = 0.15f;
        [Tooltip("Tick if MediaPipe's Left/Right come out reversed on your setup.")]
        [SerializeField] private bool swapHandedness = false;
        [Tooltip("Seconds without a detection before the hand counts as lost.")]
        [SerializeField] private float lostAfterSeconds = 0.25f;

        [Header("Smoothing (One Euro filter)")]
        [SerializeField] private bool smoothLandmarks = true;
        [Tooltip("Lower = less jitter when the hand is still.")]
        [SerializeField] private float minCutoff = 1.5f;
        [Tooltip("Higher = less lag during fast movement (image points).")]
        [SerializeField] private float betaImage = 10f;
        [Tooltip("Higher = less lag during fast movement (3D points, metres).")]
        [SerializeField] private float betaWorld = 50f;

        public event Action<HandFrame> FrameReady;
        public event Action HandLost;

        public bool IsTracking { get; private set; }
        public HandFrame LatestFrame { get; private set; }
        public TrainingHandMode TrainingHand => trainingHand;

        /// <summary>Tracking results per second (for the debug panel).</summary>
        public float ResultsPerSecond { get; private set; }
        public Vector2Int CameraResolution { get; private set; }
        public string Status { get; private set; } = "Starting…";

        private WebCamTexture _webCam;
        private HandLandmarker _landmarker;
        private TextureFrame _textureFrame;
        private readonly Stopwatch _clock = new Stopwatch();
        private long _lastTimestampMs = -1;

        // Shared between the MediaPipe callback thread and the main thread.
        private readonly object _lock = new object();
        private HandFrame _pendingFrame;
        private bool _hasPendingFrame;
        private int _resultsSinceLastSecond;

        // Main-thread state.
        private double _lastSeenTime = double.NegativeInfinity;
        private float _rateTimer;
        private OneEuroFilterVector3[] _imageFilters;
        private OneEuroFilterVector3[] _worldFilters;
        private bool _hasHandState;
        private float _rightness;              // 0 = surely left, 1 = surely right (Auto mode)
        private Handedness _stableHandedness;

        private IEnumerator Start()
        {
            if (modelAsset == null)
            {
                Fail("No model assigned. Drag hand_landmarker.bytes into 'Model Asset'.");
                yield break;
            }

            CreateFilters();

            // 1. Open the webcam.
            var devices = WebCamTexture.devices;
            if (devices.Length == 0)
            {
                Fail("No webcam found.");
                yield break;
            }

            var deviceName = devices[0].name;
            if (!string.IsNullOrEmpty(cameraName))
            {
                foreach (var d in devices)
                {
                    if (d.name == cameraName) { deviceName = d.name; break; }
                }
            }

            _webCam = new WebCamTexture(deviceName, requestedWidth, requestedHeight, requestedFps);
            _webCam.Play();
            var giveUpAt = Time.realtimeSinceStartup + 10f;
            yield return new WaitUntil(() => _webCam.width > 16 || Time.realtimeSinceStartup > giveUpAt);
            if (_webCam.width <= 16)
            {
                Fail($"Webcam '{deviceName}' did not start. Is another app using it?");
                yield break;
            }

            CameraResolution = new Vector2Int(_webCam.width, _webCam.height);
            Debug.Log($"[HandRehab] Webcam '{deviceName}' running at {_webCam.width}x{_webCam.height}");

            // 2. Show it, mirrored if requested. MediaPipe gets the same mirrored image,
            //    so landmark coordinates line up with what is on screen.
            if (screen != null)
            {
                screen.texture = _webCam;
                screen.uvRect = mirror ? new UnityEngine.Rect(1, 0, -1, 1) : new UnityEngine.Rect(0, 0, 1, 1);
            }
            if (aspectFitter != null)
            {
                aspectFitter.aspectRatio = (float)_webCam.width / _webCam.height;
            }

            // 3. Create the Hand Landmarker (CPU: the pre-built Windows plugin is CPU-only).
            var options = new HandLandmarkerOptions(
                baseOptions: new BaseOptions(BaseOptions.Delegate.CPU, modelAssetBuffer: modelAsset.bytes),
                runningMode: RunningMode.LIVE_STREAM,
                numHands: maxHands,
                minHandDetectionConfidence: minDetectionConfidence,
                minHandPresenceConfidence: minPresenceConfidence,
                minTrackingConfidence: minTrackingConfidence,
                resultCallback: OnResult);

            try
            {
                _landmarker = HandLandmarker.CreateFromOptions(options);
            }
            catch (Exception e)
            {
                Fail($"Could not create HandLandmarker: {e.Message}");
                yield break;
            }

            _textureFrame = new TextureFrame(_webCam.width, _webCam.height, TextureFormat.RGBA32);
            _clock.Start();
            Status = "Running";

            // 4. Feed every new camera frame to MediaPipe.
            var endOfFrame = new WaitForEndOfFrame();
            while (true)
            {
                yield return endOfFrame;

                if (_webCam == null || !_webCam.isPlaying || !_webCam.didUpdateThisFrame)
                {
                    continue;
                }

                // Unity textures start bottom-left, MediaPipe expects top-left: flip vertically.
                _textureFrame.ReadTextureOnCPU(_webCam, flipHorizontally: mirror, flipVertically: !_webCam.videoVerticallyMirrored);
                MpImage image = _textureFrame.BuildCPUImage();

                // Timestamps must strictly increase.
                var ts = Math.Max(_clock.ElapsedMilliseconds, _lastTimestampMs + 1);
                _lastTimestampMs = ts;

                // Like the plugin's own sample, the image is handed to MediaPipe and not disposed here.
                _landmarker.DetectAsync(image, ts);
            }
        }

        /// <summary>Called by MediaPipe on a background thread. Copy what we need; touch no Unity API.</summary>
        private void OnResult(HandLandmarkerResult result, MpImage image, long timestampMs)
        {
            lock (_lock)
            {
                _resultsSinceLastSecond++;
            }

            if (result.handLandmarks == null || result.handLandmarks.Count == 0)
            {
                return;
            }

            // Pick one hand: with a fixed training hand prefer the hand MediaPipe labels the same,
            // otherwise (or if none matches) take the first hand.
            var chosen = 0;
            if (trainingHand != TrainingHandMode.Auto && result.handLandmarks.Count > 1)
            {
                var wanted = trainingHand == TrainingHandMode.Left ? Handedness.Left : Handedness.Right;
                for (var h = 0; h < result.handLandmarks.Count; h++)
                {
                    if (ReadLabel(result, h, out _) == wanted) { chosen = h; break; }
                }
            }

            var normalized = result.handLandmarks[chosen].landmarks;
            if (normalized == null || normalized.Count < HandFrame.LandmarkCount) return;

            var rawLabel = ReadLabel(result, chosen, out var score);
            var frame = new HandFrame
            {
                Timestamp = timestampMs / 1000.0,
                RawHandedness = rawLabel,
                Handedness = rawLabel, // replaced on the main thread
                Confidence = score,
                ImagePoints = new Vector3[HandFrame.LandmarkCount],
                WorldPoints = new Vector3[HandFrame.LandmarkCount]
            };

            for (var i = 0; i < HandFrame.LandmarkCount; i++)
            {
                var p = normalized[i];
                frame.ImagePoints[i] = new Vector3(p.x, p.y, p.z);
            }

            if (result.handWorldLandmarks != null && chosen < result.handWorldLandmarks.Count)
            {
                var world = result.handWorldLandmarks[chosen].landmarks;
                for (var i = 0; i < HandFrame.LandmarkCount && i < world.Count; i++)
                {
                    // MediaPipe y points down; Unity y points up.
                    var p = world[i];
                    frame.WorldPoints[i] = new Vector3(p.x, -p.y, p.z);
                }
            }

            lock (_lock)
            {
                _pendingFrame = frame;
                _hasPendingFrame = true;
            }
        }

        private Handedness ReadLabel(HandLandmarkerResult result, int hand, out float score)
        {
            score = 0f;
            if (result.handedness == null || hand >= result.handedness.Count) return Handedness.Unknown;
            var categories = result.handedness[hand].categories;
            if (categories == null || categories.Count == 0) return Handedness.Unknown;

            score = categories[0].score;
            var label = categories[0].categoryName == "Left" ? Handedness.Right
                      : categories[0].categoryName == "Right" ? Handedness.Left
                      : Handedness.Unknown;
            if (swapHandedness && label != Handedness.Unknown)
            {
                label = label == Handedness.Left ? Handedness.Right : Handedness.Left;
            }
            return label;
        }

        private void Update()
        {
            HandFrame frame = default;
            bool hasFrame;
            int results;
            lock (_lock)
            {
                hasFrame = _hasPendingFrame;
                if (hasFrame) { frame = _pendingFrame; _hasPendingFrame = false; }
                results = _resultsSinceLastSecond;
            }

            _rateTimer += Time.unscaledDeltaTime;
            if (_rateTimer >= 1f)
            {
                lock (_lock) { _resultsSinceLastSecond = 0; }
                ResultsPerSecond = results / _rateTimer;
                _rateTimer = 0f;
            }

            if (hasFrame)
            {
                frame.Handedness = DecideHandedness(frame);
                if (smoothLandmarks) Smooth(ref frame);

                LatestFrame = frame;
                IsTracking = true;
                _lastSeenTime = Time.unscaledTimeAsDouble;
                FrameReady?.Invoke(frame);
            }
            else if (IsTracking && Time.unscaledTimeAsDouble - _lastSeenTime > lostAfterSeconds)
            {
                IsTracking = false;
                ResetHandState();
                HandLost?.Invoke();
            }
        }

        /// <summary>The stable label: the chosen training hand, or MediaPipe's label with hysteresis.</summary>
        private Handedness DecideHandedness(in HandFrame frame)
        {
            if (trainingHand == TrainingHandMode.Left) return Handedness.Left;
            if (trainingHand == TrainingHandMode.Right) return Handedness.Right;

            // Turn this frame's label + score into a "rightness" vote: 1 = right, 0 = left.
            var vote = frame.RawHandedness == Handedness.Right ? frame.Confidence
                     : frame.RawHandedness == Handedness.Left ? 1f - frame.Confidence
                     : 0.5f;

            if (!_hasHandState)
            {
                _hasHandState = true;
                _rightness = vote;
                _stableHandedness = vote >= 0.5f ? Handedness.Right : Handedness.Left;
                return _stableHandedness;
            }

            _rightness = Mathf.Lerp(_rightness, vote, handednessSmoothing);

            // Hysteresis: only switch when the average is clearly on the other side.
            if (_stableHandedness == Handedness.Left && _rightness > 0.7f) _stableHandedness = Handedness.Right;
            else if (_stableHandedness == Handedness.Right && _rightness < 0.3f) _stableHandedness = Handedness.Left;
            return _stableHandedness;
        }

        private void Smooth(ref HandFrame frame)
        {
            for (var i = 0; i < HandFrame.LandmarkCount; i++)
            {
                frame.ImagePoints[i] = _imageFilters[i].Filter(frame.ImagePoints[i], frame.Timestamp);
                frame.WorldPoints[i] = _worldFilters[i].Filter(frame.WorldPoints[i], frame.Timestamp);
            }
        }

        private void CreateFilters()
        {
            _imageFilters = new OneEuroFilterVector3[HandFrame.LandmarkCount];
            _worldFilters = new OneEuroFilterVector3[HandFrame.LandmarkCount];
            for (var i = 0; i < HandFrame.LandmarkCount; i++)
            {
                _imageFilters[i] = new OneEuroFilterVector3(minCutoff, betaImage);
                _worldFilters[i] = new OneEuroFilterVector3(minCutoff, betaWorld);
            }
        }

        private void ResetHandState()
        {
            _hasHandState = false;
            if (_imageFilters == null) return;
            for (var i = 0; i < HandFrame.LandmarkCount; i++)
            {
                _imageFilters[i].Reset();
                _worldFilters[i].Reset();
            }
        }

        private void Fail(string message)
        {
            Status = message;
            Debug.LogError($"[HandRehab] {message}");
        }

        private void OnDestroy()
        {
            _landmarker?.Close();
            _landmarker = null;
            _textureFrame?.Dispose();
            _textureFrame = null;
            if (_webCam != null)
            {
                _webCam.Stop();
                Destroy(_webCam);
                _webCam = null;
            }
        }
    }
}
