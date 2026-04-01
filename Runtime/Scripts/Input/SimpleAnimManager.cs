using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SimpleAnimManager : MonoBehaviour
{
    [System.Serializable]
    public class AnimElement
    {
        public string nomDeLAnim;
        public Transform cible; 

        [Header("Positions")]
        public Vector3 posDebut;
        public Vector3 posFin;

        [Header("Rotations")]
        public Vector3 rotDebut;
        public Vector3 rotFin;

        [Header("Echelles")]
        public Vector3 scaleDebut = Vector3.one;
        public Vector3 scaleFin = Vector3.one;

        [Header("Paramètres")]
        public float duree = 1.0f;
    }

    public List<AnimElement> mesAnimations;

    // --- CONTEXT MENU POUR L'EDITEUR ---
    [ContextMenu("▶ Jouer TOUTES (Sens Normal)")]
    public void LancerSequenceNormale() => StartCoroutine(SequenceComplete(false));

    [ContextMenu("◀ Jouer TOUTES (Sens Inverse)")]
    public void LancerSequenceInverse() => StartCoroutine(SequenceComplete(true));

    private IEnumerator SequenceComplete(bool inverserTout)
    {
        foreach (AnimElement anim in mesAnimations)
        {
            if (anim.cible != null)
                yield return StartCoroutine(RoutineAnimation(anim, inverserTout));
        }
    }

    // --- FONCTION PUBLIQUE POUR TES SCRIPTS ---
    // Tu peux appeler : JouerAnimation("OuvrirPorte") ou JouerAnimation("OuvrirPorte", true)
    public void JouerAnimation(string nom, bool inverser = false)
    {
        AnimElement anim = mesAnimations.Find(a => a.nomDeLAnim == nom);
        if (anim != null && anim.cible != null) 
            StartCoroutine(RoutineAnimation(anim, inverser));
    }

    private IEnumerator RoutineAnimation(AnimElement anim, bool inverser)
    {
        float tempsEcoule = 0;
        Transform tTarget = anim.cible;

        // On définit les points A et B selon le sens demandé
        Vector3 departP = inverser ? anim.posFin : anim.posDebut;
        Vector3 arriveeP = inverser ? anim.posDebut : anim.posFin;

        Vector3 departR = inverser ? anim.rotFin : anim.rotDebut;
        Vector3 arriveeR = inverser ? anim.rotDebut : anim.rotFin;

        Vector3 departS = inverser ? anim.scaleFin : anim.scaleDebut;
        Vector3 arriveeS = inverser ? anim.scaleDebut : anim.scaleFin;

        while (tempsEcoule < anim.duree)
        {
            if (tTarget == null) yield break;

            float t = tempsEcoule / anim.duree;

            // Application des valeurs inversées ou non
            tTarget.localPosition = Vector3.Lerp(departP, arriveeP, t);
            tTarget.localRotation = Quaternion.Lerp(Quaternion.Euler(departR), Quaternion.Euler(arriveeR), t);
            tTarget.localScale = Vector3.Lerp(departS, arriveeS, t);

            tempsEcoule += Time.deltaTime;
            yield return null;
        }

        // Finalisation propre
        if (tTarget != null)
        {
            tTarget.localPosition = arriveeP;
            tTarget.localRotation = Quaternion.Euler(arriveeR);
            tTarget.localScale = arriveeS;
        }
    }
}