using System.Collections;
using System.Collections.Generic;
using HandRehab.Tracking;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace HandRehab.Game
{
    /// <summary>
    /// "Balonul" (v1 game 1): close the hand to inflate the balloon, open it fully to let the balloon fly.
    /// Trains full fist and full opening. The camera image is hidden; the virtual hand copies the patient.
    /// Therapist keys: SPACE start, P pause, R restart, 1/2/3 difficulty.
    /// </summary>
    public sealed class BalloonGame : MonoBehaviour
    {
        public enum Difficulty { Easy, Medium, Hard }
        private enum GameState { Waiting, Playing, Paused, Finished }
        private enum BalloonState { None, Inflating, Full }

        [Header("Links")]
        [SerializeField] private MediaPipeHandSource source;
        [SerializeField] private HandAngleTracker angles;
        [SerializeField] private Transform balloonAnchor;

        [Header("UI")]
        [SerializeField] private Text hudText;
        [SerializeField] private Text centerText;
        [SerializeField] private Text therapistText;
        [SerializeField] private RectTransform gripFill;
        [SerializeField] private RectTransform gripTarget;

        [Header("Rules")]
        [SerializeField] private float sessionSeconds = 120f;
        [SerializeField] private Difficulty difficulty = Difficulty.Medium;
        [Tooltip("Grip (0 = open hand, 1 = fist) needed to fill the balloon, per difficulty.")]
        [SerializeField] private float easyGrip = 0.45f;
        [SerializeField] private float mediumGrip = 0.6f;
        [SerializeField] private float hardGrip = 0.75f;
        [Tooltip("The hand counts as open again below this grip, which releases a full balloon.")]
        [SerializeField] private float releaseGrip = 0.25f;

        [Header("Balloon look")]
        [SerializeField] private float minBalloonSize = 0.5f;
        [SerializeField] private float maxBalloonSize = 2.2f;
        [SerializeField] private Color[] balloonColors =
        {
            new Color(0.93f, 0.30f, 0.33f), new Color(0.27f, 0.55f, 0.93f),
            new Color(0.98f, 0.75f, 0.20f), new Color(0.60f, 0.38f, 0.85f)
        };

        private GameState _state = GameState.Waiting;
        private BalloonState _balloonState = BalloonState.None;
        private GameObject _balloon;
        private Renderer _balloonRenderer;
        private Color _balloonColor;
        private int _colorIndex;
        private readonly List<GameObject> _flying = new List<GameObject>();

        private float _grip;
        private float _elapsed;
        private int _repetitions;
        private float _bestGrip;
        private float _repGripMax;
        private float _repStartTime;
        private readonly List<float> _repDurations = new List<float>();
        private float _bestPipFlexion = float.NaN;
        private int _spawnToken;

        private float TargetGrip => difficulty == Difficulty.Easy ? easyGrip : difficulty == Difficulty.Hard ? hardGrip : mediumGrip;

        private void Start()
        {
            ShowCenter("Balonul\n\nStrânge pumnul ca să umfli balonul,\napoi deschide mâna ca să zboare.\n\nApasă SPACE pentru a începe");
        }

        private void Update()
        {
            HandleKeys();

            var tracked = source != null && source.IsTracking && source.LatestFrame.IsValid;
            if (tracked)
            {
                // Grip: 0 = fully open hand, 1 = tight fist.
                var raw = 1f - HandMetrics.Openness(source.LatestFrame);
                _grip = Mathf.Lerp(_grip, raw, 1f - Mathf.Pow(0.6f, Time.deltaTime * 60f));
            }
            UpdateGripBar(tracked);

            if (_state == GameState.Playing)
            {
                if (!tracked)
                {
                    ShowCenter("Arată mâna camerei");
                }
                else
                {
                    if (centerText != null && centerText.text == "Arată mâna camerei") ShowCenter("");
                    _elapsed += Time.deltaTime;
                    TrackAngles();
                    UpdateBalloon();
                    if (_elapsed >= sessionSeconds) Finish();
                }
            }
            else if (_state == GameState.Waiting && source != null && source.Status != "Running" && source.Status != "Starting…")
            {
                ShowCenter($"Problemă cu camera:\n{source.Status}");
            }

            UpdateHud(tracked);
        }

        // ---------- game flow ----------

        private void StartGame()
        {
            ClearBalloons();
            _elapsed = 0f;
            _repetitions = 0;
            _bestGrip = 0f;
            _bestPipFlexion = float.NaN;
            _repDurations.Clear();
            _state = GameState.Playing;
            ShowCenter("");
            SpawnBalloon();
        }

        private void TogglePause()
        {
            if (_state == GameState.Playing)
            {
                _state = GameState.Paused;
                ShowCenter("Pauză\n\nApasă P pentru a continua");
            }
            else if (_state == GameState.Paused)
            {
                _state = GameState.Playing;
                ShowCenter("");
            }
        }

        private void Finish()
        {
            _state = GameState.Finished;
            ClearBalloons();

            var average = 0f;
            foreach (var d in _repDurations) average += d;
            if (_repDurations.Count > 0) average /= _repDurations.Count;

            var summary = "Bravo!\n\n" +
                          $"Repetiții: {_repetitions}\n" +
                          $"Strângere maximă: {_bestGrip * 100f:0}%\n" +
                          (_repDurations.Count > 0 ? $"Timp mediu pe repetiție: {average:0.0} s\n" : "") +
                          (!float.IsNaN(_bestPipFlexion) ? $"Flexie maximă (media PIP): {_bestPipFlexion:0}°\n" : "") +
                          "\nApasă SPACE pentru o sesiune nouă";
            ShowCenter(summary);
        }

        // ---------- balloon ----------

        private void SpawnBalloon()
        {
            _balloon = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _balloon.name = "Balloon";
            Destroy(_balloon.GetComponent<Collider>());
            _balloon.transform.SetParent(balloonAnchor, false);
            _balloon.transform.localPosition = Vector3.zero;
            _balloon.transform.localScale = new Vector3(minBalloonSize, minBalloonSize * 1.15f, minBalloonSize);

            // String under the balloon.
            var str = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            str.name = "String";
            Destroy(str.GetComponent<Collider>());
            str.transform.SetParent(_balloon.transform, false);
            str.transform.localPosition = new Vector3(0f, -1.1f, 0f);
            str.transform.localScale = new Vector3(0.02f, 0.6f, 0.02f);
            str.GetComponent<Renderer>().material.color = new Color(0.35f, 0.35f, 0.35f);

            _balloonRenderer = _balloon.GetComponent<Renderer>();
            _balloonColor = balloonColors.Length > 0 ? balloonColors[_colorIndex++ % balloonColors.Length] : Color.red;
            _balloonRenderer.material.color = Color.Lerp(Color.white, _balloonColor, 0.35f);

            _balloonState = BalloonState.Inflating;
            _repGripMax = 0f;
            _repStartTime = _elapsed;
        }

        private void UpdateBalloon()
        {
            if (_balloon == null || _balloonState == BalloonState.None) return;
            _repGripMax = Mathf.Max(_repGripMax, _grip);

            if (_balloonState == BalloonState.Inflating)
            {
                var fill = Mathf.Clamp01(_grip / TargetGrip);
                SetBalloonSize(Mathf.Lerp(minBalloonSize, maxBalloonSize, fill));
                _balloonRenderer.material.color = Color.Lerp(Color.Lerp(Color.white, _balloonColor, 0.35f), _balloonColor, fill);
                if (fill >= 1f)
                {
                    _balloonState = BalloonState.Full;
                    ShowCenter("Acum deschide mâna!");
                }
            }
            else if (_balloonState == BalloonState.Full)
            {
                // Gentle pulse while waiting for the hand to open.
                SetBalloonSize(maxBalloonSize * (1f + 0.04f * Mathf.Sin(Time.time * 8f)));
                if (_grip <= releaseGrip) Release();
            }
        }

        private void SetBalloonSize(float size)
        {
            _balloon.transform.localScale = new Vector3(size, size * 1.15f, size);
        }

        private void Release()
        {
            _repetitions++;
            _bestGrip = Mathf.Max(_bestGrip, _repGripMax);
            _repDurations.Add(_elapsed - _repStartTime);
            ShowCenter("");

            var flying = _balloon;
            flying.transform.SetParent(null, true);
            _flying.Add(flying);
            StartCoroutine(FlyAway(flying));

            _balloon = null;
            _balloonState = BalloonState.None;
            StartCoroutine(SpawnAfter(0.6f, ++_spawnToken));
        }

        private IEnumerator FlyAway(GameObject balloon)
        {
            var start = balloon.transform.position;
            var t = 0f;
            while (t < 2.5f && balloon != null)
            {
                t += Time.deltaTime;
                balloon.transform.position = start + new Vector3(Mathf.Sin(t * 3f) * 0.3f, t * t * 1.6f + t * 1.5f, 0f);
                yield return null;
            }
            if (balloon != null)
            {
                _flying.Remove(balloon);
                Destroy(balloon);
            }
        }

        private IEnumerator SpawnAfter(float seconds, int token)
        {
            yield return new WaitForSeconds(seconds);
            if (token == _spawnToken && _state == GameState.Playing && _balloon == null) SpawnBalloon();
            else if (token == _spawnToken && _state == GameState.Paused) StartCoroutine(SpawnAfter(0.3f, token));
        }

        private void ClearBalloons()
        {
            _spawnToken++;
            if (_balloon != null) Destroy(_balloon);
            _balloon = null;
            _balloonState = BalloonState.None;
            foreach (var b in _flying) if (b != null) Destroy(b);
            _flying.Clear();
        }

        // ---------- measurements ----------

        private void TrackAngles()
        {
            if (angles == null || !angles.HasAngles) return;
            var sum = 0f;
            for (var f = 0; f < HandAngleTracker.FingerCount; f++) sum += angles.Angles[f, (int)FingerJoint.Pip];
            var meanPip = sum / HandAngleTracker.FingerCount;
            _bestPipFlexion = float.IsNaN(_bestPipFlexion) ? meanPip : Mathf.Max(_bestPipFlexion, meanPip);
        }

        // ---------- UI ----------

        private void UpdateGripBar(bool tracked)
        {
            if (gripFill != null)
            {
                gripFill.anchorMax = new Vector2(tracked ? Mathf.Clamp01(_grip) : 0f, 1f);
            }
            if (gripTarget != null)
            {
                gripTarget.anchorMin = new Vector2(TargetGrip, 0f);
                gripTarget.anchorMax = new Vector2(TargetGrip, 1f);
            }
        }

        private void UpdateHud(bool tracked)
        {
            if (hudText != null)
            {
                var remaining = Mathf.Max(0f, sessionSeconds - _elapsed);
                hudText.text = $"Baloane: {_repetitions}\n" +
                               $"Timp: {Mathf.FloorToInt(remaining / 60f)}:{Mathf.FloorToInt(remaining % 60f):00}\n" +
                               $"Dificultate: {DifficultyName()}";
            }
            if (therapistText != null)
            {
                var hand = tracked ? source.LatestFrame.Handedness.ToString() : "-";
                therapistText.text = $"Terapeut:  SPACE start   P pauză   R restart   1/2/3 dificultate      " +
                                     $"Mâna: {hand}   Strângere: {_grip * 100f:0}%";
            }
        }

        private string DifficultyName() =>
            difficulty == Difficulty.Easy ? "ușor" : difficulty == Difficulty.Hard ? "greu" : "mediu";

        private void ShowCenter(string message)
        {
            if (centerText != null) centerText.text = message;
        }

        private void HandleKeys()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb == null) return;
            var space = kb.spaceKey.wasPressedThisFrame;
            var pause = kb.pKey.wasPressedThisFrame;
            var restart = kb.rKey.wasPressedThisFrame;
            var d1 = kb.digit1Key.wasPressedThisFrame;
            var d2 = kb.digit2Key.wasPressedThisFrame;
            var d3 = kb.digit3Key.wasPressedThisFrame;
#else
            var space = Input.GetKeyDown(KeyCode.Space);
            var pause = Input.GetKeyDown(KeyCode.P);
            var restart = Input.GetKeyDown(KeyCode.R);
            var d1 = Input.GetKeyDown(KeyCode.Alpha1);
            var d2 = Input.GetKeyDown(KeyCode.Alpha2);
            var d3 = Input.GetKeyDown(KeyCode.Alpha3);
#endif
            if (space && (_state == GameState.Waiting || _state == GameState.Finished)) StartGame();
            if (pause) TogglePause();
            if (restart) StartGame();
            if (d1) difficulty = Difficulty.Easy;
            if (d2) difficulty = Difficulty.Medium;
            if (d3) difficulty = Difficulty.Hard;
        }
    }
}
