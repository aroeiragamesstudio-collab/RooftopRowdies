using UnityEngine;

namespace Rooftop.Core.Abilities
{
    public class SurfaceHold : MonoBehaviour
    {
        [SerializeField] float holdTime = 10f;

        public bool IsHolding { get; private set; }
        public float HoldProgress => holdTime > 0f ?
            Mathf.Clamp01(_timer / holdTime) : 0f;

        Rigidbody2D _rb;
        float _timer;
        bool _usedSinceLastLanding;

        private void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
        }

        public void Tick(bool jumpHeld, bool jumpReleased, bool grounded,
            bool touchingSurface)
        {
            if (grounded) _usedSinceLastLanding = false;

            // Começou a segurar
            if (!_usedSinceLastLanding && jumpHeld && !grounded &&
                touchingSurface && !IsHolding)
            {
                IsHolding = true;
                _usedSinceLastLanding = true;
                _rb.angularVelocity = 0f;
                _rb.bodyType = RigidbodyType2D.Static;
                _timer = 0f;
            }

            // Soltou
            if (jumpReleased && IsHolding) Release();

            if (IsHolding)
            {
                _timer += Time.deltaTime;

                if (_timer >= holdTime)
                {
                    Release();
                }
            }
        }

        private void Release()
        {
            IsHolding = false;
            _timer = 0;

            _rb.bodyType = RigidbodyType2D.Dynamic;
        }
    }
}
