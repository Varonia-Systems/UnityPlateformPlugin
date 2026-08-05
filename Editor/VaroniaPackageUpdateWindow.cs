using UnityEditor;
using UnityEngine;

namespace VaroniaBackOffice.EditorTools
{
    /// <summary>
    /// Fenêtre d'information sur les versions des packages Varonia.
    /// Purement indicative : elle n'installe et ne modifie rien.
    /// </summary>
    internal class VaroniaPackageUpdateWindow : EditorWindow
    {
        private static VaroniaPackageUpdateWindow _open;
        private Vector2 _scroll;

        // ── Palette (alignée sur Varonia Back Office) ──
        static readonly Color ColHeader = new Color(0.15f, 0.15f, 0.18f, 1f);
        static readonly Color ColCard   = new Color(0.18f, 0.18f, 0.22f, 1f);
        static readonly Color ColBlue   = new Color(0.25f, 0.55f, 1.00f, 1f);
        static readonly Color ColGreen  = new Color(0.20f, 0.80f, 0.45f, 1f);
        static readonly Color ColOrange = new Color(1.00f, 0.60f, 0.10f, 1f);
        static readonly Color ColPurple = new Color(0.65f, 0.35f, 1.00f, 1f);
        static readonly Color ColMuted  = new Color(0.60f, 0.60f, 0.66f, 1f);
        static readonly Color ColSep    = new Color(1f, 1f, 1f, 0.06f);

        static GUIStyle _card, _title, _desc, _version;
        static Texture2D _cardTex;

        internal static void Open()
        {
            var w = GetWindow<VaroniaPackageUpdateWindow>(false, "Packages Varonia");
            w.minSize = new Vector2(520, 260);
            w.Show();
            w.Focus();
        }

        internal static void RepaintIfOpen()
        {
            if (_open != null) _open.Repaint();
        }

        private void OnEnable()  { _open = this; }
        private void OnDisable() { if (_open == this) _open = null; }

