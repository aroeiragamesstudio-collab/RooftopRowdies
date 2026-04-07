using UnityEngine;

public class MidpointAnchor : MonoBehaviour
{
    [Header("Character References")]
    [Tooltip("Reference to the pig/gun character.")]
    public GunCharacterController gunCharacter;

    [Tooltip("Reference to the cat/jump character.")]
    public JumpCharacterController jumpCharacter;

    [Header("Hysteresis Thresholds")]
    [Tooltip("Players must be CLOSER than this distance for oscillation suppression to " +
             "activate. Must always be less than unfreezeDistance.")]
    [SerializeField] float freezeDistance = 2f;

    [Tooltip("Players must be FURTHER than this distance for the anchor to resume tracking. " +
             "Must always be greater than freezeDistance. The gap between the two is the " +
             "hysteresis band — prevents state from toggling at the boundary.")]
    [SerializeField] float unfreezeDistance = 3f;

    [Header("Velocity Detection")]
    [Tooltip("Smoothed midpoint speed (world units/second) above which the anchor always " +
             "tracks, even when players are close. Handles both players walking together.")]
    [SerializeField] float midpointVelocityThreshold = 0.5f;

    [Tooltip("EMA smoothing factor for midpoint speed (0–1). Lower = more resistant to " +
             "single-frame physics impulse spikes from the DistanceJoint2D constraint.")]
    [Range(0.01f, 1f)]
    [SerializeField] float velocitySmoothing = 0.15f;

    // Internal state
    Vector3 frozenPosition;        // world-space position held while frozen
    Vector3 previousMidpoint;
    float smoothedMidpointSpeed;
    bool isFrozen = false;

    private void Awake()
    {
        if (gunCharacter == null)
            gunCharacter = FindFirstObjectByType<GunCharacterController>();

        if (jumpCharacter == null)
            jumpCharacter = FindFirstObjectByType<JumpCharacterController>();

        if (gunCharacter == null || jumpCharacter == null)
        {
            Debug.LogError("[MidpointAnchor] Could not find one or both character controllers. " +
                           "Please assign them manually in the Inspector.");
            enabled = false;
            return;
        }

        if (freezeDistance >= unfreezeDistance)
        {
            Debug.LogWarning("[MidpointAnchor] freezeDistance must be less than unfreezeDistance. " +
                             "Adjusting unfreezeDistance to freezeDistance + 1.");
            unfreezeDistance = freezeDistance + 1f;
        }

        // Initialise — snap to the exact midpoint immediately.
        Vector3 startMidpoint = CalculateMidpoint();
        frozenPosition = startMidpoint;
        previousMidpoint = startMidpoint;
        transform.position = startMidpoint;
    }

    private void LateUpdate()
    {
        Vector3 currentMidpoint = CalculateMidpoint();

        // --- Step 1: EMA-smoothed midpoint speed.
        // Dampens single-frame spikes (DistanceJoint2D impulses) while still
        // detecting sustained movement like both players walking together.
        float rawSpeed = Vector3.Distance(currentMidpoint, previousMidpoint) / Time.deltaTime;
        smoothedMidpointSpeed = smoothedMidpointSpeed + velocitySmoothing * (rawSpeed - smoothedMidpointSpeed);
        previousMidpoint = currentMidpoint;

        // --- Step 2: Hysteresis state machine.
        float distanceBetweenPlayers = Vector2.Distance(
            gunCharacter.transform.position,
            jumpCharacter.transform.position
        );

        if (!isFrozen)
        {
            if (distanceBetweenPlayers < freezeDistance && smoothedMidpointSpeed <= midpointVelocityThreshold)
            {
                isFrozen = true;
                frozenPosition = currentMidpoint; // record where we stop
            }
        }
        else
        {
            if (distanceBetweenPlayers > unfreezeDistance || smoothedMidpointSpeed > midpointVelocityThreshold)
                isFrozen = false;
        }

        // --- Step 3: Set position INSTANTLY — no Lerp, no lag.
        // Cinemachine's PositionComposer damping handles all camera smoothing.
        // A second smoothing layer here compounds with Cinemachine and causes
        // the "readjusting constantly" symptom regardless of parameter tuning.
        transform.position = isFrozen ? frozenPosition : currentMidpoint;
    }

    private Vector3 CalculateMidpoint()
    {
        Vector3 midpoint = (gunCharacter.transform.position + jumpCharacter.transform.position) / 2f;
        midpoint.z = transform.position.z;
        return midpoint;
    }

    private void OnDrawGizmosSelected()
    {
        if (gunCharacter == null || jumpCharacter == null) return;

        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(gunCharacter.transform.position, jumpCharacter.transform.position);

        // Inner threshold — where freeze activates
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, freezeDistance / 2f);

        // Outer threshold — where freeze releases (hysteresis band lives between these two)
        Gizmos.color = new Color(1f, 0.5f, 0f);
        Gizmos.DrawWireSphere(transform.position, unfreezeDistance / 2f);

        Gizmos.color = isFrozen ? Color.red : Color.green;
        Gizmos.DrawSphere(transform.position, 0.15f);

#if UNITY_EDITOR
        UnityEditor.Handles.Label(
            transform.position + Vector3.up * 0.6f,
            $"state: {(isFrozen ? "FROZEN" : "tracking")}\n" +
            $"smoothed speed: {smoothedMidpointSpeed:F2} u/s\n" +
            $"player dist: {Vector2.Distance(gunCharacter.transform.position, jumpCharacter.transform.position):F2}"
        );
#endif
    }
}
