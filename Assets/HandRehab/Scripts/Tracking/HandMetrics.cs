using UnityEngine;

namespace HandRehab.Tracking
{
    public enum FingerJoint { Mcp = 0, Pip = 1, Dip = 2 }

    /// <summary>
    /// Measurements computed from the 3D world landmarks (metres).
    /// Phase 2 will validate the angles against a therapist's goniometer.
    /// </summary>
    public static class HandMetrics
    {
        /// <summary>MCP landmark index of the index, middle, ring and little finger.</summary>
        public static readonly int[] FingerMcp = { HandLandmark.IndexMcp, HandLandmark.MiddleMcp, HandLandmark.RingMcp, HandLandmark.PinkyMcp };
        public static readonly string[] FingerNames = { "Index", "Middle", "Ring", "Little" };

        /// <summary>
        /// Signed flexion of one finger joint, in degrees.
        /// 0 = straight, positive = bent towards the palm, negative = bent backwards (hyperextension).
        /// Only bending in the finger's own bending plane counts; sideways spread is removed.
        /// </summary>
        /// <param name="finger">0 = index, 1 = middle, 2 = ring, 3 = little.</param>
        public static float Flexion(in HandFrame f, int finger, FingerJoint joint)
        {
            var w = f.WorldPoints;
            var mcp = FingerMcp[finger];

            // Three consecutive points a-b-c; the joint is at b.
            int a, b, c;
            switch (joint)
            {
                case FingerJoint.Mcp: a = HandLandmark.Wrist; b = mcp; c = mcp + 1; break;
                case FingerJoint.Pip: a = mcp; b = mcp + 1; c = mcp + 2; break;
                default: a = mcp + 1; b = mcp + 2; c = mcp + 3; break;
            }

            var proximal = w[b] - w[a];
            var distal = w[c] - w[b];

            // The finger bends around a "side-to-side" axis across the hand (index MCP to little-finger MCP),
            // made perpendicular to this finger's metacarpal (wrist -> MCP).
            var metacarpal = w[mcp] - w[HandLandmark.Wrist];
            var across = w[HandLandmark.IndexMcp] - w[HandLandmark.PinkyMcp];
            var bendAxis = Vector3.ProjectOnPlane(across, metacarpal.normalized);
            if (bendAxis.sqrMagnitude < 1e-10f || proximal.sqrMagnitude < 1e-10f || distal.sqrMagnitude < 1e-10f)
            {
                return 0f;
            }

            // Left and right hands are mirror images, so the direction "towards the palm" flips.
            bendAxis = bendAxis.normalized * Chirality(f.Handedness);

            // Remove sideways spread, then measure the signed angle around the bend axis.
            var p = Vector3.ProjectOnPlane(proximal, bendAxis);
            var d = Vector3.ProjectOnPlane(distal, bendAxis);
            return Vector3.SignedAngle(p, d, bendAxis);
        }

        /// <summary>+1 for a right hand, -1 for a left hand (as seen in the mirrored camera image).</summary>
        public static float Chirality(Handedness h) => h == Handedness.Left ? -1f : 1f;

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
