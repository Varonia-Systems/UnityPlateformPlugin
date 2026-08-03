using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using VBO_Ultimate.Runtime.Scripts.Input;
using VaroniaBackOffice;

namespace VBO_Ultimate.Runtime.Scripts.UI
{
    /// <summary>
    /// Système de notation 3D (1 à 5 étoiles) interactif via Raycast d'arme.
    /// Logique inspirée de WorldSpaceDebugUI.
    /// </summary>
    public class Rating3DUI : MonoBehaviour
    {
        public static Rating3DUI Instance { get; private set; }

        [Header("Icons (Resources Name)")]
        public string starEmptyResource = "StarEmpty"; // Nom de l'étoile "vide" dans Resources
        public string starFilledResource = "StarFilled"; // Nom de l'étoile "remplie" dans Resources
        public string backgroundResource = "Background"; // Nom du sprite de fond dans Resources
        public Font customFont; // Police personnalisée (optionnel)
        public Color emptyColor = Color.gray;
        public Color filledColor = Color.yellow;
        public Color hoverColor = Color.white;
        public Color backgroundColor = new Color(0, 0, 0, 0.5f);
        public Color textColor = Color.white;

        [Header("Settings")]
        public float followDistance = 1.5f;
        public float followSpeed = 5f;
        public Vector2 starSize = new Vector2(100f, 100f);
        public float starSpacing = 110f;
        public Vector2 padding = new Vector2(20f, 20f);
        public float textPanelHeight = 40f;
        public float textPanelMargin = 10f;
        public int fontSize = 14;

        [Header("Text Effects")]
        public bool useShadow = true;
        public Color shadowColor = new Color(0, 0, 0, 0.5f);
        public Vector2 shadowEffectDistance = new Vector2(1.5f, -1.5f);
        public float textPulseSpeed = 2f;
        public float textPulseAmount = 0.05f;

        [Header("Localization")]
        public string textFR = "Notez votre expérience";
        public string textEN = "Rate your experience";
        public string textES = "Califica tu experiencia";
        
        [Header("Thank You Message")]
        public string thankYouFR = "Merci de votre retour !";
        public string thankYouEN = "Thank you for your feedback!";
        public string thankYouES = "¡Gracias por sus comentarios!";
        public float thankYouDisplayTime = 3f;

        [Header("State")]
        public int currentRating = 0;
        public int hoveredRating = 0;
        public bool isVisible = false;
        public bool isShowingThankYou = false;
        private bool _isProcessingSelection = false;
        public static bool IsRatingDisplayed = false;
        public bool debugStart = false; // Si true, l'UI est active dès le début

        [Header("Gaze Fallback (jeux sans arme / hand tracking)")]
        [Tooltip("Si aucune VaroniaWeapon n'est disponible, la sélection se fait au regard : fixer une étoile la charge puis la valide.")]
        public bool useGazeWhenNoWeapon = true;
        [Tooltip("Durée de fixation du regard pour valider une étoile.")]
        public float gazeHoldTime = 1.2f;

        [Header("Fixed Placement (optionnel)")]
        [Tooltip("Si assigné, le panneau est ANCRÉ à ce transform (position/rotation) au lieu de suivre la caméra. " +
                 "Idéal en mode regard : le panneau vit dans le décor.")]
        public Transform fixedAnchor;
        [Tooltip("Taille du réticule (en unités du canvas) affiché là où le regard croise le panneau.")]
        public float crosshairSize = 26f;

        [Tooltip("Zone (en unités du canvas, centrée) dans laquelle le réticule du regard apparaît. " +
                 "Plus petite que le rect logique du canvas pour ne pas afficher le point trop loin du panneau.")]
        public Vector2 crosshairZone = new Vector2(300f, 160f);

        [Header("Interaction Settings")]
        public float selectionHoldTime = 0.5f; // Temps de maintien nécessaire
        public float laserActivationHitboxTolerance = 5.0f; // Hitbox encore plus large pour l'activation du laser
        public int laserPoints = 15; // Nombre de points pour le laser courbé
        public float laserArcHeight = 0.5f; // Hauteur de l'arc de cercle
        private float _currentHoldTimer = 0f;
        private int _lastInteractingStar = 0;
        private Vector3[] _laserPointsPositions;

        [Header("Audio (Optional)")]
        public AudioClip spawnSound;
        [Range(0f, 1f)] public float spawnVolume = 1f;
        public AudioClip hoverSound;
        [Range(0f, 1f)] public float hoverVolume = 1f;
        public AudioClip chargeSound;
        [Range(0f, 1f)] public float chargeVolume = 1f;
        public float chargePitchStart = 1f;
        public float chargePitchEnd = 1.5f;
        public AudioClip successSound;
        [Range(0f, 1f)] public float successVolume = 1f;
        public AudioClip cancelSound;
        [Range(0f, 1f)] public float cancelVolume = 1f;
        private AudioSource _audioSource;
        private AudioSource _chargeAudioSource;

        [Header("Line Renderer")]
        public bool useLineRenderer = true;
        public float lineWidth = 0.02f; // Plus gros par défaut
        public float hitboxTolerance = 1.5f; // Facteur d'agrandissement de la hitbox
        public float laserLagSpeed = 15f; // Vitesse de suivi du laser (effet lag)
        public float laserFadeSpeed = 10f; // Vitesse de fade in/out du laser
        public bool alwaysOnTop = true; // Si true, le canvas s'affiche toujours au-dessus de la géométrie 3D (ZTest Always).
        public Color lineColorStart = new Color(1f, 1f, 0f, 0.1f); // Presque transparent au début
        public Color lineColorEnd = new Color(1f, 1f, 0f, 1f); // Opaque à la fin

