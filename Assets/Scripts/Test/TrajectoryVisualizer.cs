using UnityEngine;

/// <summary>
/// Trajectory visualizer for level design and playtesting.
/// Attach one instance to the Cat (JumpCharacterController) and one to the Pig (GunCharacterController).
///
/// ── ROPE CIRCLE ──────────────────────────────────────────────────────────────
/// Both instances now find the DistanceJoint2D on the Cat (the only one that has it)
/// and read joint.distance from it. This means both circles are always in sync,
/// even as the players adjust the rope length at runtime.
///
/// ── STATE-AWARE ARCS ─────────────────────────────────────────────────────────
/// The visualizer reads currentState from the character components every frame
/// and automatically shows the correct arc without any manual switching:
///
///   CAT (JumpCharacterController):
///     Idle / Walking     → Predictive jump arc from ground (what happens if they jump now)
///     Jumping            → Freeze arc from current mid-air position + live arc (see below)
///     Falling            → Arc using current rb.linearVelocity (where are they going)
///     Flying             → Arc using flightGravity instead of normal gravity
///     Shot               → Arc using current rb.linearVelocity
///     Swinging           → No arc (pendulum, not kinematically predictable)
///     HoldingSurface     → No arc (static)
///     SatDown            → No arc (waiting)
///     Absorbed           → No arc (controlled by Pig)
///
///   PIG (GunCharacterController):
///     Idle / Walking / Falling → Knockback arc in -gun.transform.right direction
///     absorbed == true         → CatShot arc in +gun.transform.right direction
///     Swinging                 → No arc
///     SatDown / Knockback      → No arc
///
/// ── JUMP FREEZE ARC ──────────────────────────────────────────────────────────
/// When the Cat enters the Jumping state:
///   - A FREEZE ARC is drawn from the Cat's CURRENT position (shrinks as they travel)
///   - The vertical component is fixed at originalJumpForce (the actual launch value)
///   - The horizontal component updates live with moveInput.x (so pressing left/right
///     while airborne immediately shifts where the freeze arc predicts landing)
///   - When the Cat lands (state leaves Jumping), the freeze arc disappears
///   - The regular LIVE ARC continues showing alongside it in a different color
///
/// ── BUILDS ───────────────────────────────────────────────────────────────────
/// Everything inside #if UNITY_EDITOR is stripped from builds automatically.
/// </summary>
[ExecuteAlways]
public class TrajectoryVisualizer : MonoBehaviour
{
    public enum CharacterType
    {
        CatJump,
        PigKnockback,
        CatShot,
        Custom
    }

    [Header("Character Setup")]
    [Tooltip("Which character this visualizer is attached to.")]
    public CharacterType characterType = CharacterType.CatJump;

    [Header("Launch Parameters (Edit Mode / Fallback)")]
    [Tooltip("Used in Edit Mode and as fallback if the component is not found at runtime.")]
    public float launchSpeed = 15f;

    [Tooltip("Launch angle in degrees. 90 = straight up, 0 = right, 180 = left.")]
    [Range(-180f, 180f)]
    public float launchAngleDeg = 90f;

    [Tooltip("For CatJump in Edit Mode: horizontal velocity while airborne.")]
    public float horizontalSpeed = 10f;

    [Tooltip("For CatJump in Edit Mode: horizontal direction (1 = right, -1 = left).")]
    public float horizontalDirection = 1f;

    [Header("Simulation Settings")]
    [Tooltip("How many seconds ahead to simulate.")]
    public float simulationDuration = 3f;

    [Tooltip("Number of line segments. More = smoother arc.")]
    [Range(10, 200)]
    public int resolution = 60;

    [Tooltip("When disabled, the arc only draws while this GameObject is selected.")]
    public bool alwaysDraw = true;

    [Header("Visual Settings")]
    [Tooltip("Color of the main predictive arc.")]
    public Color arcColor = Color.cyan;

    [Tooltip("Color of the freeze arc shown during a jump.")]
    public Color freezeArcColor = new Color(1f, 1f, 0f, 0.8f); // yellow

    [Tooltip("Duration each Debug.DrawLine stays visible. Keep at 0 for a live-updating arc.")]
    public float lineDuration = 0f;

