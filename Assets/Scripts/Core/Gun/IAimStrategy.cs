using UnityEngine;

public interface IAimStrategy
{
    void UpdateAim(Transform gunTransform, float offset);
    Vector2 GetAimDirection(Transform gunTransform);
}
