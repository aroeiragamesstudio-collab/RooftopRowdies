using Rooftop.Core.Abilities;
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

    [Header("Pêndulo")]
    [SerializeField] float swingDamping = 0.8f;
    [SerializeField] RopeAdjustCondition ropeAdjustCondition = RopeAdjustCondition.OnlyWhenAllyWaiting;

    // Variaveis privadas
    [HideInInspector]
    public JumpCharacterController paws;
    Vector2 moveInput;
    Rigidbody2D rb;
    InputAction moveAction;
    InputAction waitAction;
    InputAction ropeAdjustAction;
    bool falling;
    [HideInInspector]
    public bool absorbed;
    [HideInInspector]
    public bool waiting;
    float originalDamping;
    RopeSystem rope;

    private void Start()
    {
        paws = FindFirstObjectByType<JumpCharacterController>();

        rb = GetComponent<Rigidbody2D>();

        playerInput = GetComponent<PlayerInput>();

        currentState = CharacterState.Idle;

        Debug.Log($"Active control scheme = {playerInput.currentControlScheme}");

        moveAction = playerInput.currentActionMap.FindAction("Move");
        waitAction = playerInput.currentActionMap.FindAction("Wait");
        ropeAdjustAction = playerInput.currentActionMap.FindAction("RopeAdjust");

        rope = paws.GetComponent<RopeSystem>();

        originalDamping = rb.linearDamping;

        if (moveAction == null)
            Debug.LogError("Não foi encontrada a ação 'Move'. Verifique o Input Map");
    }

    private void Update()
    {
        moveInput = moveAction.ReadValue<Vector2>();
        if (waiting)
        {
            moveInput.x = 0;
        }

        if (paws != null && paws.rope != null)
        {
            bool canAdjust = ropeAdjustCondition switch
            {
                RopeAdjustCondition.Always => true,
                RopeAdjustCondition.OnlyWhenAllyWaiting => currentState == CharacterState.SatDown,
                RopeAdjustCondition.OnlyWhenWaiting => currentState == CharacterState.SatDown,
                RopeAdjustCondition.OnlyWhenSwinging => currentState == CharacterState.Swinging,
                RopeAdjustCondition.Never => false,
                _ => false
            };
            if (canAdjust) rope.TryAdjust(ropeAdjustAction?.ReadValue<float>() ?? 0f);
            rope.HandleRopeTautPenalty();
        }

        Wait();
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
                if (paws.IsKinematic() && !OnGround())
                    RopeSystem.ApplyPendulumForce(rb, paws.transform.position,
                        moveInput.x, originalSpeed);

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
