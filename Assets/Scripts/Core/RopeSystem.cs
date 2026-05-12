using UnityEngine;

namespace Rooftop.Core.Abilities
{
    public class RopeSystem : MonoBehaviour
    {
        [Header("Ajuste de Corda")]
        [SerializeField] float ropeAdjustSpeed = 2f;
        [SerializeField] float ropeMinDistance = 1f;
        public float ropeMaxDistance = 15f;

        [Header("Rope Taut Anti-Exploit")]
        [SerializeField] float ropeTautTolerance = 0.2f;
        [SerializeField] float tautGravityMultiplier = 4f;
        [SerializeField] float tautVelocityThreshold = 0.1f;

        DistanceJoint2D _distanceJoint;
        Rigidbody2D _porkyRigidBody;
        Rigidbody2D _pawsRigidBody;

        bool _tautPenaltyActive;
        float _jumpOriginalGravity;
        float _gunOriginalGravity;

        GunCharacterController _porky;
        JumpCharacterController _paws;

        public void Init(GunCharacterController porky, JumpCharacterController paws)
        {
            _porky = porky;
            _paws = paws;
            _distanceJoint = _paws.GetComponent<DistanceJoint2D>();
            _pawsRigidBody = _paws.GetComponent<Rigidbody2D>();
            _jumpOriginalGravity = _pawsRigidBody.gravityScale;
            _porkyRigidBody = _porky.GetComponent<Rigidbody2D>();
            _gunOriginalGravity = _porkyRigidBody.gravityScale;
        }

        public void TryAdjust(float verticalInput)
        {
            if (_distanceJoint == null) return;
            if (Mathf.Abs(verticalInput) < 0.1f) return;

            float newDistance = _distanceJoint.distance - (verticalInput * ropeAdjustSpeed * Time.deltaTime);
            _distanceJoint.distance = Mathf.Clamp(newDistance, ropeMinDistance, ropeMaxDistance);
        }

        public void HandleRopeTautPenalty()
        {
            if (_paws == null || _porky == null || _distanceJoint == null) return;

            // Step 1: Compute ACTUAL separation between both rigidbodies.
            // We use this instead of distanceJoint.distance because the joint's
            // .distance is the *target*, not the real-time measured separation.
            float currentSeparation = Vector2.Distance(_pawsRigidBody.position, _porkyRigidBody.position);

            // The authoritative max is this controller's ropeMaxDistance,
            // since this joint owns the rope length.
            bool ropeIsMaxTaut = currentSeparation >= (_distanceJoint.distance - ropeTautTolerance);

            if (!ropeIsMaxTaut)
            {
                // Rope is slack — release penalty on both characters
                if (_tautPenaltyActive)
                {
                    SetTautPenalty(false);
                }
                return;
            }

            // Step 2: Are both characters pulling AWAY from each other?
            // Compute the rope axis from this character (jump) toward the other (gun).
            Vector2 ropeAxis = (_porkyRigidBody.position - _pawsRigidBody.position).normalized;

            // Dot each velocity against the direction AWAY from the partner.
            // For jump: pulling away = moving opposite to ropeAxis (negative dot)
            // For gun:  pulling away = moving along ropeAxis (positive dot)
            float jumpPullAway = Vector2.Dot(_pawsRigidBody.linearVelocity, -ropeAxis);
            float gunPullAway = Vector2.Dot(_porkyRigidBody.linearVelocity, ropeAxis);

            bool bothPullingApart = jumpPullAway > tautVelocityThreshold
                                 && gunPullAway > tautVelocityThreshold;

            // Step 3: Apply or release penalty symmetrically
            if (bothPullingApart && !_tautPenaltyActive)
            {
                SetTautPenalty(true);
            }
            else if (!bothPullingApart && _tautPenaltyActive)
            {
                SetTautPenalty(false);
            }
        }

        public bool IsTautPenaltyActive => _tautPenaltyActive;

        public void SetTautPenalty(bool active)
        {
            _tautPenaltyActive = active;
            float mult = active ? tautGravityMultiplier : 1.0f;
            _pawsRigidBody.gravityScale = active
                ? _jumpOriginalGravity * mult
                : _jumpOriginalGravity;

            _porkyRigidBody.gravityScale = active
                ? _gunOriginalGravity * mult
                : _gunOriginalGravity;
        }

        public static void ApplyPendulumForce(Rigidbody2D swinger, Vector2 anchorPos,
            float horizontalInput, float speed)
        {
            Vector2 toAnchor = anchorPos - swinger.position;
            Vector2 tangent = Vector2.Perpendicular(toAnchor).normalized;

            if(horizontalInput != 0)
            {
                swinger.AddForce(-1 * horizontalInput * speed * tangent, ForceMode2D.Force);
            }
            if (horizontalInput == 0 && swinger.linearVelocity.magnitude < 0.01f)
            {
                swinger.linearVelocity = Vector2.zero;
            }
        }
    }
}