    [Header("Rope Limit — Option 1: Constraint Circle")]
    [Tooltip("Draw a circle around the connected body showing the rope boundary. " +
             "Both character instances read from the same joint on the Cat, " +
             "so both circles always reflect the current rope length.")]
    public bool showRopeLimit = true;

    [Tooltip("Color of the rope limit circle.")]
    public Color ropeLimitColor = new Color(1f, 0.5f, 0f, 0.8f);

    [Tooltip("Segments used to draw the circle via Debug.DrawLine in Play Mode.")]
    [Range(16, 64)]
    public int ropeCircleSegments = 32;

    [Header("Rope Limit — Option 2: Arc Truncation")]
    [Tooltip("Stop the arc at the rope boundary and mark the cutoff point.")]
    public bool truncateAtRopeLimit = true;

    [Tooltip("Color of the truncation cutoff marker.")]
    public Color truncationColor = new Color(1f, 0.2f, 0.8f, 1f);

#if UNITY_EDITOR

    // -------------------------------------------------------------------------
    // Cached references — set once in Start, reused every frame
    // -------------------------------------------------------------------------

    private Rigidbody2D _rb;

    // The joint always lives on the Cat, regardless of which character this
    // visualizer is attached to. Both instances find it the same way.
    private DistanceJoint2D _joint;

    // Cat-side references
    private JumpCharacterController _jumpChar;

    // Pig-side references
    private GunCharacterController _gunChar;
    private GunController _gun;

    // Reflection field cache — grabbed once in Start, used every frame
    private System.Reflection.FieldInfo _jumpForceField;
    private System.Reflection.FieldInfo _jumpSpeedField;
    private System.Reflection.FieldInfo _moveInputField;
    private System.Reflection.FieldInfo _flightGravityField;
    private System.Reflection.FieldInfo _gunCharAbsorbedField;

    // -------------------------------------------------------------------------
    // Jump freeze arc state
    // -------------------------------------------------------------------------

    // The vertical component of the jump is fixed at the moment of launch.
    // We store it so the freeze arc always uses the correct upward force
    // even as the horizontal component changes live with player input.
    private float _frozenVerticalVelocity = 0f;
    private bool _isJumpFreezeActive = false;

    // We track the previous state to detect state transitions (entering/leaving Jumping)
    private JumpCharacterController.CharacterState _prevCatState =
        JumpCharacterController.CharacterState.Idle;

    // -------------------------------------------------------------------------
    // Initialization
    // -------------------------------------------------------------------------

    private void Start()
    {
        if (!Application.isPlaying) return;

        _rb = GetComponent<Rigidbody2D>();

        // ── Find the DistanceJoint2D ──────────────────────────────────────────
        // The joint always lives on the Cat (JumpCharacterController).
        // Whether this visualizer is on the Cat or the Pig, we find it the same way.
        // This ensures both visualizer instances read the same joint.distance,
        // fixing the bug where one instance showed a stale rope length.
        var cat = FindFirstObjectByType<JumpCharacterController>();
        if (cat != null)
            _joint = cat.GetComponent<DistanceJoint2D>();

        // ── Cat setup ─────────────────────────────────────────────────────────
        _jumpChar = GetComponent<JumpCharacterController>();
        if (_jumpChar != null)
        {
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            _jumpForceField = typeof(JumpCharacterController).GetField("originalJumpForce", flags);
            _jumpSpeedField = typeof(JumpCharacterController).GetField("originalSpeed", flags);
            _moveInputField = typeof(JumpCharacterController).GetField("moveInput", flags);
            _flightGravityField = typeof(JumpCharacterController).GetField("flightGravity", flags);
        }

        // ── Pig setup ─────────────────────────────────────────────────────────
        _gunChar = GetComponent<GunCharacterController>();
        _gun = GetComponentInChildren<GunController>();

        if (_gunChar != null)
        {
            // 'absorbed' is a private bool on GunCharacterController
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            _gunCharAbsorbedField = typeof(GunCharacterController).GetField("absorbed", flags);
        }
    }

    // -------------------------------------------------------------------------
    // Edit Mode
    // -------------------------------------------------------------------------

    private void OnDrawGizmos()
    {
        if (Application.isPlaying) return;
        if (!alwaysDraw) return;
        DrawEditMode();
    }