        private GameObject _canvasRoot;
        private Canvas _canvas;
        private CanvasGroup _mainCanvasGroup;
        private CanvasGroup _thankYouCanvasGroup;
        private RectTransform _starsContainer;
        private RectTransform _thankYouContainer;
        private Text _thankYouText;
        private RectTransform _textContainer;
        private Text _instructionText;
        private List<Image> _starImages = new List<Image>();
        private List<RectTransform> _starRects = new List<RectTransform>();
        private LineRenderer _lineRenderer;
        private Vector3 _smoothHitPoint;
        private float _currentLaserAlpha = 0f;
        private Material _alwaysOnTopMat;

        private Transform _targetCamera;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            _audioSource = gameObject.AddComponent<AudioSource>();
            _audioSource.playOnAwake = false;

            _chargeAudioSource = gameObject.AddComponent<AudioSource>();
            _chargeAudioSource.playOnAwake = false;
            _chargeAudioSource.loop = true;
        }

        private void Start()
        {
            if (Camera.main != null)
                _targetCamera = Camera.main.transform;

            BuildRatingUI();
            
            if (debugStart)
            {
                StartCoroutine(DelayedStart());
            }
            else
            {
                // État initial : caché INSTANTANÉMENT — pas de fade au boot.
                // (isVisible peut arriver sérialisé à true depuis le prefab : passer par
                //  ShowRating(false) jouerait alors le FadeOutAndHide pendant que
                //  FollowCamera affiche l'UI devant la caméra → flash au lancement.)
                isVisible = false;
                IsRatingDisplayed = false;
                if (_canvasRoot != null) _canvasRoot.SetActive(false);
                if (_lineRenderer != null) _lineRenderer.enabled = false;
            }
        }

        private IEnumerator DelayedStart()
        {
            ShowRating(false);
            yield return new WaitForSeconds(1.0f);
            ShowRating(true);
        }

        private void Update()
        {
            if (!isVisible) return;

            FollowCamera();
            AnimateTextPulse();
            if (!isShowingThankYou)
            {
                HandleWeaponInteraction();
                UpdateStarsVisuals();
            }
        }

        private void AnimateTextPulse()
        {
            float pulse = 1f + Mathf.Sin(Time.time * textPulseSpeed) * textPulseAmount;
            Vector3 scale = new Vector3(pulse, pulse, pulse);
            
            if (_instructionText != null && _instructionText.gameObject.activeInHierarchy)
            {
                _instructionText.transform.localScale = scale;
            }
            
            if (_thankYouText != null && _thankYouText.gameObject.activeInHierarchy)
            {
                _thankYouText.transform.localScale = scale;
            }
        }

        private Sprite _starEmptySprite;
        private Sprite _starFilledSprite;
        private Sprite _backgroundSprite;

