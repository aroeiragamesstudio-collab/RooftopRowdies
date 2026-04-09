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
        Absorbed
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
    [SerializeField] float bottomSize = 1.5f;
    [SerializeField] float sideSize = 1.5f;
    [SerializeField] float topSize = 1.5f;

    [Header("Informação de ser atirado")]
    public float timeToNormal = 5f;

    Vector2 moveInput;
    float x;

    InputAction moveAction;
    InputAction jumpAction;
    InputAction waitAction;
    Rigidbody2D rb;

    [HideInInspector]
    public bool beingShot;

    float timePassed;
    bool holding;
    bool startHold;
    bool falling;
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

        currentState = CharacterState.Idle;
    }

    private void Start()
    {
        StartCoroutine(AssignDistanceJoint());
    }

    IEnumerator AssignDistanceJoint()
    {
        while (distanceJoint.connectedBody == null)
        {
            porky = FindFirstObjectByType<GunCharacterController>();

            if (porky != null)
            {
                distanceJoint.connectedBody = porky.GetComponent<Rigidbody2D>();
                Debug.Log("DistanceJoint atribuído com sucesso.");

                yield break;
            }

            yield return null;
        }
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

        if (jumpAction.WasPressedThisFrame() && OnGround())
        {
            Jump();
        }

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
            timePassed += Time.deltaTime;

            if (timePassed >= holdTime)
            {
                holding = false;
                timePassed = 0;

                rb.bodyType = RigidbodyType2D.Dynamic;
            }
        }

        if (startHold && OnGround())
        {
            startHold = false;
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
        switch (currentState)
        {
            case CharacterState.Idle:
                if(holding) return;
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
                break;
            case CharacterState.Shot:
                rb.linearVelocity = new Vector2(rb.linearVelocityX, rb.linearVelocityY);
                break;
            case CharacterState.Swinging:
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
    }

    private void Wait()
    {
        if (waitAction.IsPressed() && OnGround())
        {
            rb.bodyType = RigidbodyType2D.Kinematic;
        }
        else if (waitAction.WasReleasedThisFrame() && OnGround())
        {
            rb.bodyType = RigidbodyType2D.Dynamic;
        }
    }

    #region Position Check
    public bool OnGround()
    {
        // Verifica se o personagem está no chão usando OverlapCircle.
        return Physics2D.OverlapCircle(bottomPos.position, bottomSize, floorLayer);
    }

    public bool OnWall()
    {
        // Verifica se o personagem está no chão usando OverlapCircle.
        return Physics2D.OverlapCircle(sidePos.position, sideSize, wallLayer);
    }

    public bool OnCeiling()
    {
        // Verifica se o personagem está no chão usando OverlapCircle.
        return Physics2D.OverlapCircle(topPos.position, topSize, topLayer);
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

        if (rb.linearVelocityY < 0f && !falling && !OnGround())
        {
            currentState = CharacterState.Falling;

            falling = true;
        }
        else if (rb.linearVelocityY < 0f && !OnGround() && porky.OnGround())
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
            Vector2 toConnected = distanceJoint.connectedBody.position - rb.position;
            Vector2 tangent = Vector2.Perpendicular(toConnected).normalized;

            if (toConnected.magnitude >= distanceJoint.distance && x == 0)
            {
                Debug.Log(x);

                if (rb.linearVelocityY < 0) // Falling
                {
                    rb.AddForce(tangent * originalSpeed, ForceMode2D.Force);
                }
                else if (rb.linearVelocityY > 0) // Going up
                {
                    rb.AddForce(tangent * originalSpeed, ForceMode2D.Force);
                    rb.AddForce(new Vector2(0, originalSpeed * 0.5f), ForceMode2D.Force);
                }
            }

            if (x != 0)
            {
                rb.AddForce(-1 * x * tangent * originalSpeed, ForceMode2D.Force);
            }

            rb.linearVelocity = new Vector2(rb.linearVelocityX, rb.linearVelocityY);
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
}
