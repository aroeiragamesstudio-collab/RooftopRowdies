using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Tela de conclusão de fase com efeito de palavras caindo de cima.
///
/// SETUP NO EDITOR:
///  1. Crie um Canvas (Screen Space - Overlay)
///  2. Adicione um painel filho cobrindo a tela inteira com um CanvasGroup
///  3. Dentro do painel, crie um TextMeshPro para cada palavra:
///       - Word 1: "Congratulations!"  (wordObjects[0])
///  4. Ou deixe wordObjects vazio: o script cria os textos automaticamente.
///  5. Arraste este script para o painel e configure as referências.
/// </summary>
public class LevelCompleteUI : MonoBehaviour
{
    [Header("Referências")]
    [Tooltip("Painel raiz com CanvasGroup (para fade de fundo opcional)")]
    public CanvasGroup canvasGroup;

    [Tooltip("Objetos de texto para cada palavra. Deixe vazio para criação automática.")]
    public RectTransform[] wordObjects;

    [Header("Texto (usado na criação automática)")]
    public string fullText = "Congratulations!";

    [Header("Animação — queda")]
    [Tooltip("De onde cada palavra começa (em pixels acima da posição final)")]
    public float dropStartOffsetY = 400f;

    [Tooltip("Tempo que cada palavra leva para cair até a posição final")]
    public float dropDuration = 0.45f;

    [Tooltip("Intervalo entre a queda de cada palavra")]
    public float wordDelay = 0.18f;

    [Tooltip("Curve de easing da queda (use Ease In para parecer pesado)")]
    public AnimationCurve dropCurve = new AnimationCurve(
        new Keyframe(0f, 0f, 0f, 2f),
        new Keyframe(0.75f, 1.05f),   // ultrapassa levemente (bounce)
        new Keyframe(1f, 1f, 0f, 0f)  // assenta no lugar
    );

    [Header("Animação — saída")]
    public float exitDuration = 0.3f;
    public float exitOffsetY = -80f; // sobe um pouco ao sair

    [Header("Partículas (opcional)")]
    public ParticleSystem celebrationParticles;

    // Posições originais de cada palavra (definidas no editor ou geradas)
    private Vector2[] _originalPositions;
    private bool _created = false;

    // ─────────────────────────────────────────────
    private void Awake()
    {
        HideImmediate();
    }

    // ─────────────────────────────────────────────
    public IEnumerator ShowAsync(float _ = 0f) // parâmetro mantido por compatibilidade
    {
        // Cria palavras automaticamente se necessário
        if (wordObjects == null || wordObjects.Length == 0)
            CreateWordsFromText();

        // Salva posições originais e move para cima
        _originalPositions = new Vector2[wordObjects.Length];
        for (int i = 0; i < wordObjects.Length; i++)
        {
            _originalPositions[i] = wordObjects[i].anchoredPosition;
            wordObjects[i].anchoredPosition += Vector2.up * dropStartOffsetY;
            wordObjects[i].gameObject.SetActive(true);

            // Garante que o texto está visível
            var tmp = wordObjects[i].GetComponent<TextMeshProUGUI>();
            if (tmp != null) SetAlpha(tmp, 1f);
        }

        // Fundo semi-transparente
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 1f;
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;
        }

        if (celebrationParticles != null)
            celebrationParticles.Play();

        // Anima cada palavra caindo
        for (int i = 0; i < wordObjects.Length; i++)
        {
            StartCoroutine(DropWord(wordObjects[i], _originalPositions[i], dropDuration));
            yield return new WaitForSecondsRealtime(wordDelay);
        }

        // Aguarda a última palavra terminar
        yield return new WaitForSecondsRealtime(dropDuration);
    }

    // ─────────────────────────────────────────────
    public IEnumerator HideAsync(float _ = 0f)
    {
        // Sobe e some todas as palavras juntas
        if (wordObjects != null)
        {
            for (int i = 0; i < wordObjects.Length; i++)
                StartCoroutine(ExitWord(wordObjects[i], exitDuration));
        }

        yield return new WaitForSecondsRealtime(exitDuration);

        HideImmediate();

        if (celebrationParticles != null)
            celebrationParticles.Stop();
    }

    // ─────────────────────────────────────────────
    // Coroutines de animação
    // ─────────────────────────────────────────────

    private IEnumerator DropWord(RectTransform rt, Vector2 target, float duration)
    {
        Vector2 start = rt.anchoredPosition;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = dropCurve.Evaluate(Mathf.Clamp01(elapsed / duration));
            rt.anchoredPosition = Vector2.LerpUnclamped(start, target, t);
            yield return null;
        }

        rt.anchoredPosition = target;
    }

    private IEnumerator ExitWord(RectTransform rt, float duration)
    {
        Vector2 start = rt.anchoredPosition;
        Vector2 end = start + Vector2.up * Mathf.Abs(exitOffsetY);
        var tmp = rt.GetComponent<TextMeshProUGUI>();
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            rt.anchoredPosition = Vector2.Lerp(start, end, t);
            if (tmp != null) SetAlpha(tmp, 1f - t);
            yield return null;
        }
    }

    // ─────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────

    private void HideImmediate()
    {
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }

        if (wordObjects != null)
        {
            foreach (var w in wordObjects)
                if (w != null) w.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// Cria automaticamente um RectTransform + TextMeshProUGUI para cada palavra.
    /// </summary>
    private void CreateWordsFromText()
    {
        string[] words = fullText.Split(' ');
        wordObjects = new RectTransform[words.Length];
        _originalPositions = new Vector2[words.Length];

        // Posições horizontais centralizadas com espaçamento
        float totalWidth = 0f;
        float[] widths = new float[words.Length];
        float spacing = 20f;

        // Estima largura de cada palavra (aproximado)
        for (int i = 0; i < words.Length; i++)
            widths[i] = words[i].Length * 38f; // ~38px por char em fonte grande

        for (int i = 0; i < words.Length; i++)
            totalWidth += widths[i] + (i < words.Length - 1 ? spacing : 0f);

        float startX = -totalWidth / 2f;

        for (int i = 0; i < words.Length; i++)
        {
            GameObject go = new GameObject("Word_" + words[i]);
            go.transform.SetParent(transform, false);

            RectTransform rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(widths[i] + 20f, 120f);

            float centerX = startX + widths[i] / 2f;
            rt.anchoredPosition = new Vector2(centerX, 0f);
            startX += widths[i] + spacing;

            TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = words[i];
            tmp.fontSize = 72;
            tmp.fontStyle = FontStyles.Bold;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;

            // Outline simples
            tmp.outlineWidth = 0.2f;
            tmp.outlineColor = new Color32(0, 0, 0, 200);

            wordObjects[i] = rt;
            go.SetActive(false);
        }

        _created = true;
    }

    private void SetAlpha(TextMeshProUGUI tmp, float a)
    {
        Color c = tmp.color;
        c.a = a;
        tmp.color = c;
    }

    private void OnDestroy()
    {
        // Limpa objetos criados automaticamente
        if (_created && wordObjects != null)
            foreach (var w in wordObjects)
                if (w != null) Destroy(w.gameObject);
    }
}