        private void BuildRatingUI()
        {
            // Charger les ressources
            _starEmptySprite = Resources.Load<Sprite>("UI/"+starEmptyResource);
            _starFilledSprite = Resources.Load<Sprite>("UI/"+starFilledResource);
            _backgroundSprite = Resources.Load<Sprite>("UI/"+backgroundResource);

            // Canvas Root
            _canvasRoot = new GameObject("Rating3D_Canvas");
            _canvasRoot.transform.SetParent(transform);
            
            _canvas = _canvasRoot.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            CanvasScaler scaler = _canvasRoot.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 5f;
            _canvasRoot.AddComponent<GraphicRaycaster>();

            RectTransform canvasRect = _canvasRoot.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(600, 300);
            _canvasRoot.transform.localScale = Vector3.zero; // Partir de 0 pour l'effet de spawn

            // Container principal pour les éléments de notation (pour pouvoir faire un fade out groupé)
            GameObject mainPanel = new GameObject("MainPanel");
            mainPanel.transform.SetParent(_canvasRoot.transform, false);
            RectTransform mainRect = mainPanel.AddComponent<RectTransform>();
            mainRect.anchorMin = Vector2.zero;
            mainRect.anchorMax = Vector2.one;
            mainRect.sizeDelta = Vector2.zero;
            _mainCanvasGroup = mainPanel.AddComponent<CanvasGroup>();

            // Container pour le texte (au-dessus)
            GameObject textPanel = new GameObject("TextPanel");
            textPanel.transform.SetParent(mainPanel.transform, false);
            _textContainer = textPanel.AddComponent<RectTransform>();

            float containerWidth = (starSpacing * 4) + starSize.x + (padding.x * 2);
            _textContainer.sizeDelta = new Vector2(containerWidth, textPanelHeight);
            
            // Calcul de la position y (au dessus des étoiles qui sont à y=0)
            // Les étoiles ont une hauteur de 40. Elles sont centrées verticalement par rapport à leur conteneur.
            // On place le texte au dessus.
            _textContainer.anchoredPosition = new Vector2(0, (40f / 2f) + (textPanelHeight / 2f) + textPanelMargin);

            Image textBgImg = textPanel.AddComponent<Image>();
            textBgImg.sprite = _backgroundSprite;
            textBgImg.type = Image.Type.Sliced;
            textBgImg.color = backgroundColor;

            GameObject textObj = new GameObject("InstructionText");
            textObj.transform.SetParent(_textContainer.transform, false);
            _instructionText = textObj.AddComponent<Text>();
            
            // Gestion de la police
            if (customFont != null)
            {
                _instructionText.font = customFont;
            }
            else
            {
                // Gestion de la police par défaut (Arial n'est plus disponible sur les versions récentes de Unity)
#if UNITY_6000_0_OR_NEWER
                _instructionText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
#else
                _instructionText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
#endif
            }

            _instructionText.fontSize = fontSize;
            _instructionText.fontStyle = FontStyle.Bold;
            _instructionText.alignment = TextAnchor.MiddleCenter;
            _instructionText.color = textColor;
            _instructionText.text = GetLocalizedText();
            
            // Effets de texte (Shadow)
            if (useShadow)
            {
                Shadow shadow = textObj.AddComponent<Shadow>();
                shadow.effectColor = shadowColor;
                shadow.effectDistance = shadowEffectDistance;
            }

            if (alwaysOnTop) ApplyAlwaysOnTop();

            RectTransform textRect = textObj.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;

            // Container pour les étoiles (le rectangle de fond)
            GameObject container = new GameObject("StarsContainer");
            container.transform.SetParent(mainPanel.transform, false);
            _starsContainer = container.AddComponent<RectTransform>();
            
            float width = containerWidth;
            float height = 40f; 
            _starsContainer.sizeDelta = new Vector2(width, height);

            Image bgImg = container.AddComponent<Image>();
            bgImg.sprite = _backgroundSprite;
            bgImg.type = Image.Type.Sliced; // Important pour les bords arrondis (9-slicing)
            bgImg.color = backgroundColor;

            // Création des 5 étoiles
            for (int i = 1; i <= 5; i++)
            {
                GameObject starObj = new GameObject("Star_" + i);
                starObj.transform.SetParent(_starsContainer, false);
                
                RectTransform starRect = starObj.AddComponent<RectTransform>();
                starRect.sizeDelta = starSize;
                
                // Centrage : (i - 3) donne -2, -1, 0, 1, 2
                starRect.anchoredPosition = new Vector2((i - 3) * starSpacing, 0);
                
                Image img = starObj.AddComponent<Image>();
                img.sprite = _starEmptySprite;
                img.color = emptyColor;

                _starImages.Add(img);
                _starRects.Add(starRect);
            }

            // Line Renderer
            GameObject lrObj = new GameObject("RatingLine");
            lrObj.transform.SetParent(transform);
            _lineRenderer = lrObj.AddComponent<LineRenderer>();
            _lineRenderer.startWidth = lineWidth;
            _lineRenderer.endWidth = 0f; // Pointe du laser (0 pour être pointu)
            
            // Utiliser un shader qui supporte le vertex color/alpha
            _lineRenderer.material = new Material(Shader.Find("Sprites/Default"));
            
            // Initialiser le dégradé avec alpha 0
            UpdateLaserGradient(0f);
            
            _lineRenderer.positionCount = laserPoints;
            _laserPointsPositions = new Vector3[laserPoints];
            _lineRenderer.enabled = false;

            // Panneau de remerciement (caché par défaut)
            GameObject thankYouPanel = new GameObject("ThankYouPanel");
            thankYouPanel.transform.SetParent(_canvasRoot.transform, false);
            _thankYouContainer = thankYouPanel.AddComponent<RectTransform>();
            _thankYouContainer.sizeDelta = new Vector2(containerWidth, 80f);
            _thankYouContainer.anchoredPosition = Vector2.zero;
            
            _thankYouCanvasGroup = thankYouPanel.AddComponent<CanvasGroup>();
            _thankYouCanvasGroup.alpha = 0f;

            Image tyBgImg = thankYouPanel.AddComponent<Image>();
            tyBgImg.sprite = _backgroundSprite;
            tyBgImg.type = Image.Type.Sliced;
            tyBgImg.color = backgroundColor;

            GameObject tyTextObj = new GameObject("ThankYouText");
            tyTextObj.transform.SetParent(_thankYouContainer.transform, false);
            _thankYouText = tyTextObj.AddComponent<Text>();

            if (customFont != null)
            {
                _thankYouText.font = customFont;
            }
            else
            {
#if UNITY_6000_0_OR_NEWER
                _thankYouText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
#else
                _thankYouText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
#endif
            }

            _thankYouText.fontSize = fontSize + 4;
            _thankYouText.fontStyle = FontStyle.Bold;
            _thankYouText.alignment = TextAnchor.MiddleCenter;
            _thankYouText.color = textColor;

            // Effets de texte pour le message de remerciement
            if (useShadow)
            {
                Shadow shadow = tyTextObj.AddComponent<Shadow>();
                shadow.effectColor = shadowColor;
                shadow.effectDistance = shadowEffectDistance;
            }
            
            RectTransform tyTextRect = tyTextObj.GetComponent<RectTransform>();
            tyTextRect.anchorMin = Vector2.zero;
            tyTextRect.anchorMax = Vector2.one;
            tyTextRect.sizeDelta = Vector2.zero;

            thankYouPanel.SetActive(false);

            // Réticule du mode regard : apparaît là où le regard croise le panneau,
            // pour que le joueur comprenne immédiatement où il vise.
            GameObject chObj = new GameObject("GazeCrosshair");
            chObj.transform.SetParent(_canvasRoot.transform, false);
            _crosshairRect = chObj.AddComponent<RectTransform>();
            _crosshairRect.sizeDelta = new Vector2(crosshairSize, crosshairSize);
            _crosshair = chObj.AddComponent<Image>();
            _crosshair.sprite = GetSoftDotSprite(); // point net + halo en fondu, généré procéduralement
            _crosshair.color = new Color(1f, 1f, 1f, 0.85f);
            _crosshair.raycastTarget = false;
            chObj.transform.SetAsLastSibling(); // toujours dessiné au-dessus des étoiles
            chObj.SetActive(false);
        }

