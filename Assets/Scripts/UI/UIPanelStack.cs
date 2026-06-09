using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Pilha global de painéis de UI.
///
/// Dois modos de empilhamento:
///
///   Push(panel, firstSelected)
///     Modo padrão — o painel anterior é DESATIVADO e guardado. Use para
///     navegação profunda onde o painel de baixo não deve ser visível
///     (ex: Pause → Options).
///
///   PushOverlay(panel, firstSelected)
///     Modo sobreposição — o painel anterior PERMANECE ATIVO e visível, mas
///     tem sua interatividade desabilitada via CanvasGroup. Use para diálogos
///     de confirmação que devem aparecer sobre o painel atual sem escondê-lo
///     (ex: "Tem certeza que quer sair?").
///
/// Pop() funciona para ambos os modos: detecta pelo flag isOverlay e age
/// de forma adequada (reativa interatividade em vez de reativar o objeto).
/// </summary>
public static class UIPanelStack
{
    private struct PanelLayer
    {
        public GameObject panel;
        public GameObject lastSelected;
        /// <summary>
        /// True se este painel foi aberto como overlay (o painel abaixo ficou
        /// visível mas não-interativo). Pop restaura interatividade em vez de
        /// reativar SetActive.
        /// </summary>
        public bool isOverlay;
    }

    private static readonly Stack<PanelLayer> _stack = new Stack<PanelLayer>();

    /// <summary>Quantos painéis estão atualmente empilhados.</summary>
    public static int Count => _stack.Count;

    /// <summary>O painel no topo (visível) ou null se a pilha está vazia.</summary>
    public static GameObject Top => _stack.Count > 0 ? _stack.Peek().panel : null;

