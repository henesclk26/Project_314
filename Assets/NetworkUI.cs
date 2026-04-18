using Unity.Netcode;
using UnityEngine;

public class NetworkUI : MonoBehaviour
{
    public GameObject menuCamera;

    private void Start()
    {
        if (menuCamera == null)
        {
            menuCamera = GameObject.Find("Camera"); 
            // Varsa "Main Camera" ismiyle de arayabilir
            if (menuCamera == null)
            {
                menuCamera = GameObject.Find("Main Camera");
            }
        }
    }

    private void Update()
    {
        // Oyuna giriş yapılana kadar farenin kullanılabilir (kilitsiz) olmasını sağla
        if (NetworkManager.Singleton != null && !NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsServer)
        {
            if (Cursor.lockState != CursorLockMode.None)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }
    }

#if UNITY_EDITOR
    private void OnGUI()
    {
        if (NetworkManager.Singleton == null) return;

        if (!NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsServer)
        {
            GUILayout.BeginArea(new Rect(10, 10, 300, 300));
            if (GUILayout.Button("Host (Oyunu Kur)", GUILayout.Height(50)))
            {
                NetworkManager.Singleton.StartHost();
                KamerayiKapat();
            }
            if (GUILayout.Button("Client (Oyuna Katıl)", GUILayout.Height(50)))
            {
                NetworkManager.Singleton.StartClient();
                KamerayiKapat();
            }
            GUILayout.EndArea();
        }
    }
#endif

    private void KamerayiKapat()
    {
        if (menuCamera != null)
        {
            menuCamera.SetActive(false);
        }
    }
}