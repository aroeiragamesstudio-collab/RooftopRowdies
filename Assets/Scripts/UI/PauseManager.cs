using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;

/// <summary>
/// Coordena o estado de pausa do jogo de forma compatível com:
///   - Multiplayer local (qualquer um dos dois jogadores pode pausar);
///   - "Apenas quem pausou controla o menu" (impede o outro jogador de mexer no menu);
///   - Multiplos action maps de gameplay (ex: JumpPlayer e GunPlayer) sem precisar
///     configurar o nome de cada um — o script LEMBRA o map atual de cada PlayerInput
///     no instante da pausa e restaura aquele exato map no resume;
///   - Migração futura para online (a fonte do evento "Pause" é uma InputAction de
///     um PlayerInput, não um polling direto de Keyboard.current/Gamepad.all).
///
/// Setup no editor (resumo):
///   1. Adicione uma action "Pause" em CADA action map de gameplay
///      (JumpPlayer, GunPlayer, e também no map UI — para que o mesmo botão despause).
///   2. O action map "UI" precisa ter pelo menos: Navigate, Submit, Cancel, Pause.
///   3. Crie um GameObject "PauseSystem" na cena de gameplay e coloque este script nele.
///   4. Coloque um Canvas filho com o painel de pausa (3 botões: Resume, Options, Main Menu)
///      e referencie tudo no inspector.
/// </summary>
public class PauseManager : MonoBehaviour
{
    public static PauseManager Instance { get; private set; }

    [Header("Action map names (apenas o de UI e o nome da action de pausa)")]
    [Tooltip("Nome do action map de UI. Cada PlayerInput precisa ter um map com este nome.")]
    [SerializeField] string uiActionMapName = "UI";
    [Tooltip("Nome da action de pausar/despausar. Precisa existir em cada map de gameplay E no map de UI.")]
    [SerializeField] string pauseActionName = "Pause";

    [Header("UI Input Module (na EventSystem da cena)")]
    [Tooltip("InputSystemUIInputModule da EventSystem. Quando alguém pausa, o PlayerInput " +
             "do pauser passa a alimentar este módulo (Navigate/Submit/Cancel etc). Se " +
             "deixar vazio, é resolvido automaticamente em Start procurando na cena.")]
    [SerializeField] InputSystemUIInputModule uiInputModule;

    [Header("Painéis")]
    [Tooltip("Painel raiz do menu de pausa. Será ativado/desativado.")]
    [SerializeField] GameObject pausePanel;
    [Tooltip("Primeiro botão a ser focado quando o menu abre (necessário para gamepad/teclado).")]
    [SerializeField] GameObject firstSelectedOnPause;

    [Header("Cena do menu principal")]
    [Tooltip("Nome exato da cena do menu (em File > Build Settings).")]
    [SerializeField] string mainMenuSceneName = "MenuScene";

    // ─────────────────────────────────────────────
    // Estado interno
    // ─────────────────────────────────────────────

    /// <summary>True quando o jogo está pausado.</summary>
    public bool IsPaused { get; private set; }

    /// <summary>O PlayerInput de quem disparou o pause atual. Null quando não está pausado.</summary>
    public PlayerInput PauseOwner { get; private set; }

    /// <summary>Cache de todos os PlayerInput ativos na cena.</summary>
    private readonly List<PlayerInput> _allPlayerInputs = new List<PlayerInput>();

    /// <summary>
    /// Mapeia cada PlayerInput → nome do map de gameplay que ele estava usando
    /// antes da pausa. Capturamos isso em Start (uma vez) e validamos antes de cada
    /// pausa. Assim, JumpPlayer volta pra JumpPlayer e GunPlayer volta pra GunPlayer
    /// sem precisar configurar nomes hardcoded.
    /// </summary>
    private readonly Dictionary<PlayerInput, string> _gameplayMapPerPlayer =
        new Dictionary<PlayerInput, string>();

    /// <summary>
    /// Mapeia cada PlayerInput → callback assinado em sua action "Pause", para que
    /// possamos desinscrever corretamente em OnDestroy. Sem isso, cada recarregamento
    /// da cena vazaria callbacks no asset compartilhado.
    /// </summary>
    private readonly Dictionary<PlayerInput, System.Action<InputAction.CallbackContext>>
        _pauseCallbacks = new Dictionary<PlayerInput, System.Action<InputAction.CallbackContext>>();

    /// <summary>Guardado para restaurar Time.timeScale ao despausar (defensivo: evita assumir 1f).</summary>
    private float _previousTimeScale = 1f;

    // ─────────────────────────────────────────────
    // Lifecycle
    // ─────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        ResolvePlayers();
        CacheCurrentGameplayMaps();
        ResolveUIInputModule();
        SubscribeToPauseActions();

