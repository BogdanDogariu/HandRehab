# HandRehab

Hand rehabilitation app made with Unity 6.6 and C#. It tracks the patient's hand with a normal webcam using Google MediaPipe.

## What it does

- Shows the live webcam image (mirrored)
- Tracks one hand with 21 points and draws the skeleton on the video
- Lets you choose the training hand (Left / Right / Auto)
- Smooths the tracking to reduce jitter
- Measures finger flexion (MCP, PIP, DIP): + bent, − overextended
- Zero calibration with a flat hand (Z = zero, X = clear)
- Shows pinch distance, hand openness and tracking speed

## What is in the project

- `MediaPipeHandSource` – webcam + MediaPipe hand tracking
- `HandAngleTracker` / `HandMetrics` – angles and measurements
- `HandSkeletonOverlay` – draws the hand
- `HandTrackingDebugPanel` – on-screen numbers
- `HandTrackingSceneBuilder` – menu **HandRehab → Create Hand Tracking Scene**

## Next

- Rehabilitation games

## Credits

- [MediaPipe](https://github.com/google-ai-edge/mediapipe) (Google) and [MediaPipe Unity Plugin](https://github.com/homuler/MediaPipeUnityPlugin) (homuler)