    private void OnDrawGizmosSelected()
    {
        if (Application.isPlaying) return;
        DrawEditMode();
    }

    // -------------------------------------------------------------------------
    // Play Mode
    // -------------------------------------------------------------------------

    private void Update()
    {
        if (!Application.isPlaying) return;

        // ── Jump freeze arc state machine ─────────────────────────────────────
        // We detect transitions into and out of Jumping by comparing the current
        // state with what it was last frame. This avoids polling WasPressedThisFrame
        // from a separate script and keeps the visualizer fully self-contained.
        if (_jumpChar != null)
        {
            var currentCatState = _jumpChar.currentState;

            // Entered Jumping this frame
            if (currentCatState == JumpCharacterController.CharacterState.Jumping &&
                _prevCatState != JumpCharacterController.CharacterState.Jumping)
            {
                // Capture the vertical component right now.
                // rb.linearVelocityY at this point is exactly what Jump() set:
                //   rb.linearVelocity = Vector2.up * originalJumpForce
                // We read it from the rb rather than from the field so we get
                // the actual physics value, not just the configured constant.
                _frozenVerticalVelocity = _rb != null ? _rb.linearVelocityY : GetJumpForce();
                _isJumpFreezeActive = true;
            }

            // Left Jumping (landed or transitioned to another state)
            if (currentCatState != JumpCharacterController.CharacterState.Jumping &&
                _prevCatState == JumpCharacterController.CharacterState.Jumping)
            {
                _isJumpFreezeActive = false;
            }

            _prevCatState = currentCatState;
        }

        DrawPlayMode();
    }

    // -------------------------------------------------------------------------
    // Edit Mode drawing — Inspector values, Gizmos API
    // -------------------------------------------------------------------------

    private void DrawEditMode()
    {
        float gravity = Physics2D.gravity.y;
        Vector2 velocity = GetInitialVelocity();
        Vector3 startPos = transform.position;

        // In Edit Mode, find the joint locally (Start hasn't run yet)
        DistanceJoint2D joint = FindFirstObjectByType<JumpCharacterController>()
                                        ?.GetComponent<DistanceJoint2D>();
        bool hasJoint = joint != null && joint.connectedBody != null;
        Vector3 connectedPos = hasJoint ? (Vector3)joint.connectedBody.position : Vector3.zero;
        float ropeLength = hasJoint ? joint.distance : 0f;

        DrawRopeLimitGizmos(startPos, connectedPos, ropeLength, hasJoint);
        DrawArcGizmos(startPos, velocity, gravity, hasJoint, connectedPos, ropeLength, arcColor);
    }

    // -------------------------------------------------------------------------
    // Play Mode drawing — live component values, Debug.DrawLine
    // -------------------------------------------------------------------------

    private void DrawPlayMode()
    {
        float gravity = Physics2D.gravity.y;
        Vector3 startPos = transform.position;

        bool hasJoint = _joint != null && _joint.connectedBody != null;
        Vector3 connectedPos = hasJoint ? (Vector3)_joint.connectedBody.position : Vector3.zero;
        float ropeLength = hasJoint ? _joint.distance : 0f;

        // ── Rope circle ───────────────────────────────────────────────────────
        if (showRopeLimit && hasJoint)
        {
            DrawDebugCircle(connectedPos, ropeLength, ropeLimitColor, ropeCircleSegments);
            Debug.DrawLine(
                startPos, connectedPos,
                new Color(ropeLimitColor.r, ropeLimitColor.g, ropeLimitColor.b, 0.3f),
                lineDuration
            );
        }

        // ── State-aware arc selection ─────────────────────────────────────────
        if (_jumpChar != null)
            DrawCatStateArc(startPos, gravity, hasJoint, connectedPos, ropeLength);
        else if (_gunChar != null)
            DrawPigStateArc(startPos, gravity, hasJoint, connectedPos, ropeLength);
        else
            DrawArcDebug(startPos, GetLiveVelocity(), gravity, hasJoint, connectedPos, ropeLength, arcColor);
    }

    // -------------------------------------------------------------------------
    // Cat state-aware arc dispatcher
    // -------------------------------------------------------------------------

