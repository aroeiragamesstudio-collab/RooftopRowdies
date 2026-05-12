using UnityEngine;
using UnityEngine.InputSystem;

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
    public float timeBetweenShots = 1f;
    public AbsorbShootSystem absorbSystem;

    float gunCooldown;
    bool canShoot;

    [HideInInspector]
    public JumpCharacterController jumpChar;

    InputAction rightStick;
    InputAction shootBtn;
    InputAction absorbBtn;

    IAimStrategy aimStrategy;

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
        aimStrategy = (scheme == "WASD" || scheme == "Arrows") ?
            new MouseAimStrategy() :
            new StickAimStrategy(rightStick);
    }

    private void Update()
    {
        if (parentInput.GetComponent<GunCharacterController>().waiting) return;
        aimStrategy.UpdateAim(transform, offset);

        Inputs();
        GunCooldown();
        if (!absorbSystem.IsAbsorbed) return;
        absorbSystem.Follow();
    }

    void Inputs()
    {
        if (shootBtn.WasPressedThisFrame() && canShoot)
        {
            if (!absorbSystem.IsAbsorbed)
            {
                Knockback();
            }
            else
            {
                absorbSystem.Shoot();
            }

            canShoot = false;
        }

        if (absorbBtn.IsPressed())
        {
            absorbSystem.TryAbsorb();
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
        Vector2 aimDirection = aimStrategy.GetAimDirection(transform);

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
}