        if (pausePanel != null)
            pausePanel.SetActive(false);
    }

    private void ResolveUIInputModule()
    {
        if (uiInputModule != null) return;

        // Procura em qualquer EventSystem ativo da cena. Em geral só tem um.
        uiInputModule = FindFirstObjectByType<InputSystemUIInputModule>();

        if (uiInputModule == null)
        {
            Debug.LogWarning("[PauseManager] Não encontrei InputSystemUIInputModule na cena. " +
                             "O menu de pausa não vai responder a nenhum input. " +
                             "Adicione um EventSystem com InputSystemUIInputModule, ou " +
                             "atribua o módulo no inspector.");
        }
    }

    private void OnDestroy()
    {
        // Se a cena sumiu enquanto pausado, limpa qualquer UI module ownership remanescente.
        if (PauseOwner != null) ClearUIModuleFrom(PauseOwner);

        UnsubscribeFromPauseActions();
        if (Instance == this) Instance = null;

        // Garante que o jogo não fica preso em timeScale = 0 caso o usuário troque
        // de cena com o jogo pausado por algum caminho inesperado.
        Time.timeScale = 1f;
    }

    // ─────────────────────────────────────────────
    // Detecção de jogadores
    // ─────────────────────────────────────────────

    private void ResolvePlayers()
    {
        _allPlayerInputs.Clear();
        PlayerInput[] found = FindObjectsByType<PlayerInput>(FindObjectsSortMode.None);
        _allPlayerInputs.AddRange(found);

        if (_allPlayerInputs.Count == 0)
            Debug.LogWarning("[PauseManager] Nenhum PlayerInput encontrado na cena. " +
                             "O pause não vai responder a nenhum botão.");
    }

    /// <summary>
    /// Em Start, captura para cada PlayerInput qual map ele está usando agora
    /// (antes de qualquer pausa). Esse é o map "de gameplay" daquele jogador,
    /// e é o que vamos restaurar quando ele despausar.
    /// </summary>
    private void CacheCurrentGameplayMaps()
    {
        _gameplayMapPerPlayer.Clear();
        foreach (PlayerInput pi in _allPlayerInputs)
        {
            string mapName = ResolveGameplayMapName(pi);
            if (!string.IsNullOrEmpty(mapName))
            {
                _gameplayMapPerPlayer[pi] = mapName;
                Debug.Log($"[PauseManager] '{pi.gameObject.name}' usa map de gameplay '{mapName}'.");
            }
            else
            {
                Debug.LogWarning($"[PauseManager] Não consegui determinar o map de gameplay de " +
                                 $"'{pi.gameObject.name}'. O resume desse jogador pode falhar.");
            }
        }
    }

    /// <summary>
    /// Determina o map de gameplay de um PlayerInput. Tenta, em ordem:
    ///   1. pi.currentActionMap (o map ativo agora — a opção mais robusta);
    ///   2. pi.defaultActionMap (configurado no inspector);
    ///   3. O primeiro map do asset que NÃO seja o map de UI (último recurso).
    /// </summary>
    private string ResolveGameplayMapName(PlayerInput pi)
    {
        if (pi == null || pi.actions == null) return null;

        // Opção 1: o map atual. Em Start, o LocalMultiplayerManager já chamou
        // SwitchCurrentControlScheme, e o map default do PlayerInput já está ativo.
        if (pi.currentActionMap != null && pi.currentActionMap.name != uiActionMapName)
            return pi.currentActionMap.name;

        // Opção 2: o default configurado no inspector.
        if (!string.IsNullOrEmpty(pi.defaultActionMap) && pi.defaultActionMap != uiActionMapName)
            return pi.defaultActionMap;

        // Opção 3: primeiro map do asset que não seja UI.
        foreach (InputActionMap m in pi.actions.actionMaps)
        {
            if (m.name != uiActionMapName)
                return m.name;
        }

        return null;
    }

    // ─────────────────────────────────────────────
    // Subscrição da action "Pause"
    // ─────────────────────────────────────────────

    private void SubscribeToPauseActions()
    {
        foreach (PlayerInput pi in _allPlayerInputs)
        {
            // Procuramos a action "Pause" pelo NOME, em qualquer map. O Input System
            // resolve a action de acordo com o map atualmente habilitado, então a
            // mesma referência funciona quando o jogador está em gameplay E quando
            // está no map de UI (desde que o nome da action exista nos dois).
            InputAction pauseAction = pi.actions != null ? pi.actions.FindAction(pauseActionName) : null;
            if (pauseAction == null)
            {
                Debug.LogWarning($"[PauseManager] PlayerInput em '{pi.gameObject.name}' não tem " +
                                 $"a action '{pauseActionName}'. Esse jogador não conseguirá pausar.");
                continue;
            }

            // Captura o PlayerInput específico no closure — assim o callback sabe
            // QUEM pausou. Guardamos o delegate para conseguirmos remover depois.
            PlayerInput owner = pi;
            System.Action<InputAction.CallbackContext> callback = ctx => OnPauseActionPerformed(owner);

            pauseAction.performed += callback;
            _pauseCallbacks[pi] = callback;
        }
    }

    private void UnsubscribeFromPauseActions()
    {
        foreach (KeyValuePair<PlayerInput, System.Action<InputAction.CallbackContext>> entry in _pauseCallbacks)
        {
            if (entry.Key == null || entry.Key.actions == null) continue;
            InputAction action = entry.Key.actions.FindAction(pauseActionName);
            if (action != null) action.performed -= entry.Value;
        }
        _pauseCallbacks.Clear();
    }

    // ─────────────────────────────────────────────
    // Toggle de pause
    // ─────────────────────────────────────────────

    private void OnPauseActionPerformed(PlayerInput requester)
    {
        if (!IsPaused)
        {
            Pause(requester);
            return;
        }

        // Está pausado e quem apertou não é o dono → ignora (anti "menu wars").
        if (requester != PauseOwner) return;

        // Se há algum painel EMPILHADO em cima do painel de pausa (ex: Options aberto),
        // tratar Pause como "voltar/cancel" — fecha esse painel mas mantém o pause.
        // Só fecha o pause em si quando o painel de pausa é o topo da pilha.
        if (UIPanelStack.Top != null && UIPanelStack.Top != pausePanel)
        {
            UIPanelStack.Pop();
            return;
        }

        Resume();
    }

    /// <summary>
    /// Pausa o jogo. Pode ser chamado externamente (ex: cinemática que precisa pausar).
    /// </summary>
    public void Pause(PlayerInput owner)
    {
        if (IsPaused) return;
        if (owner == null)
        {
            Debug.LogError("[PauseManager] Pause() chamado com owner = null.");
            return;
        }

        IsPaused = true;
        PauseOwner = owner;

        // 1. Congela toda a simulação.
        _previousTimeScale = Time.timeScale;
        Time.timeScale = 0f;

        // 2. Antes de mexer em qualquer map, ATUALIZA o cache do map atual de cada
        //    jogador. Isso protege contra cenários onde algum sistema externo trocou
        //    o map durante o jogo (ex: um modo veículo). Assim o resume sempre volta
        //    pro estado correto e não pro estado de Start().
        for (int i = 0; i < _allPlayerInputs.Count; i++)
        {
            PlayerInput pi = _allPlayerInputs[i];
            if (pi == null) continue;
            if (pi.currentActionMap != null && pi.currentActionMap.name != uiActionMapName)
                _gameplayMapPerPlayer[pi] = pi.currentActionMap.name;
        }

        // 3. Para CADA jogador: troca o map para UI no dono, desabilita os outros.
        //    IMPORTANTE: assina o uiInputModule ao PlayerInput do dono ANTES de trocar
        //    o map. Esse é o passo que faz o EventSystem ler input desse jogador.
        //    Sem isso, o map UI fica habilitado mas o módulo da EventSystem está
        //    apontando pra outras actions (ou pra cópias sem device pareado), e o
        //    menu não responde a nada. Doc oficial:
        //    "when the PlayerInput configures the Actions for a specific player, it
        //     assigns the same Action configuration to the InputSystemUIInputModule"
        AssignUIModuleTo(owner);

        foreach (PlayerInput pi in _allPlayerInputs)
        {
            if (pi == null) continue;
            if (pi == owner)
                SwitchToUIMap(pi);
            else
                pi.DeactivateInput();
        }

        // 4. Mostra o painel de pausa via UIPanelStack. A pilha cuida de:
        //    - Ativar o painel
        //    - Selecionar o primeiro item (firstSelectedOnPause)
        //    - Lembrar dele para que, quando Options abrir por cima, a gente
        //      consiga voltar exatamente para esse painel ao fechar Options.
        if (pausePanel != null)
            UIPanelStack.Push(pausePanel, firstSelectedOnPause);

        Debug.Log($"[PauseManager] Pausado por '{owner.gameObject.name}' (map: '{owner.currentActionMap?.name}').");
    }

    /// <summary>
    /// Despausa. Pode ser chamado pelo botão "Resume" ou pelo botão de pause do dono.
    /// </summary>
    public void Resume()
    {
        if (!IsPaused) return;

        // 1. Tira o UI module do PlayerInput do dono — depois de despausar, ninguém
        //    deveria estar conduzindo o EventSystem. Isso evita que o EventSystem
        //    receba navegação fantasma do gamepad quando o jogador volta ao gameplay.
        ClearUIModuleFrom(PauseOwner);

        // 2. Restaura input de todo mundo no map de gameplay que CADA jogador tinha.
        foreach (PlayerInput pi in _allPlayerInputs)
        {
            if (pi == null) continue;

            if (pi == PauseOwner)
            {
                SwitchToGameplayMap(pi);
            }
            else
            {
                // O outro jogador estava só desabilitado — reativar é suficiente.
                pi.ActivateInput();
            }
        }

        // Limpa toda a UI empilhada (pause panel + options se estava aberto).
        // Não usamos Pop em loop porque queremos certeza de estado limpo ao retomar
        // o gameplay — qualquer painel residual ficaria desenhado por cima da tela.
        UIPanelStack.Clear();

        // 3. Restaura o tempo POR ÚLTIMO — evita um frame onde a simulação roda
        //    mas o input ainda não voltou ao map de gameplay.
        Time.timeScale = _previousTimeScale > 0f ? _previousTimeScale : 1f;

        IsPaused = false;
        PauseOwner = null;

        Debug.Log("[PauseManager] Despausado.");
    }

    /// <summary>
    /// Atribui o uiInputModule da EventSystem ao PlayerInput do owner. Quando isso é
    /// feito, o PlayerInput sincroniza suas action references com o módulo — então
    /// o módulo passa a ler Navigate/Submit/Cancel da MESMA cópia privada de actions
    /// que esse jogador está usando, com os MESMOS devices pareados.
    /// </summary>
    private void AssignUIModuleTo(PlayerInput owner)
    {
        if (uiInputModule == null || owner == null) return;
        owner.uiInputModule = uiInputModule;
    }

    /// <summary>
    /// Tira a referência do PlayerInput. Sem isso, mesmo após despausar o EventSystem
    /// ainda receberia navegação do dono enquanto ele joga.
    /// </summary>
    private void ClearUIModuleFrom(PlayerInput owner)
    {
        if (owner == null) return;
        // Só limpa se ainda for esse jogador que possui — protege contra race conditions
        // (ex: se outro sistema já trocou o owner do módulo por algum motivo).
        if (owner.uiInputModule == uiInputModule)
            owner.uiInputModule = null;
    }

    private void SwitchToUIMap(PlayerInput pi)
    {
        if (pi.actions == null) return;
        InputActionMap uiMap = pi.actions.FindActionMap(uiActionMapName);
        if (uiMap == null)
        {
            Debug.LogWarning($"[PauseManager] Action map '{uiActionMapName}' não encontrado em " +
                             $"'{pi.gameObject.name}'. O dono do pause vai ficar sem input.");
            return;
        }
        pi.SwitchCurrentActionMap(uiActionMapName);
    }

    private void SwitchToGameplayMap(PlayerInput pi)
    {
        if (pi.actions == null) return;

        // Usa o map ESPECÍFICO daquele jogador que cacheamos antes.
        if (!_gameplayMapPerPlayer.TryGetValue(pi, out string mapName) || string.IsNullOrEmpty(mapName))
        {
            // Fallback: tenta resolver de novo agora (último recurso).
            mapName = ResolveGameplayMapName(pi);
        }

        if (string.IsNullOrEmpty(mapName))
        {
            Debug.LogError($"[PauseManager] Não consegui restaurar o map de gameplay de " +
                           $"'{pi.gameObject.name}'. Esse jogador vai ficar sem input.");
            return;
        }

        InputActionMap gameMap = pi.actions.FindActionMap(mapName);
        if (gameMap == null)
        {
            Debug.LogWarning($"[PauseManager] Map '{mapName}' sumiu do asset de '{pi.gameObject.name}'. " +
                             "O asset foi modificado em runtime?");
            return;
        }

        pi.SwitchCurrentActionMap(mapName);
    }

    // ─────────────────────────────────────────────
    // Botões do menu (chame estes via OnClick no inspector)
    // ─────────────────────────────────────────────

    public void OnResumeButton()
    {
        Resume();
    }

    public void OnOptionsButton()
    {
        if (MenuController.instance != null)
        {
            MenuController.instance.OpenOptions();
        }
        else
        {
            Debug.LogWarning("[PauseManager] MenuController.instance é null. " +
                             "A cena de gameplay foi iniciada sem passar pelo menu? " +
                             "Considere adicionar um fallback (ex: instanciar um prefab do menu de opções).");
        }
    }

    public void OnMainMenuButton()
    {
        Time.timeScale = 1f;
        IsPaused = false;
        PauseOwner = null;

        // Limpa a UI empilhada da gameplay antes de trocar de cena, senão o painel
        // de pausa ou de options ficaria "vivo" enquanto a cena nova carrega.
        UIPanelStack.Clear();

        if (MultiplayerSessionData.Instance != null)
            MultiplayerSessionData.Instance.Reset();

        if (MenuController.instance != null)
            MenuController.instance.BackToMenu();

        SceneManager.LoadScene(mainMenuSceneName);
    }
}