using UnityEngine;
using VaroniaBackOffice;

/// <summary>
/// Fait le lien entre VaroniaInput et SimpleAnimManager.
/// Pour chaque bouton Varonia, tu peux définir quelle animation jouer
/// à l'appui (Press) et au relâchement (Release).
/// </summary>
public class VaroniaInputAnimBridge : MonoBehaviour
{
    [System.Serializable]
    public class AnimMapping
    {
        [Header("Animation à jouer à l'appui")]
        public string animPress;
        public bool   inverserPress = false;

        [Header("Animation à jouer au relâchement")]
        public string animRelease;
        public bool   inverserRelease = false;
    }

    [Header("Référence au SimpleAnimManager")]
    public SimpleAnimManager animManager;

    [Header("Mappings par bouton")]
    public AnimMapping primary;
    public AnimMapping secondary;
    public AnimMapping tertiary;
    public AnimMapping quaternary;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void OnEnable()
    {
        VaroniaInput.OnPrimaryDownStatic    += OnPrimaryDown;
        VaroniaInput.OnPrimaryUpStatic      += OnPrimaryUp;
        VaroniaInput.OnSecondaryDownStatic  += OnSecondaryDown;
        VaroniaInput.OnSecondaryUpStatic    += OnSecondaryUp;
        VaroniaInput.OnTertiaryDownStatic   += OnTertiaryDown;
        VaroniaInput.OnTertiaryUpStatic     += OnTertiaryUp;
        VaroniaInput.OnQuaternaryDownStatic += OnQuaternaryDown;
        VaroniaInput.OnQuaternaryUpStatic   += OnQuaternaryUp;
    }

    private void OnDisable()
    {
        VaroniaInput.OnPrimaryDownStatic    -= OnPrimaryDown;
        VaroniaInput.OnPrimaryUpStatic      -= OnPrimaryUp;
        VaroniaInput.OnSecondaryDownStatic  -= OnSecondaryDown;
        VaroniaInput.OnSecondaryUpStatic    -= OnSecondaryUp;
        VaroniaInput.OnTertiaryDownStatic   -= OnTertiaryDown;
        VaroniaInput.OnTertiaryUpStatic     -= OnTertiaryUp;
        VaroniaInput.OnQuaternaryDownStatic -= OnQuaternaryDown;
        VaroniaInput.OnQuaternaryUpStatic   -= OnQuaternaryUp;
    }

    // ── Handlers ──────────────────────────────────────────────────────────────

    private void OnPrimaryDown()    => Jouer(primary,    press: true);
    private void OnPrimaryUp()      => Jouer(primary,    press: false);
    private void OnSecondaryDown()  => Jouer(secondary,  press: true);
    private void OnSecondaryUp()    => Jouer(secondary,  press: false);
    private void OnTertiaryDown()   => Jouer(tertiary,   press: true);
    private void OnTertiaryUp()     => Jouer(tertiary,   press: false);
    private void OnQuaternaryDown() => Jouer(quaternary, press: true);
    private void OnQuaternaryUp()   => Jouer(quaternary, press: false);

    // ── Logique centrale ──────────────────────────────────────────────────────

    private void Jouer(AnimMapping mapping, bool press)
    {
        if (animManager == null)
        {
            Debug.LogWarning("[VaroniaInputAnimBridge] Aucun SimpleAnimManager assigné !");
            return;
        }

        if (press)
        {
            if (!string.IsNullOrEmpty(mapping.animPress))
                animManager.JouerAnimation(mapping.animPress, mapping.inverserPress);
        }
        else
        {
            if (!string.IsNullOrEmpty(mapping.animRelease))
                animManager.JouerAnimation(mapping.animRelease, mapping.inverserRelease);
        }
    }
}
