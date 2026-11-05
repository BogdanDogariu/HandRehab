using HandRehab.Game;
using HandRehab.Tracking;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace HandRehab.EditorTools
{
    /// <summary>
    /// Menu: HandRehab ▸ Create Balloon Game Scene.
    /// Builds the "Balonul" game: hidden camera, stylised virtual hand, balloon, HUD and grip bar.
    /// </summary>
    public static class BalloonGameSceneBuilder
    {
        private const string ScenePath = "Assets/HandRehab/Scenes/BalloonGame.unity";
        private const string ModelPath = "Packages/com.github.homuler.mediapipe/PackageResources/MediaPipe/hand_landmarker.bytes";

        [MenuItem("HandRehab/Create Balloon Game Scene")]
        public static void Create()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            // Camera: soft sky background, looking at the hand (left) and the balloon (right).
            var cam = Camera.main;
            if (cam != null)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.80f, 0.91f, 0.98f);
                cam.transform.position = new Vector3(0f, 0.3f, -10f);
                cam.transform.rotation = Quaternion.identity;
                cam.fieldOfView = 40f;
            }

            // Tracking: webcam + MediaPipe, but no camera image on screen.
            var trackingGo = new GameObject("HandTracking");
            var source = trackingGo.AddComponent<MediaPipeHandSource>();
            var model = AssetDatabase.LoadAssetAtPath<TextAsset>(ModelPath) ?? FindModel();
            var so = new SerializedObject(source);
            so.FindProperty("modelAsset").objectReferenceValue = model;
            so.ApplyModifiedPropertiesWithoutUndo();

            var angles = trackingGo.AddComponent<HandAngleTracker>();
            var aso = new SerializedObject(angles);
            aso.FindProperty("handSourceBehaviour").objectReferenceValue = source;
            aso.ApplyModifiedPropertiesWithoutUndo();

            // Virtual hand on the left.
            var handGo = new GameObject("VirtualHand");
            handGo.transform.position = new Vector3(-2.8f, -0.4f, 0f);
            var hand = handGo.AddComponent<VirtualHand>();
            var hso = new SerializedObject(hand);
            hso.FindProperty("handSourceBehaviour").objectReferenceValue = source;
            hso.ApplyModifiedPropertiesWithoutUndo();

            // Balloon spot on the right.
            var anchor = new GameObject("BalloonAnchor").transform;
            anchor.position = new Vector3(2.6f, -0.2f, 0f);

            // UI
            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            var canvas = (RectTransform)canvasGo.transform;

            var hud = CreateText(canvas, "Hud", 40, TextAnchor.UpperLeft, new Color(0.10f, 0.20f, 0.30f));
            Place(hud.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, -30), new Vector2(600, 200));

            var center = CreateText(canvas, "Message", 46, TextAnchor.MiddleCenter, new Color(0.10f, 0.20f, 0.30f));
            Place(center.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 180), new Vector2(1400, 600));
            center.fontStyle = FontStyle.Bold;

            var therapist = CreateText(canvas, "TherapistBar", 22, TextAnchor.LowerLeft, new Color(0.25f, 0.35f, 0.45f));
            Place(therapist.rectTransform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(40, 16), new Vector2(1800, 40));

            var barLabel = CreateText(canvas, "GripLabel", 26, TextAnchor.LowerCenter, new Color(0.10f, 0.20f, 0.30f));
            barLabel.text = "Strângere";
            Place(barLabel.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 120), new Vector2(800, 40));

            var barBack = CreateImage(canvas, "GripBar", new Color(1f, 1f, 1f, 0.7f));
            Place(barBack, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 90), new Vector2(800, 36));

            var fill = CreateImage(barBack, "Fill", new Color(0.36f, 0.72f, 0.86f));
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = new Vector2(0f, 1f);
            fill.offsetMin = fill.offsetMax = Vector2.zero;

            var target = CreateImage(barBack, "Target", new Color(0.10f, 0.20f, 0.30f));
            target.anchorMin = new Vector2(0.6f, 0f);
            target.anchorMax = new Vector2(0.6f, 1f);
            target.sizeDelta = new Vector2(6f, 12f);
            target.anchoredPosition = Vector2.zero;

            // Game logic
            var game = new GameObject("BalloonGame").AddComponent<BalloonGame>();
            var gso = new SerializedObject(game);
            gso.FindProperty("source").objectReferenceValue = source;
            gso.FindProperty("angles").objectReferenceValue = angles;
            gso.FindProperty("balloonAnchor").objectReferenceValue = anchor;
            gso.FindProperty("hudText").objectReferenceValue = hud;
            gso.FindProperty("centerText").objectReferenceValue = center;
            gso.FindProperty("therapistText").objectReferenceValue = therapist;
            gso.FindProperty("gripFill").objectReferenceValue = fill;
            gso.FindProperty("gripTarget").objectReferenceValue = target;
            gso.ApplyModifiedPropertiesWithoutUndo();

            System.IO.Directory.CreateDirectory("Assets/HandRehab/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();
            Selection.activeGameObject = trackingGo;

            if (model == null)
            {
                EditorUtility.DisplayDialog("HandRehab", "Scene created, but hand_landmarker.bytes was not found. Assign it on HandTracking.", "OK");
            }
            else
            {
                Debug.Log($"[HandRehab] Created {ScenePath}. Set Training Hand on HandTracking, press Play, then SPACE.");
            }
        }

        private static Text CreateText(Transform parent, string name, int size, TextAnchor align, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var text = go.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size;
            text.alignment = align;
            text.color = color;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        private static RectTransform CreateImage(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return (RectTransform)go.transform;
        }

        private static void Place(RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
        }

        private static TextAsset FindModel()
        {
            foreach (var guid in AssetDatabase.FindAssets("hand_landmarker"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith("/hand_landmarker.bytes")) return AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            }
            return null;
        }
    }
}