        private void UpdateLaserGradient(float alpha)
        {
            if (_lineRenderer == null) return;
            
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new GradientColorKey[] { new GradientColorKey(lineColorStart, 0.0f), new GradientColorKey(lineColorEnd, 1.0f) },
                new GradientAlphaKey[] { 
                    new GradientAlphaKey(lineColorStart.a * alpha, 0.0f), 
                    new GradientAlphaKey(lineColorEnd.a * alpha, 1.0f) 
                }
            );
            _lineRenderer.colorGradient = gradient;
        }

        private void FollowCamera()
        {
            if (_targetCamera == null)
            {
                if (Camera.main != null) _targetCamera = Camera.main.transform;
                else return;
            }

            // Mode ancré : le panneau vit à un endroit FIXE du décor (position/rotation de l'ancre).
            if (fixedAnchor != null)
            {
                _canvasRoot.transform.SetPositionAndRotation(fixedAnchor.position, fixedAnchor.rotation);
                float anchoredScale = 1f / 65f;
                _canvasRoot.transform.localScale = Vector3.Lerp(_canvasRoot.transform.localScale,
                    Vector3.one * anchoredScale, Time.deltaTime * 10f);
                return;
            }

            // Positionnement devant la caméra.
            // En mode regard : on FIGE le panneau tant qu'une étoile est visée — sinon le
            // recentrage ferait fuir l'étoile que le joueur essaie de fixer (dwell impossible).
            bool freezeForGaze = _gazeMode && hoveredRating > 0 && !isShowingThankYou;
            if (!freezeForGaze)
            {
                Vector3 targetPos = _targetCamera.position + _targetCamera.forward * followDistance;
                _canvasRoot.transform.position = Vector3.Lerp(_canvasRoot.transform.position, targetPos, Time.deltaTime * followSpeed);

                // Regarder la caméra
                _canvasRoot.transform.LookAt(_canvasRoot.transform.position + (_canvasRoot.transform.position - _targetCamera.position));
            }

            // Effet de spawn (scale up)
            float targetScale = 1f / 65f;
            _canvasRoot.transform.localScale = Vector3.Lerp(_canvasRoot.transform.localScale, Vector3.one * targetScale, Time.deltaTime * 10f);
        }

        private void HandleWeaponInteraction()
        {
            if (_isProcessingSelection || isShowingThankYou)
            {
                if (_lineRenderer != null) _lineRenderer.enabled = false;
                StopChargeSound();
                return;
            }

            // Pas d'arme (jeu hand tracking ?) -> interaction au regard.
            bool hasWeapon = VaroniaWeapon.Instance != null
                          && VaroniaWeapon.Instance.currentweapons.Count > 0
                          && VaroniaWeapon.Instance.currentweapons[0] != null
                          && VaroniaWeapon.Instance.currentweapons[0].beginRaycast != null;

            if (!hasWeapon)
            {
                if (_lineRenderer != null) _lineRenderer.enabled = false;
                _gazeMode = true;
                if (useGazeWhenNoWeapon) HandleGazeInteraction();
                return;
            }
            _gazeMode = false;

            _Weapon weapon0 = VaroniaWeapon.Instance.currentweapons[0];

            Ray ray = new Ray(weapon0.beginRaycast.position, weapon0.beginRaycast.forward);
            
            // On fait un raycast manuel sur les RectTransforms des étoiles car elles sont en WorldSpace
            hoveredRating = 0;
            int potentialHover = 0;
            float closestDist = float.MaxValue;
            Vector3 hitPoint = weapon0.beginRaycast.position + weapon0.beginRaycast.forward * followDistance;

            for (int i = 0; i < _starRects.Count; i++)
            {
                // Vérification simple de collision avec le plan du canvas
                Plane p = new Plane(_canvasRoot.transform.forward, _canvasRoot.transform.position);
                if (p.Raycast(ray, out float enter))
                {
                    Vector3 worldPoint = ray.GetPoint(enter);
                    Vector3 localPoint = _starRects[i].InverseTransformPoint(worldPoint);
                    
                    Rect r = _starRects[i].rect;
                    
                    // 1. Vérification pour la SELECTION (hitbox serrée)
                    Vector2 selectionSize = r.size * hitboxTolerance;
                    Rect selectionHitbox = new Rect(r.center - selectionSize * 0.5f, selectionSize);
                    
                    if (selectionHitbox.Contains(localPoint))
                    {
                        if (enter < closestDist)
                        {
                            closestDist = enter;
                            hoveredRating = i + 1;
                            hitPoint = worldPoint;
                        }
                    }

                    // 2. Vérification pour l'APPARITION DU LASER (hitbox plus large)
                    Vector2 laserSize = r.size * laserActivationHitboxTolerance;
                    Rect laserHitbox = new Rect(r.center - laserSize * 0.5f, laserSize);
                    if (laserHitbox.Contains(localPoint))
                    {
                        potentialHover = i + 1;
                        if (hoveredRating == 0) hitPoint = worldPoint; // On pointe vers l'étoile même en tolérance large
                    }
                }
            }

            // Update Line Renderer
            if (useLineRenderer && _lineRenderer != null)
            {
                bool targetVisible = (potentialHover > 0);
                
                // Fade In/Out
                float targetAlpha = targetVisible ? 1f : 0f;
                _currentLaserAlpha = Mathf.MoveTowards(_currentLaserAlpha, targetAlpha, Time.deltaTime * laserFadeSpeed);
                
                _lineRenderer.enabled = (_currentLaserAlpha > 0.01f);
                UpdateLaserGradient(_currentLaserAlpha);

                if (_lineRenderer.enabled)
                {
                    // Effet d'arc de cercle avec courbe de Bézier
                    // Le point 0 est l'arme, le dernier point est l'impact lissé (_smoothHitPoint)
                    _smoothHitPoint = Vector3.Lerp(_smoothHitPoint, hitPoint, Time.deltaTime * laserLagSpeed);
                    
                    Vector3 startPos = weapon0.beginRaycast.position;
                    Vector3 endPos = _smoothHitPoint;
                    
                    // Calcul du point de contrôle pour l'arc
                    // On crée un arc de cercle qui se courbe dans la direction opposée au mouvement (le lag)
                    Vector3 midPoint = (startPos + endPos) * 0.5f;
                    
                    // Vecteur de décalage basé sur le lag (la différence entre la visée brute et lissée)
                    Vector3 lagVector = hitPoint - _smoothHitPoint;
                    
                    // On ajoute aussi une courbure verticale de base (laserArcHeight)
                    Vector3 upDir = weapon0.beginRaycast.up;
                    
                    // Le point de contrôle est le milieu décalé par le lag et la hauteur fixe
                    // Cela crée un arc qui "traîne" derrière le mouvement
                    Vector3 controlPoint = midPoint + lagVector + (upDir * laserArcHeight);

                    // Génération de la courbe de Bézier quadratique
                    for (int i = 0; i < laserPoints; i++)
                    {
                        float t = (float)i / (laserPoints - 1);
                        
                        // Formule de Bézier quadratique : (1-t)^2 * P0 + 2(1-t)t * P1 + t^2 * P2
                        Vector3 bezierPoint = Mathf.Pow(1 - t, 2) * startPos + 
                                              2 * (1 - t) * t * controlPoint + 
                                              Mathf.Pow(t, 2) * endPos;
                        
                        _laserPointsPositions[i] = bezierPoint;
                    }

                    _lineRenderer.SetPositions(_laserPointsPositions);

                    // Feedback visuel sur le Line Renderer : il s'épaissit pendant le chargement
                    float thicknessMultiplier = 1f + (_currentHoldTimer / selectionHoldTime) * 2f;
                    _lineRenderer.startWidth = lineWidth * thicknessMultiplier;
                    _lineRenderer.endWidth = 0f; // Toujours en pointe
                }
                else
                {
                    // Si on sort de la hitbox, on "téléporte" discrètement le point de lissage pour la prochaine fois
                    _smoothHitPoint = hitPoint;
                }
            }

            // Interaction clic avec maintien
            if (hoveredRating > 0 && VaroniaInput.GetButton(0, VaroniaButton.Primary))
            {
                if (_lastInteractingStar == hoveredRating)
                {
                    float prevTimer = _currentHoldTimer;
                    _currentHoldTimer += Time.deltaTime;
                    
                    // Gestion du son de chargement progressif
                    if (chargeSound != null && _chargeAudioSource != null)
                    {
                        if (!_chargeAudioSource.isPlaying)
                        {
                            _chargeAudioSource.clip = chargeSound;
                            _chargeAudioSource.Play();
                        }

                        float progress = Mathf.Clamp01(_currentHoldTimer / selectionHoldTime);
                        _chargeAudioSource.volume = chargeVolume * progress; // Monte avec le temps
                        _chargeAudioSource.pitch = Mathf.Lerp(chargePitchStart, chargePitchEnd, progress);
                    }

                    if (_currentHoldTimer >= selectionHoldTime)
                    {
                        StopChargeSound();
                        SetRating(hoveredRating);
                        _currentHoldTimer = 0f; // Reset après validation
                    }
                }
                else
                {
                    StopChargeSound();
                    if (_currentHoldTimer > 0) PlaySound(cancelSound, cancelVolume);
                    _lastInteractingStar = hoveredRating;
                    _currentHoldTimer = 0f;
                    PlaySound(hoverSound, hoverVolume);
                }
            }
            else
            {
                StopChargeSound();
                if (_currentHoldTimer > 0) PlaySound(cancelSound, cancelVolume);
                
                if (hoveredRating != _lastInteractingStar)
                {
                    if (hoveredRating > 0) PlaySound(hoverSound, hoverVolume);
                    _lastInteractingStar = hoveredRating;
                }
                
                _currentHoldTimer = 0f;
            }
        }

        private bool _gazeMode;
        private Image _crosshair;
        private RectTransform _crosshairRect;

        // Réticule "point + halo" généré à la volée : un centre net qui fond
        // progressivement vers l'extérieur (pas de dépendance à un sprite).
        private static Sprite _softDotSprite;
        private static Sprite GetSoftDotSprite()
        {
            if (_softDotSprite != null) return _softDotSprite;

            const int S = 64;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            // smoothstep façon HLSL (edge0, edge1, x) — ATTENTION : Mathf.SmoothStep(a,b,t)
            // de Unity interpole entre a et b, ce n'est PAS la même fonction (ça donnait
            // un carré d'alpha quasi uniforme au lieu d'un point).
            float Smooth(float e0, float e1, float x)
            {
                float t = Mathf.Clamp01((x - e0) / (e1 - e0));
                return t * t * (3f - 2f * t);
            }

            for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float dx = (x + 0.5f) / S - 0.5f;
                float dy = (y + 0.5f) / S - 0.5f;
                float r = Mathf.Sqrt(dx * dx + dy * dy) * 2f;    // 0 centre .. 1 bord

                float core = 1f - Smooth(0.16f, 0.34f, r);       // point net
                float halo = (1f - Smooth(0.20f, 1.00f, r)) * 0.35f; // halo en fondu
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(core + halo)));
            }
            tex.Apply();

            _softDotSprite = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f));
            _softDotSprite.hideFlags = HideFlags.HideAndDontSave;
            return _softDotSprite;
        }

        /// <summary>
        /// Sélection au regard (dwell) : le rayon part de la caméra ; fixer la même
        /// étoile pendant gazeHoldTime la valide. Aucun bouton ni arme nécessaire.
        /// </summary>
        private void HandleGazeInteraction()
        {
            if (_targetCamera == null) return;

            Ray ray = new Ray(_targetCamera.position, _targetCamera.forward);

            hoveredRating = 0;
            float closestDist = float.MaxValue;
            bool inPanelZone = false;
            Plane p = new Plane(_canvasRoot.transform.forward, _canvasRoot.transform.position);
            if (p.Raycast(ray, out float enter))
            {
                Vector3 worldPoint = ray.GetPoint(enter);
                for (int i = 0; i < _starRects.Count; i++)
                {
                    Vector3 localPoint = _starRects[i].InverseTransformPoint(worldPoint);
                    Rect r = _starRects[i].rect;
                    Vector2 sz = r.size * hitboxTolerance;
                    Rect hitbox = new Rect(r.center - sz * 0.5f, sz);
                    if (hitbox.Contains(localPoint) && enter < closestDist)
                    {
                        closestDist = enter;
                        hoveredRating = i + 1;
                    }
                }

                // Réticule : visible dès que le regard tombe dans la zone crosshairZone
                // (centrée sur le panneau), positionné pile au point visé.
                RectTransform canvasRect = _canvasRoot.GetComponent<RectTransform>();
                Vector3 canvasLocal = canvasRect.InverseTransformPoint(worldPoint);
                inPanelZone = new Rect(-crosshairZone * 0.5f, crosshairZone).Contains(canvasLocal);

                if (_crosshair != null)
                {
                    if (inPanelZone)
                    {
                        if (!_crosshair.gameObject.activeSelf) _crosshair.gameObject.SetActive(true);
                        _crosshairRect.anchoredPosition = new Vector2(canvasLocal.x, canvasLocal.y);

                        // feedback : blanc en balade, jaune qui grossit pendant la charge
                        float progress = hoveredRating > 0 ? Mathf.Clamp01(_currentHoldTimer / gazeHoldTime) : 0f;
                        _crosshair.color = Color.Lerp(new Color(1f, 1f, 1f, 0.85f), filledColor, progress);
                        _crosshairRect.sizeDelta = Vector2.one * crosshairSize * (1f + progress * 0.6f);
                    }
                    else _crosshair.gameObject.SetActive(false);
                }
            }
            else if (_crosshair != null) _crosshair.gameObject.SetActive(false);

            if (hoveredRating > 0)
            {
                if (_lastInteractingStar != hoveredRating)
                {
                    _lastInteractingStar = hoveredRating;
                    _currentHoldTimer = 0f;
                    PlaySound(hoverSound, hoverVolume);
                }

                _currentHoldTimer += Time.deltaTime;

                // même feedback sonore de charge que le mode arme
                if (chargeSound != null && _chargeAudioSource != null)
                {
                    if (!_chargeAudioSource.isPlaying)
                    {
                        _chargeAudioSource.clip = chargeSound;
                        _chargeAudioSource.Play();
                    }
                    float progress = Mathf.Clamp01(_currentHoldTimer / gazeHoldTime);
                    _chargeAudioSource.volume = chargeVolume * progress;
                    _chargeAudioSource.pitch = Mathf.Lerp(chargePitchStart, chargePitchEnd, progress);
                }

                if (_currentHoldTimer >= gazeHoldTime)
                {
                    StopChargeSound();
                    SetRating(hoveredRating);
                    _currentHoldTimer = 0f;
                }
            }
            else
            {
                StopChargeSound();
                if (_currentHoldTimer > 0) PlaySound(cancelSound, cancelVolume);
                _lastInteractingStar = 0;
                _currentHoldTimer = 0f;
            }
        }

        private void StopChargeSound()
        {
            if (_chargeAudioSource != null && _chargeAudioSource.isPlaying)
            {
                _chargeAudioSource.Stop();
            }
        }

        private void PlaySound(AudioClip clip, float volume = 1f)
        {
            if (clip != null && _audioSource != null)
            {
                _audioSource.PlayOneShot(clip, volume);
            }
        }

        private void UpdateStarsVisuals()
        {
            if (_instructionText != null)
            {
                _instructionText.text = GetLocalizedText();
            }

            for (int i = 0; i < _starImages.Count; i++)
            {
                int starIndex = i + 1;
                Image img = _starImages[i];
                RectTransform rect = _starRects[i];

                Color targetColor = emptyColor;
                Sprite targetSprite = _starEmptySprite;
                float targetScale = 1.0f;

                if (starIndex <= currentRating)
                {
                    targetColor = filledColor;
                    targetSprite = _starFilledSprite;
                }
                
                // Effet de hover
                if (starIndex <= hoveredRating)
                {
                    if (starIndex > currentRating)
                    {
                        targetColor = Color.Lerp(emptyColor, hoverColor, 0.5f);
                        targetSprite = _starFilledSprite;
                    }
                    
                    targetScale = 1.2f;

                    // Si c'est l'étoile en train d'être chargée, on ajoute un feedback sur l'échelle
                    if (starIndex == hoveredRating && _currentHoldTimer > 0)
                    {
                        float progress = _currentHoldTimer / (_gazeMode ? gazeHoldTime : selectionHoldTime);
                        targetScale += progress * 0.3f; // Elle grossit encore plus
                        targetColor = Color.Lerp(targetColor, filledColor, progress); // Elle jaunit
                        targetSprite = _starFilledSprite;
                    }
                }

                img.sprite = targetSprite;
                img.color = Color.Lerp(img.color, targetColor, Time.deltaTime * 10f);
                rect.localScale = Vector3.Lerp(rect.localScale, Vector3.one * targetScale, Time.deltaTime * 10f);
            }
        }

        public void SetRating(int rating)
        {
            if (_isProcessingSelection) return;
            _isProcessingSelection = true;

            currentRating = rating;
            Debug.Log($"[Rating3DUI] Utilisateur a noté : {rating} étoiles");

            // Persistance + envoi back-office (une seule fois par notation, protégé par _isProcessingSelection).
            if (BackOfficeVaronia.Instance != null)
            {
                // Historique local : ajoute {date, note} dans Rating.json (même dossier que GlobalConfig.json).
                BackOfficeVaronia.Instance.AppendRating(rating);

                // Envoi MQTT (même logique que SetScore/SetSoftState).
                if (BackOfficeVaronia.Instance.mqttClient != null)
                    BackOfficeVaronia.Instance.mqttClient.SetRating(rating);
            }

            PlaySound(successSound, successVolume);
            
            // Animation de feedback
            StartCoroutine(PulseRatingEffect(rating));

            // Nouveau : Afficher le message de remerciement après un court délai
            StartCoroutine(DelayedThankYou());
        }

        private IEnumerator DelayedThankYou()
        {
            yield return new WaitForSeconds(0.3f);
            ShowThankYouMessage();
        }

        private void ShowThankYouMessage()
        {
            isShowingThankYou = true;
            
            // Commencer le fade out du panneau principal
            StartCoroutine(FadeOutMainPanel());

            // Désactiver le laser et le réticule
            if (_lineRenderer != null) _lineRenderer.enabled = false;
            if (_crosshair != null) _crosshair.gameObject.SetActive(false);
            StopChargeSound();

            // Afficher le panneau de remerciement avec animation
            if (_thankYouContainer != null)
            {
                _thankYouContainer.gameObject.SetActive(true);
                _thankYouContainer.localScale = Vector3.zero; // Départ pour l'animation de scale
                if (_thankYouCanvasGroup != null) _thankYouCanvasGroup.alpha = 0f;
                if (_thankYouText != null) _thankYouText.text = GetLocalizedThankYou();
                
                StartCoroutine(AnimateThankYouIn());
            }

            // Lancer la disparition automatique
            StartCoroutine(AutoCloseThankYou());
        }

        private IEnumerator FadeOutMainPanel()
        {
            float duration = 0.3f;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = 1f - (elapsed / duration);
                if (_mainCanvasGroup != null) _mainCanvasGroup.alpha = t;
                yield return null;
            }
            if (_mainCanvasGroup != null) _mainCanvasGroup.alpha = 0f;
            if (_starsContainer != null) _starsContainer.gameObject.SetActive(false);
            if (_textContainer != null) _textContainer.gameObject.SetActive(false);
        }

        private IEnumerator AnimateThankYouIn()
        {
            float duration = 0.5f;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                // Courbe d'interpolation pour un effet "smooth"
                float smoothT = Mathf.SmoothStep(0, 1, t);
                
                if (_thankYouContainer != null) _thankYouContainer.localScale = Vector3.one * smoothT;
                if (_thankYouCanvasGroup != null) _thankYouCanvasGroup.alpha = smoothT;
                
                yield return null;
            }
            if (_thankYouContainer != null) _thankYouContainer.localScale = Vector3.one;
            if (_thankYouCanvasGroup != null) _thankYouCanvasGroup.alpha = 1f;
        }

        private IEnumerator AutoCloseThankYou()
        {
            yield return new WaitForSeconds(thankYouDisplayTime);
            ShowRating(false);
        }

        private string GetLocalizedThankYou()
        {
            string lang = "Fr";
            if (BackOfficeVaronia.Instance != null && BackOfficeVaronia.Instance.config != null)
            {
                lang = BackOfficeVaronia.Instance.config.Language;
            }

            if (string.Equals(lang, "Fr", StringComparison.OrdinalIgnoreCase)) return thankYouFR;
            if (string.Equals(lang, "En", StringComparison.OrdinalIgnoreCase)) return thankYouEN;
            if (string.Equals(lang, "Es", StringComparison.OrdinalIgnoreCase)) return thankYouES;
            
            return thankYouEN; // Default
        }

        private string GetLocalizedText()
        {
            string lang = "Fr";
            if (BackOfficeVaronia.Instance != null && BackOfficeVaronia.Instance.config != null)
            {
                lang = BackOfficeVaronia.Instance.config.Language;
            }

            if (string.Equals(lang, "Fr", StringComparison.OrdinalIgnoreCase)) return textFR;
            if (string.Equals(lang, "En", StringComparison.OrdinalIgnoreCase)) return textEN;
            if (string.Equals(lang, "Es", StringComparison.OrdinalIgnoreCase)) return textES;
            
            return textEN; // Default
        }

        private IEnumerator PulseRatingEffect(int rating)
        {
            for (int i = 0; i < rating; i++)
            {
                _starRects[i].localScale = Vector3.one * 1.5f;
                yield return new WaitForSeconds(0.05f);
            }
        }

        public void ShowRating(bool visible)
        {
            if (visible == isVisible) return;

            if (visible)
            {
                StopAllCoroutines(); // Arrête les éventuels Fade Out en cours
                isVisible = true;
                IsRatingDisplayed = true;
                isShowingThankYou = false;
                _isProcessingSelection = false;
                StopChargeSound();

                if (_canvasRoot != null)
                {
                    _canvasRoot.SetActive(true);
                    _canvasRoot.transform.localScale = Vector3.zero; // Reset pour l'effet de spawn
                    PlaySound(spawnSound, spawnVolume);

                    // S'assurer que les bons panneaux sont actifs
                    if (_starsContainer != null) _starsContainer.gameObject.SetActive(true);
                    if (_textContainer != null) _textContainer.gameObject.SetActive(true);
                    if (_mainCanvasGroup != null) _mainCanvasGroup.alpha = 1f;
                    if (_thankYouContainer != null) _thankYouContainer.gameObject.SetActive(false);
                    currentRating = 0;
                    hoveredRating = 0;

                    if (alwaysOnTop) ApplyAlwaysOnTop();

                    // Reset position au centre de la vue
                    if (_targetCamera != null)
                    {
                        _canvasRoot.transform.position = _targetCamera.position + _targetCamera.forward * followDistance;
                        _canvasRoot.transform.LookAt(_canvasRoot.transform.position + (_canvasRoot.transform.position - _targetCamera.position));
                    }
                }

                if (_lineRenderer != null)
                {
                    _lineRenderer.enabled = useLineRenderer;
                }
            }
            else
            {
                // Fade out avant de cacher
                StartCoroutine(FadeOutAndHide());
            }
        }

        private void ApplyAlwaysOnTop()
        {
            if (_canvas == null) return;

            if (_alwaysOnTopMat == null)
            {
                Shader shader = Shader.Find("UI/AlwaysOnTop");
                if (shader == null) shader = Shader.Find("UI/Default"); // Fallback
                _alwaysOnTopMat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }

            foreach (var graphic in _canvas.GetComponentsInChildren<Graphic>(true))
            {
                graphic.material = _alwaysOnTopMat;
            }
        }

        private IEnumerator FadeOutAndHide()
        {
            float duration = 0.5f;
            float elapsed = 0f;
            
            // On commence par désactiver les interactions
            _isProcessingSelection = true;
            IsRatingDisplayed = false;
            
            // On récupère l'alpha de départ du canvas racine (ou des groupes)
            // On va utiliser le scale pour la racine et l'alpha pour les groupes
            Vector3 startScale = _canvasRoot.transform.localScale;
            
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = 1f - (elapsed / duration);
                float smoothT = Mathf.SmoothStep(0, 1, t);
                
                if (_canvasRoot != null) _canvasRoot.transform.localScale = startScale * smoothT;
                if (_mainCanvasGroup != null && _mainCanvasGroup.gameObject.activeInHierarchy) _mainCanvasGroup.alpha = smoothT;
                if (_thankYouCanvasGroup != null && _thankYouCanvasGroup.gameObject.activeInHierarchy) _thankYouCanvasGroup.alpha = smoothT;
                
                yield return null;
            }

            isVisible = false;
            if (_canvasRoot != null) _canvasRoot.SetActive(false);
            if (_lineRenderer != null) _lineRenderer.enabled = false;
            if (_crosshair != null) _crosshair.gameObject.SetActive(false);
            StopChargeSound();
        }

        // Fonction d'appel pour l'utilisateur
        [ContextMenu("Toggle Rating UI")]
        public void ToggleRatingInstance()
        {
            ShowRating(!isVisible);
        }

        public static void ToggleRating()
        {
            if (Instance != null) Instance.ShowRating(!Instance.isVisible);
        }

        /// <summary>
        /// Affiche la notation ANCRÉE à un point fixe du décor (mode regard + réticule).
        /// Passe null pour revenir au comportement "suit la caméra".
        /// </summary>
        public void ShowRatingAt(Transform anchor)
        {
            fixedAnchor = anchor;
            if (anchor != null && _canvasRoot != null)
                _canvasRoot.transform.SetPositionAndRotation(anchor.position, anchor.rotation);
            ShowRating(true);
        }
    }
}
