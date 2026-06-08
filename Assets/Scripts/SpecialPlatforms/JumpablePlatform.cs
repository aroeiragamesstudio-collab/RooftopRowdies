using UnityEngine;

public class JumpablePlatform : MonoBehaviour
{
    [SerializeField] float forceToAdd = 20f;

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.gameObject.CompareTag("Player") && CheckIfOnGround(collision.gameObject))
        {
            Debug.Log(1);
            if (collision.gameObject.TryGetComponent<JumpCharacterController>(out var jumpChar))
            {
                Debug.Log("jumpChar up");
                jumpChar.AddForce(forceToAdd);
            }
            else if (collision.gameObject.TryGetComponent<GunCharacterController>(out var gunChar))
            {
                Debug.Log("gunChar up");
                gunChar.AddForce(forceToAdd);
            }
        }
    }

    bool CheckIfOnGround(GameObject obj)
    {
        if (obj.TryGetComponent<JumpCharacterController>(out var jumpChar))
        {
            return jumpChar.OnGround();
        }
        else if(obj.TryGetComponent<GunCharacterController>(out var gunChar))
        {
            return gunChar.OnGround();
        }

        return false;
    }
}
