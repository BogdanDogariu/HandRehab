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
    /// Results arrive on a MediaPipe thread; they are copied and handed out on the main thread.
    /// </summary>
    public sealed class MediaPipeHandSource : MonoBehaviour, IHandSource
    {
        public enum HandPreference { Any, LeftOnly, RightOnly }

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

        [Header("Hand selection")]
        [Tooltip("Track only the patient's affected hand, or whichever hand is seen.")]
        [SerializeField] private HandPreference handPreference = HandPreference.Any;
        [Tooltip("Tick if Left/Right come out reversed on your setup.")]
        [SerializeField] private bool swapHandedness = false;
        [Tooltip("Seconds without a detection before the hand counts as lost.")]
        [SerializeField] private float lostAfterSeconds = 0.25f;

        public event Action<HandFrame> FrameReady;
        public event Action HandLost;

        public bool IsTracking { get; private set; }
        public HandFrame LatestFrame { get; private set; }

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

        private double _lastSeenTime = double.NegativeInfinity;
        private float _rateTimer;

        private IEnumerator Start()
        {
            if (modelAsset == null)
            {
                Fail("No model assigned. Drag hand_landmarker.bytes into 'Model Asset'.");
                yield break;
            }

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

            for (var h = 0; h < result.handLandmarks.Count; h++)
            {
                var handedness = Handedness.Unknown;
                var confidence = 0f;
                if (result.handedness != null && h < result.handedness.Count && result.handedness[h].categories?.Count > 0)
                {
                    var category = result.handedness[h].categories[0];
                    confidence = category.score;
                    handedness = category.categoryName == "Left" ? Handedness.Left
                               : category.categoryName == "Right" ? Handedness.Right
                               : Handedness.Unknown;
                    if (swapHandedness && handedness != Handedness.Unknown)
                    {
                        handedness = handedness == Handedness.Left ? Handedness.Right : Handedness.Left;
                    }
                }

                if (handPreference == HandPreference.LeftOnly && handedness != Handedness.Left) continue;
                if (handPreference == HandPreference.RightOnly && handedness != Handedness.Right) continue;

                var normalized = result.handLandmarks[h].landmarks;
                if (normalized == null || normalized.Count < HandFrame.LandmarkCount) continue;

                var frame = new HandFrame
                {
                    Timestamp = timestampMs / 1000.0,
                    Handedness = handedness,
                    Confidence = confidence,
                    ImagePoints = new Vector3[HandFrame.LandmarkCount],
                    WorldPoints = new Vector3[HandFrame.LandmarkCount]
                };

                for (var i = 0; i < HandFrame.LandmarkCount; i++)
                {
                    var p = normalized[i];
                    frame.ImagePoints[i] = new Vector3(p.x, p.y, p.z);
                }

                if (result.handWorldLandmarks != null && h < result.handWorldLandmarks.Count)
                {
                    var world = result.handWorldLandmarks[h].landmarks;
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
                return; // first matching hand only
            }
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
                LatestFrame = frame;
                IsTracking = true;
                _lastSeenTime = Time.unscaledTimeAsDouble;
                FrameReady?.Invoke(frame);
            }
            else if (IsTracking && Time.unscaledTimeAsDouble - _lastSeenTime > lostAfterSeconds)
            {
                IsTracking = false;
                HandLost?.Invoke();
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