    // ─────────────────────────────────────────────────────────────────────────
    // Push — o painel anterior É desativado (navegação profunda)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Abre 'newPanel' por cima do que estiver no topo. O painel antigo é
    /// desativado e seu item selecionado é guardado para restaurar depois.
    /// Use para navegação onde o painel de baixo não precisa ser visível.
    /// </summary>
    public static void Push(GameObject newPanel, GameObject firstSelected = null)
    {
        if (newPanel == null)
        {
            Debug.LogError("[UIPanelStack] Push chamado com newPanel = null.");
            return;
        }

        if (_stack.Count > 0)
        {
            SaveCurrentSelection();

            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(null);

            // Desativa o painel do topo (comportamento normal).
            _stack.Peek().panel.SetActive(false);
        }

        newPanel.SetActive(true);
        _stack.Push(new PanelLayer
        {
            panel = newPanel,
            lastSelected = firstSelected,
            isOverlay = false
        });

        FocusFirst(firstSelected ?? FindFirstSelectableIn(newPanel));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PushOverlay — o painel anterior PERMANECE visível mas não-interativo
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Abre 'newPanel' como sobreposição: o painel abaixo continua visível,
    /// mas tem interatividade bloqueada via CanvasGroup. Use para diálogos de
    /// confirmação que devem aparecer "em cima" sem esconder o conteúdo de baixo.
    ///
    /// REQUISITO: o painel abaixo precisa ter um CanvasGroup. Se não tiver,
    /// um aviso é logado e a interatividade não pode ser bloqueada corretamente
    /// (o Push() normal é mais seguro nesse caso).
    /// </summary>
    public static void PushOverlay(GameObject newPanel, GameObject firstSelected = null)
    {
        if (newPanel == null)
        {
            Debug.LogError("[UIPanelStack] PushOverlay chamado com newPanel = null.");
            return;
        }

        if (_stack.Count > 0)
        {
            SaveCurrentSelection();

            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(null);

            // Bloqueia interatividade do painel abaixo — mas NÃO desativa.
            GameObject below = _stack.Peek().panel;
            CanvasGroup cg = below.GetComponent<CanvasGroup>();
            if (cg != null)
            {
                cg.interactable = false;
                cg.blocksRaycasts = false;
            }
            else
            {
                Debug.LogWarning($"[UIPanelStack] PushOverlay: '{below.name}' não tem CanvasGroup. " +
                                 "A interatividade abaixo não será bloqueada. " +
                                 "Adicione um CanvasGroup ao painel para evitar cliques fantasmas.");
            }
        }

        newPanel.SetActive(true);
        _stack.Push(new PanelLayer
        {
            panel = newPanel,
            lastSelected = firstSelected,
            isOverlay = true
        });

        FocusFirst(firstSelected ?? FindFirstSelectableIn(newPanel));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Pop — fecha o topo e restaura o estado do painel abaixo
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Fecha o painel do topo e restaura o anterior. Detecta automaticamente
    /// se o painel fechado era um overlay ou não, e age de forma adequada:
    ///   - Overlay: reativa interatividade do painel abaixo (que nunca foi desativado).
    ///   - Normal: reativa SetActive do painel abaixo.
    /// </summary>
    public static void Pop()
    {
        if (_stack.Count == 0)
        {
            Debug.LogWarning("[UIPanelStack] Pop chamado com a pilha vazia.");
            return;
        }

        PanelLayer top = _stack.Pop();

        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);

        if (top.panel != null) top.panel.SetActive(false);

        if (_stack.Count > 0)
        {
            PanelLayer previous = _stack.Peek();

            if (top.isOverlay)
            {
                // O painel abaixo nunca foi desativado — só restaura interatividade.
                CanvasGroup cg = previous.panel != null
                    ? previous.panel.GetComponent<CanvasGroup>()
                    : null;

                if (cg != null)
                {
                    cg.interactable = true;
                    cg.blocksRaycasts = true;
                }
                // O painel já está ativo, então não chamamos SetActive(true).
            }
            else
            {
                // Push normal — reativa o painel.
                if (previous.panel != null) previous.panel.SetActive(true);
            }

            GameObject toSelect = previous.lastSelected != null
                                  && previous.lastSelected.activeInHierarchy
                ? previous.lastSelected
                : FindFirstSelectableIn(previous.panel);

            FocusFirst(toSelect);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Clear / ForceReset
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Esvazia a pilha desativando todos os painéis e restaurando os
    /// CanvasGroups de painéis que estavam bloqueados como overlay.
    /// Útil quando você sai para o menu principal ou recarrega a cena.
    /// </summary>
    public static void Clear()
    {
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);

        while (_stack.Count > 0)
        {
            PanelLayer layer = _stack.Pop();
            if (layer.panel == null) continue;

            // Se era overlay o CanvasGroup pode estar com interactable=false —
            // restauramos antes de desativar para não deixar o objeto num estado
            // bloqueado caso ele seja reutilizado depois.
            CanvasGroup cg = layer.panel.GetComponent<CanvasGroup>();
            if (cg != null)
            {
                cg.interactable = true;
                cg.blocksRaycasts = true;
            }

            layer.panel.SetActive(false);
        }
    }

    /// <summary>
    /// Força o reset da pilha SEM chamar SetActive nos painéis.
    /// Use apenas quando os painéis já foram destruídos ou quando a cena
    /// acabou de ser carregada e o estado anterior da pilha estática é inválido.
    /// (A pilha é estática, então sobrevive entre cenas — ForceReset limpa
    /// referências antigas sem tentar acessar GameObjects que não existem mais.)
    /// </summary>
    public static void ForceReset()
    {
        _stack.Clear();

        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers privados
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Atualiza o lastSelected do topo da pilha com o que está realmente
    /// selecionado agora (o usuário pode ter navegado desde o Push original).
    /// </summary>
    private static void SaveCurrentSelection()
    {
        if (_stack.Count == 0) return;

        GameObject currentlySelected = EventSystem.current != null
            ? EventSystem.current.currentSelectedGameObject
            : null;

        if (currentlySelected == null) return;

        PanelLayer current = _stack.Pop();
        current.lastSelected = currentlySelected;
        _stack.Push(current);
    }

    private static void FocusFirst(GameObject target)
    {
        if (target != null && EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(target);
    }

    private static GameObject FindFirstSelectableIn(GameObject root)
    {
        if (root == null) return null;
        Selectable[] selectables = root.GetComponentsInChildren<Selectable>(false);
        foreach (Selectable s in selectables)
        {
            if (s != null && s.IsInteractable())
                return s.gameObject;
        }
        return null;
    }
}