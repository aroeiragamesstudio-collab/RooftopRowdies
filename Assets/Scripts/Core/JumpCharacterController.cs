using Rooftop.Core.Abilities;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using static UnityEditor.ShaderData;

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
    [SerializeField] Transform bottomPos, sidePos, topPos;
    [SerializeField] LayerMask floorLayer, wallLayer, topLayer;
    [SerializeField] Vector2 bottomSize = new Vector2(1.5f, 0.1f);
    [SerializeField] Vector2 sideSize = new Vector2(0.1f, 1.5f);
    [SerializeField] Vector2 topSize = new Vector2(1.5f, 0.1f);

    [Header("Informação de ser atirado")]
    public float timeToNormal = 5f;

    [Header("Pêndulo")]
    [SerializeField] RopeAdjustCondition ropeAdjustCondition = RopeAdjustCondition.OnlyWhenAllyWaiting;
    [SerializeField] float swingDamping = 0.8f;

    [Header("Habilidades")]
    public RopeSystem rope;
    public Flight flight;
    public SurfaceHold holder;

    [HideInInspector]
    public Vector2 moveInput;
    float originalDamping;
    float lastHorizontalDir = 1;

    InputAction moveAction;
    InputAction jumpAction;
    InputAction waitAction;
    InputAction flightAction;
    InputAction ropeAdjustAction;
    Rigidbody2D rb;

    [HideInInspector]
    public bool beingShot;

    float timePassed;
    bool jumping;
    bool holding;
    bool falling;
    bool waiting;
    bool facingRight = true;

    [HideInInspector]
    public GunCharacterController porky;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();

        playerInput = GetComponent<PlayerInput>();

        Debug.Log($"Active control scheme = {playerInput.currentControlScheme}");

        moveAction = playerInput.currentActionMap.FindAction("Move");
        jumpAction = playerInput.currentActionMap.FindAction("Jump");
        waitAction = playerInput.currentActionMap.FindAction("Wait");
        flightAction = playerInput.currentActionMap.FindAction("Flight");
        ropeAdjustAction = playerInput.currentActionMap.FindAction("RopeAdjust");

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
        }

        moveInput = moveAction.ReadValue<Vector2>();

        // Verificar depois para tentar melhorar a chamada
        if (waiting || beingShot)
        {
            moveInput.x = 0;
        }

        if (jumpAction.WasPressedThisFrame() && OnGround() && !waiting)
        {
            Jump();
        }

        if (moveInput.x != 0)
            lastHorizontalDir = Mathf.Sign(moveInput.x);

        if(rope != null)
        {
            bool canAdjust = ropeAdjustCondition switch
            {
                RopeAdjustCondition.Always => true,
                RopeAdjustCondition.OnlyWhenAllyWaiting => porky.currentState == GunCharacterController.CharacterState.SatDown,
                RopeAdjustCondition.OnlyWhenWaiting => currentState == CharacterState.SatDown,
                RopeAdjustCondition.OnlyWhenSwinging => currentState == CharacterState.Swinging,
                RopeAdjustCondition.Never => false,
                _ => false
            };
            if (canAdjust) rope.TryAdjust(ropeAdjustAction?.ReadValue<float>() ?? 0f);
            rope.HandleRopeTautPenalty();
        }

        // VERIFICAR SE NÃO FAZ MAIS SENTIDO SEGURAR MULTIPLAS VEZES AO INVÉS DE SÓ UMA
        holder.Tick(jumpAction.IsPressed(), jumpAction.WasReleasedThisFrame(),
            OnGround(), OnCeiling() || OnWall());

        flight.Tick(flightAction.IsPressed(), flightAction.WasReleasedThisFrame(),
            OnGround(), OnWall(), rope != null && rope.IsTautPenaltyActive);

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
                if(porky.IsKinematic() && !OnGround())
                    RopeSystem.ApplyPendulumForce(rb, porky.transform.position,
                    moveInput.x, originalSpeed);

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
                    rb.linearVelocityX = lastHorizontalDir * flight.FlightSpeed;
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

        if (holder.IsHolding)
        {
            currentState = CharacterState.HoldingSurface;
            NotBeingShot();
            return;
        }

        if (flight.IsFlying)
        {
            currentState = CharacterState.Flying;
            NotBeingShot();
            return;
        }

        if (beingShot)
        {
            currentState = CharacterState.Shot;
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

    private void NotBeingShot()
    {
        if (beingShot)
        {
            beingShot = false;
            timePassed = 0;
        }
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
