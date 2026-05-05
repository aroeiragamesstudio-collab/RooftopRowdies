using System.Collections;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Código base do porco/jogador com arma, sendo aqui que estarão as maiorias das mudanças que envolvem diretamente ele
/// </summary>
public class GunCharacterController : MonoBehaviour
{
    public enum CharacterState
    {
        Idle,
        Walking,
        Falling,
        Shooting,
        Swinging,
        SatDown,
        Knockback
    }

    [Header("Informações Base")]
    public CharacterState currentState;
    [SerializeField] float originalSpeed = 10f;
    public PlayerInput playerInput;

    [Header("Detecção de Solo")]
    [SerializeField] Transform bottomPos;
    [SerializeField] LayerMask floorLayer;
    [SerializeField] Vector2 bottomSize = new Vector2(1.5f, 0.2f);

    [Header("Ajuste de Corda")]
    [SerializeField] RopeAdjustCondition ropeAdjustCondition = RopeAdjustCondition.OnlyWhenAllyWaiting;

    [Header("Pêndulo")]
    [SerializeField] float swingDamping = 0.8f;

    // Variaveis privadas
    [HideInInspector]
    public JumpCharacterController paws;
    Vector2 moveInput;
    Rigidbody2D rb;
    InputAction moveAction;
    InputAction waitAction;
    DistanceJoint2D distanceJoint;
    bool falling;
    [HideInInspector]
    public bool absorbed;
    [HideInInspector]
    public bool waiting;
    float x;
    float originalDamping;

    private void Start()
    {
        paws = FindFirstObjectByType<JumpCharacterController>();

        rb = GetComponent<Rigidbody2D>();
        distanceJoint = paws.GetComponent<DistanceJoint2D>();

        playerInput = GetComponent<PlayerInput>();

        currentState = CharacterState.Idle;

        Debug.Log($"Active control scheme = {playerInput.currentControlScheme}");

        moveAction = playerInput.currentActionMap.FindAction("Move");
        waitAction = playerInput.currentActionMap.FindAction("Wait");

        originalDamping = rb.linearDamping;

        if (moveAction == null)
            Debug.LogError("Não foi encontrada a ação 'Move'. Verifique o Input Map");
    }

    private void Update()
    {
        if (!waiting)
        {
            moveInput = moveAction.ReadValue<Vector2>();
            x = moveInput.x;
        }

        Wait();
        HandleRopeAdjust();
        IsSwinging();
    }

    private void FixedUpdate()
    {
        rb.linearDamping = originalDamping;
        switch (currentState)
        {
            case CharacterState.Idle:
                rb.linearVelocityX = moveInput.x * originalSpeed;
                break;
            case CharacterState.Walking:
                rb.linearVelocityX = moveInput.x * originalSpeed;
                break;
            case CharacterState.Falling:
                rb.linearVelocityX = moveInput.x * originalSpeed;
                break;
            case CharacterState.Shooting:
                break;
            case CharacterState.Swinging:
                rb.linearDamping = swingDamping;
                HandlePendulumMotion();

                // Check if the player has surpassed the other player's height while swinging
                if (rb.linearVelocityY > 0 && paws != null)
                {
                    // If player surpasses the other player's height, stay swinging
                    if (transform.position.y > paws.transform.position.y)
                    {
                        currentState = CharacterState.Swinging;
                    }
                }
                break;
            case CharacterState.SatDown:
                break;
            case CharacterState.Knockback:
                break;
        }
    }

    public bool OnGround()
    {
        // Verifica se o personagem está no chão usando OverlapCircle.
        return Physics2D.OverlapBox(bottomPos.position, bottomSize, 0f, floorLayer);
    }

    private void IsSwinging()
    {
        if (paws == null) return;

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
        else if (rb.linearVelocityY < 0f && !OnGround() && paws.currentState == JumpCharacterController.CharacterState.SatDown)
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

    private void HandlePendulumMotion()
    {
        if (paws.IsKinematic() && !OnGround())
        {
            Vector2 toConnected = paws.transform.position - (Vector3)rb.position;
            Vector2 tangent = Vector2.Perpendicular(toConnected).normalized;

            if (x != 0)
            {
                rb.AddForce(-1 * x * tangent * originalSpeed, ForceMode2D.Force);
            }
            //if (x == 0 && rb.linearVelocity.magnitude < 0.1f)
            //{
            //    rb.linearVelocity = Vector2.zero;
            //}
        }
    }

    private void HandleRopeAdjust()
    {
        if (distanceJoint == null || paws == null) return;

        bool conditionMet = ropeAdjustCondition switch
        {
            RopeAdjustCondition.Always => true,
            RopeAdjustCondition.OnlyWhenAllyWaiting => paws.currentState == JumpCharacterController.CharacterState.SatDown,
            RopeAdjustCondition.OnlyWhenWaiting => currentState == CharacterState.SatDown,
            RopeAdjustCondition.OnlyWhenSwinging => currentState == CharacterState.Swinging,
            RopeAdjustCondition.Never => false,
            _ => false
        };

        if (!conditionMet) return;

        // moveInput.y is already being read — W = +1 (extend), S = -1 (retract)
        // We invert so W = shorter (pull up) and S = longer (let out) — change sign if you prefer the opposite
        float verticalInput = moveInput.y;
        paws.AdjustRopeDistance(verticalInput);
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

    public bool IsAllyAbsorbed => absorbed;
}
