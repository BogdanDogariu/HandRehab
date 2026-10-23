using UnityEngine;

namespace HandRehab.Tracking
{
    public enum Handedness
    {
        Unknown,
        Left,
        Right
    }

    /// <summary>
    /// One tracked hand at one moment in time. This is the only hand data the rest of the
    /// app (overlay, recorder, games) ever sees, so MediaPipe stays hidden behind IHandSource.
    /// </summary>
    public struct HandFrame
    {
        public const int LandmarkCount = 21;

        /// <summary>Seconds since tracking started.</summary>
        public double Timestamp;

        /// <summary>Which hand this is, from the patient's point of view.</summary>
        public Handedness Handedness;

        /// <summary>Handedness classification score, 0..1.</summary>
        public float Confidence;

        /// <summary>
        /// 21 points normalised to the displayed (mirrored) camera image:
        /// x 0 = left edge, 1 = right edge; y 0 = top, 1 = bottom; z = relative depth (smaller = closer).
        /// </summary>
        public Vector3[] ImagePoints;

        /// <summary>21 points in metres, origin near the hand's centre (MediaPipe world landmarks).</summary>
        public Vector3[] WorldPoints;

        public bool IsValid => ImagePoints != null && ImagePoints.Length == LandmarkCount;
    }

    /// <summary>MediaPipe hand landmark indices.</summary>
    public static class HandLandmark
    {
        public const int Wrist = 0;
        public const int ThumbCmc = 1, ThumbMcp = 2, ThumbIp = 3, ThumbTip = 4;
        public const int IndexMcp = 5, IndexPip = 6, IndexDip = 7, IndexTip = 8;
        public const int MiddleMcp = 9, MiddlePip = 10, MiddleDip = 11, MiddleTip = 12;
        public const int RingMcp = 13, RingPip = 14, RingDip = 15, RingTip = 16;
        public const int PinkyMcp = 17, PinkyPip = 18, PinkyDip = 19, PinkyTip = 20;

        /// <summary>The 21 bones MediaPipe draws between landmarks (pairs of indices).</summary>
        public static readonly int[] Connections =
        {
            0, 1,   1, 2,   2, 3,   3, 4,          // thumb
            0, 5,   5, 6,   6, 7,   7, 8,          // index
            5, 9,   9, 10,  10, 11, 11, 12,        // middle
            9, 13,  13, 14, 14, 15, 15, 16,        // ring
            13, 17, 0, 17,  17, 18, 18, 19, 19, 20 // pinky + palm
        };
    }
}
