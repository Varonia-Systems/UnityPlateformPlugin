using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
#if XR_MANAGEMENT
using UnityEngine.XR.Management;
#endif
using VaroniaBackOffice;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Debug Menu: Scene selection (Numeric keys) + Shortcuts cheatsheet.
/// F1: Toggle this menu.
/// </summary>
public class DebugSceneMenu : MonoBehaviour
{
    [Header("Display")]
    [SerializeField] private float width = 460f;
    [SerializeField] private int scenesPerPage = 10;

    /// <summary>Facteur d'échelle manuel (1 = 1080p).</summary>
    [Header("UI Scale")]
    public float scaleFactor = 1f;

    // ─── Colors ─────────────────────────────────────────────────────────────

    static readonly Color ColBg     = new Color(0.11f, 0.11f, 0.14f, 0.98f);
    static readonly Color ColAccent = new Color(1.00f, 0.75f, 0.00f, 1f);
    static readonly Color ColGood   = new Color(0.30f, 0.85f, 0.65f, 1f);
    static readonly Color ColBad    = new Color(1.00f, 0.40f, 0.40f, 1f);
    static readonly Color ColMuted  = new Color(0.55f, 0.55f, 0.62f, 1f);
    static readonly Color ColValue  = new Color(0.92f, 0.92f, 0.95f, 1f);
    static readonly Color ColDiv    = new Color(1f,    1f,    1f,    0.06f);

    // ─── Cheatsheet ───────────────────────────────────────────────────────────

    struct ShortcutEntry
    {
        public string key;
        public string desc;
        public Color  keyColor;
        public ShortcutEntry(string k, string d, Color c) { key = k; desc = d; keyColor = c; }
    }

    struct Section
    {
        public string          title;
        public Color           titleColor;
        public ShortcutEntry[] entries;
        public Section(string t, Color c, ShortcutEntry[] e) { title = t; titleColor = c; entries = e; }
    }

    static readonly Section[] Sections = new Section[]
    {
        new Section("SYSTEM", new Color(1f, 0.75f, 0f, 1f), new ShortcutEntry[]
        {
            new ShortcutEntry("F8",  "Cycle Overlays (0→1→2→0)",   new Color(0.92f, 0.92f, 0.95f, 1f)),
            new ShortcutEntry("F9",  "Toggle VR Overlay",          new Color(0.30f, 0.85f, 0.65f, 1f)),
            new ShortcutEntry("F10", "Cycle Debug: OFF/DEBUG/SUPER", new Color(0.30f, 0.85f, 0.65f, 1f)),
        })
    };

    // ─── State ────────────────────────────────────────────────────────────────

    private bool         _menuVisible = false;
    private List<string> _sceneNames  = new List<string>();
    private int          _currentPage = 0;

    // Advanced Change Scene (tableaux personnalisés avec vignettes, cf. Project Settings).
    private VaroniaRuntimeSettings _settings;
    private AdvancedSceneBoard      _activeBoard;
    // Vue courante en mode avancé, cyclée par TAB : 0..BoardCount-1 = board, BoardCount = liste classique.
    private int                     _advViewIndex;

    // ─── Game trigger (hold-to-arm + self-timer) ───────────────────────────────
    // F2/F3 : maintenir 2 s → arme un retardateur de 5 s → lance la partie.
    // Relâcher pendant l'armement annule ; ré-appuyer pendant le décompte annule.

    private const float ArmHoldDuration  = 2f;    // durée de maintien pour armer le mode différé
    private const float CountdownDuration = 8f;    // décompte avant lancement
    private const float ArmShowDelay     = 0.35f;  // au-delà : on affiche l'overlay (un tap rapide ne clignote pas)

    private enum TriggerPhase { Idle, Arming, Countdown }
    private TriggerPhase _triggerPhase    = TriggerPhase.Idle;
    private bool         _pendingIsF3     = false; // true = F3 (W/ TUTO), false = F2 (NO TUTO)
    private float        _holdTimer       = 0f;    // monte vers ArmHoldDuration
    private float        _countdownTimer  = 0f;    // descend depuis CountdownDuration

    // ─── VR toggle (F11) ────────────────────────────────────────────────────────
    // Maintenir F11 ~2 s (avec retour visuel) → active/désactive la VR (et inversement).

    private const float VrToggleHoldDuration = 2f;    // durée de maintien pour basculer la VR
    private const float VrFlashDuration      = 1.3f;  // durée d'affichage du résultat après bascule

    private float _vrHoldTimer;            // monte vers VrToggleHoldDuration tant que F11 est tenue
    private bool  _vrToggleConsumed;       // évite de re-basculer en boucle sur un même maintien
    private bool  _vrDisabled;             // état courant (false = VR active au démarrage)
    private float _vrFlashTimer;           // > 0 : on affiche encore le résultat de la dernière bascule

