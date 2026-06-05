using System;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using VBO_Ultimate.Runtime.Scripts.Input;

namespace VaroniaBackOffice
{
    /// <summary>Inspector custom pour <see cref="VaroniaInputEvents"/> — style cohérent
    /// avec les autres éditeurs Varonia : cible, résolution, état live, puis les events.</summary>
    [CustomEditor(typeof(VaroniaInputEvents))]
    public class VaroniaInputEventsEditor : Editor
    {
        static readonly Color ColCard   = new Color(0.15f, 0.15f, 0.19f, 1f);
        static readonly Color ColAccent = new Color(0.30f, 0.85f, 0.65f, 1f);
        static readonly Color ColBlue   = new Color(0.40f, 0.70f, 1.00f, 1f);
        static readonly Color ColWarn   = new Color(1.00f, 0.60f, 0.10f, 1f);
        static readonly Color ColError  = new Color(1.00f, 0.40f, 0.40f, 1f);
        static readonly Color ColText   = new Color(0.92f, 0.92f, 0.95f, 1f);
        static readonly Color ColMuted  = new Color(0.55f, 0.55f, 0.62f, 1f);

        static GUIStyle _header, _section, _kvKey, _kvVal, _cardStyle;
        static Texture2D _cardTex;
        static bool _styles;