    private void DrawCatStateArc(
        Vector3 startPos, float gravity,
        bool hasJoint, Vector3 connectedPos, float ropeLength)
    {
        var state = _jumpChar.currentState;

        switch (state)
        {
            // ── On the ground: show what happens if they jump right now ───────
            case JumpCharacterController.CharacterState.Idle:
            case JumpCharacterController.CharacterState.Walking:
                DrawArcDebug(startPos, GetLiveCatJumpVelocity(), gravity,
                             hasJoint, connectedPos, ropeLength, arcColor);
                break;

            // ── Actively jumping: show freeze arc + live arc ──────────────────
            // The freeze arc uses the captured vertical velocity plus live horizontal input.
            // The live arc uses fully live values for comparison.
            // Both originate from the CURRENT position so the freeze arc shrinks
            // naturally as the character travels through the air.
            case JumpCharacterController.CharacterState.Jumping:
                if (_isJumpFreezeActive)
                {
                    // Horizontal component: live input so pressing left/right shifts
                    // the predicted landing in real time.
                    float liveHorizontal = GetLiveCatHorizontalVelocity();
                    Vector2 freezeVelocity = new Vector2(liveHorizontal, _frozenVerticalVelocity);

                    DrawArcDebug(startPos, freezeVelocity, gravity,
                                 hasJoint, connectedPos, ropeLength, freezeArcColor);
                }
                // Live arc alongside in main color
                DrawArcDebug(startPos, GetLiveCatJumpVelocity(), gravity,
                             hasJoint, connectedPos, ropeLength, arcColor);
                break;

            // ── Falling: show where they're actually going right now ──────────
            // We use rb.linearVelocity directly — this is the real physics velocity,
            // not a predicted one. Useful for seeing where a fall will end up.
            case JumpCharacterController.CharacterState.Falling:
                if (_rb != null)
                    DrawArcDebug(startPos, _rb.linearVelocity, gravity,
                                 hasJoint, connectedPos, ropeLength, arcColor);
                break;

            // ── Flying: same as falling but with reduced gravity ──────────────
            // flightGravity is a multiplier on gravity (e.g. 0.2), so the actual
            // downward acceleration is gravity * flightGravity. We build the
            // effective gravity value and pass it to SampleArc.
            case JumpCharacterController.CharacterState.Flying:
                float flightGravMult = _flightGravityField != null
                    ? (float)_flightGravityField.GetValue(_jumpChar)
                    : 0.2f;
                float effectiveGravity = Physics2D.gravity.y * flightGravMult;
                if (_rb != null)
                    DrawArcDebug(startPos, _rb.linearVelocity, effectiveGravity,
                                 hasJoint, connectedPos, ropeLength, arcColor);
                break;

            // ── Shot: arc using actual physics velocity at this moment ────────
            case JumpCharacterController.CharacterState.Shot:
                if (_rb != null)
                    DrawArcDebug(startPos, _rb.linearVelocity, gravity,
                                 hasJoint, connectedPos, ropeLength, arcColor);
                break;

            // ── These states have no meaningful free-flight arc ───────────────
            // Swinging:      pendulum physics, not kinematically predictable
            // HoldingSurface: static body, not moving
            // SatDown:        waiting, not moving
            // Absorbed:       position controlled by Pig, not this character
            case JumpCharacterController.CharacterState.Swinging:
            case JumpCharacterController.CharacterState.HoldingSurface:
            case JumpCharacterController.CharacterState.SatDown:
            case JumpCharacterController.CharacterState.Absorbed:
                break;
        }
    }

    // -------------------------------------------------------------------------
    // Pig state-aware arc dispatcher
    // -------------------------------------------------------------------------

