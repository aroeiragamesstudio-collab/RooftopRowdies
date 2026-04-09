using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Windows;

/// <summary>
/// Script que controla as funções da arma do porco/personagem que usa a arma.
/// </summary>
public class GunController : MonoBehaviour
{
    [Header("Informações associadas ao jogador que possui")]
    public PlayerInput parentInput;
    public Rigidbody2D rbPlayer;
    public InputActionReference aimActionRef;

    [Header("Atributos da arma")]
    public float offset;
    [Tooltip("Controla o quanto a arma empurra o personagem.")]
    public float gunKnockback = 5f;
    [Tooltip("Controla o quão longe o gato é atirado.")]
    public float shootForce = 10f;
    public float timeBetweenShots = 1f;

    [Header("Informações sobre absorver")]
    public float absorbDistance = 2f;
    public float aimTolerance = 0.8f;

    float gunCooldown;
    bool canShoot;
    bool absorbed;
    bool isUsingMouse;

    [HideInInspector]
    public JumpCharacterController jumpChar;

    InputAction rightStick;
    InputAction shootBtn;
    InputAction absorbBtn;

    private void Start()
    {
        parentInput = transform.parent.GetComponent<PlayerInput>();

        Debug.Log("Mapa atual: " + parentInput.currentActionMap.name);

        rightStick = parentInput.currentActionMap.FindAction("Aim");
        shootBtn = parentInput.currentActionMap.FindAction("Shoot");
        absorbBtn = parentInput.currentActionMap.FindAction("Absorb");

        jumpChar = FindFirstObjectByType<JumpCharacterController>();

        UpdateAimMode();
    }

    public void UpdateAimMode()
    {
        string scheme = parentInput.currentControlScheme;
        isUsingMouse = scheme == "WASD" || scheme == "Arrows";
        Debug.Log($"[GunController] Scheme changed to '{scheme}' — isUsingMouse: {isUsingMouse}");
    }

    private void Update()
    {
        if (isUsingMouse)
        {
            AimGunWithMouse();
        }
        else
        {
            AimGunWithStick();
        }

        Inputs();
        GunCooldown();
        if (!absorbed) return;
        AbsorbedPlayerMovement();
    }

    // Calcula a direção que a arma está mirando de acordo com o analógico direito do gamepad
    private void AimGunWithStick()
    {
        if (rightStick == null)
        {
            Debug.LogError("RIGHTStick não está atribuido");
            return;
        }

        Vector2 stickDir = rightStick.ReadValue<Vector2>().normalized;
        if (stickDir.magnitude < 0.1f) return;                                         // evita rotacionar com valor muito pequeno

        float rotation_z = Mathf.Atan2(stickDir.y, stickDir.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0f, 0f, rotation_z + offset);
    }

    // Calcula a direção que a arma está mirando através do Mouse do jogador.
    void AimGunWithMouse()
    {
        // Detecta a direção especifica em que o mouse está na tela e transforma em uma posição no mundo.
        Vector3 difference = Camera.main.ScreenToWorldPoint(Mouse.current.position.
            ReadValue()) - transform.position;
        difference.Normalize();                                                         // Transforma em um vetor de 1 para simplificar o calculo.
        float rotation_z = Mathf.Atan2(difference.y, difference.x) * Mathf.Rad2Deg;     // Transforma o vetor pego do mouse em graus para rotacionar o objeto.
        transform.rotation = Quaternion.Euler(0f, 0f, rotation_z + offset);             // Rotaciona apenas no eixo Z.
    }

    void Inputs()
    {
        if (shootBtn.WasPressedThisFrame() && canShoot)
        {
            if (!absorbed)
            {
                Knockback();
            }
            else
            {
                jumpChar.beingShot = true;
                ShootPlayer();
            }

            canShoot = false;
        }

        if (absorbBtn.IsPressed())
        {
            TryAbsorbPlayer();
        }
    }

    // Controla quando o jogador pode atirar novamente
    void GunCooldown()
    {
        if (!canShoot)
        {
            gunCooldown += Time.deltaTime;

            if (gunCooldown >= timeBetweenShots) // Reseta o cooldown após o tempo necessário
            {
                canShoot = true;
                gunCooldown = 0;
            }
        }
    }

    // Controla a direção que o porco vai voar
    private void Knockback()
    {
        Vector2 aimDirection;

        if (isUsingMouse)
        {
            Vector2 mouseWorldPos = Camera.main.ScreenToWorldPoint(Mouse.current.position.ReadValue());
            aimDirection = (mouseWorldPos - transform.parent.GetComponent<Rigidbody2D>().position).normalized;
        }
        else
        {
            Vector2 stickDir = rightStick.ReadValue<Vector2>();
            Vector2 lastStickDirection = stickDir;

            aimDirection = lastStickDirection.normalized;
        }

        Vector2 knockbackDir = -aimDirection; // Direção oposta ao tiro
        rbPlayer.AddForce(knockbackDir * gunKnockback, ForceMode2D.Impulse);

        // Testar depois
        /*
        if (gunChar.x == 0)
            rbPlayer.AddForce(knockbackDir * gunForce, ForceMode2D.Impulse);
        else
            rbPlayer.AddForce(knockbackDir * (gunForce * 0.75f), ForceMode2D.Impulse);
        */
    }

    void TryAbsorbPlayer()
    {
        float distanceToPlayer = Vector2.Distance(transform.position, jumpChar.transform.position);
        Vector2 directionToPlayer = (jumpChar.transform.position - transform.position).normalized;
        float aimDotProduct = Vector2.Dot(transform.right, directionToPlayer);

        if(distanceToPlayer <= absorbDistance && aimDotProduct >= aimTolerance)
        {
            absorbed = true;
            ChangeAbsorbedPlayer(transform.position, Vector2.zero);
        }
    }

    void ChangeAbsorbedPlayer(Vector3 position, Vector2 velocity)
    {
        jumpChar.gameObject.SetActive(!absorbed);

        if (absorbed)
        {
            jumpChar.GetComponent<Collider2D>().enabled = false;
            jumpChar.GetComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
            jumpChar.GetComponent<Rigidbody2D>().linearVelocity = Vector2.zero;
        }
        else
        {
            jumpChar.beingShot = true;
            jumpChar.GetComponent<Collider2D>().enabled=true;
            jumpChar.GetComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Dynamic;

            jumpChar.transform.position = position;
            jumpChar.GetComponent<Rigidbody2D>().linearVelocity = velocity;
        }
    }

    // Atualiza a posição do personagem absorvido para seguir a arma
    void AbsorbedPlayerMovement()
    {
        jumpChar.transform.position = transform.position;
    }

    // Lida com o disparo do personagem absorvido
    void ShootPlayer()
    {
        Vector3 shootPosition = transform.position + transform.right * 0.5f; // Posição de disparo
        Vector2 shootDirection = transform.right;

        Vector2 shootVelocity = shootDirection * shootForce;

        absorbed = false;
        ChangeAbsorbedPlayer(shootPosition, shootVelocity);
    }
}
