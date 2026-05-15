using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Pilha global de painéis de UI. Resolve o problema clássico de "menus em camadas":
/// quando você abre um painel novo (ex: Options) por cima de outro (ex: Pause), o
/// painel anterior tem que SAIR, mas voltar exatamente como estava — incluindo o
/// item que o player tinha selecionado — quando o de cima fecha.
///
/// Por que uma pilha em vez de um sistema ad-hoc?
///   - Cada Open empurra o painel atual e seu item selecionado para a pilha.
///   - Cada Close pega o último e restaura tudo. Ordem garantida.
///   - Funciona com QUALQUER quantidade de níveis (Pause → Options → "Tem certeza?"
///     → ... ) sem precisar reescrever lógica para cada combinação.
///   - Resolve o bug "EventSystem perde a seleção": ao desativar o painel atual,
///     a gente PRIMEIRO limpa a seleção pra null, DEPOIS desativa, DEPOIS seleciona
///     o novo item no painel novo. Sem essa ordem, o EventSystem fica olhando pra
///     um GameObject inativo e perde a navegação.
///
/// Uso:
///   UIPanelStack.Push(optionsPanel, optionsFirstButton);    // abre options
///   UIPanelStack.Pop();                                      // fecha options, volta o pause
/// </summary>
public static class UIPanelStack
{
    private struct PanelLayer
    {
        public GameObject panel;
        public GameObject lastSelected;  // o que estava selecionado quando empilhamos
    }

    private static readonly Stack<PanelLayer> _stack = new Stack<PanelLayer>();

    /// <summary>Quantos painéis estão atualmente empilhados.</summary>
    public static int Count => _stack.Count;

    /// <summary>O painel no topo (visível) ou null se a pilha está vazia.</summary>
    public static GameObject Top => _stack.Count > 0 ? _stack.Peek().panel : null;

    /// <summary>
    /// Abre 'newPanel' por cima do que estiver no topo. O painel antigo é desativado
    /// e seu item selecionado é guardado para restaurar depois. O 'firstSelected' do
    /// novo painel é focado automaticamente.
    /// </summary>
    public static void Push(GameObject newPanel, GameObject firstSelected = null)
    {
        if (newPanel == null)
        {
            Debug.LogError("[UIPanelStack] Push chamado com newPanel = null.");
            return;
        }

        // 1. Captura quem está no topo agora (se houver) e a seleção atual.
        if (_stack.Count > 0)
        {
            PanelLayer current = _stack.Peek();
            // Atualiza o lastSelected do topo com o que está realmente selecionado AGORA
            // (o usuário pode ter navegado desde o Push original).
            GameObject currentlySelected = EventSystem.current != null
                ? EventSystem.current.currentSelectedGameObject
                : null;

            if (currentlySelected != null)
            {
                _stack.Pop();
                current.lastSelected = currentlySelected;
                _stack.Push(current);
            }

            // Limpa a seleção ANTES de desativar o painel — sem isso o EventSystem
            // segura uma referência a um GameObject que está ficando inativo, e a
            // navegação quebra silenciosamente.
            if (EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(null);

            current.panel.SetActive(false);
        }

        // 2. Empilha e ativa o novo.
        newPanel.SetActive(true);
        _stack.Push(new PanelLayer { panel = newPanel, lastSelected = firstSelected });

        // 3. Seleciona o primeiro item do novo painel.
        // Resolve automaticamente se o caller não passou um.
        GameObject toSelect = firstSelected != null ? firstSelected : FindFirstSelectableIn(newPanel);
        if (toSelect != null && EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(toSelect);
    }

    /// <summary>
    /// Fecha o painel do topo e restaura o anterior. Volta a seleção exatamente para
    /// onde o usuário estava (last selected do nível anterior).
    /// </summary>
    public static void Pop()
    {
        if (_stack.Count == 0)
        {
            Debug.LogWarning("[UIPanelStack] Pop chamado com a pilha vazia.");
            return;
        }

        PanelLayer top = _stack.Pop();

        // Limpa a seleção primeiro, depois desativa.
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);

        if (top.panel != null) top.panel.SetActive(false);

        // Restaura o painel anterior, se houver.
        if (_stack.Count > 0)
        {
            PanelLayer previous = _stack.Peek();
            if (previous.panel != null) previous.panel.SetActive(true);

            GameObject toSelect = previous.lastSelected != null && previous.lastSelected.activeInHierarchy
                ? previous.lastSelected
                : FindFirstSelectableIn(previous.panel);

            if (toSelect != null && EventSystem.current != null)
                EventSystem.current.SetSelectedGameObject(toSelect);
        }
    }

    /// <summary>
    /// Esvazia a pilha desativando todos os painéis. Útil quando você sai pra menu
    /// principal ou recarrega a cena e quer um estado limpo.
    /// </summary>
    public static void Clear()
    {
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);

        while (_stack.Count > 0)
        {
            PanelLayer layer = _stack.Pop();
            if (layer.panel != null) layer.panel.SetActive(false);
        }
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

    public static void ForceReset()
    {
        _stack.Clear();
    }
}