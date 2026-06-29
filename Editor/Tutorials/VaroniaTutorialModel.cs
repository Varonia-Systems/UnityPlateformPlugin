// Modèle de données + contrat pour le système de tutoriels Varonia.
//
// N'IMPORTE QUEL package Varonia peut apparaître dans la fenêtre "Varonia/Tuto"
// en implémentant IVaroniaTutorialProvider (classe publique, sans paramètre de
// constructeur). La fenêtre découvre tous les providers par reflection
// (TypeCache) : aucun enregistrement manuel, aucune liste centrale à maintenir.
//
// Le contenu est purement déclaratif (cards de doc) : un package décrit ses
// composants, leurs propriétés et quelques actions cliquables. Aucune logique
// d'UI ici — c'est la fenêtre qui se charge du rendu.

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace VaroniaBackOffice.Tutorials
{
    /// <summary>Propriété documentée d'un composant (nom affiché + explication courte).</summary>
    public struct TutorialProperty
    {
        public string Name;
        public string Description;

        public TutorialProperty(string name, string description)
        {
            Name = name;
            Description = description;
        }
    }

    /// <summary>Bouton d'action affiché sur une card (créer un objet, sélectionner le script, ouvrir une doc…).</summary>
    public class TutorialAction
    {
        public string Label;
        public string Tooltip;
        public Action Run;

        public TutorialAction(string label, Action run, string tooltip = "")
        {
            Label = label;
            Run = run;
            Tooltip = tooltip;
        }
    }

    /// <summary>Une fiche de tuto = un composant / une fonctionnalité d'un package.</summary>
    public class TutorialCard
    {
        public string Title;                                            // ex: "SimpleRotator"
        public string Category;                                         // ex: "Animation / Mouvement"
        public string Summary;                                          // une ligne
        public string Description;                                      // paragraphe(s), \n et rich text autorisés
        public readonly List<string> WhenToUse = new List<string>();    // puces "cas d'usage"
        public readonly List<TutorialProperty> Properties = new List<TutorialProperty>();
        public readonly List<TutorialAction> Actions = new List<TutorialAction>();

        public TutorialCard(string title, string category = "", string summary = "")
        {
            Title = title;
            Category = category;
            Summary = summary;
        }

        // ── Fluent helpers (pour écrire les providers de façon compacte) ──

        public TutorialCard Desc(string description) { Description = description; return this; }

        public TutorialCard Use(params string[] cases) { WhenToUse.AddRange(cases); return this; }

        public TutorialCard Prop(string name, string description)
        {
            Properties.Add(new TutorialProperty(name, description));
            return this;
        }

        public TutorialCard Act(TutorialAction action)
        {
            if (action != null) Actions.Add(action);
            return this;
        }

        /// <summary>Vrai si la card matche une requête de recherche (titre, résumé, description, catégorie).</summary>
        public bool Matches(string query)
        {
            if (string.IsNullOrEmpty(query)) return true;
            query = query.ToLowerInvariant();
            return (Title != null && Title.ToLowerInvariant().Contains(query))
                || (Summary != null && Summary.ToLowerInvariant().Contains(query))
                || (Category != null && Category.ToLowerInvariant().Contains(query))
                || (Description != null && Description.ToLowerInvariant().Contains(query));
        }
    }

    /// <summary>Un package Varonia et l'ensemble de ses fiches tuto.</summary>
    public class TutorialPackage
    {
        public string Id;            // ex: "com.varonia.misc"
        public string DisplayName;   // ex: "Misc"
        public string Description;   // sous-titre du package
        public int Order = 100;      // tri dans la sidebar (plus petit = plus haut)
        public readonly List<TutorialCard> Cards = new List<TutorialCard>();

        public TutorialPackage(string id, string displayName, string description = "", int order = 100)
        {
            Id = id;
            DisplayName = displayName;
            Description = description;
            Order = order;
        }

        public TutorialCard AddCard(TutorialCard card)
        {
            if (card != null) Cards.Add(card);
            return card;
        }
    }

    /// <summary>
    /// Implémente cette interface (classe publique, constructeur sans paramètre) dans
    /// n'importe quel package pour qu'il apparaisse automatiquement dans "Varonia/Tuto".
    /// </summary>
    public interface IVaroniaTutorialProvider
    {
        TutorialPackage GetPackage();
    }

    /// <summary>Fabriques d'actions courantes, réutilisables par tous les providers.</summary>
    public static class TutorialActions
    {
        /// <summary>Crée un GameObject avec le composant donné, le sélectionne et le ping (undo-friendly).</summary>
        public static TutorialAction AddToScene(Type componentType, string label = null)
        {
            string text = label ?? "＋ Ajouter à la scène";
            return new TutorialAction(text, () =>
            {
                if (componentType == null) return;
                var go = ObjectFactory.CreateGameObject(componentType.Name, componentType);
                Selection.activeGameObject = go;
                EditorGUIUtility.PingObject(go);
            }, $"Crée un GameObject '{(componentType != null ? componentType.Name : "?")}' avec le composant.");
        }

        /// <summary>Sélectionne et ping le fichier de script (MonoScript) correspondant au type.</summary>
        public static TutorialAction SelectScript(Type type, string label = null)
        {
            string text = label ?? "📄 Voir le script";
            return new TutorialAction(text, () =>
            {
                if (type == null) return;
                foreach (var guid in AssetDatabase.FindAssets($"{type.Name} t:MonoScript"))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var ms = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                    if (ms != null && ms.GetClass() == type)
                    {
                        Selection.activeObject = ms;
                        EditorGUIUtility.PingObject(ms);
                        return;
                    }
                }
                Debug.LogWarning($"[Varonia Tuto] Script introuvable pour le type '{type.Name}'.");
            }, "Localise le fichier .cs dans le Project.");
        }

        /// <summary>Ouvre une URL (doc en ligne, etc.).</summary>
        public static TutorialAction OpenUrl(string label, string url)
        {
            return new TutorialAction(label, () => Application.OpenURL(url), url);
        }

        /// <summary>Action libre.</summary>
        public static TutorialAction Custom(string label, Action run, string tooltip = "")
        {
            return new TutorialAction(label, run, tooltip);
        }
    }
}
