using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BreakablePlatform : MonoBehaviour
{
    [SerializeField] SpriteRenderer sprite;
    [SerializeField] Collider2D col;

    [SerializeField] float rebuildTime;
    [SerializeField] float breakTime;

    [SerializeField] Animator anim;

    bool once;

    bool breaking = false;

    public void BrokenPlatform()
    {
        sprite.enabled = false;

        StartCoroutine(RebuildPlatform());
    }

    public void FixedPlatform()
    {
        col.enabled = true;
        breaking = false;
        once = false;
    }

    IEnumerator RebuildPlatform()
    {
        yield return new WaitForSeconds(rebuildTime);

        sprite.enabled = true;

        if (!once)
        {
            //anim.SetTrigger("rebuild");

            once = true;
        }
        

        yield return new WaitForSeconds(0.5f);

        FixedPlatform();
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.gameObject.CompareTag("Player") && breaking == false) 
        {
            //anim.SetTrigger("warning");

            StartCoroutine(DestroyPlatform());

            breaking = true;
        }
    }

    IEnumerator DestroyPlatform()
    {
        yield return new WaitForSeconds(breakTime);

        //anim.SetTrigger("destroy");

        Debug.Log("destroy start");

        col.enabled = false;

        yield return new WaitForSeconds(0.5f);

        BrokenPlatform();

        Debug.Log("destroy end");
    }
}
