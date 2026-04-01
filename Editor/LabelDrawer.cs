using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(LabelAttribute))]
public class LabelDrawer : PropertyDrawer
{
    public override void OnGUI(Rect pos, SerializedProperty prop, GUIContent label)
    {
        var attr = (LabelAttribute)attribute;
        EditorGUI.PropertyField(pos, prop, new GUIContent(attr.Name));
    }
}