    // ─── Styles ───────────────────────────────────────────────────────────────

    private bool      _stylesBuilt;
    private float     _lastScale = 1f;
    private GUIStyle  _titleStyle;
    private GUIStyle  _sectionStyle;
    private GUIStyle  _labelStyle;
    private GUIStyle  _pillStyle;
    private GUIStyle  _keyStyle;
    private GUIStyle  _descStyle;
    private GUIStyle  _footerStyle;
    private GUIStyle  _f2Style;
    private GUIStyle  _f3Style;
    private GUIStyle  _overlayBigStyle;
    private GUIStyle  _overlayTitleStyle;
    private GUIStyle  _overlaySubStyle;
    private Texture2D _whiteTex; // texture neutre teintée via GUI.color (dim + barres)
    private Texture2D _bgTex;
    private Texture2D _accentTex;
    private Texture2D _activePillTex;
    private Texture2D _divTex;
    private Texture2D _rowHoverTex; // highlight au survol d'une ligne cliquable

    // ─────────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        _settings = VaroniaRuntimeSettings.Load();

        int count = SceneManager.sceneCountInBuildSettings;
        for (int i = 0; i < count; i++)
        {
            string path = SceneUtility.GetScenePathByBuildIndex(i);
            _sceneNames.Add(System.IO.Path.GetFileNameWithoutExtension(path));
        }
    }

    private bool AdvancedActive =>
        _settings != null && _settings.advancedChangeScene &&
        _settings.sceneBoards != null && _settings.sceneBoards.Count > 0;

    private int BoardCount =>
        (_settings != null && _settings.sceneBoards != null) ? _settings.sceneBoards.Count : 0;

    /// <summary>Vrai si la vue courante affiche un board (sinon : liste classique, ou mode avancé off).</summary>
    private bool ShowingAdvancedBoard => AdvancedActive && _advViewIndex < BoardCount;

    /// <summary>Index du board correspondant à la valeur du champ sélecteur GameConfig (0 par défaut).</summary>
    private int ResolveActiveBoardIndex()
    {
        if (_settings == null || _settings.sceneBoards == null || _settings.sceneBoards.Count == 0) return 0;

        int sel = int.MinValue;
        if (!string.IsNullOrEmpty(_settings.advancedSelectorField) && BackOfficeVaronia.Instance != null)
            sel = BackOfficeVaronia.Instance.GetGameConfigField<int>(_settings.advancedSelectorField, int.MinValue);

        for (int i = 0; i < _settings.sceneBoards.Count; i++)
            if (_settings.sceneBoards[i] != null && _settings.sceneBoards[i].matchValue == sel) return i;

        return 0; // aucun match (ou pas de GameConfig) → premier tableau
    }

    /// <summary>TAB (menu ouvert, mode avancé) : cycle entre les boards puis la liste classique.</summary>
    private void HandleAdvancedTab()
    {
        if (!AdvancedActive) return;
#if ENABLE_INPUT_SYSTEM
        bool tab = Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame;
#else
        bool tab = Input.GetKeyDown(KeyCode.Tab);
#endif
        if (tab) _advViewIndex = (_advViewIndex + 1) % (BoardCount + 1); // +1 = la vue classique
    }

    private void OnDestroy() { CleanTextures(); }

    private void Update()
    {
#if ENABLE_INPUT_SYSTEM
        bool toggleMenu = Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame;
#else
        bool toggleMenu = Input.GetKeyDown(KeyCode.F1);
#endif
        if (toggleMenu)
        {
            _menuVisible = !_menuVisible;
            if (_menuVisible && AdvancedActive) _advViewIndex = ResolveActiveBoardIndex(); // board du gamemode à l'ouverture
        }
        // Le retardateur tourne en continu : une fois armé, le décompte se poursuit
        // même si le menu est refermé. Seul le *démarrage* (Idle→Arming) exige le menu.
        UpdateGameTrigger();

        // F11 (maintien) : bascule la VR. Fonctionne en permanence, menu ouvert ou non.
        UpdateVrToggle();

        if (!_menuVisible) return;

        HandleAdvancedTab(); // TAB : cycle board(s) ↔ liste classique
        HandlePagination();
        HandleSelection();
    }

    private void UpdateGameTrigger()
    {
        float dt = Time.unscaledDeltaTime;

        switch (_triggerPhase)
        {
            case TriggerPhase.Idle:
                // F2/F3 fonctionnent en permanence (indépendamment du menu F1).
                if      (F2Pressed()) { _triggerPhase = TriggerPhase.Arming; _pendingIsF3 = false; _holdTimer = 0f; }
                else if (F3Pressed()) { _triggerPhase = TriggerPhase.Arming; _pendingIsF3 = true;  _holdTimer = 0f; }
                break;

            case TriggerPhase.Arming:
                // Le déclenchement se fait UNIQUEMENT au relâchement (key up).
                // Tant que la touche est tenue, on ne fait qu'accumuler le temps de maintien.
                bool stillHeld = _pendingIsF3 ? IsF3Held() : IsF2Held();
                if (stillHeld)
                {
                    _holdTimer += dt;
                    break;
                }

                // ── Key up : seul moment où l'action est déclenchée ──
                if (_holdTimer < ArmHoldDuration)
                {
                    // Appui court → départ immédiat (classique).
                    _triggerPhase = TriggerPhase.Idle;
                    BackOfficeVaronia.Instance.TriggerStartGame(_pendingIsF3);
                }
                else
                {
                    // Maintenu ≥ 2 s → mode différé : on lance le décompte et on ferme le menu F1.
                    _triggerPhase   = TriggerPhase.Countdown;
                    _countdownTimer = CountdownDuration;
                    _menuVisible    = false;
                }
                break;

            case TriggerPhase.Countdown:
                // Retardateur : tourne tout seul. Un nouvel appui F2/F3 annule.
                if (F2OrF3Pressed()) { _triggerPhase = TriggerPhase.Idle; break; }

                _countdownTimer -= dt;
                if (_countdownTimer <= 0f)
                {
                    _triggerPhase = TriggerPhase.Idle;
                    BackOfficeVaronia.Instance.TriggerStartGame(_pendingIsF3);
                }
                break;
        }
    }

    // ─── Input helpers (F2/F3) ──────────────────────────────────────────────────

    private static bool IsF2Held()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.f2Key.isPressed;
