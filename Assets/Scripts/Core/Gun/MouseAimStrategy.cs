using UnityEngine;
using UnityEngine.InputSystem;

public class MouseAimStrategy : IAimStrategy
{
    public Vector2 GetAimDirection(Transform gunTransform)
    {
        Vector2 mouseWorldPos = Camera.main.ScreenToWorldPoint(Mouse.current.position.ReadValue());
        return (mouseWorldPos - gunTransform.parent.GetComponent<Rigidbody2D>().position).normalized;
    }

    public void UpdateAim(Transform gunTransform, float offset)
    {
        // Detecta a direção especifica em que o mouse está na tela e transforma em uma posição no mundo.
        Vector3 difference = Camera.main.ScreenToWorldPoint(Mouse.current.position.
            ReadValue()) - gunTransform.position;
        difference.Normalize();                                                         // Transforma em um vetor de 1 para simplificar o calculo.
        float rotation_z = Mathf.Atan2(difference.y, difference.x) * Mathf.Rad2Deg;     // Transforma o vetor pego do mouse em graus para rotacionar o objeto.
        gunTransform.rotation = Quaternion.Euler(0f, 0f, rotation_z + offset);             // Rotaciona apenas no eixo Z.
    }
}
