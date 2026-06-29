// Fenêtre "Varonia/Tuto" — navigateur de tutoriels façon doc interactive.
//
//   • Sidebar gauche : un package Varonia par ligne (auto-découverts).
//   • Zone droite    : les fiches (cards) du package sélectionné, repliables,
//                      avec description, propriétés clés et boutons d'action.
//   • Recherche      : filtre les cards du package courant.
//
// Charte alignée sur les autres fenêtres Varonia (fond sombre, accent vert).
// IMGUI pour rester compatible partout.

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace VaroniaBackOffice.Tutorials
{
    public class VaroniaTutorialWindow : EditorWindow
    {
        // ─── Colors ───────────────────────────────────────────────────────────────
        static readonly Color colBg          = new Color(0.11f, 0.11f, 0.14f, 1f);
        static readonly Color colSidebar     = new Color(0.085f, 0.085f, 0.11f, 1f);
        static readonly Color colCard        = new Color(0.15f, 0.15f, 0.19f, 1f);
        static readonly Color colCardHeader  = new Color(0.18f, 0.18f, 0.23f, 1f);
        static readonly Color colAccent      = new Color(0.30f, 0.85f, 0.65f, 1f);
        static readonly Color colSelected    = new Color(0.30f, 0.85f, 0.65f, 0.16f);
        static readonly Color colTextPrimary = new Color(0.92f, 0.92f, 0.95f, 1f);
        static readonly Color colTextSecond  = new Color(0.62f, 0.62f, 0.70f, 1f);
        static readonly Color colTextMuted   = new Color(0.42f, 0.42f, 0.50f, 1f);
        static readonly Color colDivider     = new Color(1f, 1f, 1f, 0.06f);

        // ─── Styles (lazy) ──────────────────────────────────────────────────────────
        static bool _stylesBuilt;
        static GUIStyle _title, _pkgName, _pkgDesc, _sideItem, _sideItemSel, _sideCount,
                        _cardTitle, _cardCategory, _cardSummary, _cardBody, _propName,
                        _propDesc, _sectionLabel, _footer, _searchHint;
        static Texture2D _texSel, _texCardHeader;

        // ─── State ────────────────────────────────────────────────────────────────
        int _selectedPackage;
        string _search = "";
        Vector2 _sideScroll, _contentScroll;
        readonly HashSet<string> _expanded = new HashSet<string>(); // clé = "pkg/title"

        const float SidebarWidth = 190f;

        [MenuItem("Varonia/Tuto", priority = 0)]
        public static void ShowWindow()
        {
            var w = GetWindow<VaroniaTutorialWindow>();
            w.titleContent = new GUIContent("Varonia Tuto");
            w.minSize = new Vector2(720, 480);
        }

        // ─── Styles ─────────────────────────────────────────────────────────────────

        static Texture2D MakeTex(Color c)
        {
            var t = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        void BuildStyles()
        {
            if (_stylesBuilt && _texSel != null) return;
            _stylesBuilt = true;

            _texSel = MakeTex(colSelected);
            _texCardHeader = MakeTex(colCardHeader);

            _title = new GUIStyle { fontSize = 15, fontStyle = FontStyle.Bold, normal = { textColor = colTextPrimary } };

            _pkgName = new GUIStyle { fontSize = 12, fontStyle = FontStyle.Bold, normal = { textColor = colTextPrimary },
                                      padding = new RectOffset(8, 4, 6, 0) };
            _pkgDesc = new GUIStyle { fontSize = 9, normal = { textColor = colTextMuted }, wordWrap = true,
                                      padding = new RectOffset(8, 4, 0, 6) };
            _sideCount = new GUIStyle { fontSize = 9, normal = { textColor = colTextMuted }, alignment = TextAnchor.MiddleRight,
                                        padding = new RectOffset(0, 8, 0, 0) };

            _sideItem = new GUIStyle { fontSize = 12, normal = { textColor = colTextSecond },
                                       padding = new RectOffset(10, 6, 7, 7) };
            _sideItemSel = new GUIStyle(_sideItem) { fontStyle = FontStyle.Bold, normal = { textColor = colAccent, background = _texSel } };

            _cardTitle = new GUIStyle { fontSize = 13, fontStyle = FontStyle.Bold, normal = { textColor = colTextPrimary } };
            _cardCategory = new GUIStyle { fontSize = 9, fontStyle = FontStyle.Bold, normal = { textColor = colAccent } };
            _cardSummary = new GUIStyle { fontSize = 11, normal = { textColor = colTextSecond }, wordWrap = true, fontStyle = FontStyle.Italic };
            _cardBody = new GUIStyle { fontSize = 11, normal = { textColor = colTextPrimary }, wordWrap = true, richText = true };
            _propName = new GUIStyle { fontSize = 11, fontStyle = FontStyle.Bold, normal = { textColor = colTextPrimary } };
            _propDesc = new GUIStyle { fontSize = 11, normal = { textColor = colTextSecond }, wordWrap = true };
            _sectionLabel = new GUIStyle { fontSize = 9, fontStyle = FontStyle.Bold, normal = { textColor = colTextMuted } };
            _footer = new GUIStyle { fontSize = 9, normal = { textColor = colTextMuted }, alignment = TextAnchor.MiddleCenter };
            _searchHint = new GUIStyle { fontSize = 11, normal = { textColor = colTextMuted }, alignment = TextAnchor.MiddleCenter, wordWrap = true };
        }

        // ─── GUI ──────────────────────────────────────────────────────────────────

        void OnGUI()
        {
            BuildStyles();
            EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), colBg);

            var packages = VaroniaTutorialRegistry.Packages;

            DrawHeader(packages.Count);

            EditorGUILayout.BeginHorizontal();
            DrawSidebar(packages);
            DrawContent(packages);
            EditorGUILayout.EndHorizontal();
        }

        void DrawHeader(int packageCount)
        {
            EditorGUILayout.BeginHorizontal(GUILayout.Height(38));
            GUILayout.Space(14);
            EditorGUILayout.BeginVertical();
            GUILayout.Space(9);
            GUILayout.Label("VARONIA · TUTORIELS", _title);
            EditorGUILayout.EndVertical();

            GUILayout.FlexibleSpace();

            EditorGUILayout.BeginVertical();
            GUILayout.Space(8);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label($"{packageCount} package(s)", _sideCount, GUILayout.Width(90));
            if (GUILayout.Button("↻", GUILayout.Width(26), GUILayout.Height(20)))
            {
                VaroniaTutorialRegistry.Refresh();
                _selectedPackage = 0;
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
            GUILayout.Space(12);
            EditorGUILayout.EndHorizontal();

            var r = GUILayoutUtility.GetRect(1, 1);
            EditorGUI.DrawRect(new Rect(0, r.y, position.width, 1), colDivider);
        }

        void DrawSidebar(IReadOnlyList<TutorialPackage> packages)
        {
            var rect = EditorGUILayout.BeginVertical(GUILayout.Width(SidebarWidth), GUILayout.ExpandHeight(true));
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, SidebarWidth, position.height), colSidebar);

            GUILayout.Space(6);
            GUILayout.Label("PACKAGES", _sectionLabel, GUILayout.Width(SidebarWidth));
            GUILayout.Space(2);

            _sideScroll = EditorGUILayout.BeginScrollView(_sideScroll, GUILayout.Width(SidebarWidth));
            if (packages.Count == 0)
            {
                GUILayout.Space(8);
                GUILayout.Label("Aucun package tuto\ndétecté.", _pkgDesc, GUILayout.Width(SidebarWidth - 4));
            }
            for (int i = 0; i < packages.Count; i++)
            {
                bool sel = i == _selectedPackage;
                var style = sel ? _sideItemSel : _sideItem;

                EditorGUILayout.BeginHorizontal(GUILayout.Width(SidebarWidth));
                if (GUILayout.Button($"{packages[i].DisplayName}", style, GUILayout.Width(SidebarWidth - 38), GUILayout.Height(30)))
                {
                    _selectedPackage = i;
                    _contentScroll = Vector2.zero;
                }
                GUILayout.Label(packages[i].Cards.Count.ToString(), _sideCount, GUILayout.Width(30), GUILayout.Height(30));
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        void DrawContent(IReadOnlyList<TutorialPackage> packages)
        {
            EditorGUILayout.BeginVertical();
            GUILayout.Space(8);

            if (packages.Count == 0)
            {
                GUILayout.FlexibleSpace();
                GUILayout.Label(
                    "Aucun tutoriel disponible.\n\nUn package devient visible ici dès qu'il fournit une classe\n" +
                    "implémentant IVaroniaTutorialProvider.",
                    _searchHint);
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndVertical();
                return;
            }

            _selectedPackage = Mathf.Clamp(_selectedPackage, 0, packages.Count - 1);
            var pkg = packages[_selectedPackage];

            // Titre + recherche
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(8);
            EditorGUILayout.BeginVertical();
            GUILayout.Label(pkg.DisplayName, _pkgName);
            if (!string.IsNullOrEmpty(pkg.Description))
                GUILayout.Label(pkg.Description, _pkgDesc);
            EditorGUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            EditorGUILayout.BeginVertical(GUILayout.Width(220));
            GUILayout.Space(6);
            _search = EditorGUILayout.TextField(_search, EditorStyles.toolbarSearchField, GUILayout.Width(210));
            EditorGUILayout.EndVertical();
            GUILayout.Space(12);
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(4);
            var dr = GUILayoutUtility.GetRect(1, 1);
            EditorGUI.DrawRect(new Rect(dr.x + 8, dr.y, position.width - SidebarWidth - 24, 1), colDivider);
            GUILayout.Space(6);

            _contentScroll = EditorGUILayout.BeginScrollView(_contentScroll);

            int shown = 0;
            foreach (var card in pkg.Cards)
            {
                if (!card.Matches(_search)) continue;
                DrawCard(pkg, card);
                shown++;
            }

            if (shown == 0)
                GUILayout.Label($"Aucune fiche ne correspond à « {_search} ».", _searchHint);

            GUILayout.Space(10);
            EditorGUILayout.EndScrollView();

            GUILayout.Label("Varonia Tutoriels  ·  doc interactive  ·  packages auto-découverts", _footer);
            GUILayout.Space(4);
            EditorGUILayout.EndVertical();
        }

        void DrawCard(TutorialPackage pkg, TutorialCard card)
        {
            string key = pkg.Id + "/" + card.Title;
            bool open = _expanded.Contains(key);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(8);
            EditorGUILayout.BeginVertical();

            // Fond de card
            var cardRect = EditorGUILayout.BeginVertical();
            cardRect.x -= 2; cardRect.width += 4; cardRect.y -= 2; cardRect.height += 4;
            EditorGUI.DrawRect(cardRect, colCard);
            EditorGUI.DrawRect(new Rect(cardRect.x, cardRect.y, 3, cardRect.height), colAccent);

            // ── Header cliquable (foldout) ──
            var headerRect = EditorGUILayout.BeginHorizontal();
            EditorGUI.DrawRect(headerRect, colCardHeader);
            GUILayout.Space(8);
            EditorGUILayout.BeginVertical();
            GUILayout.Space(6);
            if (!string.IsNullOrEmpty(card.Category))
                GUILayout.Label(card.Category.ToUpperInvariant(), _cardCategory);
            GUILayout.Label((open ? "▼  " : "▶  ") + card.Title, _cardTitle);
            if (!string.IsNullOrEmpty(card.Summary))
            {
                GUILayout.Space(1);
                GUILayout.Label(card.Summary, _cardSummary);
            }
            GUILayout.Space(6);
            EditorGUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            // Toggle au clic sur le header
            if (Event.current.type == EventType.MouseDown && headerRect.Contains(Event.current.mousePosition))
            {
                if (open) _expanded.Remove(key); else _expanded.Add(key);
                Event.current.Use();
            }

            // ── Détails ──
            if (open)
            {
                GUILayout.Space(8);
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(10);
                EditorGUILayout.BeginVertical();

                if (!string.IsNullOrEmpty(card.Description))
                {
                    GUILayout.Label(card.Description, _cardBody);
                    GUILayout.Space(8);
                }

                if (card.WhenToUse.Count > 0)
                {
                    GUILayout.Label("QUAND L'UTILISER", _sectionLabel);
                    GUILayout.Space(2);
                    foreach (var u in card.WhenToUse)
                        GUILayout.Label("•  " + u, _propDesc);
                    GUILayout.Space(8);
                }

                if (card.Properties.Count > 0)
                {
                    GUILayout.Label("PROPRIÉTÉS CLÉS", _sectionLabel);
                    GUILayout.Space(2);
                    foreach (var p in card.Properties)
                    {
                        EditorGUILayout.BeginHorizontal();
                        GUILayout.Label(p.Name, _propName, GUILayout.Width(150));
                        GUILayout.Label(p.Description, _propDesc);
                        EditorGUILayout.EndHorizontal();
                        GUILayout.Space(2);
                    }
                    GUILayout.Space(8);
                }

                if (card.Actions.Count > 0)
                {
                    EditorGUILayout.BeginHorizontal();
                    foreach (var a in card.Actions)
                    {
                        if (GUILayout.Button(new GUIContent(a.Label, a.Tooltip), GUILayout.Height(24), GUILayout.MinWidth(120)))
                            a.Run?.Invoke();
                        GUILayout.Space(4);
                    }
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.EndHorizontal();
                    GUILayout.Space(6);
                }

                EditorGUILayout.EndVertical();
                GUILayout.Space(12);
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndVertical();   // fond de card
            EditorGUILayout.EndVertical();
            GUILayout.Space(12);
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(8);
        }
    }
}
