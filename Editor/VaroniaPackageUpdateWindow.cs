using UnityEditor;
using UnityEngine;

namespace VaroniaBackOffice.EditorTools
{
    /// <summary>
    /// Fenêtre de suivi des packages Varonia : commit installé vs HEAD du dépôt Git,
    /// mise à jour en un clic, et alerte "à pousser" pour les packages en dossier local.
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

        static GUIStyle _card, _title, _desc, _version;
        static Texture2D _cardTex;

        internal static void Open()
        {
            var w = GetWindow<VaroniaPackageUpdateWindow>(false, "Packages Varonia");
            w.minSize = new Vector2(560, 280);
            w.Show();
            w.Focus();
        }

        internal static void RepaintIfOpen() { if (_open != null) _open.Repaint(); }

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

            bool busy = VaroniaPackageUpdateChecker.IsRunning || VaroniaPackageUpdateChecker.IsUpdating;

            // ── Barre d'actions ──
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(12);
            using (new EditorGUI.DisabledScope(busy))
            {
                if (GUILayout.Button(VaroniaPackageUpdateChecker.IsRunning ? "Vérification…" : "Vérifier maintenant",
                                     GUILayout.Height(26), GUILayout.Width(150)))
                    VaroniaPackageUpdateChecker.Check(openWindowWhenDone: false);

                bool anyUpdate = false;
                foreach (var s in VaroniaPackageUpdateChecker.Statuses)
                    if (s.HasUpdate && !s.IsLocalFolder) { anyUpdate = true; break; }

                using (new EditorGUI.DisabledScope(!anyUpdate))
                    if (GUILayout.Button("Tout mettre à jour", GUILayout.Height(26), GUILayout.Width(150)))
                        VaroniaPackageUpdateChecker.UpdateAll();
            }
            GUILayout.FlexibleSpace();

            bool auto = VaroniaPackageUpdateChecker.AutoCheckEnabled;
            bool next = EditorGUILayout.ToggleLeft(
                new GUIContent(" Vérifier automatiquement (1×/jour)",
                    "Vérification silencieuse au démarrage de l'éditeur. La fenêtre ne s'ouvre que s'il y a du neuf."),
                auto, GUILayout.Width(230));
            if (next != auto) VaroniaPackageUpdateChecker.AutoCheckEnabled = next;

            GUILayout.Space(12);
            EditorGUILayout.EndHorizontal();

            if (VaroniaPackageUpdateChecker.IsUpdating)
            {
                GUILayout.Space(4);
                EditorGUILayout.HelpBox("Mise à jour en cours : " + (VaroniaPackageUpdateChecker.UpdatingName ?? "…") +
                                        " — l'éditeur va recompiler.", MessageType.Info);
            }
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

                // Barre d'accent : vert = à jour, orange = action requise, bleu = en cours, violet = erreur
                Color accent = st.HasUpdate || st.NeedsPush ? ColOrange
                             : st.Error != null ? ColPurple
                             : st.Checked ? ColGreen : ColBlue;
                if (Event.current.type == EventType.Repaint)
                    EditorGUI.DrawRect(new Rect(cardRect.x, cardRect.y, cardRect.width, 2f), accent);

                // Ligne 1 : nom + état
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label(st.DisplayName + (st.IsLocalFolder ? "   (dossier local)" : ""), _title);
                GUILayout.FlexibleSpace();

                if (!st.Checked)
                    GUILayout.Label(st.LocalVersion + "  ·  vérification…", _version);
                else if (st.Error != null)
                    GUILayout.Label(st.LocalVersion, _version);
                else if (st.HasUpdate)
                    GUILayout.Label(st.LocalVersion + "  ·  " + VaroniaPackageStatus.Short(st.LocalHash) + "  →  " + VaroniaPackageStatus.Short(st.RemoteHash),
                        new GUIStyle(_version) { normal = { textColor = ColOrange }, fontStyle = FontStyle.Bold });
                else
                    GUILayout.Label(st.LocalVersion + "  ·  " + VaroniaPackageStatus.Short(st.LocalHash) + "  ✓ à jour",
                        new GUIStyle(_version) { normal = { textColor = ColGreen } });
                EditorGUILayout.EndHorizontal();

                // Ligne 2 : détail
                GUILayout.Space(2);
                if (st.Error != null)
                    GUILayout.Label("⚠  " + st.Error, _desc);
                else if (st.IsLocalFolder && st.NeedsPush)
                    GUILayout.Label("⚠  Travail local non publié : " + st.AheadCount + " commit(s) non poussé(s), " + st.DirtyCount +
                                    " fichier(s) modifié(s) non commité(s). Les autres projets ne le verront pas.", _desc);
                else if (st.IsLocalFolder && st.HasUpdate)
                    GUILayout.Label("Le dépôt distant a avancé : fais un git pull dans le dossier du package.", _desc);
                else if (st.HasUpdate)
                    GUILayout.Label("Un commit plus récent est publié. « Mettre à jour » relance la résolution UPM sur ce commit.", _desc);
                else
                    GUILayout.Label(st.Name, _desc);

                // Ligne 3 : actions
                GUILayout.Space(4);
                EditorGUILayout.BeginHorizontal();
                if (st.HasUpdate && !st.IsLocalFolder)
                {
                    using (new EditorGUI.DisabledScope(busy))
                        if (GUILayout.Button("Mettre à jour", EditorStyles.miniButton, GUILayout.Width(110)))
                            VaroniaPackageUpdateChecker.Update(st);
                }
                if (st.RepoUrl != null && GUILayout.Button("Ouvrir le dépôt", EditorStyles.miniButton, GUILayout.Width(110)))
                    Application.OpenURL(st.RepoUrl);
                if (st.IsLocalFolder && !string.IsNullOrEmpty(st.LocalPath) &&
                    GUILayout.Button("Ouvrir le dossier", EditorStyles.miniButton, GUILayout.Width(110)))
                    EditorUtility.RevealInFinder(st.LocalPath);
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.EndVertical();
                GUILayout.Space(12);
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();

            GUILayout.Space(6);
            GUILayout.Label("Comparaison par commit via git ls-remote (dépôts privés OK). Après « Mettre à jour », pense à check-in manifest.json + packages-lock.json.",
                new GUIStyle(EditorStyles.centeredGreyMiniLabel) { wordWrap = true });
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
            EditorGUILayout.LabelField("Commit installé vs dépôt Git — mise à jour en un clic",
                new GUIStyle(EditorStyles.centeredGreyMiniLabel) { fontSize = 10 });

            GUILayout.Space(8);
            EditorGUILayout.EndVertical();
        }
    }
}
