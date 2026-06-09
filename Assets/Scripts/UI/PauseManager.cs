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
    [Tooltip("Painel de confirmação de saída do jogo (fica sobre o pausePanel como overlay).")]
    [SerializeField] GameObject quitConfirmPanel;
    [Tooltip("Primeiro botão focado no painel de confirmação (prefira o botão Cancel por segurança).")]
    [SerializeField] GameObject firstSelectedOnQuitConfirm;

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
    /// <summary>
    /// Cada PlayerInput pode ter MÚLTIPLAS actions "Pause" (uma em cada map onde
    /// existe — gameplay map e UI map). Subscrevemos em TODAS para que, independente
    /// de qual map esteja ativo no momento da pressão, nossa callback seja chamada.
    /// O dicionário guarda a lista de (action, callback) para conseguirmos desinscrever.
    /// </summary>
    private readonly Dictionary<PlayerInput, List<(InputAction action, System.Action<InputAction.CallbackContext> callback)>>
        _pauseCallbacks = new Dictionary<PlayerInput, List<(InputAction, System.Action<InputAction.CallbackContext>)>>();

    /// <summary>
    /// Pedidos pendentes de pausa/resume. Os callbacks da action NÃO mudam o estado
    /// imediatamente — eles só registram aqui qual jogador pediu. O Update consome
    /// na frame seguinte. Isso evita o bug do Unity Input System onde alterar o
    /// enable de um action map de dentro do callback do `performed` deixa as
    /// callbacks órfãs (Unity Issue #538).
    /// </summary>
    private PlayerInput _pendingPauseRequest;
    private bool _pendingResumeRequest;

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
            if (pi == null || pi.actions == null) continue;

            // IMPORTANTE: subscrevemos na action "Pause" de CADA map onde ela existe,
            // não só em uma. Razão: cada map tem sua PRÓPRIA InputAction com aquele nome
            // (instâncias diferentes!). Quando o map de gameplay está ativo, a action
            // de gameplay dispara performed; quando o map UI está ativo, é a do map UI
            // que dispara. Se a gente subscreve só em uma, perde os eventos da outra.
            //
            // Sem isso, o bug clássico aparece: pause funciona, mas qualquer toggle
            // depois (ou interações cruzadas com UI) deixam de chamar nosso callback.
            List<(InputAction, System.Action<InputAction.CallbackContext>)> subs =
                new List<(InputAction, System.Action<InputAction.CallbackContext>)>();

            int found = 0;
            foreach (InputActionMap map in pi.actions.actionMaps)
            {
                InputAction pauseAction = map.FindAction(pauseActionName);
                if (pauseAction == null) continue;

                PlayerInput owner = pi;
                System.Action<InputAction.CallbackContext> callback = ctx => OnPauseActionPerformed(owner);

                pauseAction.performed += callback;
                subs.Add((pauseAction, callback));
                found++;
            }

            if (found == 0)
            {
                Debug.LogWarning($"[PauseManager] PlayerInput em '{pi.gameObject.name}' não tem " +
                                 $"a action '{pauseActionName}' em nenhum map. Esse jogador não conseguirá pausar.");
                continue;
            }

            _pauseCallbacks[pi] = subs;
        }
    }

    private void UnsubscribeFromPauseActions()
    {
        foreach (var entry in _pauseCallbacks)
        {
            if (entry.Key == null) continue;
            foreach (var pair in entry.Value)
            {
                if (pair.action != null)
                    pair.action.performed -= pair.callback;
            }
        }
        _pauseCallbacks.Clear();
    }

    // ─────────────────────────────────────────────
    // Toggle de pause
    // ─────────────────────────────────────────────

    private void OnPauseActionPerformed(PlayerInput requester)
    {
        // Só registramos a intenção. A mudança real (incluindo SwitchCurrentActionMap)
        // acontece em Update, na PRÓXIMA frame. Por quê?
        //
        // Há um bug conhecido do Unity Input System (Issue #538) onde desabilitar uma
        // action enquanto seu callback está executando deixa o callback órfão — depois
        // que a action é reabilitada, o callback nunca mais é chamado. Como nosso
        // Pause()/Resume() chamam SwitchCurrentActionMap (que desabilita a action atual
        // e habilita a do novo map), executar isso DENTRO do callback do `performed`
        // produz exatamente esse cenário.
        //
        // Dois sintomas que isso causa, vistos no nosso jogo:
        //   1. "Pause funciona, mas no segundo Pause não responde mais" — clássico.
        //   2. Comportamento inconsistente entre devices, porque a ordem das callbacks
        //      é dependente de qual action disparou.
        //
        // Solução: enfileirar a transição. Update consome no próximo frame, fora do
        // contexto de callback do Input System.

        if (!IsPaused)
        {
            _pendingPauseRequest = requester;
            return;
        }

        // Está pausado e quem apertou não é o dono → ignora.
        if (requester != PauseOwner) return;

        // Se há painel empilhado em cima do pause (ex: Options), Pause = back.
        // Pop não mexe em maps, então pode ser chamado direto sem deferir.
        if (UIPanelStack.Top != null && UIPanelStack.Top != pausePanel)
        {
            UIPanelStack.Pop();
            return;
        }

        _pendingResumeRequest = true;
    }

    /// <summary>
    /// Consome pedidos pendentes de pausa/resume um frame depois do callback do Input.
    /// Isso é o que evita o "callback órfão" descrito em OnPauseActionPerformed.
    /// </summary>
    private void Update()
    {
        if (_pendingPauseRequest != null)
        {
            PlayerInput requester = _pendingPauseRequest;
            _pendingPauseRequest = null;
            // Re-checa o estado atual: pode ter mudado entre o callback e este frame
            // (raro, mas possível com múltiplos jogadores apertando simultaneamente).
            if (!IsPaused) Pause(requester);
        }

        if (_pendingResumeRequest)
        {
            _pendingResumeRequest = false;
            if (IsPaused) Resume();
        }
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
        //    Configura o EventSystem para LER do PlayerInput do dono. Detalhe:
        //    AssignUIModuleTo internamente já habilita o map UI do owner antes de
        //    setar as referências, então a ordem aqui (Assign primeiro, Switch depois)
        //    funciona — a Switch só confirma que UI é o map "current" do PlayerInput
        //    e desabilita o map de gameplay anterior.
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

    // ─────────────────────────────────────────────
    // Wiring direto do InputSystemUIInputModule
    //
    // Por que NÃO usamos owner.uiInputModule = uiInputModule?
    //
    // Esse setter pede pro PlayerInput "auto-sincronizar" Submit/Cancel/Navigate/etc
    // do asset dele com o módulo. Mas o setter foi desenhado pra ser chamado UMA vez,
    // no início. Reatribuir/limpar várias vezes (cada pause/resume) tem dois problemas:
    //
    //   1. A sincronização lê do asset — mas se a action de UI estiver desabilitada
    //      (porque o map dela ainda não foi habilitado quando atribuímos), o módulo
    //      acaba com referências que apontam para actions inativas.
    //   2. Há bugs registrados na Unity sobre NullReferenceException ao alterar
    //      essa propriedade em runtime (Issue 1293556).
    //
    // A alternativa estável: pegar as InputAction específicas do map "UI" do JOGADOR
    // que pausou (cópia privada, com os devices pareados dele) e enfiar diretamente
    // nos slots de InputActionReference do módulo. Isso pula o PlayerInput inteiro
    // e força o módulo a ler exatamente das actions que a gente acabou de habilitar.
    // ─────────────────────────────────────────────

    /// <summary>
    /// Estado do módulo antes da pausa, pra restaurarmos certinho no resume.
    /// </summary>
    private struct UIModuleState
    {
        public InputActionReference move;
        public InputActionReference submit;
        public InputActionReference cancel;
        public InputActionReference point;
        public InputActionReference leftClick;
        public InputActionReference rightClick;
        public InputActionReference middleClick;
        public InputActionReference scrollWheel;
        public bool valid;
    }
    private UIModuleState _previousModuleState;

    /// <summary>
    /// Aponta os slots do InputSystemUIInputModule para as InputActions específicas
    /// do PlayerInput "owner", no map de UI dele. Como o PlayerInput dá uma cópia
    /// privada das actions com os devices pareados a esse jogador, o módulo passa
    /// a ler input EXCLUSIVAMENTE desse jogador.
    /// </summary>
    private void AssignUIModuleTo(PlayerInput owner)
    {
        if (uiInputModule == null || owner == null || owner.actions == null) return;

        InputActionMap uiMap = owner.actions.FindActionMap(uiActionMapName);
        if (uiMap == null)
        {
            Debug.LogWarning($"[PauseManager] '{owner.gameObject.name}' não tem map '{uiActionMapName}'. " +
                             "Não consigo configurar o EventSystem.");
            return;
        }

        // Salva o que estava lá antes, pra restaurar no resume.
        _previousModuleState = new UIModuleState
        {
            move = uiInputModule.move,
            submit = uiInputModule.submit,
            cancel = uiInputModule.cancel,
            point = uiInputModule.point,
            leftClick = uiInputModule.leftClick,
            rightClick = uiInputModule.rightClick,
            middleClick = uiInputModule.middleClick,
            scrollWheel = uiInputModule.scrollWheel,
            valid = true
        };

        // Habilita o map de UI ANTES de plugar as referências — actions desabilitadas
        // não disparam eventos pro módulo, mesmo que o módulo tenha referência delas.
        uiMap.Enable();

        // Plugin direto. Cada slot do módulo aceita uma InputActionReference que pode
        // ser criada com InputActionReference.Create(InputAction).
        uiInputModule.move = ToRef(uiMap.FindAction("Navigate"));
        uiInputModule.submit = ToRef(uiMap.FindAction("Submit"));
        uiInputModule.cancel = ToRef(uiMap.FindAction("Cancel"));
        uiInputModule.point = ToRef(uiMap.FindAction("Point"));      // pode ser null se não existir
        uiInputModule.leftClick = ToRef(uiMap.FindAction("Click"));      // ou "LeftClick" — adapte ao seu asset
        uiInputModule.scrollWheel = ToRef(uiMap.FindAction("ScrollWheel"));
        // rightClick e middleClick deixados como estavam — geralmente não usados em menu de gamepad.

        Debug.Log($"[PauseManager] UI module agora lendo de '{owner.gameObject.name}' " +
                  $"(Submit existe? {uiMap.FindAction("Submit") != null}, " +
                  $"Cancel existe? {uiMap.FindAction("Cancel") != null}, " +
                  $"Navigate existe? {uiMap.FindAction("Navigate") != null}).");
    }

    /// <summary>
    /// Restaura o estado anterior do módulo e desabilita o map de UI do owner.
    /// </summary>
    private void ClearUIModuleFrom(PlayerInput owner)
    {
        if (uiInputModule == null) return;

        // Restaura referências antigas (geralmente vão estar vazias se ninguém
        // configurou o módulo no inspector — o que está OK).
        if (_previousModuleState.valid)
        {
            uiInputModule.move = _previousModuleState.move;
            uiInputModule.submit = _previousModuleState.submit;
            uiInputModule.cancel = _previousModuleState.cancel;
            uiInputModule.point = _previousModuleState.point;
            uiInputModule.leftClick = _previousModuleState.leftClick;
            uiInputModule.rightClick = _previousModuleState.rightClick;
            uiInputModule.middleClick = _previousModuleState.middleClick;
            uiInputModule.scrollWheel = _previousModuleState.scrollWheel;
            _previousModuleState = default;
        }

        // O map de UI será desabilitado naturalmente pelo SwitchToGameplayMap,
        // que vem em seguida. Não precisamos chamar Disable() aqui — fazê-lo
        // ANTES do switch reproduziria o bug "callback órfão" que já temos
        // proteção contra na action de Pause, mas não em outras actions (ex:
        // Submit). Deixa o switch cuidar disso.
    }

    private static InputActionReference ToRef(InputAction action)
    {
        return action != null ? InputActionReference.Create(action) : null;
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

    public void OnExitButton()
    {
        if (quitConfirmPanel == null)
        {
            Debug.LogWarning("[PauseManager] quitConfirmPanel não está atribuído. " +
                             "Crie um painel de confirmação e arraste para o campo no Inspector.");
            return;
        }

        UIPanelStack.PushOverlay(quitConfirmPanel, firstSelectedOnQuitConfirm);
    }

    public void OnCancelQuit()
    {
        // Pop remove o quitConfirmPanel e restaura interatividade do pausePanel
        // automaticamente (PushOverlay + Pop se cancelam).
        UIPanelStack.Pop();
    }

    /// <summary>
    /// Confirma a saída do jogo. Em editor, para o play mode; em build, fecha o app.
    /// </summary>
    public void OnConfirmQuit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
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