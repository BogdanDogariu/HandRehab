using HandRehab.Tracking;
using UnityEngine;

namespace HandRehab.Game
{
    /// <summary>
    /// Stylised 3D hand that copies the tracked hand in real time:
    /// 21 round joints, bones as cylinders and a filled palm, all in one colour.
    /// It is built from the 3D world points, so it moves exactly like the patient's hand.
    /// </summary>
    public sealed class VirtualHand : MonoBehaviour
    {
        [SerializeField] private MonoBehaviour handSourceBehaviour; // must implement IHandSource
        [Tooltip("Metres to scene units. 12 makes an adult hand about 2 units tall.")]
        [SerializeField] private float scale = 12f;
        [SerializeField] private Color color = new Color(0.36f, 0.72f, 0.86f);
        [SerializeField] private float jointRadius = 0.12f;
        [SerializeField] private float tipRadius = 0.14f;
        [SerializeField] private float boneRadius = 0.09f;
        [Tooltip("How quickly the hand follows new tracking frames (higher = faster).")]
        [SerializeField, Range(0.05f, 1f)] private float follow = 0.5f;

        private static readonly int[] PalmOutline = { 0, 1, 5, 9, 13, 17 };

        private IHandSource _source;
        private GameObject _root;
        private Transform[] _joints;
        private Transform[] _bones;
        private Mesh _palmMesh;
        private Material _material;
        private readonly Vector3[] _target = new Vector3[HandFrame.LandmarkCount];
        private readonly Vector3[] _current = new Vector3[HandFrame.LandmarkCount];
        private readonly Vector3[] _palmVertices = new Vector3[12];
        private bool _hasPose;

        public bool IsVisible => _root != null && _root.activeSelf;

        private void Awake()
        {
            _source = handSourceBehaviour as IHandSource;
            if (_source == null)
            {
                Debug.LogError("[HandRehab] VirtualHand needs a component that implements IHandSource.");
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
            _root = new GameObject("VirtualHandMesh");
            _root.transform.SetParent(transform, false);

            // One shared material in a single colour, taken from Unity's default lit material.
            var probe = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _material = new Material(probe.GetComponent<Renderer>().sharedMaterial) { color = color };
            Destroy(probe);

            _joints = new Transform[HandFrame.LandmarkCount];
            for (var i = 0; i < HandFrame.LandmarkCount; i++)
            {
                var isTip = i == 4 || i == 8 || i == 12 || i == 16 || i == 20;
                _joints[i] = CreatePart(PrimitiveType.Sphere, $"Joint {i}");
                _joints[i].localScale = Vector3.one * (isTip ? tipRadius : jointRadius) * 2f;
            }

            var boneCount = HandLandmark.Connections.Length / 2;
            _bones = new Transform[boneCount];
            for (var b = 0; b < boneCount; b++)
            {
                _bones[b] = CreatePart(PrimitiveType.Cylinder, $"Bone {b}");
            }

            // Palm: a flat shape through wrist, thumb base and the four knuckles, visible from both sides.
            var palm = new GameObject("Palm", typeof(MeshFilter), typeof(MeshRenderer));
            palm.transform.SetParent(_root.transform, false);
            _palmMesh = new Mesh { name = "Palm" };
            _palmMesh.MarkDynamic();
            _palmMesh.vertices = _palmVertices;
            _palmMesh.triangles = new[]
            {
                0, 1, 2,  0, 2, 3,  0, 3, 4,  0, 4, 5,       // front
                6, 8, 7,  6, 9, 8,  6, 10, 9, 6, 11, 10      // back (reversed)
            };
            palm.GetComponent<MeshFilter>().sharedMesh = _palmMesh;
            palm.GetComponent<MeshRenderer>().sharedMaterial = _material;

            _root.SetActive(false);
        }

        private Transform CreatePart(PrimitiveType type, string partName)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = partName;
            Destroy(go.GetComponent<Collider>());
            go.GetComponent<Renderer>().sharedMaterial = _material;
            go.transform.SetParent(_root.transform, false);
            return go.transform;
        }

        private void OnFrame(HandFrame frame)
        {
            if (!frame.IsValid || frame.WorldPoints == null) return;
            for (var i = 0; i < HandFrame.LandmarkCount; i++)
            {
                _target[i] = frame.WorldPoints[i] * scale;
            }
            if (!_hasPose)
            {
                System.Array.Copy(_target, _current, _target.Length);
                _hasPose = true;
            }
            _root.SetActive(true);
        }

        private void OnLost()
        {
            _hasPose = false;
            if (_root != null) _root.SetActive(false);
        }

        private void Update()
        {
            if (!_hasPose) return;

            // Glide towards the newest pose so the hand looks fluid between camera frames.
            var t = 1f - Mathf.Pow(1f - follow, Time.deltaTime * 60f);
            for (var i = 0; i < HandFrame.LandmarkCount; i++)
            {
                _current[i] = Vector3.Lerp(_current[i], _target[i], t);
                _joints[i].localPosition = _current[i];
            }

            for (var b = 0; b < _bones.Length; b++)
            {
                var a = _current[HandLandmark.Connections[b * 2]];
                var c = _current[HandLandmark.Connections[b * 2 + 1]];
                var dir = c - a;
                var length = dir.magnitude;
                var bone = _bones[b];
                if (length < 1e-4f)
                {
                    bone.localScale = Vector3.zero;
                    continue;
                }
                // Unity's cylinder is 2 units tall along Y with a diameter of 1.
                bone.localPosition = (a + c) * 0.5f;
                bone.localRotation = Quaternion.FromToRotation(Vector3.up, dir / length);
                bone.localScale = new Vector3(boneRadius * 2f, length * 0.5f, boneRadius * 2f);
            }

            for (var k = 0; k < PalmOutline.Length; k++)
            {
                _palmVertices[k] = _current[PalmOutline[k]];
                _palmVertices[k + 6] = _current[PalmOutline[k]];
            }
            _palmMesh.vertices = _palmVertices;
            _palmMesh.RecalculateNormals();
            _palmMesh.RecalculateBounds();
        }

        private void OnDestroy()
        {
            if (_palmMesh != null) Destroy(_palmMesh);
            if (_material != null) Destroy(_material);
        }
    }
}