#else
        return Input.GetKey(KeyCode.F2);
#endif
    }

    private static bool IsF3Held()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.f3Key.isPressed;
#else
        return Input.GetKey(KeyCode.F3);
#endif
    }

    private static bool F2Pressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.f2Key.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.F2);
#endif
    }

    private static bool F3Pressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.f3Key.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.F3);
#endif
    }

    private static bool F2OrF3Pressed()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current == null) return false;
        return Keyboard.current.f2Key.wasPressedThisFrame || Keyboard.current.f3Key.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.F2) || Input.GetKeyDown(KeyCode.F3);
#endif
    }

    // ─── VR toggle (F11) ────────────────────────────────────────────────────────

    private void UpdateVrToggle()
    {
        float dt = Time.unscaledDeltaTime;

        if (_vrFlashTimer > 0f) _vrFlashTimer -= dt;

        if (IsF11Held())
        {
            _vrHoldTimer += dt;
            if (!_vrToggleConsumed && _vrHoldTimer >= VrToggleHoldDuration)
            {
                _vrToggleConsumed = true;       // une seule bascule par maintien
                _vrFlashTimer     = VrFlashDuration;
                ToggleVR();
            }
        }
        else
        {
            // Relâchement → on réarme pour le prochain maintien.
            _vrHoldTimer      = 0f;
            _vrToggleConsumed = false;
        }
    }

    private void ToggleVR()
    {
        _vrDisabled = !_vrDisabled;
        SetVR(!_vrDisabled);
    }

    /// <summary>
    /// Active/désactive la VR à chaud. Deux chemins selon le projet :
    ///  - XR Management (OpenXR, projets récents) : Start/StopSubsystems (réversible,
    ///    on ne dé-initialise pas le loader → caméra en mono sur l'écran quand stoppé).
    ///  - OpenVR built-in (anciens projets) : UnityEngine.XR.XRSettings.enabled.
    /// </summary>
    private static void SetVR(bool enabled)
    {
        try
        {
#if XR_MANAGEMENT
            var mgr = XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager : null;
            if (mgr == null)
            {
                Debug.LogWarning("[VBO][VRToggle] XR Manager indisponible — bascule VR ignorée.");
                return;
            }

            if (enabled) mgr.StartSubsystems();
            else         mgr.StopSubsystems();

            Debug.Log($"[VBO][VRToggle] VR {(enabled ? "activée" : "désactivée")} (XR Management).");
#elif OFFICIELOPENVR
            // OpenVR built-in (legacy VR) : on coupe/réactive le rendu stéréo.
            UnityEngine.XR.XRSettings.enabled = enabled;
            Debug.Log($"[VBO][VRToggle] VR {(enabled ? "activée" : "désactivée")} (OpenVR legacy).");
#else
            Debug.LogWarning("[VBO][VRToggle] Aucun backend VR géré (ni XR Management, ni OpenVR) — bascule indisponible.");
#endif
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[VBO][VRToggle] Échec de la bascule VR : {e.Message}");
        }
    }

    private static bool IsF11Held()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.f11Key.isPressed;
#else
        return Input.GetKey(KeyCode.F11);
#endif
    }

    private void HandlePagination()
    {
        int maxPages = Mathf.Max(1, Mathf.CeilToInt((float)_sceneNames.Count / scenesPerPage));
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current == null) return;
        if (Keyboard.current.pageUpKey.wasPressedThisFrame)   _currentPage = (_currentPage - 1 + maxPages) % maxPages;
        if (Keyboard.current.pageDownKey.wasPressedThisFrame) _currentPage = (_currentPage + 1) % maxPages;
