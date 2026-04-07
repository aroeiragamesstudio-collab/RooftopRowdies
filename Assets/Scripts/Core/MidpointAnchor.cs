using UnityEngine;

public class MidpointAnchor : MonoBehaviour
{
    [Header("Referências dos Personagens")]
    public GunCharacterController gunCharacter;
    public JumpCharacterController jumpCharacter;

    [Header("Configurações de Distância")]
    [Tooltip("Distância minima entre os personagens antes do objeto começar a se mover. " +
             "Abaixo do limite, a ancora fica na ultima posição.")]
    [SerializeField] float minimumDistance = 2f;

    [Tooltip("Quão suave é o movimento até o meio. 0 = snap instantaneo, alto = mais suave.")]
    [SerializeField] float smoothSpeed = 5f;

    // The last valid midpoint that was calculated when distance >= minimumDistance
    Vector3 targetPosition;

    private void Awake()
    {
        // Auto-find characters if not assigned in the Inspector.
        // FindFirstObjectByType is the Unity 6 recommended replacement for FindObjectOfType.
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

        // Initialize the anchor at the midpoint between the two characters on start.
        targetPosition = CalculateMidpoint();
        transform.position = targetPosition;
    }

    private void LateUpdate()
    {
        // LateUpdate is intentional here: both characters will have already moved
        // during Update/FixedUpdate this frame, so we read their final positions.

        float distanceBetweenPlayers = Vector2.Distance(
            gunCharacter.transform.position,
            jumpCharacter.transform.position
        );

        // Only recalculate the target if the players are further apart than the minimum threshold.
        // This is the "dead zone" concept: inside the minimum distance, the anchor is frozen.
        if (distanceBetweenPlayers >= minimumDistance)
        {
            targetPosition = CalculateMidpoint();
        }

        // Smoothly move toward the target. Using Vector3.Lerp with Time.deltaTime gives
        // frame-rate independent interpolation that feels responsive but not snappy.
        transform.position = Vector3.Lerp(transform.position, targetPosition, smoothSpeed * Time.deltaTime);
    }

    /// <summary>
    /// Returns the exact world-space midpoint between both characters.
    /// Z is preserved from this object's own position so it doesn't drift on the Z axis
    /// (important for 2D games where Z controls render order/camera distance).
    /// </summary>
    private Vector3 CalculateMidpoint()
    {
        Vector3 midpoint = (gunCharacter.transform.position + jumpCharacter.transform.position) / 2f;
        midpoint.z = transform.position.z; // Keep Z stable — do not let it drift.
        return midpoint;
    }

    // ------------------------------------------------------------------
    // Gizmos: draws a visual debug aid in the Scene view so you can see
    // the minimum distance bubble and the anchor position at a glance.
    // ------------------------------------------------------------------
    private void OnDrawGizmosSelected()
    {
        if (gunCharacter == null || jumpCharacter == null) return;

        // Draw a line between both players
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(gunCharacter.transform.position, jumpCharacter.transform.position);

        // Draw the minimum distance circle centered on the anchor
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, minimumDistance / 2f);

        // Draw the anchor itself
        Gizmos.color = Color.green;
        Gizmos.DrawSphere(transform.position, 0.15f);
    }
}