        void EnsureStyles()
        {
            if (_styles && _cardTex != null) return;
            _styles = true;
            _cardTex = MakeTex(ColCard);
            _cardStyle = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(12, 12, 10, 10),
                margin  = new RectOffset(0, 0, 4, 4),
                normal  = { background = _cardTex }
            };
            _header  = new GUIStyle(EditorStyles.boldLabel) { fontSize = 15, normal = { textColor = ColText } };
            _section = new GUIStyle(EditorStyles.boldLabel) { fontSize = 10, normal = { textColor = ColMuted } };
            _kvKey   = new GUIStyle(EditorStyles.label)     { normal = { textColor = ColMuted } };
            _kvVal   = new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = ColText } };
        }

        static Texture2D MakeTex(Color c)
        {
            var t = new Texture2D(1, 1);
            t.SetPixel(0, 0, c); t.Apply();
            t.hideFlags = HideFlags.HideAndDontSave;
            return t;
        }

        public override void OnInspectorGUI()
        {
            EnsureStyles();
            serializedObject.Update();

            var comp = (VaroniaInputEvents)target;

            GUILayout.Space(4);
            GUILayout.Label("VARONIA INPUT EVENTS", _header);
            GUILayout.Label("Expose les inputs d'une arme en UnityEvents. Écoute VaroniaInput " +
                            "(toute source : MQTT, Striker, souris debug…).",
                new GUIStyle(EditorStyles.miniLabel) { wordWrap = true, normal = { textColor = ColMuted } });
            GUILayout.Space(6);

            // ── Carte 1 : Cible ──
            var byCtrlProp = serializedObject.FindProperty("matchByController");
            DrawCard("CIBLE", ColBlue, () =>
            {
                EditorGUILayout.PropertyField(byCtrlProp,
                    new GUIContent("Match By Controller", "Résoudre l'arme via le 1er device de GlobalConfig.Devices qui matche le Controller."));

                if (byCtrlProp.boolValue)
                {
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("controller"),
                        new GUIContent("Controller", "Type de contrôleur à rechercher dans GlobalConfig.Devices."));
                    GUILayout.Space(4);
                    DrawControllerMatch(comp.controller);
                }
                else
                {
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("weaponIndex"),
                        new GUIContent("Weapon Index", "Index d'arme ciblé directement."));
                }
            });

            // ── Carte 2 : Runtime (live) ──
            if (Application.isPlaying)
            {
                DrawCard("RUNTIME", comp.Resolved ? ColAccent : ColWarn, () =>
                {
                    DrawStatus(comp.Resolved, comp.Resolved ? "Cible résolue — events actifs" : "Cible non résolue");
                    KV("Weapon index", comp.Resolved ? comp.ResolvedIndex.ToString() : "—");

                    if (comp.Resolved)
                    {
                        DrawBtn("Primary [1]",   VaroniaInput.GetButton(comp.ResolvedIndex, VaroniaButton.Primary));
                        DrawBtn("Secondary [2]", VaroniaInput.GetButton(comp.ResolvedIndex, VaroniaButton.Secondary));
                        DrawBtn("Tertiary [3]",  VaroniaInput.GetButton(comp.ResolvedIndex, VaroniaButton.Tertiary));
                        DrawBtn("Quaternary [4]",VaroniaInput.GetButton(comp.ResolvedIndex, VaroniaButton.Quaternary));
                    }
                });
                Repaint();
            }

            // ── Carte 3 : Events ──
            DrawCard("EVENTS", ColAccent, () =>
            {
                DrawEventPair("Primary (tir)", "onPrimaryDown",    "onPrimaryUp");
                DrawEventPair("Secondary",     "onSecondaryDown",  "onSecondaryUp");
                DrawEventPair("Tertiary",      "onTertiaryDown",   "onTertiaryUp");
                DrawEventPair("Quaternary",    "onQuaternaryDown", "onQuaternaryUp");

                GUILayout.Space(4);
                GUILayout.Label("GÉNÉRIQUE", _section);
                EditorGUILayout.PropertyField(serializedObject.FindProperty("onAnyButton"),
                    new GUIContent("On Any Button (bouton, pressé)"));
            });

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawEventPair(string title, string downProp, string upProp)
        {
            GUILayout.Label(title.ToUpper(), _section);
            EditorGUILayout.PropertyField(serializedObject.FindProperty(downProp), new GUIContent("▼ Down (appui)"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty(upProp),   new GUIContent("▲ Up (relâché)"));
            GUILayout.Space(4);
        }

        // ── Prévisualisation du match Controller en mode édition ──
        private void DrawControllerMatch(Controller ctrl)
        {
            if (Application.isPlaying) return; // en play, la carte RUNTIME affiche déjà l'index résolu

            int ctrlVal = (int)ctrl;
            string path = ConfigPath();
            JArray devices = null;
            string error = null;
            try
            {
                if (File.Exists(path)) devices = JObject.Parse(File.ReadAllText(path))["Devices"] as JArray;
                else error = "GlobalConfig.json introuvable.";
            }
            catch (Exception e) { error = "Lecture JSON impossible : " + e.Message; }

            int foundIndex = -1;
            if (devices != null)
                for (int i = 0; i < devices.Count; i++)
                {
                    int? c = devices[i]?["Controller"]?.Value<int?>();
                    if (c.HasValue && c.Value == ctrlVal) { foundIndex = i; break; }
                }

            if (error != null)
                EditorGUILayout.HelpBox(error, MessageType.None);
            else if (foundIndex >= 0)
                EditorGUILayout.HelpBox($"✓ Match : {ctrl} → weaponIndex {foundIndex}", MessageType.Info);
            else
                EditorGUILayout.HelpBox($"✗ Aucun device avec Controller={ctrl} dans GlobalConfig.Devices ({devices?.Count ?? 0}).", MessageType.Warning);
        }

        private static string ConfigPath()
        {
            string root = Application.persistentDataPath.Replace(
                Application.companyName + "/" + Application.productName, "Varonia");
            return Path.Combine(root, "GlobalConfig.json");
        }

        // ── Helpers UI ──
        private void DrawCard(string title, Color accent, Action body)
        {
            Rect r = EditorGUILayout.BeginVertical(_cardStyle);
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 2f), accent);
            GUILayout.Space(2);
            GUILayout.Label(title, _section);
            GUILayout.Space(2);
            body();
            EditorGUILayout.EndVertical();
            GUILayout.Space(6);
        }

        private void DrawStatus(bool ok, string text)
        {
            EditorGUILayout.BeginHorizontal();
            var dot = new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = ok ? ColAccent : ColError } };
            GUILayout.Label(ok ? "✓" : "✗", dot, GUILayout.Width(16));
            GUILayout.Label(text, new GUIStyle(EditorStyles.label) { wordWrap = true, normal = { textColor = ok ? ColAccent : ColError } });
            EditorGUILayout.EndHorizontal();
            GUILayout.Space(2);
        }

        private void DrawBtn(string label, bool on)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(label, _kvKey, GUILayout.Width(150));
            var st = new GUIStyle(_kvVal) { normal = { textColor = on ? ColAccent : ColMuted } };
            GUILayout.Label(on ? "● ON" : "○ off", st);
            EditorGUILayout.EndHorizontal();
        }

        private void KV(string key, string val)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(key, _kvKey, GUILayout.Width(150));
            GUILayout.Label(val, _kvVal);
            EditorGUILayout.EndHorizontal();
        }
    }
}
