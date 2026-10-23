using UnityEngine;
using UnityEngine.UI;

namespace HandRehab.Tracking
{
    /// <summary>
    /// Draws the 21 landmarks and bones on top of the camera image using UI elements.
    /// Put this on a full-size child RectTransform of the camera RawImage.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class HandSkeletonOverlay : MonoBehaviour
    {
        [SerializeField] private MonoBehaviour handSourceBehaviour; // must implement IHandSource
        [SerializeField] private Color jointColor = new Color(1f, 0.85f, 0.2f);
        [SerializeField] private Color tipColor = new Color(1f, 0.35f, 0.25f);
        [SerializeField] private Color boneColor = new Color(0.2f, 0.9f, 1f, 0.9f);
        [SerializeField] private float jointSize = 14f;
        [SerializeField] private float boneWidth = 5f;

        private IHandSource _source;
        private RectTransform _rect;
        private RectTransform[] _joints;
        private RectTransform[] _bones;
        private GameObject _root;

        private void Awake()
        {
            _rect = (RectTransform)transform;
            _source = handSourceBehaviour as IHandSource;
            if (_source == null)
            {
                Debug.LogError("[HandRehab] HandSkeletonOverlay needs a component that implements IHandSource.");
                enabled = false;
                return;
            }
            Build();
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

        private void Build()
        {
            _root = new GameObject("Skeleton", typeof(RectTransform));
            var rootRect = (RectTransform)_root.transform;
            rootRect.SetParent(transform, false);
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = rootRect.offsetMax = Vector2.zero;

            var boneCount = HandLandmark.Connections.Length / 2;
            _bones = new RectTransform[boneCount];
            for (var i = 0; i < boneCount; i++)
            {
                _bones[i] = CreateImage($"Bone {i}", boneColor);
                _bones[i].pivot = new Vector2(0f, 0.5f);
            }

            _joints = new RectTransform[HandFrame.LandmarkCount];
            for (var i = 0; i < HandFrame.LandmarkCount; i++)
            {
                var isTip = i == 4 || i == 8 || i == 12 || i == 16 || i == 20;
                _joints[i] = CreateImage($"Joint {i}", isTip ? tipColor : jointColor);
                _joints[i].sizeDelta = Vector2.one * (isTip ? jointSize * 1.3f : jointSize);
            }

            _root.SetActive(false);
        }

        private RectTransform CreateImage(string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(_root.transform, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return rt;
        }

        /// <summary>Normalised image point (top-left origin) → local position in this rect (centre origin).</summary>
        private Vector2 ToLocal(Vector3 p)
        {
            var r = _rect.rect;
            return new Vector2((p.x - 0.5f) * r.width, (0.5f - p.y) * r.height);
        }

        private void OnFrame(HandFrame frame)
        {
            if (!frame.IsValid) return;
            _root.SetActive(true);

            for (var i = 0; i < HandFrame.LandmarkCount; i++)
            {
                _joints[i].anchoredPosition = ToLocal(frame.ImagePoints[i]);
            }

            for (var b = 0; b < _bones.Length; b++)
            {
                var a = ToLocal(frame.ImagePoints[HandLandmark.Connections[b * 2]]);
                var c = ToLocal(frame.ImagePoints[HandLandmark.Connections[b * 2 + 1]]);
                var d = c - a;
                _bones[b].anchoredPosition = a;
                _bones[b].sizeDelta = new Vector2(d.magnitude, boneWidth);
                _bones[b].localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
            }
        }

        private void OnLost()
        {
            if (_root != null) _root.SetActive(false);
        }
    }
}
