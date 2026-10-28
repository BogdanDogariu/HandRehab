using UnityEngine;

namespace HandRehab.Tracking
{
    /// <summary>
    /// One Euro filter (Casiez et al., 2012) for a Vector3.
    /// Smooths strongly when the point is still (removes jitter) and lightly when it moves fast (keeps lag low).
    /// minCutoff: lower = smoother at rest. beta: higher = less lag during fast movement.
    /// </summary>
    public sealed class OneEuroFilterVector3
    {
        private readonly float _minCutoff;
        private readonly float _beta;
        private readonly float _derivativeCutoff;

        private Vector3 _value;
        private Vector3 _speed;
        private double _lastTime;
        private bool _initialised;

        public OneEuroFilterVector3(float minCutoff, float beta, float derivativeCutoff = 1f)
        {
            _minCutoff = minCutoff;
            _beta = beta;
            _derivativeCutoff = derivativeCutoff;
        }

        public Vector3 Filter(Vector3 input, double timeSeconds)
        {
            if (!_initialised)
            {
                _initialised = true;
                _value = input;
                _speed = Vector3.zero;
                _lastTime = timeSeconds;
                return input;
            }

            var dt = (float)(timeSeconds - _lastTime);
            if (dt <= 1e-4f)
            {
                return _value;
            }
            _lastTime = timeSeconds;

            var rawSpeed = (input - _value) / dt;
            _speed = Vector3.Lerp(_speed, rawSpeed, Alpha(_derivativeCutoff, dt));

            var cutoff = _minCutoff + _beta * _speed.magnitude;
            _value = Vector3.Lerp(_value, input, Alpha(cutoff, dt));
            return _value;
        }

        public void Reset() => _initialised = false;

        private static float Alpha(float cutoffHz, float dt)
        {
            var tau = 1f / (2f * Mathf.PI * cutoffHz);
            return 1f / (1f + tau / dt);
        }
    }
}
