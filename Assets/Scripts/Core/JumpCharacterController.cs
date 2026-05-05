using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Script principal do personagem gato/que pula, sendo aqui a maioria das mudanças dele
/// </summary>
public class JumpCharacterController : MonoBehaviour
{
    public enum CharacterState
    {
        Idle,
        Walking,
        Falling,
        Jumping,
        Shot,
        Swinging,
        SatDown,
        HoldingSurface,
        Absorbed,
        Flying,
    }

    [Header("Atributos Base")]
    public CharacterState currentState;
    [SerializeField] float originalSpeed = 10f;
    [SerializeField] float originalJumpForce = 15f;
    public PlayerInput playerInput;

    [Header("Informação da mecânica de agarrar")]
    public float holdTime = 10f;
    [SerializeField] Transform bottomPos, sidePos, topPos;
    [SerializeField] LayerMask floorLayer, wallLayer, topLayer;
    [SerializeField] Vector2 bottomSize = new Vector2(1.5f, 0.1f);
    [SerializeField] Vector2 sideSize = new Vector2(0.1f, 1.5f);
    [SerializeField] Vector2 topSize = new Vector2(1.5f, 0.1f);

    [Header("Informação de ser atirado")]
    public float timeToNormal = 5f;

    [Header("Vôo")]
    public float flightGravity = 0.2f;
    public float flightTime = 3f;
    public float flightSpeed = 2f;

    [Header("Ajuste de Corda")]
    [SerializeField] RopeAdjustCondition ropeAdjustCondition = RopeAdjustCondition.OnlyWhenAllyWaiting;
    [SerializeField] float ropeAdjustSpeed = 2f;
    [SerializeField] float ropeMinDistance = 1f;
    [SerializeField] float ropeMaxDistance = 15f;

    [Header("Pêndulo")]
    [SerializeField] float swingDamping = 0.8f;

    Vector2 moveInput;
    float x;
    float dir = 1;
    float originalGravity;
    float originalDamping;

    InputAction moveAction;
    InputAction jumpAction;
    InputAction waitAction;
    InputAction flightAction;
    Rigidbody2D rb;

    [HideInInspector]
    public bool beingShot;

    float timePassed;
    float timePassedFlying;
    bool jumping;
    bool holding;
    bool startHold;
    bool falling;
    bool flying;
    bool waiting;
    bool facingRight = true;

    [HideInInspector]
    public GunCharacterController porky;
    DistanceJoint2D distanceJoint;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        distanceJoint = GetComponent<DistanceJoint2D>();

        playerInput = GetComponent<PlayerInput>();

        Debug.Log($"Active control scheme = {playerInput.currentControlScheme}");

        moveAction = playerInput.currentActionMap.FindAction("Move");
        jumpAction = playerInput.currentActionMap.FindAction("Jump");
        waitAction = playerInput.currentActionMap.FindAction("Wait");
        flightAction = playerInput.currentActionMap.FindAction("Flight");

        originalGravity = rb.gravityScale;

        originalDamping = rb.linearDamping;

