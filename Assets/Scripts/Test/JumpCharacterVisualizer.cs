using UnityEngine;

/// <summary>
/// Handles visual color feedback for the Jump Character (cat) based on its current state.
/// Attach this script to the same GameObject as JumpCharacterController, or any child that
/// has a SpriteRenderer. It reads public state from JumpCharacterController and never
/// modifies physics or input — pure visual responsibility.
/// </summary>
public class JumpCharacterVisualizer : MonoBehaviour
{
    [Header("Character Reference")]
    [Tooltip("Reference to the Jump Character controller. Auto-found if left empty.")]
    public JumpCharacterController jumpChar;

    [Header("Sprite Reference")]
    [Tooltip("The SpriteRenderer to colorize. Auto-found on this GameObject if left empty.")]
    public SpriteRenderer spriteRenderer;

    // ──────────────────────────────────────────────
    // State Colors
    // ──────────────────────────────────────────────
    [Header("State Colors")]
    [Tooltip("Color when Idle on the ground.")]
    public Color idleColor       = new Color(0.4f, 0.8f, 1f);   // light blue

    [Tooltip("Color when Walking.")]
    public Color walkingColor    = new Color(0.3f, 1f, 0.5f);   // light green

    [Tooltip("Color when Falling freely.")]
    public Color fallingColor    = new Color(1f, 0.6f, 0.2f);   // orange

    [Tooltip("Color when Jumping (initial upward motion).")]
    public Color jumpingColor    = new Color(1f, 1f, 0.3f);     // yellow

    [Tooltip("Color when Shot by the gun character.")]
    public Color shotColor       = new Color(1f, 0.2f, 0.2f);   // bright red

    [Tooltip("Color when Swinging on the rope.")]
    public Color swingingColor   = new Color(0.8f, 0.4f, 1f);   // purple

    [Tooltip("Color when SatDown / waiting.")]
    public Color satDownColor    = new Color(0.5f, 0.5f, 0.5f); // grey

    [Tooltip("Color when Flying (mid-air sustained flight).")]
    public Color flyingColor     = new Color(0.2f, 0.9f, 1f);   // cyan

    [Header("HoldingSurface – Time-Based Fade")]
    [Tooltip("Starting color when the character first grabs a surface.")]
    public Color holdStartColor  = new Color(0.2f, 1f, 0.2f);   // bright green

    [Tooltip("End color when hold time is almost expired (character is about to fall).")]
    public Color holdEndColor    = new Color(0.1f, 0.1f, 0.1f); // near black

    [Header("Absorbed by Gun Character")]
    public Color absorbedColor   = new Color(1f, 0.85f, 0f);    // gold

    // ──────────────────────────────────────────────
    // Color transition smoothing
    // ──────────────────────────────────────────────
    [Header("Transition Speed")]
    [Tooltip("How fast the color snaps to the target when NOT in a timed state. " +
             "Higher = snappier. Use 0 for instant.")]
    [Range(0f, 50f)]
    public float colorLerpSpeed = 12f;

    // Internal tracking
    private Color _currentColor;
    private JumpCharacterController.CharacterState _lastState;

    // ──────────────────────────────────────────────
    // Lifecycle
    // ──────────────────────────────────────────────

    private void Awake()
    {
        // Auto-resolve references if not assigned in Inspector
        if (jumpChar == null)
            jumpChar = GetComponent<JumpCharacterController>();

        if (jumpChar == null)
            jumpChar = GetComponentInParent<JumpCharacterController>();

        if (jumpChar == null)
            Debug.LogError("[JumpCharacterVisualizer] Could not find JumpCharacterController! " +
                           "Assign it manually in the Inspector.");

        if (spriteRenderer == null)
            spriteRenderer = GetComponent<SpriteRenderer>();

        if (spriteRenderer == null)
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();

        if (spriteRenderer == null)
            Debug.LogError("[JumpCharacterVisualizer] Could not find SpriteRenderer! " +
                           "Assign it manually in the Inspector.");

        _currentColor = idleColor;
    }

    private void Update()
    {
        if (jumpChar == null || spriteRenderer == null) return;

        Color targetColor = ResolveTargetColor();

        // Smooth interpolation between current and target color.
        // For timed states (HoldingSurface, Flying) the target color is already
        // calculated frame-by-frame inside ResolveTargetColor(), so we apply
        // it directly (lerpSpeed is ignored there for precision).
        if (colorLerpSpeed <= 0f)
            _currentColor = targetColor;
        else
            _currentColor = Color.Lerp(_currentColor, targetColor, colorLerpSpeed * Time.deltaTime);

        spriteRenderer.color = _currentColor;
    }

    // ──────────────────────────────────────────────
    // Core Logic
    // ──────────────────────────────────────────────

    /// <summary>
    /// Returns the color that the sprite SHOULD be this frame, taking the
    /// current character state into account. Timed states return an interpolated
    /// color based on how far along the timer is.
    /// </summary>
    private Color ResolveTargetColor()
    {
        switch (jumpChar.currentState)
        {
            case JumpCharacterController.CharacterState.Idle:
                return idleColor;

            case JumpCharacterController.CharacterState.Walking:
                return walkingColor;

            case JumpCharacterController.CharacterState.Falling:
                return fallingColor;

            case JumpCharacterController.CharacterState.Jumping:
                return jumpingColor;

            case JumpCharacterController.CharacterState.Shot:
                // Pulse between white and shotColor to feel more dramatic
                float pulse = Mathf.Abs(Mathf.Sin(Time.time * 8f));
                return Color.Lerp(shotColor, Color.white, pulse * 0.4f);

            case JumpCharacterController.CharacterState.Swinging:
                return swingingColor;

            case JumpCharacterController.CharacterState.SatDown:
                return satDownColor;

            case JumpCharacterController.CharacterState.HoldingSurface:
                // ── TIME-BASED DARKENING ──────────────────────────────────────────
                // t goes from 0 (just grabbed) → 1 (hold time fully expired).
                // We use the public HoldProgress property we expose on the controller.
                float holdT = jumpChar.HoldProgress;
                return Color.Lerp(holdStartColor, holdEndColor, holdT);

            case JumpCharacterController.CharacterState.Flying:
                // ── TIME-BASED FADE FOR FLYING ───────────────────────────────────
                // t goes from 0 (just started flying) → 1 (flight time nearly over).
                float flyT = jumpChar.FlyProgress;
                // Lerp from bright cyan toward a faded blue as flight runs out
                return Color.Lerp(flyingColor, fallingColor, flyT);

            case JumpCharacterController.CharacterState.Absorbed:
                return absorbedColor;

            default:
                return idleColor;
        }
    }

    // ──────────────────────────────────────────────
    // Editor Gizmo (optional debug helper)
    // ──────────────────────────────────────────────
    private void OnGUI()
    {
#if UNITY_EDITOR
        if (jumpChar == null) return;
        // Draws a small on-screen label with the current state during Play Mode.
        // Remove this block if you don't want it in your editor.
        GUI.Label(new Rect(10, 10, 300, 20),
            $"[JumpChar] State: {jumpChar.currentState} | HoldProg: {jumpChar.HoldProgress:F2} | FlyProg: {jumpChar.FlyProgress:F2}");
#endif
    }
}
