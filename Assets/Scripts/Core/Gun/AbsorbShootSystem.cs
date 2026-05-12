using System;
using UnityEngine;

public class AbsorbShootSystem : MonoBehaviour
{
    [Header("Absorb")]
    public float absorbDistance = 2f;
    public float aimTolerance = 0.8f;

    [Header("Shoot")]
    public float shootForce = 13f;

    public bool IsAbsorbed { get; private set; }

    JumpCharacterController _paws;
    GunCharacterController _porky;
    Transform _gunTransform;

    public void Init(Transform gun, JumpCharacterController paws, 
        GunCharacterController porky)
    {
        _gunTransform = gun;
        _paws = paws;
        _porky = porky;
    }

    public void TryAbsorb()
    {
        if(IsAbsorbed || _paws == null) return;

        float distanceToPlayer = Vector2.Distance(transform.position, _paws.transform.position);
        Vector2 directionToPlayer = (_paws.transform.position - transform.position).normalized;
        float aimDotProduct = Vector2.Dot(transform.right, directionToPlayer);

        if (distanceToPlayer <= absorbDistance && aimDotProduct >= aimTolerance)
        {
            IsAbsorbed = true;
            _porky.absorbed = true;
            SetAbsorbedState(true, _gunTransform.position, Vector2.zero);
        }
    }

    public void Shoot()
    {
        if(!IsAbsorbed) return;

        Vector3 shootPosition = _gunTransform.position + _gunTransform.right * 0.5f; // Posição de disparo
        Vector2 shootVelocity = _gunTransform.right * shootForce;

        IsAbsorbed = false;
        _porky.absorbed = false;
        _paws.beingShot = true;
        SetAbsorbedState(false, shootPosition, shootVelocity);
    }

    public void Follow()
    {
        if(IsAbsorbed && _paws != null)
            _paws.transform.position = _gunTransform.position;
    }

    private void SetAbsorbedState(bool absorb, Vector3 position, Vector2 velocity)
    {
        var col = _paws.GetComponent<Collider2D>();
        var rb = _paws.GetComponent<Rigidbody2D>();

        _paws.gameObject.SetActive(!absorb);

        if (absorb)
        {
            col.enabled = false;
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.linearVelocity = Vector2.zero;
        }
        else
        {
            _paws.currentState = JumpCharacterController.CharacterState.Shot;
            col.enabled = true;
            rb.bodyType = RigidbodyType2D.Dynamic;
            _paws.transform.position = position;
            rb.linearVelocity = velocity;
        }
    }
}
