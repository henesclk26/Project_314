using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Netcode;
using System.Collections;

public class EscapeMenuManager : MonoBehaviour
{
    [Header("ESC Menü Paneli")]
    public GameObject escMenuPanel;

    private bool isMenuOpen = false;
    private bool isReturning = false;

    void Start()
    {
        if (escMenuPanel != null)
            escMenuPanel.SetActive(false);
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape) && !isReturning)
            ToggleMenu();
    }

    public void ToggleMenu()
    {
        isMenuOpen = !isMenuOpen;

        if (escMenuPanel != null)
            escMenuPanel.SetActive(isMenuOpen);

        if (isMenuOpen)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else if (!FirstPersonController.LocalPlayerIsDead)
        {
            if (GameManager.Instance == null || !GameManager.Instance.isGameOver)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }
    }

    public void ReturnToMainMenu()
    {
        if (isReturning) return;
        isReturning = true;
        StartCoroutine(ReturnToMainMenuCoroutine());
    }

    private IEnumerator ReturnToMainMenuCoroutine()
    {
        Time.timeScale = 1f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        if (MultiplayerManager.Instance != null && MultiplayerManager.Instance.HasActiveLobby)
        {
            var leaveTask = MultiplayerManager.Instance.LeaveLobby();
            while (!leaveTask.IsCompleted)
                yield return null;
        }
        else if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            NetworkManager.Singleton.Shutdown();
            yield return new WaitForSecondsRealtime(0.1f);
        }

        var menuController = Object.FindFirstObjectByType<SciFiMenuController>();
        if (menuController != null)
        {
            menuController.ShowMainMenu();
            isReturning = false;
            if (escMenuPanel != null) escMenuPanel.SetActive(false);
            isMenuOpen = false;
            yield break;
        }

        SceneManager.LoadScene("sci-fi-map");
        isReturning = false;
    }
}
