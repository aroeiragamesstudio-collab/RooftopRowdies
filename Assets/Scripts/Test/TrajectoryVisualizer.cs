using UnityEngine;

/// <summary>
/// Trajectory visualizer for level design and playtesting.
/// Attach this script to a character (Cat or Pig) to preview their movement arc
/// in the Scene view — both in Edit Mode and during Play Mode.
///
/// LIVE VALUES (Play Mode):
///   During Play Mode, the visualizer reads values directly from the character
///   components instead of using the Inspector fields. This means:
///
///   CatJump:
///     - Jump force is read live from JumpCharacterController.originalJumpForce
///     - Horizontal speed and direction are read live from JumpCharacterController
///       (specifically the moveInput the cat is currently holding)
///     - The arc updates in real time as you move the analog stick / hold a direction key
///
///   PigKnockback:
///     - Force is read live from GunController.gunForce
///     - Direction is read live from the gun child object's transform.right,
///       which GunController already rotates toward the mouse/stick every frame.
///       Knockback goes in -transform.right (opposite to aim), matching your Knockback() code.
///
///   CatShot:
///     - Same force and gun transform as PigKnockback, but direction is +transform.right
///       (forward, matching your ShootPlayer() code).
///
///   Inspector fields (launchSpeed, launchAngleDeg, etc.) are still used in Edit Mode
///   and as fallback if the relevant component is not found at runtime.
///
/// HOW TO VIEW DURING PLAY MODE:
///   Keep the Scene view tab open alongside the Game view.
///   The arc draws every frame via Debug.DrawLine (Scene view only).
///
/// ROPE LIMIT FEATURES:
///   Option 1 — Circle centered on the connected body showing max reach.
///   Option 2 — Arc truncation at rope length with a cutoff marker.
///
/// BUILDS:
///   Everything inside #if UNITY_EDITOR is stripped from your final build.
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
    [Tooltip("Which movement to visualize.")]
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

    [Tooltip("When disabled, the arc only draws while this GameObject is selected in the Scene view.")]
    public bool alwaysDraw = true;

    [Header("Visual Settings")]
    public Color arcColor = Color.cyan;

    [Tooltip("Duration each Debug.DrawLine segment stays visible. " +
             "Keep at 0 to redraw every frame (persistent-looking arc during Play Mode).")]
    public float lineDuration = 0f;

    [Header("Rope Limit — Option 1: Constraint Circle")]
    [Tooltip("Draw a circle around the connected body showing the rope boundary.")]
    public bool showRopeLimit = true;

    [Tooltip("Color of the rope limit circle.")]
    public Color ropeLimitColor = new Color(1f, 0.5f, 0f, 0.8f);

    [Tooltip("Segments used to draw the circle in Play Mode (Gizmos handles it automatically in Edit Mode).")]
    [Range(16, 64)]
    public int ropeCircleSegments = 32;

    [Header("Rope Limit — Option 2: Arc Truncation")]
    [Tooltip("Stop the arc at the rope boundary and mark the cutoff point.")]
    public bool truncateAtRopeLimit = true;

    [Tooltip("Color of the truncation cutoff marker.")]
    public Color truncationColor = new Color(1f, 0.2f, 0.8f, 1f);

