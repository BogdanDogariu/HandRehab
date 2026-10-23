using HandRehab.Tracking;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace HandRehab.EditorTools
{
    /// <summary>
    /// Menu: HandRehab ▸ Create Hand Tracking Scene.
    /// Builds the Phase 1 test scene (camera view, skeleton overlay, debug panel) and wires it up.
    /// </summary>
    public static class HandTrackingSceneBuilder
    {
        private const string ScenePath = "Assets/HandRehab/Scenes/HandTracking.unity";
        private const string ModelPath = "Packages/com.github.homuler.mediapipe/PackageResources/MediaPipe/hand_landmarker.bytes";

        [MenuItem("HandRehab/Create Hand Tracking Scene")]
        public static void Create()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            var cam = Camera.main;
            if (cam != null)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.black;
            }

            // Canvas
            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            // Camera view, kept at the webcam's aspect ratio inside the screen
            var viewGo = new GameObject("CameraView", typeof(RectTransform), typeof(RawImage), typeof(AspectRatioFitter));
            var viewRect = (RectTransform)viewGo.transform;
            viewRect.SetParent(canvasGo.transform, false);
            Stretch(viewRect);
            var raw = viewGo.GetComponent<RawImage>();
            raw.color = Color.white;
            raw.raycastTarget = false;
            var fitter = viewGo.GetComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fitter.aspectRatio = 16f / 9f;

            // Overlay on top of the camera view
            var overlayGo = new GameObject("HandOverlay", typeof(RectTransform));
            var overlayRect = (RectTransform)overlayGo.transform;
            overlayRect.SetParent(viewRect, false);
            Stretch(overlayRect);

            // Debug text
            var textGo = new GameObject("DebugPanel", typeof(RectTransform), typeof(Text));
            var textRect = (RectTransform)textGo.transform;
            textRect.SetParent(canvasGo.transform, false);
            textRect.anchorMin = textRect.anchorMax = textRect.pivot = new Vector2(0, 1);
            textRect.anchoredPosition = new Vector2(24, -24);
            textRect.sizeDelta = new Vector2(620, 420);
            var text = textGo.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 26;
            text.color = Color.white;
            text.raycastTarget = false;
            textGo.AddComponent<Outline>().effectColor = new Color(0, 0, 0, 0.8f);

            // Tracking object
            var trackingGo = new GameObject("HandTracking");
            var source = trackingGo.AddComponent<MediaPipeHandSource>();
            var model = AssetDatabase.LoadAssetAtPath<TextAsset>(ModelPath) ?? FindModel();

            var so = new SerializedObject(source);
            so.FindProperty("screen").objectReferenceValue = raw;
            so.FindProperty("aspectFitter").objectReferenceValue = fitter;
            so.FindProperty("modelAsset").objectReferenceValue = model;
            so.ApplyModifiedPropertiesWithoutUndo();

            var overlay = overlayGo.AddComponent<HandSkeletonOverlay>();
            var oso = new SerializedObject(overlay);
            oso.FindProperty("handSourceBehaviour").objectReferenceValue = source;
            oso.ApplyModifiedPropertiesWithoutUndo();

            var panel = trackingGo.AddComponent<HandTrackingDebugPanel>();
            var pso = new SerializedObject(panel);
            pso.FindProperty("source").objectReferenceValue = source;
            pso.FindProperty("label").objectReferenceValue = text;
            pso.ApplyModifiedPropertiesWithoutUndo();

            System.IO.Directory.CreateDirectory("Assets/HandRehab/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();
            Selection.activeGameObject = trackingGo;

            if (model == null)
            {
                EditorUtility.DisplayDialog("HandRehab",
                    "Scene created, but hand_landmarker.bytes was not found.\n\n" +
                    "Install the MediaPipe Unity Plugin first, then assign the model on the HandTracking object.", "OK");
            }
            else
            {
                Debug.Log($"[HandRehab] Created {ScenePath}. Press Play and hold your hand in front of the webcam.");
            }
        }

        private static TextAsset FindModel()
        {
            foreach (var guid in AssetDatabase.FindAssets("hand_landmarker"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith("/hand_landmarker.bytes"))
                {
                    return AssetDatabase.LoadAssetAtPath<TextAsset>(path);
                }
            }
            return null;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }
    }
}
