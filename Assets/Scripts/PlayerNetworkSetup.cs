using UnityEngine;
using Unity.Netcode;

public class PlayerNetworkSetup : NetworkBehaviour
{
    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (!IsOwner)
        {
            // Eğer bu karakter bizim değilse (ağdaki başka bir oyuncuysa)
            // Kamerasını ve AudioListener'ını kapatıyoruz.
            var cam = GetComponentInChildren<Camera>();
            if (cam != null)
            {
                cam.gameObject.SetActive(false);
                
                var listener = cam.GetComponent<AudioListener>();
                if (listener != null)
                {
                    listener.enabled = false;
                }
            }

            // Diğer oyuncunun karakterinin bizim klavye/mouse girdilerimizle hareket etmemesi için
            // FirstPersonController scriptini kapatıyoruz.
            var fpc = GetComponent("FirstPersonController") as MonoBehaviour;
            if (fpc != null)
            {
                fpc.enabled = false;
            }
        }
    }
}