    private void DrawPigStateArc(
        Vector3 startPos, float gravity,
        bool hasJoint, Vector3 connectedPos, float ropeLength)
    {
        var state = _gunChar.currentState;

        // Check absorbed separately because it's a field, not a state on GunCharacterController.
        // We use the public IsAllyAbsorbed property if available, otherwise fall back to reflection.
        bool catAbsorbed = _gunChar.IsAllyAbsorbed;

        switch (state)
        {
            case GunCharacterController.CharacterState.Idle:
            case GunCharacterController.CharacterState.Walking:
            case GunCharacterController.CharacterState.Falling:
                if (catAbsorbed)
                {
                    // Cat is inside the gun — show where it will be shot
                    DrawArcDebug(startPos, GetLiveCatShotVelocity(), gravity,
                                 hasJoint, connectedPos, ropeLength, arcColor);
                }
                else
                {
                    // Cat is free — show pig knockback arc
                    DrawArcDebug(startPos, GetLivePigKnockbackVelocity(), gravity,
                                 hasJoint, connectedPos, ropeLength, arcColor);
                }
                break;

            // ── These states have no meaningful arc ───────────────────────────
            // Swinging:  pendulum
            // SatDown:   waiting, kinematic
            // Knockback: mid-knockback, live velocity in rb is more accurate
            //            but showing it would be redundant with what you can see
            case GunCharacterController.CharacterState.Swinging:
            case GunCharacterController.CharacterState.SatDown:
            case GunCharacterController.CharacterState.Knockback:
                break;

            // Shooting state: show knockback (what the pig will feel)
            case GunCharacterController.CharacterState.Shooting:
                DrawArcDebug(startPos, GetLivePigKnockbackVelocity(), gravity,
                             hasJoint, connectedPos, ropeLength, arcColor);
                break;
        }
    }

    // -------------------------------------------------------------------------
    // Velocity builders — live values from components
    // -------------------------------------------------------------------------

    private Vector2 GetLiveVelocity()
    {
        switch (characterType)
        {
            case CharacterType.CatJump: return GetLiveCatJumpVelocity();
            case CharacterType.PigKnockback: return GetLivePigKnockbackVelocity();
            case CharacterType.CatShot: return GetLiveCatShotVelocity();
            default: return AngleToVelocity(launchAngleDeg, launchSpeed);
        }
    }

    private Vector2 GetLiveCatJumpVelocity()
    {
        if (_jumpChar == null)
            return new Vector2(horizontalSpeed * horizontalDirection, launchSpeed);

        float jumpForce = GetJumpForce();
        float speed = _jumpSpeedField != null
            ? (float)_jumpSpeedField.GetValue(_jumpChar)
            : horizontalSpeed;

        return new Vector2(speed * GetLiveCatInputX(), jumpForce);
    }

    /// <summary>
    /// Returns only the horizontal velocity component, using live moveInput.x.
    /// Separated so the freeze arc can combine it with a fixed vertical.
    /// </summary>
    private float GetLiveCatHorizontalVelocity()
    {
        if (_jumpChar == null) return horizontalSpeed * horizontalDirection;

        float speed = _jumpSpeedField != null
            ? (float)_jumpSpeedField.GetValue(_jumpChar)
            : horizontalSpeed;

        return speed * GetLiveCatInputX();
    }

    private float GetLiveCatInputX()
    {
        if (_moveInputField == null) return horizontalDirection;
        Vector2 input = (Vector2)_moveInputField.GetValue(_jumpChar);
        return input.x;
    }

    private float GetJumpForce()
    {
        return _jumpForceField != null
            ? (float)_jumpForceField.GetValue(_jumpChar)
            : launchSpeed;
    }

    private Vector2 GetLivePigKnockbackVelocity()
    {
        if (_gun == null) return AngleToVelocity(launchAngleDeg, launchSpeed);
        // Knockback goes opposite to aim: matches knockbackDir = -aimDirection in GunController
        return -(Vector2)_gun.transform.right * _gun.gunKnockback;
    }

    private Vector2 GetLiveCatShotVelocity()
    {
        if (_gun == null) return AngleToVelocity(launchAngleDeg, launchSpeed);
        // Shot goes forward: matches shootDirection = transform.right in GunController
        return (Vector2)_gun.transform.right * _gun.shootForce;
    }

    // -------------------------------------------------------------------------
    // Edit Mode velocity — Inspector values only
    // -------------------------------------------------------------------------

    private Vector2 GetInitialVelocity()
    {
        switch (characterType)
        {
            case CharacterType.CatJump:
                return new Vector2(horizontalSpeed * horizontalDirection, launchSpeed);
            default:
                return AngleToVelocity(launchAngleDeg, launchSpeed);
        }
    }

    // -------------------------------------------------------------------------
    // Arc drawing — Gizmos (Edit Mode)
    // -------------------------------------------------------------------------