        private void EnsureStyles()
        {
            if (_card != null && _cardTex != null) return;

            _cardTex = new Texture2D(1, 1);
            _cardTex.SetPixel(0, 0, ColCard);
            _cardTex.Apply();
            _cardTex.hideFlags = HideFlags.HideAndDontSave;

            _card = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(12, 12, 10, 10),
                margin  = new RectOffset(0, 0, 4, 4),
                normal  = { background = _cardTex }
            };
            _title   = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12 };
            _desc    = new GUIStyle(EditorStyles.label) { fontSize = 10, wordWrap = true, normal = { textColor = ColMuted } };
            _version = new GUIStyle(EditorStyles.miniLabel) { fontSize = 11, alignment = TextAnchor.MiddleRight };
        }

        private void OnGUI()
        {
            EnsureStyles();
            DrawHeader();

            GUILayout.Space(6);

            // ── Barre d'actions ──
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(12);

            using (new EditorGUI.DisabledScope(VaroniaPackageUpdateChecker.IsRunning))
                if (GUILayout.Button(VaroniaPackageUpdateChecker.IsRunning ? "Vérification…" : "Vérifier maintenant",
                                     GUILayout.Height(26), GUILayout.Width(160)))
                    VaroniaPackageUpdateChecker.Check(openWindowWhenDone: false);

            GUILayout.FlexibleSpace();

            bool auto = VaroniaPackageUpdateChecker.AutoCheckEnabled;
            bool next = EditorGUILayout.ToggleLeft(
                new GUIContent(" Vérifier automatiquement (1×/jour)",
                    "Vérification silencieuse au démarrage de l'éditeur. La fenêtre ne s'ouvre que si une mise à jour existe."),
                auto, GUILayout.Width(230));
            if (next != auto) VaroniaPackageUpdateChecker.AutoCheckEnabled = next;

            GUILayout.Space(12);
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(6);

            var list = VaroniaPackageUpdateChecker.Statuses;
            if (list.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    VaroniaPackageUpdateChecker.IsRunning
                        ? "Lecture des packages…"
                        : "Aucun package Varonia détecté. Clique sur « Vérifier maintenant ».",
                    MessageType.Info);
                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            foreach (var st in list)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(12);
                Rect cardRect = EditorGUILayout.BeginVertical(_card);

                // Barre d'accent : vert = à jour, orange = mise à jour dispo, bleu = en cours, violet = erreur
                Color accent = st.HasUpdate ? ColOrange
                             : st.Error != null ? ColPurple
                             : st.Checked ? ColGreen : ColBlue;

                if (Event.current.type == EventType.Repaint)
                    EditorGUI.DrawRect(new Rect(cardRect.x, cardRect.y, cardRect.width, 2f), accent);

                // Ligne 1 : nom + versions
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label(st.DisplayName, _title);
                GUILayout.FlexibleSpace();

                if (st.HasUpdate)
                {
                    var up = new GUIStyle(_version) { normal = { textColor = ColOrange }, fontStyle = FontStyle.Bold };
                    GUILayout.Label($"{st.LocalVersion}  →  {st.RemoteVersion}", up);
                }
                else if (st.Error != null)
                {
                    GUILayout.Label(st.LocalVersion, _version);
                }
                else if (st.Checked)
                {
                    var ok = new GUIStyle(_version) { normal = { textColor = ColGreen } };
                    GUILayout.Label($"{st.LocalVersion}  ✓ à jour", ok);
                }
                else
                {
                    GUILayout.Label($"{st.LocalVersion}  ·  vérification…", _version);
                }
                EditorGUILayout.EndHorizontal();

                // Ligne 2 : détail
                GUILayout.Space(2);
                if (st.Error != null)
                    GUILayout.Label("⚠  " + st.Error, _desc);
                else if (st.HasUpdate)
                    GUILayout.Label($"Une version plus récente est publiée sur GitHub ({st.RepoName}).", _desc);
                else
                    GUILayout.Label(st.Name, _desc);

                // Ligne 3 : lien dépôt
                if (st.RepoUrl != null)
                {
                    GUILayout.Space(4);
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("Ouvrir le dépôt", EditorStyles.miniButton, GUILayout.Width(110)))
                        Application.OpenURL(st.RepoUrl);
                    if (GUILayout.Button("Copier l'URL", EditorStyles.miniButton, GUILayout.Width(100)))
                        EditorGUIUtility.systemCopyBuffer = st.RepoUrl + ".git";
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.EndHorizontal();
                }

                EditorGUILayout.EndVertical();
                GUILayout.Space(12);
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();

            GUILayout.Space(6);
            var footer = new GUIStyle(EditorStyles.centeredGreyMiniLabel) { wordWrap = true };
            GUILayout.Label("Information uniquement — aucune mise à jour n'est installée automatiquement.", footer);
            GUILayout.Space(6);
        }

        private void DrawHeader()
        {
            var rect = EditorGUILayout.BeginVertical();
            EditorGUI.DrawRect(rect, ColHeader);

            GUILayout.Space(10);
            var bar = GUILayoutUtility.GetRect(0, 2, GUILayout.ExpandWidth(true));
            float third = bar.width / 3f;
            EditorGUI.DrawRect(new Rect(bar.x, bar.y, third, bar.height), ColBlue);
            EditorGUI.DrawRect(new Rect(bar.x + third, bar.y, third, bar.height), ColGreen);
            EditorGUI.DrawRect(new Rect(bar.x + third * 2, bar.y, third, bar.height), ColPurple);
            GUILayout.Space(8);

            EditorGUILayout.LabelField("PACKAGES VARONIA", new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 16, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white }
            });
            EditorGUILayout.LabelField("Versions locales et versions publiées sur GitHub",
                new GUIStyle(EditorStyles.centeredGreyMiniLabel) { fontSize = 10 });

            GUILayout.Space(8);
            EditorGUILayout.EndVertical();
        }
    }
}
