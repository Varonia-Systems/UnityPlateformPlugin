using System;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using VBO_Ultimate.Runtime.Scripts.Input;

namespace VaroniaBackOffice
{
    /// <summary>
    /// Inspector custom pour <see cref="MQTTInput"/> — style cohérent avec les autres
    /// éditeurs Varonia. Indique si un device de GlobalConfig.Devices correspond au
    /// Controller choisi et affiche un maximum d'infos (index, MAC, état runtime).
    /// </summary>
    [CustomEditor(typeof(MQTTInput))]
    public class MQTTInputEditor : Editor
    {
        // ── Palette (alignée sur les autres éditeurs) ──
        static readonly Color ColCard   = new Color(0.15f, 0.15f, 0.19f, 1f);
        static readonly Color ColAccent = new Color(0.30f, 0.85f, 0.65f, 1f);
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

            var mqtt = (MQTTInput)target;

            GUILayout.Space(4);
            GUILayout.Label("MQTT INPUT", _header);
            GUILayout.Label("Script d'input simplifié — résout le 1er device de GlobalConfig.Devices " +
                            "dont le Controller correspond.", new GUIStyle(EditorStyles.miniLabel) { wordWrap = true, normal = { textColor = ColMuted } });
            GUILayout.Space(6);

            // ── Carte 1 : Controller choisi ──
            DrawCard("CONTROLLER", ColAccent, () =>
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("controller"),
                    new GUIContent("Controller", "Type de contrôleur à rechercher dans GlobalConfig.Devices."));
            });

            // ── Carte 2 : Résolution (config en edit, runtime en play) ──
            var ctrl = mqtt.controller;
            int ctrlVal = (int)ctrl;

            if (Application.isPlaying)
            {
                bool resolved = mqtt.Resolved;
                DrawCard("RUNTIME", resolved ? ColAccent : ColWarn, () =>
                {
                    DrawStatus(resolved,
                        resolved ? "Device résolu et abonné" : "En attente / aucun device trouvé");
                    KV("Controller", $"{ctrl} ({ctrlVal})");
                    KV("Weapon index", resolved ? mqtt.WeaponIndex.ToString() : "—");
                    KV("MAC", string.IsNullOrEmpty(mqtt.Mac) ? "—" : mqtt.Mac);

                    if (resolved)
                    {
                        bool conn = VaroniaInput.GetIsConnected(mqtt.WeaponIndex);
                        KVColor("Connected", conn ? "yes" : "no", conn ? ColAccent : ColError);

                        bool p = VaroniaInput.GetButton(mqtt.WeaponIndex, VaroniaButton.Primary);
                        bool s = VaroniaInput.GetButton(mqtt.WeaponIndex, VaroniaButton.Secondary);
                        bool t = VaroniaInput.GetButton(mqtt.WeaponIndex, VaroniaButton.Tertiary);
                        bool q = VaroniaInput.GetButton(mqtt.WeaponIndex, VaroniaButton.Quaternary);
                        KVColor("Primary (tir) [1]", p ? "● ON" : "○ off", p ? ColAccent : ColMuted);
                        KVColor("Secondary [2]",     s ? "● ON" : "○ off", s ? ColAccent : ColMuted);
                        KVColor("Tertiary [3]",      t ? "● ON" : "○ off", t ? ColAccent : ColMuted);
                        KVColor("Quaternary [4]",    q ? "● ON" : "○ off", q ? ColAccent : ColMuted);
                    }
                });
                Repaint(); // live
            }
            else
            {
                // Edit mode : on lit GlobalConfig.json pour prévisualiser le match.
                DrawMatchPreview(ctrl, ctrlVal);
            }

            serializedObject.ApplyModifiedProperties();
        }

        // ── Prévisualisation du match en mode édition (lecture GlobalConfig.json) ──
        private void DrawMatchPreview(Controller ctrl, int ctrlVal)
        {
            string path = ConfigPath();
            JArray devices = null;
            string error = null;
            try
            {
                if (File.Exists(path))
                {
                    var root = JObject.Parse(File.ReadAllText(path));
                    devices = root["Devices"] as JArray;
                }
                else error = "GlobalConfig.json introuvable.";
            }
            catch (Exception e) { error = "Lecture JSON impossible : " + e.Message; }

            int    foundIndex = -1;
            string foundSerial = null;
            int    deviceCount = devices?.Count ?? 0;

            if (devices != null)
            {
                for (int i = 0; i < devices.Count; i++)
                {
                    int? c = devices[i]?["Controller"]?.Value<int?>();
                    if (c.HasValue && c.Value == ctrlVal)
                    {
                        foundIndex  = i;
                        foundSerial = devices[i]?["SerialNumber"]?.Value<string>();
                        break;
                    }
                }
            }

            bool found = foundIndex >= 0;
            DrawCard("MATCH (depuis GlobalConfig)", found ? ColAccent : (error != null ? ColError : ColWarn), () =>
            {
                if (error != null)
                {
                    DrawStatus(false, error);
                    KV("Controller", $"{ctrl} ({ctrlVal})");
                    return;
                }

                DrawStatus(found,
                    found ? "Controller trouvé dans Devices"
                          : $"Aucun device avec ce Controller ({deviceCount} device(s))");

                KV("Controller", $"{ctrl} ({ctrlVal})");
                KV("Devices", deviceCount.ToString());

                if (found)
                {
                    KV("→ Index (weaponIndex)", foundIndex.ToString());
                    if (string.IsNullOrEmpty(foundSerial))
                        KVColor("→ Serial (MAC)", "VIDE — abonnement impossible", ColError);
                    else
                        KV("→ Serial (MAC)", foundSerial);
                }
                else if (deviceCount == 0)
                {
                    EditorGUILayout.Space(2);
                    GUILayout.Label("La liste Devices est vide. Ajoute des devices dans Varonia > GlobalConfig.",
                        new GUIStyle(EditorStyles.miniLabel) { wordWrap = true, normal = { textColor = ColMuted } });
                }
            });

            GUILayout.Space(2);
            GUILayout.Label("→ " + path, new GUIStyle(EditorStyles.miniLabel) { wordWrap = true, normal = { textColor = ColMuted } });
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

        private void KV(string key, string val)      => KVColor(key, val, ColText);

        private void KVColor(string key, string val, Color valColor)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(key, _kvKey, GUILayout.Width(150));
            var st = new GUIStyle(_kvVal) { normal = { textColor = valColor } };
            GUILayout.Label(val, st);
            EditorGUILayout.EndHorizontal();
        }
    }
}