    private void DrawRopeLimitGizmos(
        Vector3 startPos, Vector3 connectedPos, float ropeLength, bool hasJoint)
    {
        if (!showRopeLimit || !hasJoint) return;

        Gizmos.color = ropeLimitColor;
        Gizmos.DrawWireSphere(connectedPos, ropeLength);
        Gizmos.color = new Color(ropeLimitColor.r, ropeLimitColor.g, ropeLimitColor.b, 0.3f);
        Gizmos.DrawLine(startPos, connectedPos);
    }

    private void DrawArcGizmos(
        Vector3 startPos, Vector2 velocity, float gravity,
        bool hasJoint, Vector3 connectedPos, float ropeLength,
        Color color)
    {
        Vector3 prev = startPos;
        float highestY = startPos.y;
        Vector3 highestPoint = startPos;
        Vector3 landingPoint = startPos;
        bool truncated = false;
        Vector3 truncationPoint = startPos;
        float dt = simulationDuration / resolution;

        Gizmos.color = color;

        for (int i = 1; i <= resolution; i++)
        {
            float t = i * dt;
            Vector3 curr = SampleArc(startPos, velocity, gravity, t);

            if (truncateAtRopeLimit && hasJoint && !truncated)
            {
                if (Vector2.Distance(curr, connectedPos) > ropeLength)
                {
                    truncationPoint = FindRopeCrossing(prev, curr, connectedPos, ropeLength);
                    truncated = true;
                    Gizmos.color = color;
                    Gizmos.DrawLine(prev, truncationPoint);
                    Gizmos.color = truncationColor;
                    Gizmos.DrawWireSphere(truncationPoint, 0.25f);
                    break;
                }
            }

            Gizmos.color = color;
            Gizmos.DrawLine(prev, curr);

            if (curr.y > highestY) { highestY = curr.y; highestPoint = curr; }
            if (curr.y >= startPos.y) landingPoint = curr;

            prev = curr;
        }

        if (!truncated)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(highestPoint, 0.2f);
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(landingPoint, 0.2f);
            Gizmos.color = new Color(color.r, color.g, color.b, 0.3f);
            Gizmos.DrawLine(startPos, new Vector3(landingPoint.x, startPos.y, startPos.z));
            DrawLabels(startPos, highestPoint, landingPoint, false, Vector3.zero);
        }
        else
        {
            DrawLabels(startPos, highestPoint, landingPoint, true, truncationPoint);
        }

