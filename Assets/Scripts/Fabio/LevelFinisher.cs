using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// A tela final só aparece quando os DOIS jogadores (mesma tag "Player")
/// estiverem na plataforma ao mesmo tempo.
/// </summary>
public class LevelFinisher : MonoBehaviour
{
    [Header("Jogadores")]
    public string playerTag = "Player";
    [Tooltip("Quantos jogadores precisam estar na plataforma para ativar")]
    public int requiredPlayers = 2;

    [Header("Configurações de Tempo")]
    public float displayDuration = 3f;
    public float fadeDuration = 0.8f;
    public float blackFadeDuration = 1.2f;

    [Header("Referência da UI")]
    public LevelCompleteUI levelCompleteUI;
    public Canvas correctCanvas;

    [Header("Próxima Cena")]
    [Tooltip("Nome da próxima cena. Se vazio, carrega a próxima do Build Settings.")]
    public string nextSceneName = "";

    // Rastreia os objetos que estão dentro do trigger (por instância, não por tag)
    private HashSet<GameObject> _playersInside = new HashSet<GameObject>();
    private bool _triggered = false;
    private Image _blackOverlay;

    private void Start()
    {
        if (levelCompleteUI == null)
            levelCompleteUI = FindFirstObjectByType<LevelCompleteUI>();

        CreateBlackOverlay();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (_triggered) return;
        if (!other.CompareTag(playerTag)) return;

        _playersInside.Add(other.gameObject);

        if (_playersInside.Count >= requiredPlayers)
        {
            _triggered = true;
            StartCoroutine(FinishLevelSequence());
        }
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (_triggered) return;
        if (!other.CompareTag(playerTag)) return;

        _playersInside.Remove(other.gameObject);
    }

    private IEnumerator FinishLevelSequence()
    {
        FreezePlayers();

        if (levelCompleteUI != null)
            yield return levelCompleteUI.ShowAsync(fadeDuration);

        yield return new WaitForSecondsRealtime(displayDuration);

        yield return FadeToBlack(blackFadeDuration);

        LoadNextScene();
    }

    private void FreezePlayers()
    {
        foreach (GameObject player in _playersInside)
        {
            if (player == null) continue;
            Rigidbody2D rb = player.GetComponent<Rigidbody2D>();
            if (rb != null)
            {
                rb.linearVelocity = Vector2.zero;
                rb.bodyType = RigidbodyType2D.Kinematic;
            }
        }
    }

    private IEnumerator FadeToBlack(float duration)
    {
        if (_blackOverlay == null) yield break;

        _blackOverlay.gameObject.SetActive(true);

        float elapsed = 0f;
        Color c = _blackOverlay.color;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            c.a = Mathf.Clamp01(elapsed / duration);
            _blackOverlay.color = c;
            yield return null;
        }

        c.a = 1f;
        _blackOverlay.color = c;
    }

    private void LoadNextScene()
    {
        Time.timeScale = 1f;

        if (!string.IsNullOrEmpty(nextSceneName))
        {
            MenuController.instance.BackToMenu();
            SceneManager.LoadScene(nextSceneName);
        }
        else
        {
            int next = SceneManager.GetActiveScene().buildIndex + 1;
            if (next < SceneManager.sceneCountInBuildSettings)
                SceneManager.LoadScene(next);
            else
                Debug.LogWarning("[LevelFinisher] Não há próxima cena no Build Settings!");
        }
    }

    private void CreateBlackOverlay()
    {
        Canvas canvas = correctCanvas;
        if (canvas == null)
        {
            GameObject cgo = new GameObject("Canvas_BlackFade");
            canvas = cgo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 999;
            cgo.AddComponent<UnityEngine.UI.CanvasScaler>();
            cgo.AddComponent<UnityEngine.UI.GraphicRaycaster>();
        }

        GameObject overlay = new GameObject("BlackOverlay");
        overlay.transform.SetParent(canvas.transform, false);
        overlay.transform.SetAsLastSibling();

        RectTransform rt = overlay.AddComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        _blackOverlay = overlay.AddComponent<Image>();
        _blackOverlay.color = new Color(0, 0, 0, 0);
        overlay.SetActive(false);
    }
}