        currentState = CharacterState.Idle;
    }

    // Criar metodos para limpar o Update
    private void Update()
    {
        if (beingShot)
        {
            timePassed += Time.deltaTime;

            if (timePassed >= timeToNormal)
            {
                beingShot = false;
                timePassed = 0;
            }
            return;
        }

        // Verificar depois para tentar melhorar a chamada
        if (!waiting)
        {
            moveInput = moveAction.ReadValue<Vector2>();

            if (moveInput.x != 0)
                dir = moveInput.x;

            x = moveInput.x;

            if (jumpAction.WasPressedThisFrame() && OnGround())
            {
                Jump();
            }

        }

        HandleRopeAdjust();

        // VERIFICAR SE NÃO FAZ MAIS SENTIDO SEGURAR MULTIPLAS VEZES AO INVÉS DE SÓ UMA
        if (!startHold && jumpAction.IsPressed() && !OnGround() && (OnCeiling() || OnWall()))
        {
            holding = true;

            startHold = true;

            rb.angularVelocity = 0f;
            rb.bodyType = RigidbodyType2D.Static;
        }

        if (jumpAction.WasReleasedThisFrame())
        {
            holding = false;

            rb.bodyType = RigidbodyType2D.Dynamic;

            timePassed = 0;
        }

        if (holding)
        {
            currentState = CharacterState.HoldingSurface;
            timePassed += Time.deltaTime;

            if (timePassed >= holdTime)
            {
                holding = false;
                timePassed = 0;

                rb.bodyType = RigidbodyType2D.Dynamic;
            }
            return;
        }

        if (startHold && OnGround())
        {
            startHold = false;
        }

        Flying();

        if (flying)
        {
            timePassedFlying += Time.deltaTime;

            if (timePassedFlying >= flightTime)
            {
                flying = false;
                rb.gravityScale = originalGravity;
            }
        }

        if(OnGround() || OnWall())
        {
            flying = false;
            rb.gravityScale = originalGravity;
            timePassedFlying = 0;
        }

        IsSwinging();
        Wait();

        if (moveInput.x < 0 && facingRight || moveInput.x > 0 && !facingRight)
        {
            Flip();
        }
    }

    private void FixedUpdate()
    {
        rb.linearDamping = originalDamping;
        switch (currentState)
        {
            case CharacterState.Idle:
                if (holding) return;
                rb.linearVelocityX = moveInput.x * originalSpeed;
                break;
            case CharacterState.Walking:
                rb.linearVelocityX = moveInput.x * originalSpeed;
                break;
            case CharacterState.Falling:
                rb.linearVelocityX = moveInput.x * originalSpeed;

                // Depois colocar o resto da verificação de queda

                break;
            case CharacterState.Jumping:
                rb.linearVelocityX = moveInput.x * originalSpeed;
                break;
            case CharacterState.Shot:
                rb.linearVelocity = new Vector2(rb.linearVelocityX, rb.linearVelocityY);
                break;
            case CharacterState.Swinging:
                rb.linearDamping = swingDamping;
                HandlePendulumMotion();

                // Check if the player has surpassed the other player's height while swinging
                if (rb.linearVelocityY > 0 && porky != null)
                {
                    // If player surpasses the other player's height, stay swinging
                    if (transform.position.y > porky.transform.position.y)
                    {
                        currentState = CharacterState.Swinging;
                    }
                }
                break;
            case CharacterState.SatDown:
                break;
            case CharacterState.HoldingSurface:
                break;
            case CharacterState.Absorbed:
                break;
            case CharacterState.Flying:
                if(moveInput.x != 0)
                    rb.linearVelocityX = moveInput.x * originalSpeed;
                else
                    rb.linearVelocityX = dir * flightSpeed;
                break;
        }
    }

    private void Flip()
    {
        Vector3 currentScale = transform.localScale;
        currentScale.x *= -1;
        transform.localScale = currentScale;

        facingRight = !facingRight;
    }

    private void Jump()
    {
        rb.linearVelocity = Vector2.up * originalJumpForce;
        jumping = true;
    }

    private void Wait()
    {
        if (waitAction.IsPressed() && OnGround())
        {
            rb.linearVelocity = Vector2.zero;
            rb.bodyType = RigidbodyType2D.Kinematic;
            waiting = true;
        }
        else if (waitAction.WasReleasedThisFrame() && OnGround())
        {
            rb.bodyType = RigidbodyType2D.Dynamic;
            waiting = false;
        }
    }

    private void Flying()
    {
        if (flightAction.IsPressed() && !OnGround())
        {
            flying = true;
            rb.gravityScale = flightGravity;
        }

        if (flightAction.WasReleasedThisFrame() && flying)
        {
            flying = false;
            rb.gravityScale = originalGravity;
        }
    }

    #region Position Check
    public bool OnGround()
    {
        // Verifica se o personagem está no chão usando OverlapCircle.
        return Physics2D.OverlapBox(bottomPos.position, bottomSize, 0f, floorLayer);
    }

    public bool OnWall()
    {
        // Verifica se o personagem está no chão usando OverlapCircle.
        return Physics2D.OverlapBox(sidePos.position, sideSize, 0f, wallLayer);
    }

    public bool OnCeiling()
    {
        // Verifica se o personagem está no chão usando OverlapCircle.
        return Physics2D.OverlapBox(topPos.position, topSize, 0f, topLayer);
    }
    #endregion

    private void IsSwinging()
    {
        if (porky == null) return;

        if(beingShot)
        {
            currentState = CharacterState.Shot;
            return;
        }

        if (flying)
        {
            currentState = CharacterState.Flying;
            return;
        }

        if (jumping == true && rb.linearVelocityY > 0f)
        {
            currentState = CharacterState.Jumping;
            return;
        }
        jumping = false;

        if (waiting)
        {
            currentState = CharacterState.SatDown;
            return;
        }

        if (rb.linearVelocityY < 0f && !falling && !OnGround())
        {
            currentState = CharacterState.Falling;

            falling = true;
        }
        else if (rb.linearVelocityY < 0f && !OnGround() && porky.currentState == GunCharacterController.CharacterState.SatDown)
        {
            currentState = CharacterState.Swinging;
        }
        else if (rb.linearVelocityY >= 0f && falling == true)
        {
            falling = false;
        }
        else if (OnGround() && moveInput.x == 0)
            currentState = CharacterState.Idle;
    }

    private void HandlePendulumMotion()
    {
        if (porky.IsKinematic() && !OnGround())
        {
            Vector2 toConnected = porky.transform.position - (Vector3)rb.position;
            Vector2 tangent = Vector2.Perpendicular(toConnected).normalized;

            if (x != 0)
            {
                rb.AddForce(-1 * x * tangent * originalSpeed, ForceMode2D.Force);
            }
            if (x == 0 && rb.linearVelocity.magnitude < 0.1f)
            {
                rb.linearVelocity = Vector2.zero;
            }
        }
    }

    private void HandleRopeAdjust()
    {
        if (distanceJoint == null || porky == null) return;

        bool conditionMet = ropeAdjustCondition switch
        {
            RopeAdjustCondition.Always => true,
            RopeAdjustCondition.OnlyWhenAllyWaiting => porky.currentState == GunCharacterController.CharacterState.SatDown,
            RopeAdjustCondition.OnlyWhenWaiting => currentState == CharacterState.SatDown,
            RopeAdjustCondition.OnlyWhenSwinging => currentState == CharacterState.Swinging,
            RopeAdjustCondition.Never => false,
            _ => false
        };

        if (!conditionMet) return;

        // moveInput.y is already being read — W = +1 (extend), S = -1 (retract)
        // We invert so W = shorter (pull up) and S = longer (let out) — change sign if you prefer the opposite
        float verticalInput = moveInput.y;
        AdjustRopeDistance(verticalInput);
    }

    public void AdjustRopeDistance(float verticalInput)
    {
        if (distanceJoint == null) return;
        if (Mathf.Abs(verticalInput) < 0.1f) return;

        float newDistance = distanceJoint.distance - (verticalInput * ropeAdjustSpeed * Time.deltaTime);
        distanceJoint.distance = Mathf.Clamp(newDistance, ropeMinDistance, ropeMaxDistance);
    }

    public bool IsKinematic()
    {
        if (rb.bodyType == RigidbodyType2D.Kinematic)
        {
            return true;
        }
        else
        {
            return false;
        }
    }

    public float HoldProgress => holdTime > 0f ? Mathf.Clamp01(timePassed / holdTime) : 0f;
    public float FlyProgress => flightTime > 0f ? Mathf.Clamp01(timePassedFlying / flightTime) : 0f;

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireCube(bottomPos.position, bottomSize);

        Gizmos.color = Color.blue;
        Gizmos.DrawWireCube(sidePos.position, sideSize);

        Gizmos.color = Color.red;
        Gizmos.DrawWireCube(topPos.position, topSize);
    }
}
