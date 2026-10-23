# HandRehab – Phase 0 setup (Windows, Unity 6.6)

Goal of this step: your webcam image shows in Unity with a live hand skeleton and joint angles on top.

## 1. Create the Unity project

1. Open **Unity Hub → New project**, pick editor **6.6**, template **Universal 3D** (or 3D Core), name it `HandRehab`.
2. Close Unity once the project has opened.

## 2. Install the MediaPipe Unity Plugin (v0.16.3)

1. Download `com.github.homuler.mediapipe-0.16.3.tgz` from
   https://github.com/homuler/MediaPipeUnityPlugin/releases/tag/v0.16.3 (under **Assets**).
2. Put the file inside your project, in the `Packages` folder:
   `HandRehab/Packages/com.github.homuler.mediapipe-0.16.3.tgz`
   (keeping it in the project means teammates and Git get the same plugin).
3. Open the project. **Window → Package Manager → + → Install package from tarball…** and pick that file.
4. Wait for the import to finish and check the Console has no red errors.

> The plugin targets Unity 2022.3+ and supports Unity 6. If 6.6 shows compile errors from the plugin, send me the first error line.

## 3. Add the HandRehab scripts

Copy the `Assets/HandRehab` folder from this package into your project's `Assets` folder,
and copy `.gitignore`, `.gitattributes` and this file to the project root (next to `Assets`).

```
HandRehab/
├── Assets/HandRehab/Scripts/Tracking/   ← runtime code
├── Assets/HandRehab/Scripts/Editor/     ← scene builder (editor only)
├── Packages/com.github.homuler.mediapipe-0.16.3.tgz
├── .gitignore  .gitattributes  SETUP.md
```

## 4. Build and run the test scene

1. Menu **HandRehab → Create Hand Tracking Scene**. It creates `Assets/HandRehab/Scenes/HandTracking.unity`
   and links the hand model (`hand_landmarker.bytes`) automatically.
2. Press **Play**. Allow camera access if Windows asks.
3. Hold one hand 40–70 cm from the webcam, palm facing the camera, in good front light.

You should see: the mirrored camera image, yellow joints with red fingertips, blue bones,
and a panel with camera resolution, tracking rate, Left/Right, openness, pinch distance and finger flexion angles.

## 5. Settings worth knowing (select the `HandTracking` object)

| Setting | What it does |
| --- | --- |
| Camera Name | Leave empty for the first webcam; type the exact name to choose another |
| Requested Width/Height/Fps | Try 640×480 if the tracking rate is low |
| Mirror | Selfie view; keep on |
| Hand Preference | Track only the patient's affected hand |
| Swap Handedness | Tick if Left/Right appear reversed |
| Min … Confidence | Lower (0.3–0.4) if impaired hands are lost often |

## 6. Version control

```
git init
git lfs install
git add .
git commit -m "Phase 0: project setup with MediaPipe hand tracking"
```

## Phase 0 done when

- [ ] Plugin installs with no Console errors
- [ ] HandTracking scene shows the skeleton following your hand
- [ ] Tracking rate written down (debug panel, "results/s") at 1280×720 and 640×480
- [ ] Project committed to Git

## Troubleshooting

- **"No webcam found" / black image** – close other apps using the camera (Teams, Zoom, browser tabs).
- **Unity Editor crashes on Play** – on Windows the plugin can crash the editor on native errors. Check that the model is assigned, then reopen and send me `%LOCALAPPDATA%\Unity\Editor\Editor-prev.log`.
- **Skeleton offset from the hand** – make sure `HandOverlay` is a child of `CameraView` and stretched to fill it.
- **Low tracking rate** – the pre-built Windows plugin runs on the CPU; lower the camera resolution first.
