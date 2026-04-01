using System.Collections.Generic;
using UnityEngine;

namespace VBO_Ultimate.Runtime.Scripts.Input
{
    
    public class VaroniaWeapon : MonoBehaviour
    {
        public static VaroniaWeapon Instance { get; private set; }

        
        [Header("Weapon Infos")]
        public List<_WeaponInfo> weaponList = new List<_WeaponInfo>();
        
        [Header("Weapons")]
        public List<_Weapon> currentweapons = new List<_Weapon>();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            AutoFillWeaponList();
        }
        
        [ContextMenu("Update Weapon List")]
        private void AutoFillWeaponList()
        {
            weaponList.Clear();
            var infos = Resources.LoadAll<_WeaponInfo>("");
            foreach (var info in infos)
            {
                weaponList.Add(info);
            }
        }
        
        public _WeaponInfo GetWeaponById(int id)
        {
            return weaponList.Find(w => w.ModelId == id);
        }
        
        
    }
    
    
 
    
}
