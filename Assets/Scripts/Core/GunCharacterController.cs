using System.Collections;
using Unity.VisualScripting;
using UnityEditor.Experimental.GraphView;
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
    [SerializeField] float ropeAdjustSpeed = 2f;
    [SerializeField] float ropeMinDistance = 1f;
    [SerializeField] float ropeMaxDistance = 15f;

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

    void Awake()
    {

    }

    private void Start()
    {
        //StartCoroutine(AssignDistanceJoint());

        paws = FindFirstObjectByType<JumpCharacterController>();

        rb = GetComponent<Rigidbody2D>();
        distanceJoint = paws.GetComponent<DistanceJoint2D>();

        playerInput = GetComponent<PlayerInput>();

        currentState = CharacterState.Idle;

        Debug.Log($"Active control scheme = {playerInput.currentControlScheme}");

        moveAction = playerInput.currentActionMap.FindAction("Move");
        waitAction = playerInput.currentActionMap.FindAction("Wait");

        if (moveAction == null)
            Debug.LogError("Não foi encontrada a ação 'Move'. Verifique o Input Map");
    }

    IEnumerator AssignDistanceJoint()
    {
        while (distanceJoint.connectedBody == null)
        {
            paws = FindFirstObjectByType<JumpCharacterController>();

            if (paws != null)
            {
                distanceJoint.connectedBody = paws.GetComponent<Rigidbody2D>();
                Debug.Log("DistanceJoint atribuído com sucesso.");

                yield break;
            }

            yield return null;
        }
    }

    private void Update()
    {
        if (!waiting)
        {
            moveInput = moveAction.ReadValue<Vector2>();
            x = moveInput.x;
            HandleRopeAdjust();
        }

        Wait();
        IsSwinging();
    }

    private void FixedUpdate()
    {
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

    private void HandleRopeAdjust()
    {
        if (distanceJoint == null || paws == null) return;

        bool conditionMet = ropeAdjustCondition switch
        {
            RopeAdjustCondition.Always => true,
            RopeAdjustCondition.OnlyWhenAllyWaiting => paws.currentState == JumpCharacterController.CharacterState.SatDown,
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

    public bool IsAllyAbsorbed => absorbed;
}