#else
        if (Input.GetKeyDown(KeyCode.PageUp))   _currentPage = (_currentPage - 1 + maxPages) % maxPages;
        if (Input.GetKeyDown(KeyCode.PageDown)) _currentPage = (_currentPage + 1) % maxPages;
#endif
    }

    private void HandleSelection()
    {
        if (ShowingAdvancedBoard)
        {
            // Vue board : les touches numériques sélectionnent les scènes du tableau courant.
            var board = _settings.sceneBoards[_advViewIndex];
            if (board == null || board.scenes == null) return;
            int max = Mathf.Min(10, board.scenes.Count);
            for (int i = 0; i < max; i++)
                if (IsNumericDown((i + 1) % 10)) ExecuteScene(board.scenes[i].sceneName);
            return;
        }

        int startIdx = _currentPage * scenesPerPage;
        for (int i = 0; i < scenesPerPage; i++)
        {
            int sceneIdx = startIdx + i;
            if (sceneIdx >= _sceneNames.Count) break;
            if (IsNumericDown((i + 1) % 10)) ExecuteAction(sceneIdx);
        }
    }

    private void ExecuteAction(int buildIndex)
    {
        if (buildIndex < 0 || buildIndex >= _sceneNames.Count) return;
        ExecuteScene(_sceneNames[buildIndex]);
    }

    /// <summary>
    /// Lance une scène par son nom : soit via la cible SendMessage configurée
    /// (debugMenuTargetObject/Method), soit via SceneManager.LoadScene(nom).
    /// </summary>
    private void ExecuteScene(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName)) return;

        var settings = _settings != null ? _settings : VaroniaRuntimeSettings.Load();
        string targetObjectName = settings != null ? settings.debugMenuTargetObjectName : null;
        string targetMethodName = settings != null ? settings.debugMenuTargetMethodName : null;

        _menuVisible = false;

        if (!string.IsNullOrEmpty(targetObjectName) && !string.IsNullOrEmpty(targetMethodName))
        {
            GameObject target = GameObject.Find(targetObjectName);
            if (target != null)
                target.SendMessage(targetMethodName, sceneName, SendMessageOptions.DontRequireReceiver);
        }
        else
        {
            SceneManager.LoadScene(sceneName);
        }
    }

    // ─── GUI ──────────────────────────────────────────────────────────────────

    private void OnGUI()
    {
        float scale = (Screen.height / 1080f) * scaleFactor;

        if (_menuVisible)
        {
            EnsureStyles(scale);
            DrawMenu(scale);
        }

        // Overlay du mode différé, dessiné EN DERNIER → passe au-dessus du menu F1.
        // Un tap rapide (< ArmShowDelay) n'affiche rien.
        bool showOverlay = _triggerPhase == TriggerPhase.Countdown
                        || (_triggerPhase == TriggerPhase.Arming && _holdTimer >= ArmShowDelay);
        if (showOverlay)
        {
            EnsureStyles(scale);
            DrawTriggerOverlay(scale);
        }

        // Overlay VR (F11) : pendant le maintien (après le seuil) ou pendant le flash résultat.
        bool showVrOverlay = _vrFlashTimer > 0f || _vrHoldTimer >= ArmShowDelay;
        if (showVrOverlay)
        {
            EnsureStyles(scale);
            DrawVrToggleOverlay(scale);
        }
    }

    private void DrawMenu(float scale)
    {
        bool adv = ShowingAdvancedBoard;
        if (adv) _activeBoard = _settings.sceneBoards[_advViewIndex];

        int   startIdx         = _currentPage * scenesPerPage;
        int   scenesInThisPage  = adv ? 0 : Mathf.Min(scenesPerPage, _sceneNames.Count - startIdx);
        int   totalPages       = Mathf.Max(1, Mathf.CeilToInt((float)_sceneNames.Count / scenesPerPage));

        // ── Dimensions ──
        float rowH    = 22f * scale;
        float divH    = 1f * scale;
        float secH    = 20f * scale;
        float padBot  = 14f * scale;
        float padTop  = 12f * scale;

        float sWidth = width * scale;
        float lw     = sWidth - 30f * scale;

        // Grille avancée (vignettes) — paramètres.
        const int cols = 2;
        float gap   = 8f * scale;
        float tileW = (lw - (cols - 1) * gap) / cols;
        float tileH = tileW * 0.58f;
        int   nAdv  = (adv && _activeBoard != null && _activeBoard.scenes != null) ? _activeBoard.scenes.Count : 0;
        int   rowsAdv = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(nAdv, 1) / (float)cols));

        // Hauteur de la section "scènes" selon le mode.
        float sceneSectionH = adv
            ? 25f * scale + rowsAdv * tileH + (rowsAdv - 1) * gap + 10f * scale
            : 40f * scale + (scenesInThisPage * rowH) + 25f * scale;

        float triggersBlockH = divH + 30f * scale + 25f * scale;
        float shortcutsH = 0f;
        foreach (var sec in Sections) shortcutsH += divH + 6f * scale + secH + (sec.entries.Length * rowH);

        float totalH = padTop + sceneSectionH + triggersBlockH + shortcutsH + padBot;

        Rect panel = new Rect((Screen.width - sWidth) * 0.5f, (Screen.height - totalH) * 0.5f, sWidth, totalH);

        GUI.DrawTexture(panel, _bgTex);
        GUI.DrawTexture(new Rect(panel.x, panel.y, 3f * scale, panel.height), _accentTex);

        float lx = panel.x + 15f * scale;
        float y  = panel.y + padTop;

        // ── 1. SCENE SELECTION ──
        string baseTitle = adv
            ? ((_activeBoard != null && !string.IsNullOrEmpty(_activeBoard.label)) ? _activeBoard.label : "SCENE SELECTION")
            : (AdvancedActive ? "ALL SCENES" : "SCENE SELECTION");
        // En mode avancé, TAB cycle entre les boards et la liste classique → on l'indique.
        string sceneTitle = AdvancedActive ? baseTitle.ToUpper() + "   ·   TAB ⇄" : baseTitle.ToUpper();
        GUI.Label(new Rect(lx, y, lw, 20f * scale), sceneTitle, _titleStyle);
        y += 25f * scale;

        if (adv)
        {
            y = DrawAdvancedSceneGrid(scale, panel, lx, lw, y, cols, gap, tileW, tileH);
        }
        else
        {
            for (int i = 0; i < scenesInThisPage; i++)
            {
                int  sceneIdx = startIdx + i;
                bool isCurrent = (sceneIdx == SceneManager.GetActiveScene().buildIndex);

                // Hit area sur toute la ligne — clic = même action que la touche numérique.
                Rect rowRect = new Rect(panel.x + 8f * scale, y, sWidth - 16f * scale, rowH);

                // Highlight au hover (visuel uniquement)
                if (rowRect.Contains(Event.current.mousePosition))
                    GUI.DrawTexture(rowRect, _rowHoverTex);

                GUI.Label(new Rect(lx, y, lw, rowH), _sceneNames[sceneIdx].ToUpper(), _labelStyle);

                Rect badgeRect = new Rect(panel.x + sWidth - 65f * scale, y + 2f * scale, 50f * scale, rowH - 6f * scale);
                if (isCurrent)
                {
                    _pillStyle.normal.background = _activePillTex;
                    _pillStyle.normal.textColor  = ColGood;
                    GUI.Label(badgeRect, "ACTIVE", _pillStyle);
                }
                else
                {
                    _pillStyle.normal.background = null;
                    _pillStyle.normal.textColor  = ColMuted;
                    GUI.Label(badgeRect, $"[{(i + 1) % 10}]", _pillStyle);
                }

                // Bouton invisible posé par-dessus toute la ligne — déclenche le load au clic.
                // GUIStyle.none = pas de rendu, mais GUI.Button capte bien l'événement.
                if (GUI.Button(rowRect, GUIContent.none, GUIStyle.none))
                    ExecuteAction(sceneIdx);

                y += rowH;
            }

            y += 4f * scale;
            GUI.Label(new Rect(lx, y, lw, 15f * scale), $"PAGE {_currentPage + 1}/{totalPages}  •  PGUP / PGDN", _footerStyle);
            y += 20f * scale;
        }

        // ── 2. GAME TRIGGERS (TUTORIAL) ──
        GUI.DrawTexture(new Rect(panel.x + 8f * scale, y, sWidth - 16f * scale, divH), _divTex);
        y += 8f * scale;
        GUI.Label(new Rect(lx, y, lw, 20f * scale), "GAME TRIGGERS", _sectionStyle);
        y += 20f * scale;

        float halfW = lw / 2f;
        GUI.Label(new Rect(lx,         y, halfW, 22f * scale), "F2: START (W/ TUTO)", _f2Style);
        GUI.Label(new Rect(lx + halfW, y, halfW, 22f * scale), "F3: START (NO TUTO)",  _f3Style);
        y += 30f * scale;

        // ── 3. SYSTEM ──
        float keyW  = 90f * scale;
        float descX = lx + keyW + 6f * scale;
        float descW = lw - keyW - 6f * scale;

        foreach (var sec in Sections)
        {
            GUI.DrawTexture(new Rect(panel.x + 8f * scale, y, sWidth - 16f * scale, divH), _divTex);
            y += divH + 6f * scale;

            _sectionStyle.normal.textColor = sec.titleColor;
            GUI.Label(new Rect(lx, y, lw, secH), sec.title, _sectionStyle);
            y += secH;

            foreach (var e in sec.entries)
            {
                _keyStyle.normal.textColor = e.keyColor;
                GUI.Label(new Rect(lx,    y, keyW,  rowH), e.key,  _keyStyle);
                GUI.Label(new Rect(descX, y, descW, rowH), e.desc, _descStyle);
                y += rowH;
            }
        }
    }

    // ─── Advanced Change Scene (grille de vignettes intégrée au menu) ────────────

    /// <summary>Dessine la grille de vignettes à la place de la liste classique et renvoie le nouveau Y.</summary>
    private float DrawAdvancedSceneGrid(float scale, Rect panel, float lx, float lw, float y,
                                        int cols, float gap, float tileW, float tileH)
    {
        var board = _activeBoard;
        int n = (board != null && board.scenes != null) ? board.scenes.Count : 0;

        if (n == 0)
        {
            GUI.Label(new Rect(lx, y, lw, 22f * scale), "Aucune scène dans ce tableau.", _labelStyle);
            return y + 28f * scale;
        }

        float startY = y;
        for (int i = 0; i < n; i++)
        {
            var entry = board.scenes[i];
            if (entry == null) continue;

            int   col = i % cols, row = i / cols;
            float tx  = lx + col * (tileW + gap);
            float ty  = startY + row * (tileH + gap);
            Rect  tile = new Rect(tx, ty, tileW, tileH);

            if (tile.Contains(Event.current.mousePosition))
                GUI.DrawTexture(tile, _rowHoverTex);

            // Vignette (laisse la place en bas pour nom + description).
            float textH   = 36f * scale;
            float imgH    = tileH - textH;
            Rect  imgRect = new Rect(tx + 3f * scale, ty + 3f * scale, tileW - 6f * scale, imgH - 6f * scale);
            if (entry.image != null) GUI.DrawTexture(imgRect, entry.image, ScaleMode.ScaleAndCrop);
            else                     GUI.DrawTexture(imgRect, _divTex);

            // Nom + index (raccourci numérique).
            string label = i < 10 ? $"[{(i + 1) % 10}] {entry.sceneName}" : entry.sceneName;
            GUI.Label(new Rect(tx + 5f * scale, ty + imgH, tileW - 10f * scale, 18f * scale), label, _labelStyle);

            // Description (optionnelle) sous le nom.
            if (!string.IsNullOrEmpty(entry.description))
                GUI.Label(new Rect(tx + 5f * scale, ty + imgH + 17f * scale, tileW - 10f * scale, 16f * scale),
                          entry.description, _descStyle);

            if (GUI.Button(tile, GUIContent.none, GUIStyle.none))
                ExecuteScene(entry.sceneName);
        }

        int rows = Mathf.CeilToInt(n / (float)cols);
        return startY + rows * tileH + (rows - 1) * gap + 10f * scale;
    }

    // ─── Overlay retardateur ────────────────────────────────────────────────────

    private void DrawTriggerOverlay(float scale)
    {
        float sw = Screen.width, sh = Screen.height;
        bool  counting = _triggerPhase == TriggerPhase.Countdown;

        Color modeCol = _pendingIsF3 ? ColBad : ColAccent;
        string modeTxt = _pendingIsF3 ? "START (NO TUTO)" : "START (W/ TUTO)";

        // Léger assombrissement de l'écran pendant le décompte.
        if (counting)
        {
            GUI.color = new Color(0f, 0f, 0f, 0.35f);
            GUI.DrawTexture(new Rect(0, 0, sw, sh), _whiteTex);
            GUI.color = Color.white;
        }

        // Carte centrée.
        float cw = 340f * scale, ch = 168f * scale;
        Rect card = new Rect((sw - cw) * 0.5f, (sh - ch) * 0.5f, cw, ch);
        GUI.DrawTexture(card, _bgTex);
        GUI.color = modeCol;
        GUI.DrawTexture(new Rect(card.x, card.y, 3f * scale, card.height), _whiteTex);
        GUI.color = Color.white;

        float pad = 16f * scale;
        float ix  = card.x + pad;
        float iw  = cw - pad * 2f;
        float iy  = card.y + pad;

        // Titre + mode.
        bool armed = !counting && _holdTimer >= ArmHoldDuration;

        _overlayTitleStyle.normal.textColor = modeCol;
        GUI.Label(new Rect(ix, iy, iw, 18f * scale),
                  counting ? "DÉMARRAGE DANS…"
                           : (armed ? "PRÊT — RELÂCHEZ POUR DIFFÉRER" : "MAINTENIR POUR DIFFÉRER"),
                  _overlayTitleStyle);
        GUI.Label(new Rect(ix, iy + 16f * scale, iw, 16f * scale), modeTxt, _overlaySubStyle);

        // Zone centrale.
        float midY = iy + 36f * scale;
        float midH = 66f * scale;
        float fill;
        if (counting)
        {
            int secs = Mathf.CeilToInt(_countdownTimer);
            _overlayBigStyle.normal.textColor = modeCol;
            GUI.Label(new Rect(ix, midY, iw, midH), secs.ToString(), _overlayBigStyle);
            fill = Mathf.Clamp01(_countdownTimer / CountdownDuration); // se vide
        }
        else
        {
            _overlayBigStyle.normal.textColor = modeCol;
            GUI.Label(new Rect(ix, midY, iw, midH), "●", _overlayBigStyle);
            fill = Mathf.Clamp01(_holdTimer / ArmHoldDuration); // se remplit
        }

        // Barre de progression.
        float barY = card.y + ch - pad - 18f * scale;
        float barH = 6f * scale;
        Rect barTrack = new Rect(ix, barY, iw, barH);
        GUI.color = new Color(1f, 1f, 1f, 0.10f);
        GUI.DrawTexture(barTrack, _whiteTex);
        GUI.color = modeCol;
        GUI.DrawTexture(new Rect(ix, barY, iw * fill, barH), _whiteTex);
        GUI.color = Color.white;

        // Aide.
        GUI.Label(new Rect(ix, barY + barH + 4f * scale, iw, 14f * scale),
                  counting ? "Appuyez à nouveau pour annuler"
                           : (armed ? "Relâchez pour lancer le compte à rebours (8 s)"
                                    : "Maintenez 2 s pour différer • relâchez = départ immédiat"),
                  _overlaySubStyle);
    }

    // ─── Overlay VR toggle (F11) ────────────────────────────────────────────────

    private void DrawVrToggleOverlay(float scale)
    {
        float sw = Screen.width, sh = Screen.height;

        bool   done       = _vrToggleConsumed;                 // bascule effectuée → on affiche le résultat
        bool   willDisable = !_vrDisabled;                     // pendant le maintien : action à venir
        Color  vrCol      = new Color(0.40f, 0.80f, 1f, 1f);   // accent VR (cyan)
        Color  resultCol  = _vrDisabled ? ColBad : ColGood;    // rouge = désactivée, vert = activée
        Color  accent     = done ? resultCol : vrCol;

        // Carte centrée, décalée vers le bas pour ne pas recouvrir l'overlay F2/F3.
        float cw = 340f * scale, ch = 150f * scale;
        Rect card = new Rect((sw - cw) * 0.5f, (sh - ch) * 0.5f + 120f * scale, cw, ch);
        GUI.DrawTexture(card, _bgTex);
        GUI.color = accent;
        GUI.DrawTexture(new Rect(card.x, card.y, 3f * scale, card.height), _whiteTex);
        GUI.color = Color.white;

        float pad = 16f * scale;
        float ix  = card.x + pad;
        float iw  = cw - pad * 2f;
        float iy  = card.y + pad;

        // Titre.
        _overlayTitleStyle.normal.textColor = accent;
        string title = done
            ? (_vrDisabled ? "VR DÉSACTIVÉE" : "VR ACTIVÉE")
            : (willDisable ? "DÉSACTIVER LA VR…" : "ACTIVER LA VR…");
        GUI.Label(new Rect(ix, iy, iw, 18f * scale), title, _overlayTitleStyle);
        GUI.Label(new Rect(ix, iy + 16f * scale, iw, 16f * scale), "F11", _overlaySubStyle);

        // Glyphe central.
        float midY = iy + 36f * scale;
        float midH = 66f * scale;
        _overlayBigStyle.normal.textColor = accent;
        GUI.Label(new Rect(ix, midY, iw, midH), "VR", _overlayBigStyle);

        // Barre de progression (remplie pendant le maintien, pleine une fois fait).
        float fill = done ? 1f : Mathf.Clamp01(_vrHoldTimer / VrToggleHoldDuration);
        float barY = card.y + ch - pad - 18f * scale;
        float barH = 6f * scale;
        GUI.color = new Color(1f, 1f, 1f, 0.10f);
        GUI.DrawTexture(new Rect(ix, barY, iw, barH), _whiteTex);
        GUI.color = accent;
        GUI.DrawTexture(new Rect(ix, barY, iw * fill, barH), _whiteTex);
        GUI.color = Color.white;

        // Aide.
        GUI.Label(new Rect(ix, barY + barH + 4f * scale, iw, 14f * scale),
                  done ? "Maintenez F11 pour rebasculer"
                       : "Continuez de maintenir pour basculer la VR",
                  _overlaySubStyle);
    }

    private bool IsNumericDown(int digit)
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current == null) return false;
        return digit switch {
            0 => Keyboard.current.digit0Key.wasPressedThisFrame || Keyboard.current.numpad0Key.wasPressedThisFrame,
            1 => Keyboard.current.digit1Key.wasPressedThisFrame || Keyboard.current.numpad1Key.wasPressedThisFrame,
            2 => Keyboard.current.digit2Key.wasPressedThisFrame || Keyboard.current.numpad2Key.wasPressedThisFrame,
            3 => Keyboard.current.digit3Key.wasPressedThisFrame || Keyboard.current.numpad3Key.wasPressedThisFrame,
            4 => Keyboard.current.digit4Key.wasPressedThisFrame || Keyboard.current.numpad4Key.wasPressedThisFrame,
            5 => Keyboard.current.digit5Key.wasPressedThisFrame || Keyboard.current.numpad5Key.wasPressedThisFrame,
            6 => Keyboard.current.digit6Key.wasPressedThisFrame || Keyboard.current.numpad6Key.wasPressedThisFrame,
            7 => Keyboard.current.digit7Key.wasPressedThisFrame || Keyboard.current.numpad7Key.wasPressedThisFrame,
            8 => Keyboard.current.digit8Key.wasPressedThisFrame || Keyboard.current.numpad8Key.wasPressedThisFrame,
            9 => Keyboard.current.digit9Key.wasPressedThisFrame || Keyboard.current.numpad9Key.wasPressedThisFrame,
            _ => false
        };
