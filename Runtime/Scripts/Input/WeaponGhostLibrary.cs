using System;
using System.Collections.Generic;
using UnityEngine;

namespace VBO_Ultimate.Runtime.Scripts.Input
{
    [Serializable]
    public class WeaponGhostEntry
    {
        public string friendlyName = "New Weapon";
        public Mesh mesh;
        public Vector3 positionOffset = Vector3.zero;
        public Vector3 rotationOffset = Vector3.zero;
    }

    [CreateAssetMenu(fileName = "WeaponGhostLibrary", menuName = "VBO/Weapon Ghost Library")]
    public class WeaponGhostLibrary : ScriptableObject
    {
        public List<WeaponGhostEntry> entries = new List<WeaponGhostEntry>();
    }
}
