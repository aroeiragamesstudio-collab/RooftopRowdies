using UnityEngine;

/// <summary>
/// Handles visual color feedback for the Gun Character (pig) based on its current state.
/// Attach this script to the same GameObject as GunCharacterController, or any child that
/// has a SpriteRenderer. It reads public state only — no physics, no input.
/// </summary>
public class GunCharacterVisualizer : MonoBehaviour
{
    [Header("Character Reference")]
    [Tooltip("Reference to the Gun Character controller. Auto-found if left empty.")]
    public GunCharacterController gunChar;

    [Header("Sprite Reference")]
    [Tooltip("The SpriteRenderer to colorize. Auto-found on this GameObject if left empty.")]
    public SpriteRenderer spriteRenderer;

    // ──────────────────────────────────────────────
    // State Colors
    // ──────────────────────────────────────────────
    [Header("State Colors")]
    [Tooltip("Color when Idle.")]
    public Color idleColor       = new Color(1f, 0.85f, 0.85f); // pale pink

    [Tooltip("Color when Walking.")]
    public Color walkingColor    = new Color(1f, 0.65f, 0.65f); // warm pink

    [Tooltip("Color when Falling.")]
    public Color fallingColor    = new Color(1f, 0.4f, 0.2f);   // orange

    [Header("Shooting")]
    [Tooltip("Flash color on shoot (brief).")]
    public Color shootingColor   = new Color(1f, 1f, 0.1f);     // yellow flash

    [Header("Knockback")]
    [Tooltip("Color when taking knockback from own gun.")]
    public Color knockbackColor  = new Color(0.9f, 0.2f, 0.5f); // hot pink

    [Header("Swinging")]
    public Color swingingColor   = new Color(0.7f, 0.5f, 1f);   // lavender

    [Header("SatDown / Waiting")]
    public Color satDownColor    = new Color(0.55f, 0.55f, 0.55f); // grey

    [Header("Absorbed ally inside gun")]
    [Tooltip("Color when the jump character is currently absorbed into the gun.")]
    public Color chargedColor    = new Color(0f, 1f, 0.6f);     // teal — 'loaded'

    // ──────────────────────────────────────────────
    // Shoot Flash
    // ──────────────────────────────────────────────
    [Header("Shoot Flash Settings")]
    [Tooltip("How long the shoot flash lasts in seconds.")]
    [Range(0.02f, 0.5f)]
    public float shootFlashDuration = 0.08f;

    // ──────────────────────────────────────────────
    // Transition
    // ──────────────────────────────────────────────
    [Header("Transition Speed")]
    [Range(0f, 50f)]
    public float colorLerpSpeed = 12f;

    // ──────────────────────────────────────────────
    // Internal
    // ──────────────────────────────────────────────
    private Color _currentColor;
    private GunCharacterController.CharacterState _lastState;

    // We track the shoot flash independently with a simple timer.
    private float _shootFlashTimer;
    private bool  _isFlashing;

    // We need to detect when the Shooting state is first entered this frame.
    private GunCharacterController.CharacterState _prevState;

    // ──────────────────────────────────────────────
    // Lifecycle
    // ──────────────────────────────────────────────

    private void Awake()
    {
        if (gunChar == null)
            gunChar = GetComponent<GunCharacterController>();

        if (gunChar == null)
            gunChar = GetComponentInParent<GunCharacterController>();

        if (gunChar == null)
            Debug.LogError("[GunCharacterVisualizer] Could not find GunCharacterController! " +
                           "Assign it manually in the Inspector.");

        if (spriteRenderer == null)
            spriteRenderer = GetComponent<SpriteRenderer>();

        if (spriteRenderer == null)
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();

        if (spriteRenderer == null)
            Debug.LogError("[GunCharacterVisualizer] Could not find SpriteRenderer! " +
                           "Assign it manually in the Inspector.");

        _currentColor = idleColor;
        _prevState    = GunCharacterController.CharacterState.Idle;
    }

    private void Update()
    {
        if (gunChar == null || spriteRenderer == null) return;

        DetectShootFlash();

        Color targetColor = ResolveTargetColor();

        if (colorLerpSpeed <= 0f)
            _currentColor = targetColor;
        else
            _currentColor = Color.Lerp(_currentColor, targetColor, colorLerpSpeed * Time.deltaTime);

        spriteRenderer.color = _currentColor;

        _prevState = gunChar.currentState;
    }

    // ──────────────────────────────────────────────
    // Shoot Flash Detection
    // ──────────────────────────────────────────────

    /// <summary>
    /// Detects the moment the Shooting state is entered and starts a flash timer.
    /// This gives a punchy instant response independent of the lerp speed.
    /// </summary>
    private void DetectShootFlash()
    {
        bool justShot = (gunChar.currentState == GunCharacterController.CharacterState.Shooting &&
                         _prevState            != GunCharacterController.CharacterState.Shooting);

        if (justShot)
        {
            _isFlashing      = true;
            _shootFlashTimer = shootFlashDuration;
        }

        if (_isFlashing)
        {
            _shootFlashTimer -= Time.deltaTime;
            if (_shootFlashTimer <= 0f)
                _isFlashing = false;
        }
    }

    // ──────────────────────────────────────────────
    // Core Logic
    // ──────────────────────────────────────────────

    private Color ResolveTargetColor()
    {
        // Shoot flash takes priority over everything else
        if (_isFlashing)
        {
            // Fade the flash out over its duration for a natural feel
            float flashAlpha = _shootFlashTimer / shootFlashDuration;
            return Color.Lerp(idleColor, shootingColor, flashAlpha);
        }

        switch (gunChar.currentState)
        {
            case GunCharacterController.CharacterState.Idle:
                // Check if ally is absorbed — show a charged teal even at idle
                return gunChar.IsAllyAbsorbed ? chargedColor : idleColor;

            case GunCharacterController.CharacterState.Walking:
                return gunChar.IsAllyAbsorbed ? chargedColor : walkingColor;

            case GunCharacterController.CharacterState.Falling:
                return fallingColor;

            case GunCharacterController.CharacterState.Shooting:
                return shootingColor;

            case GunCharacterController.CharacterState.Swinging:
                return swingingColor;

            case GunCharacterController.CharacterState.SatDown:
                return satDownColor;

            case GunCharacterController.CharacterState.Knockback:
                // Pulse rapidly between knockback color and white for impact feel
                float kPulse = Mathf.Abs(Mathf.Sin(Time.time * 12f));
                return Color.Lerp(knockbackColor, Color.white, kPulse * 0.5f);

            default:
                return idleColor;
        }
    }

    // ──────────────────────────────────────────────
    // Editor Debug
    // ──────────────────────────────────────────────
    private void OnGUI()
    {
#if UNITY_EDITOR
        if (gunChar == null) return;
        GUI.Label(new Rect(10, 30, 300, 20),
            $"[GunChar] State: {gunChar.currentState} | AbsorbedAlly: {gunChar.IsAllyAbsorbed}");
#endif
    }
}
