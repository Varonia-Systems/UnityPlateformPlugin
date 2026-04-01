using UnityEditor;
using UnityEngine;
using VBO_Ultimate.Runtime.Scripts.Input;

namespace VaroniaBackOffice
{
    public static class WeaponOffsetCalibratorContextMenu
    {
        [MenuItem("GameObject/Varonia/Add Weapon Offset Calibrator", false, 20)]
        private static void AddWeaponOffsetCalibrator(MenuCommand command)
        {
            var parent = command.context as GameObject;

            var child = new GameObject("WeaponOffsetCalibrator");
            child.AddComponent<WeaponOffsetCalibrator>();

            if (parent != null)
            {
                GameObjectUtility.SetParentAndAlign(child, parent);
            }

            Undo.RegisterCreatedObjectUndo(child, "Add WeaponOffsetCalibrator");
            Selection.activeGameObject = child;
        }

        [MenuItem("GameObject/Varonia/Add Weapon Offset Calibrator", true)]
        private static bool AddWeaponOffsetCalibratorValidate()
        {
            return true;
        }
    }
}