#if UNITY_EDITOR

    // -------------------------------------------------------------------------
    // Cached component references — populated in Start so we don't search
    // every frame with GetComponent during Play Mode.
    // -------------------------------------------------------------------------

    // Shared
    private Rigidbody2D _rb;
    private DistanceJoint2D _joint;

    // Cat
    private JumpCharacterController _jumpChar;

    // Pig / Gun
    private GunController _gun;

    // Cached private field accessors via reflection.
    // We grab these once in Start rather than every frame — reflection is slow,
    // caching the FieldInfo is the standard pattern to avoid that cost.
    private System.Reflection.FieldInfo _jumpForceField;
    private System.Reflection.FieldInfo _jumpSpeedField;
    private System.Reflection.FieldInfo _moveInputField;

    private void Start()
    {
        if (!Application.isPlaying) return;

        _rb = GetComponent<Rigidbody2D>();
        _joint = GetComponent<DistanceJoint2D>();

        // Cat setup
        _jumpChar = GetComponent<JumpCharacterController>();
        if (_jumpChar != null)
        {
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            _jumpForceField = typeof(JumpCharacterController).GetField("originalJumpForce", flags);
            _jumpSpeedField = typeof(JumpCharacterController).GetField("originalSpeed", flags);
            _moveInputField = typeof(JumpCharacterController).GetField("moveInput", flags);
        }

        // Pig setup — gun is a child object
        _gun = GetComponentInChildren<GunController>();
    }

    // -------------------------------------------------------------------------
    // Edit Mode callbacks
    // -------------------------------------------------------------------------

    private void OnDrawGizmos()
    {
        if (Application.isPlaying) return;
        if (!alwaysDraw) return;
        DrawTrajectoryGizmos();
    }

    private void OnDrawGizmosSelected()
    {
        if (Application.isPlaying) return;
        DrawTrajectoryGizmos();
    }

    // -------------------------------------------------------------------------
    // Play Mode: runs every frame
    // -------------------------------------------------------------------------

    private void Update()
    {
        if (!Application.isPlaying) return;
        DrawTrajectoryDebug();
    }

    // -------------------------------------------------------------------------
    // Edit Mode drawing — Gizmos API
    // -------------------------------------------------------------------------

    private void DrawTrajectoryGizmos()
    {
        float gravity = Physics2D.gravity.y;
        Vector2 velocity = GetInitialVelocity();   // Inspector values
        Vector3 startPos = transform.position;

        DistanceJoint2D joint = GetComponent<DistanceJoint2D>();
        bool hasJoint = joint != null && joint.connectedBody != null;
        Vector3 connectedPos = hasJoint ? (Vector3)joint.connectedBody.position : Vector3.zero;
        float ropeLength = hasJoint ? joint.distance : 0f;

        if (showRopeLimit && hasJoint)
        {
            Gizmos.color = ropeLimitColor;
            Gizmos.DrawWireSphere(connectedPos, ropeLength);
            Gizmos.color = new Color(ropeLimitColor.r, ropeLimitColor.g, ropeLimitColor.b, 0.3f);
            Gizmos.DrawLine(startPos, connectedPos);
        }

        DrawArcGizmos(startPos, velocity, gravity, hasJoint, connectedPos, ropeLength);
    }

    // -------------------------------------------------------------------------
    // Play Mode drawing — Debug.DrawLine, reads live component values
    // -------------------------------------------------------------------------

    private void DrawTrajectoryDebug()
    {
        float gravity = Physics2D.gravity.y;
        Vector2 velocity = GetLiveVelocity();       // Live component values
        Vector3 startPos = transform.position;

        // Re-read joint every frame — joint.distance can be changed at runtime
        bool hasJoint = _joint != null && _joint.connectedBody != null;
        Vector3 connectedPos = hasJoint ? (Vector3)_joint.connectedBody.position : Vector3.zero;
        float ropeLength = hasJoint ? _joint.distance : 0f;

        if (showRopeLimit && hasJoint)
        {
            DrawDebugCircle(connectedPos, ropeLength, ropeLimitColor, ropeCircleSegments);
            Debug.DrawLine(
                startPos, connectedPos,
                new Color(ropeLimitColor.r, ropeLimitColor.g, ropeLimitColor.b, 0.3f),
                lineDuration
            );
        }

        DrawArcDebug(startPos, velocity, gravity, hasJoint, connectedPos, ropeLength);
    }

    // -------------------------------------------------------------------------
    // GetInitialVelocity — Edit Mode, uses Inspector fields
    // -------------------------------------------------------------------------

    private Vector2 GetInitialVelocity()
    {
        switch (characterType)
        {
            case CharacterType.CatJump:
                return new Vector2(horizontalSpeed * horizontalDirection, launchSpeed);

            case CharacterType.PigKnockback:
            case CharacterType.CatShot:
            case CharacterType.Custom:
            default:
                return AngleToVelocity(launchAngleDeg, launchSpeed);
        }
    }

    // -------------------------------------------------------------------------
    // GetLiveVelocity — Play Mode, reads directly from components
    // -------------------------------------------------------------------------

    /// <summary>
    /// Reads the current state of the character components to build a velocity
    /// vector that matches what the character would actually launch at right now.
    ///
    /// Falls back to Inspector values if the relevant component is not found,
    /// so this is safe even if the script is placed on an unexpected object.
    /// </summary>
    private Vector2 GetLiveVelocity()
    {
        switch (characterType)
        {
            case CharacterType.CatJump:
                return GetLiveCatJumpVelocity();

            case CharacterType.PigKnockback:
                return GetLivePigKnockbackVelocity();

            case CharacterType.CatShot:
                return GetLiveCatShotVelocity();

            case CharacterType.Custom:
            default:
                return AngleToVelocity(launchAngleDeg, launchSpeed);
        }
    }

    /// <summary>
    /// Cat jump velocity, built from live component state.
    ///
    /// Vertical component: originalJumpForce — this is always the same value
    /// since Jump() sets velocity to Vector2.up * originalJumpForce directly.
    ///
    /// Horizontal component: originalSpeed * moveInput.x
    /// moveInput.x is the current stick/key horizontal input, so the arc
    /// leans left or right in real time as the player holds a direction.
    /// If moveInput.x is 0 (no input held), the arc goes straight up.
    /// </summary>
    private Vector2 GetLiveCatJumpVelocity()
    {
        if (_jumpChar == null) // Fallback
            return new Vector2(horizontalSpeed * horizontalDirection, launchSpeed);

        float jumpForce = _jumpForceField != null
            ? (float)_jumpForceField.GetValue(_jumpChar)
            : launchSpeed;

        float speed = _jumpSpeedField != null
            ? (float)_jumpSpeedField.GetValue(_jumpChar)
            : horizontalSpeed;

        // moveInput is a Vector2 in JumpCharacterController.
        // We read .x for horizontal direction — positive = right, negative = left.
        float inputX = 0f;
        if (_moveInputField != null)
        {
            Vector2 input = (Vector2)_moveInputField.GetValue(_jumpChar);
            inputX = input.x;
        }

        return new Vector2(speed * inputX, jumpForce);
    }

    /// <summary>
    /// Pig knockback velocity, built from live component state.
    ///
    /// GunController rotates the gun child's transform toward the mouse/stick
    /// every frame in AimGunWithMouse() / AimGunWithStick().
    /// transform.right is therefore always the live aim direction.
    ///
    /// Your Knockback() code does: knockbackDir = -aimDirection
    /// So we negate transform.right here to match exactly.
    /// </summary>
    private Vector2 GetLivePigKnockbackVelocity()
    {
        if (_gun == null) // Fallback
            return AngleToVelocity(launchAngleDeg, launchSpeed);

        // -transform.right = knockback direction (opposite to aim)
        Vector2 knockbackDir = -(Vector2)_gun.transform.right;
        return knockbackDir * _gun.gunKnockback;
    }

    /// <summary>
    /// Cat shot velocity, built from live component state.
    ///
    /// Your ShootPlayer() code does:
    ///   shootDirection = transform.right
    ///   shootVelocity  = shootDirection * gunForce
    /// So we read the same gun transform.right and gunForce directly.
    /// </summary>
    private Vector2 GetLiveCatShotVelocity()
    {
        if (_gun == null) // Fallback
            return AngleToVelocity(launchAngleDeg, launchSpeed);

        Vector2 shotDir = (Vector2)_gun.transform.right;
        return shotDir * _gun.gunKnockback;
    }

    // -------------------------------------------------------------------------
    // Arc drawing — separated so Gizmos and Debug paths share the same loop
    // -------------------------------------------------------------------------

    private void DrawArcGizmos(
        Vector3 startPos, Vector2 velocity, float gravity,
        bool hasJoint, Vector3 connectedPos, float ropeLength)
    {
        Vector3 previousPoint = startPos;
        float highestY = startPos.y;
        Vector3 highestPoint = startPos;
        Vector3 landingPoint = startPos;
        bool truncated = false;
        Vector3 truncationPoint = startPos;

        float dt = simulationDuration / resolution;
        Gizmos.color = arcColor;

        for (int i = 1; i <= resolution; i++)
        {
            float t = i * dt;
            Vector3 currentPoint = SampleArc(startPos, velocity, gravity, t);

            if (truncateAtRopeLimit && hasJoint && !truncated)
            {
                if (Vector2.Distance(currentPoint, connectedPos) > ropeLength)
                {
                    truncationPoint = FindRopeCrossing(previousPoint, currentPoint, connectedPos, ropeLength);
                    truncated = true;

                    Gizmos.color = arcColor;
                    Gizmos.DrawLine(previousPoint, truncationPoint);
                    Gizmos.color = truncationColor;
                    Gizmos.DrawWireSphere(truncationPoint, 0.25f);
                    break;
                }
            }

            Gizmos.color = arcColor;
            Gizmos.DrawLine(previousPoint, currentPoint);

            if (currentPoint.y > highestY) { highestY = currentPoint.y; highestPoint = currentPoint; }
            if (currentPoint.y >= startPos.y) landingPoint = currentPoint;

            previousPoint = currentPoint;
        }

        if (!truncated)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(highestPoint, 0.2f);
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(landingPoint, 0.2f);
            Gizmos.color = new Color(arcColor.r, arcColor.g, arcColor.b, 0.3f);
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

    private void DrawArcDebug(
        Vector3 startPos, Vector2 velocity, float gravity,
        bool hasJoint, Vector3 connectedPos, float ropeLength)
    {
        Vector3 previousPoint = startPos;
        float highestY = startPos.y;
        Vector3 highestPoint = startPos;
        Vector3 landingPoint = startPos;
        bool truncated = false;
        Vector3 truncationPoint = startPos;

        float dt = simulationDuration / resolution;

        for (int i = 1; i <= resolution; i++)
        {
            float t = i * dt;
            Vector3 currentPoint = SampleArc(startPos, velocity, gravity, t);

            if (truncateAtRopeLimit && hasJoint && !truncated)
            {
                if (Vector2.Distance(currentPoint, connectedPos) > ropeLength)
                {
                    truncationPoint = FindRopeCrossing(previousPoint, currentPoint, connectedPos, ropeLength);
                    truncated = true;

                    Debug.DrawLine(previousPoint, truncationPoint, arcColor, lineDuration);
                    DrawDebugCross(truncationPoint, truncationColor, 0.25f);
                    break;
                }
            }

            Debug.DrawLine(previousPoint, currentPoint, arcColor, lineDuration);

            if (currentPoint.y > highestY) { highestY = currentPoint.y; highestPoint = currentPoint; }
            if (currentPoint.y >= startPos.y) landingPoint = currentPoint;

            previousPoint = currentPoint;
        }

        if (!truncated)
        {
            DrawDebugCross(highestPoint, Color.yellow, 0.2f);
            DrawDebugCross(landingPoint, Color.red, 0.2f);
            Debug.DrawLine(
                startPos,
                new Vector3(landingPoint.x, startPos.y, startPos.z),
                new Color(arcColor.r, arcColor.g, arcColor.b, 0.4f),
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
        float x = startPos.x + velocity.x * t;
        float y = startPos.y + velocity.y * t + 0.5f * gravity * t * t;
        return new Vector3(x, y, startPos.z);
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
        float angleStep = 360f / segments;
        Vector3 prev = center + new Vector3(radius, 0f, 0f);

        for (int i = 1; i <= segments; i++)
        {
            float angle = i * angleStep * Mathf.Deg2Rad;
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

    private void DrawLabels(Vector3 start, Vector3 apex, Vector3 landing, bool wasTruncated, Vector3 truncationPoint)
    {
        UnityEditor.Handles.color = Color.white;

        if (!wasTruncated)
        {
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
    // Auto-fill context menu helpers (right-click the component in Inspector)
    // -------------------------------------------------------------------------

    [ContextMenu("Auto-Fill from JumpCharacterController")]
    private void AutoFillFromJumpController()
    {
        JumpCharacterController jump = GetComponent<JumpCharacterController>();
        if (jump == null) { Debug.LogWarning("TrajectoryVisualizer: No JumpCharacterController found."); return; }

        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var speedField = typeof(JumpCharacterController).GetField("originalSpeed", flags);
        var jumpField = typeof(JumpCharacterController).GetField("originalJumpForce", flags);

        if (speedField != null) horizontalSpeed = (float)speedField.GetValue(jump);
        if (jumpField != null) launchSpeed = (float)jumpField.GetValue(jump);

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