#else
        return Input.GetKeyDown(KeyCode.Alpha0 + digit) || Input.GetKeyDown(KeyCode.Keypad0 + digit);
#endif
    }

    private void EnsureStyles(float scale)
    {
        if (_stylesBuilt && Mathf.Approximately(scale, _lastScale)) return;
        _stylesBuilt = true;
        _lastScale   = scale;

        if (_whiteTex == null)      _whiteTex      = MakeTex(Color.white);
        if (_bgTex == null)         _bgTex         = MakeTex(ColBg);
        if (_accentTex == null)     _accentTex     = MakeTex(ColAccent);
        if (_activePillTex == null) _activePillTex = MakeTex(new Color(ColGood.r, ColGood.g, ColGood.b, 0.15f));
        if (_divTex == null)        _divTex        = MakeTex(ColDiv);
        if (_rowHoverTex == null)   _rowHoverTex   = MakeTex(new Color(ColAccent.r, ColAccent.g, ColAccent.b, 0.10f));

        _titleStyle   = new GUIStyle { fontSize = Mathf.RoundToInt(11 * scale), fontStyle = FontStyle.Bold, normal = { textColor = ColAccent } };
        _sectionStyle = new GUIStyle { fontSize = Mathf.RoundToInt(9 * scale),  fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, normal = { textColor = ColAccent } };
        _labelStyle   = new GUIStyle { fontSize = Mathf.RoundToInt(10 * scale), fontStyle = FontStyle.Bold, normal = { textColor = ColValue } };
        _pillStyle    = new GUIStyle { fontSize = Mathf.RoundToInt(8 * scale),  fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        _keyStyle     = new GUIStyle { fontSize = Mathf.RoundToInt(9 * scale),  fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, normal = { textColor = ColValue } };
        _descStyle    = new GUIStyle { fontSize = Mathf.RoundToInt(9 * scale),  fontStyle = FontStyle.Normal, alignment = TextAnchor.MiddleLeft, normal = { textColor = ColMuted } };
        _footerStyle  = new GUIStyle(_labelStyle) { fontSize = Mathf.RoundToInt(9 * scale), alignment = TextAnchor.MiddleCenter, normal = { textColor = ColMuted } };
        _f2Style      = new GUIStyle(_footerStyle) { fontSize = Mathf.RoundToInt(11 * scale), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft,  normal = { textColor = ColAccent } };
        _f3Style      = new GUIStyle(_footerStyle) { fontSize = Mathf.RoundToInt(11 * scale), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight, normal = { textColor = ColBad } };

        _overlayBigStyle   = new GUIStyle { fontSize = Mathf.RoundToInt(64 * scale), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = ColValue } };
        _overlayTitleStyle = new GUIStyle { fontSize = Mathf.RoundToInt(12 * scale), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = ColAccent } };
        _overlaySubStyle   = new GUIStyle { fontSize = Mathf.RoundToInt(10 * scale), fontStyle = FontStyle.Normal, alignment = TextAnchor.MiddleCenter, normal = { textColor = ColMuted } };
    }

    private static Texture2D MakeTex(Color col)
    {
        var t = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        t.SetPixel(0, 0, col); t.Apply();
        t.hideFlags = HideFlags.HideAndDontSave;
        return t;
    }

    private void CleanTextures()
    {
        if (_whiteTex)      Destroy(_whiteTex);
        if (_bgTex)         Destroy(_bgTex);
        if (_accentTex)     Destroy(_accentTex);
        if (_activePillTex) Destroy(_activePillTex);
        if (_divTex)        Destroy(_divTex);
        if (_rowHoverTex)   Destroy(_rowHoverTex);
    }
}