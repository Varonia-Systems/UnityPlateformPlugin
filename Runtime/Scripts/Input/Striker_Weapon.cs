using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VaroniaBackOffice;
using VBO_Ultimate.Runtime.Scripts.Input;

#if STRIKER_LINK
using StrikerLink.Unity.Runtime.Core;
using StrikerLink.Unity.Runtime.HapticEngine;
#endif

public class Striker_Weapon : _Weapon
{
#if STRIKER_LINK
    private StrikerDevice striker;
    private int _weaponIndex;

    
    IEnumerator Start()
    {
        striker = GetComponent<StrikerDevice>();
        var tracking = GetComponentInParent<VaroniaWeaponTracking>();
        _weaponIndex = tracking != null ? tracking.weaponIndex : 0;
        
        yield return new WaitUntil(() => striker.isConnected);
        yield return new WaitForSeconds(1);
        StartCoroutine(InitEffect());

    }



    void Update()
    {
        int wIdx = _weaponIndex;
        VaroniaBackOffice.VaroniaInput.SetDeviceData(wIdx, striker.isConnected,(int)(striker.batteryLevel*100),0,0,WeaponInfo.DisplayNameModel);

        if (DebugModeOverlay.IsDebugMode) return;

        // (1) Trigger → Primary
        VaroniaBackOffice.VaroniaInput.SetButton(wIdx, VaroniaBackOffice.VaroniaButton.Primary, striker.GetTrigger());
        
        // (2) TouchpadLeft ou TouchpadRight → Secondary
        bool touchpad= striker.GetButton(StrikerLink.Shared.Devices.DeviceFeatures.DeviceButton.TouchpadLeft)
                            || striker.GetButton(StrikerLink.Shared.Devices.DeviceFeatures.DeviceButton.TouchpadRight);
        VaroniaBackOffice.VaroniaInput.SetButton(wIdx, VaroniaBackOffice.VaroniaButton.Secondary, touchpad);
        
        // (3) ReloadTouched -> Tertiary
        VaroniaBackOffice.VaroniaInput.SetButton(wIdx, VaroniaBackOffice.VaroniaButton.Tertiary, striker.GetSensor(StrikerLink.Shared.Devices.DeviceFeatures.DeviceSensor.ReloadTouched));
        
        // (4) SideLeft ou SideRight -> Quaternary
        bool side= striker.GetButton(StrikerLink.Shared.Devices.DeviceFeatures.DeviceButton.SideLeft)
                       || striker.GetButton(StrikerLink.Shared.Devices.DeviceFeatures.DeviceButton.SideRight);
        VaroniaBackOffice.VaroniaInput.SetButton(wIdx, VaroniaBackOffice.VaroniaButton.Quaternary, side);

    }



    IEnumerator InitEffect()
    {
        var settings = VaroniaRuntimeSettings.Load();
        
        GetComponent<StrikerHaptics>().AddToLibrary( settings.InitStrikerLibrary);
        
        
        Color C = Color.blue;
        
        for (int i = 0; i < 3; i++)
        {
            striker.PlaySolidLedEffect(Color.green);
            striker.PlaySolidLedEffect(Color.green, 0, StrikerLink.Shared.Devices.Types.DeviceMavrik.LedGroup.FrontRings);
            yield return new WaitForSeconds(0.3f);
            
            StrikerHaptics.Instance.PlayHaptic(settings.InitStrikerHaptic);
            striker.PlaySolidLedEffect(C);
            striker.PlaySolidLedEffect(C, 0, StrikerLink.Shared.Devices.Types.DeviceMavrik.LedGroup.FrontRings);
            yield return new WaitForSeconds(0.1f);
            
        }
        striker.PlaySolidLedEffect(Color.green, 0, StrikerLink.Shared.Devices.Types.DeviceMavrik.LedGroup.FrontRings);
        striker.PlaySolidLedEffect(Color.green); 
        
    }
    

#endif
    

}
