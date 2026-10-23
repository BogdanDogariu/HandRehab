using System;

namespace HandRehab.Tracking
{
    /// <summary>
    /// Anything that produces hand frames: the live webcam + MediaPipe source now,
    /// a recorded-session player later. Events are always raised on Unity's main thread.
    /// </summary>
    public interface IHandSource
    {
        /// <summary>Raised once per new tracking result that contains a hand.</summary>
        event Action<HandFrame> FrameReady;

        /// <summary>Raised when a hand that was being tracked is no longer seen.</summary>
        event Action HandLost;

        /// <summary>True while a hand is currently tracked.</summary>
        bool IsTracking { get; }

        /// <summary>The most recent frame (check IsValid).</summary>
        HandFrame LatestFrame { get; }
    }
}
