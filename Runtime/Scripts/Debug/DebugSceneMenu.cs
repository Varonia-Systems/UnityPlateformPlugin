using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
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

    // ─── Game trigger (hold-to-arm + self-timer) ───────────────────────────────
    // F2/F3 : maintenir 2 s → arme un retardateur de 5 s → lance la partie.
    // Relâcher pendant l'armement annule ; ré-appuyer pendant le décompte annule.

    private const float ArmHoldDuration  = 2f;    // durée de maintien pour armer le mode différé
    private const float CountdownDuration = 5f;    // décompte avant lancement
    private const float ArmShowDelay     = 0.35f;  // au-delà : on affiche l'overlay (un tap rapide ne clignote pas)

    private enum TriggerPhase { Idle, Arming, Countdown }
    private TriggerPhase _triggerPhase    = TriggerPhase.Idle;
    private bool         _pendingIsF3     = false; // true = F3 (W/ TUTO), false = F2 (NO TUTO)
    private float        _holdTimer       = 0f;    // monte vers ArmHoldDuration
    private float        _countdownTimer  = 0f;    // descend depuis CountdownDuration

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
        int count = SceneManager.sceneCountInBuildSettings;
        for (int i = 0; i < count; i++)
        {
            string path = SceneUtility.GetScenePathByBuildIndex(i);
            _sceneNames.Add(System.IO.Path.GetFileNameWithoutExtension(path));
        }
    }

    private void OnDestroy() { CleanTextures(); }

    private void Update()
    {
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame)
            _menuVisible = !_menuVisible;
#else
        if (Input.GetKeyDown(KeyCode.F1)) _menuVisible = !_menuVisible;
#endif
        // Le retardateur tourne en continu : une fois armé, le décompte se poursuit
        // même si le menu est refermé. Seul le *démarrage* (Idle→Arming) exige le menu.
        UpdateGameTrigger();

        if (!_menuVisible) return;

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
        var settings = VaroniaRuntimeSettings.Load();
        string targetObjectName = settings != null ? settings.debugMenuTargetObjectName : null;
        string targetMethodName = settings != null ? settings.debugMenuTargetMethodName : null;

        if (!string.IsNullOrEmpty(targetObjectName) && !string.IsNullOrEmpty(targetMethodName))
        {
            GameObject target = GameObject.Find(targetObjectName);
            if (target != null) target.SendMessage(targetMethodName, _sceneNames[buildIndex], SendMessageOptions.DontRequireReceiver);
            _menuVisible = false;
        }
        else
        {
            _menuVisible = false;
            SceneManager.LoadScene(buildIndex);
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
    }

    private void DrawMenu(float scale)
    {
        int   startIdx        = _currentPage * scenesPerPage;
        int   scenesInThisPage = Mathf.Min(scenesPerPage, _sceneNames.Count - startIdx);
        int   totalPages      = Mathf.Max(1, Mathf.CeilToInt((float)_sceneNames.Count / scenesPerPage));

        // ── Dimensions ──
        float rowH    = 22f * scale;
        float headerH = 40f * scale;
        float divH    = 1f * scale;
        float secH    = 20f * scale;
        float padBot  = 14f * scale;
        float padTop  = 12f * scale;

        float scenesBlockH = headerH + (scenesInThisPage * rowH) + 25f * scale; 
        float triggersBlockH = divH + 30f * scale + 25f * scale; 
        float shortcutsH = 0f;
        foreach (var sec in Sections) shortcutsH += divH + 6f * scale + secH + (sec.entries.Length * rowH);

        float totalH = padTop + scenesBlockH + triggersBlockH + shortcutsH + padBot;
        float sWidth = width * scale;

        Rect panel = new Rect((Screen.width - sWidth) * 0.5f, (Screen.height - totalH) * 0.5f, sWidth, totalH);

        GUI.DrawTexture(panel, _bgTex);
        GUI.DrawTexture(new Rect(panel.x, panel.y, 3f * scale, panel.height), _accentTex);

        float lx = panel.x + 15f * scale;
        float lw = sWidth - 30f * scale;
        float y  = panel.y + padTop;

        // ── 1. SCENE SELECTION ──
        GUI.Label(new Rect(lx, y, lw, 20f * scale), "SCENE SELECTION", _titleStyle);
        y += 25f * scale;

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

        // ── 2. GAME TRIGGERS (TUTORIAL) ──
        GUI.DrawTexture(new Rect(panel.x + 8f * scale, y, sWidth - 16f * scale, divH), _divTex);
        y += 8f * scale;
        GUI.Label(new Rect(lx, y, lw, 20f * scale), "GAME TRIGGERS", _sectionStyle);
        y += 20f * scale;

        float halfW = lw / 2f;
        GUI.Label(new Rect(lx,         y, halfW, 22f * scale), "F2: START (NO TUTO)", _f2Style);
        GUI.Label(new Rect(lx + halfW, y, halfW, 22f * scale), "F3: START (W/ TUTO)",  _f3Style);
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

    // ─── Overlay retardateur ────────────────────────────────────────────────────

    private void DrawTriggerOverlay(float scale)
    {
        float sw = Screen.width, sh = Screen.height;
        bool  counting = _triggerPhase == TriggerPhase.Countdown;

        Color modeCol = _pendingIsF3 ? ColBad : ColAccent;
        string modeTxt = _pendingIsF3 ? "START (W/ TUTO)" : "START (NO TUTO)";

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
                           : (armed ? "Relâchez pour lancer le compte à rebours (5 s)"
                                    : "Maintenez 2 s pour différer • relâchez = départ immédiat"),
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