using UnityEngine;

namespace HandRehab.Tracking
{
    /// <summary>
    /// First measurements computed from 3D world landmarks. These are early versions;
    /// Phase 2 will validate them against a therapist's goniometer.
    /// </summary>
    public static class HandMetrics
    {
        /// <summary>
        /// Flexion at joint b in degrees: 0 = straight finger, larger = more bent.
        /// a-b-c are three consecutive landmarks, e.g. IndexMcp, IndexPip, IndexDip for the PIP joint.
        /// </summary>
        public static float FlexionDegrees(in HandFrame f, int a, int b, int c)
        {
            var ba = f.WorldPoints[a] - f.WorldPoints[b];
            var bc = f.WorldPoints[c] - f.WorldPoints[b];
            if (ba.sqrMagnitude < 1e-10f || bc.sqrMagnitude < 1e-10f) return 0f;
            return 180f - Vector3.Angle(ba, bc);
        }

        /// <summary>Distance between thumb tip and index tip in centimetres.</summary>
        public static float PinchDistanceCm(in HandFrame f) =>
            Vector3.Distance(f.WorldPoints[HandLandmark.ThumbTip], f.WorldPoints[HandLandmark.IndexTip]) * 100f;

        /// <summary>
        /// 0 = fist, about 1 = fully open: mean fingertip-to-wrist distance divided by palm length
        /// (wrist to middle MCP), rescaled. Rough indicator for grip games until calibration exists.
        /// </summary>
        public static float Openness(in HandFrame f)
        {
            var w = f.WorldPoints[HandLandmark.Wrist];
            var palm = Vector3.Distance(w, f.WorldPoints[HandLandmark.MiddleMcp]);
            if (palm < 1e-5f) return 0f;
            var sum = Vector3.Distance(w, f.WorldPoints[HandLandmark.IndexTip])
                    + Vector3.Distance(w, f.WorldPoints[HandLandmark.MiddleTip])
                    + Vector3.Distance(w, f.WorldPoints[HandLandmark.RingTip])
                    + Vector3.Distance(w, f.WorldPoints[HandLandmark.PinkyTip]);
            var ratio = sum / 4f / palm; // ~1.0 in a fist, ~1.9 fully open
            return Mathf.Clamp01((ratio - 1.0f) / 0.9f);
        }
    }
}
