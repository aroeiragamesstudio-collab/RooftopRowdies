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

        private void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
        }

        public void Tick(bool jumpHeld, bool jumpReleased, bool grounded,
            bool touchingSurface)
        {
            if (grounded && !IsHolding) _timer = 0f;

            // Começou a segurar
            if (jumpHeld && !grounded &&
                touchingSurface && !IsHolding && _timer < holdTime)
            {
                IsHolding = true;
                _rb.angularVelocity = 0f;
                _rb.bodyType = RigidbodyType2D.Static;
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

        public void Reset()
        {
            IsHolding = false;
            _timer = 0f;
        }

        private void Release()
        {
            IsHolding = false;

            _rb.bodyType = RigidbodyType2D.Dynamic;
        }
    }
}
