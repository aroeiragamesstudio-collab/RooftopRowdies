using UnityEngine;
using UnityEngine.InputSystem;

public class StickAimStrategy : IAimStrategy
{
    readonly InputAction rightStick;
    public StickAimStrategy(InputAction stick) => rightStick = stick;

    public Vector2 GetAimDirection(Transform gunTransform)
    {
        Vector2 stickDir = rightStick.ReadValue<Vector2>();
        Vector2 lastStickDirection = stickDir;

        return lastStickDirection.normalized;
    }

    public void UpdateAim(Transform gunTransform, float offset)
    {
        Vector2 stickDir = rightStick.ReadValue<Vector2>().normalized;
        if (stickDir.magnitude < 0.1f) return;   // evita rotacionar com valor muito pequeno

        float rotation_z = Mathf.Atan2(stickDir.y, stickDir.x) * Mathf.Rad2Deg;
        gunTransform.rotation = Quaternion.Euler(0f, 0f, rotation_z + offset);
    }
}