        Gizmos.color = Color.white;
        Gizmos.DrawWireSphere(startPos, 0.1f);
    }

    // -------------------------------------------------------------------------
    // Arc drawing — Debug.DrawLine (Play Mode)
    // -------------------------------------------------------------------------

    private void DrawArcDebug(
        Vector3 startPos, Vector2 velocity, float gravity,
        bool hasJoint, Vector3 connectedPos, float ropeLength,
        Color color)
    {
        Vector3 prev = startPos;
        float highestY = startPos.y;
        Vector3 highestPoint = startPos;
        Vector3 landingPoint = startPos;
        bool truncated = false;
        Vector3 truncationPoint = startPos;
        float dt = simulationDuration / resolution;

        for (int i = 1; i <= resolution; i++)
        {
            float t = i * dt;
            Vector3 curr = SampleArc(startPos, velocity, gravity, t);

            if (truncateAtRopeLimit && hasJoint && !truncated)
            {
                if (Vector2.Distance(curr, connectedPos) > ropeLength)
                {
                    truncationPoint = FindRopeCrossing(prev, curr, connectedPos, ropeLength);
                    truncated = true;
                    Debug.DrawLine(prev, truncationPoint, color, lineDuration);
                    DrawDebugCross(truncationPoint, truncationColor, 0.25f);
                    break;
                }
            }

            Debug.DrawLine(prev, curr, color, lineDuration);

            if (curr.y > highestY) { highestY = curr.y; highestPoint = curr; }
            if (curr.y >= startPos.y) landingPoint = curr;

            prev = curr;
        }

        if (!truncated)
        {
            DrawDebugCross(highestPoint, Color.yellow, 0.2f);
            DrawDebugCross(landingPoint, Color.red, 0.2f);
            Debug.DrawLine(
                startPos,
                new Vector3(landingPoint.x, startPos.y, startPos.z),
                new Color(color.r, color.g, color.b, 0.4f),
                lineDuration
            );
        }
        else
        {
            DrawDebugCross(truncationPoint, truncationColor, 0.25f);
        }
    }

    // -------------------------------------------------------------------------
    // Utility
    // -------------------------------------------------------------------------

    private Vector3 SampleArc(Vector3 startPos, Vector2 velocity, float gravity, float t)
    {
        return new Vector3(
            startPos.x + velocity.x * t,
            startPos.y + velocity.y * t + 0.5f * gravity * t * t,
            startPos.z
        );
    }

    private Vector3 FindRopeCrossing(Vector3 from, Vector3 to, Vector3 anchor, float ropeLength)
    {
        Vector3 low = from, high = to;
        for (int i = 0; i < 8; i++)
        {
            Vector3 mid = (low + high) * 0.5f;
            if (Vector2.Distance(mid, anchor) < ropeLength) low = mid;
            else high = mid;
        }
        return (low + high) * 0.5f;
    }

    private void DrawDebugCircle(Vector3 center, float radius, Color color, int segments)
    {
        float step = 360f / segments;
        Vector3 prev = center + new Vector3(radius, 0f, 0f);
        for (int i = 1; i <= segments; i++)
        {
            float angle = i * step * Mathf.Deg2Rad;
            Vector3 next = center + new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f);
            Debug.DrawLine(prev, next, color, lineDuration);
            prev = next;
        }
    }

    private void DrawDebugCross(Vector3 center, Color color, float size)
    {
        Debug.DrawLine(center + Vector3.left * size, center + Vector3.right * size, color, lineDuration);
        Debug.DrawLine(center + Vector3.down * size, center + Vector3.up * size, color, lineDuration);
    }

    private Vector2 AngleToVelocity(float angleDeg, float speed)
    {
        float rad = angleDeg * Mathf.Deg2Rad;
        return new Vector2(Mathf.Cos(rad) * speed, Mathf.Sin(rad) * speed);
    }

    private void DrawLabels(
        Vector3 start, Vector3 apex, Vector3 landing,
        bool wasTruncated, Vector3 truncationPoint)
    {
        if (!wasTruncated)
        {
            UnityEditor.Handles.color = Color.white;
            UnityEditor.Handles.Label(apex + Vector3.up * 0.3f, $"Peak: +{apex.y - start.y:F1}u");
            UnityEditor.Handles.Label(landing + Vector3.up * 0.3f, $"Range: {Mathf.Abs(landing.x - start.x):F1}u");
        }
        else
        {
            UnityEditor.Handles.color = truncationColor;
            UnityEditor.Handles.Label(
                truncationPoint + Vector3.up * 0.35f,
                $"Rope limit\n{Vector2.Distance(start, truncationPoint):F1}u from start"
            );
        }
    }

    // -------------------------------------------------------------------------
    // Auto-fill context menu helpers
    // -------------------------------------------------------------------------

    [ContextMenu("Auto-Fill from JumpCharacterController")]
    private void AutoFillFromJumpController()
    {
        JumpCharacterController jump = GetComponent<JumpCharacterController>();
        if (jump == null) { Debug.LogWarning("TrajectoryVisualizer: No JumpCharacterController found."); return; }

        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var sf = typeof(JumpCharacterController).GetField("originalSpeed", flags);
        var jf = typeof(JumpCharacterController).GetField("originalJumpForce", flags);

        if (sf != null) horizontalSpeed = (float)sf.GetValue(jump);
        if (jf != null) launchSpeed = (float)jf.GetValue(jump);

        characterType = CharacterType.CatJump;
        launchAngleDeg = 90f;

        Debug.Log($"TrajectoryVisualizer: Auto-filled — launchSpeed={launchSpeed}, horizontalSpeed={horizontalSpeed}");
    }

    [ContextMenu("Auto-Fill from GunController")]
    private void AutoFillFromGunController()
    {
        GunController gun = GetComponentInChildren<GunController>();
        if (gun == null) { Debug.LogWarning("TrajectoryVisualizer: No GunController found."); return; }

        launchSpeed = gun.gunKnockback;
        characterType = CharacterType.PigKnockback;

        Debug.Log($"TrajectoryVisualizer: Auto-filled — launchSpeed={launchSpeed}");
    }

#endif // UNITY_EDITOR
}