using System;
using UnityEngine;

namespace Rooftop.Core.Abilities
{
    public class Flight : MonoBehaviour
    {
        [Header("Vôo")]
        [SerializeField] float flightGravity = 0.2f;
        [SerializeField] float flightTime = 3f;
        [SerializeField] float flightSpeed = 2f;

        public bool IsFlying { get; private set; }
        public float FlightSpeed => flightSpeed;
        public float FlyProgress => flightTime > 0f ? 
            Mathf.Clamp01(_timeFlying / flightTime) : 0f;

        Rigidbody2D rb;
        float _originalGravity;
        float _timeFlying;

        private void Awake()
        {
            rb = GetComponent<Rigidbody2D>();
            _originalGravity = rb.gravityScale;
        }

        public void Tick(bool flightHeld, bool flightReleased, bool grounded,
            bool onWall, bool blocked)
        {
            if (blocked) return;

            if(flightHeld && !grounded)
            {
                IsFlying = true;
                rb.gravityScale = flightGravity;
            }

            if (flightReleased && IsFlying) Deactivate();

            if (IsFlying)
            {
                _timeFlying += Time.deltaTime;

                if (_timeFlying >= flightTime)
                {
                    IsFlying = false;
                    rb.gravityScale = _originalGravity;
                }
            }

            if ((grounded || onWall) && IsFlying) Deactivate();
        }

        private void Deactivate()
        {
            IsFlying = false;
            rb.gravityScale = _originalGravity;
            _timeFlying = 0f;
        }
    }
}