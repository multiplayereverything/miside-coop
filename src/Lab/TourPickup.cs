using UnityEngine;
using Object = UnityEngine.Object;

namespace MiSideTogether
{
    // Lab tour "pickup" (2 players, Scene 6): one player takes the collectible cartridge the way a player does; the item must be
    // gone on every PC. -mt-pickupclient: the client takes it, the host watches.
    //   pickup.gone: (each PC) the item is gone 15 s after the press
    public sealed partial class TogetherRuntime
    {
        public static bool LabPickupClient;
        FlashTaker labPickup;
        GameObject labPickupItem;
        ObjectInteractive labPickupButton;
        float labPickupAt = -1;
        bool labPickupPressed;

        void PickupTour(float now, string scene)
        {
            if (scene != "Scene 6 - BasementFirst" || LocalPlayer == null || now - tourSceneAt < 25) return;
            if (labPickup == null)
            {
                labPickup = Object.FindObjectOfType<FlashTaker>(true);
                if (labPickup == null) { CheckOnce("pickup.gone", false, "no collectible in " + scene); return; }
                labPickupItem = labPickup.objectTake;
                labPickupButton = labPickup.GetComponentInChildren<ObjectInteractive>(true);
                labPickupAt = now;
                Log("Lab: collectible " + SceneKeys.Key(labPickup) + " item " + (labPickupItem != null ? labPickupItem.name : "none"));
                return;
            }
            if (!labPickupPressed && now - labPickupAt >= 3 && Hosting != LabPickupClient && labPickupButton != null)
            {
                labPickupButton.active = true;
                PilotStepTo(labPickupButton);
                if (LabPress(labPickupButton)) { labPickupPressed = true; Log("Lab: this player takes the collectible"); }
            }
            if (now - labPickupAt < 20) return;
            bool gone = labPickupItem == null;
            CheckOnce("pickup.gone", gone, (gone ? "gone" : "still here") + "; taken by the " + (LabPickupClient ? "client" : "host"));
        }
    }
}
