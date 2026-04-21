using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;

namespace SwitchToggleMission
{
    public class SwitchMinigameUI : MonoBehaviour
    {
        public static SwitchMinigameUI Instance { get; private set; }

        public GameObject minigamePanel;
        public SwitchToggle[] switches;
        public TextMeshProUGUI statusText;
        public TextMeshProUGUI taskCompleteText;

        private InteractableObject currentObject;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            SetPanelActive(false);
            SetTaskCompleteVisible(false);
        }

        private void OnDisable()
        {
            ReleaseCurrentObject();
        }

        private void OnDestroy()
        {
            CancelInvoke();
            ReleaseCurrentObject();

            if (Instance == this)
            {
                Instance = null;
            }
        }

        public void OpenMinigame(InteractableObject source)
        {
            CancelInvoke();

            if (currentObject != null && currentObject != source)
            {
                currentObject.ReleaseObject();
            }

            currentObject = source;
            ResetAllSwitches();
            SetPanelActive(true);
            SetTaskCompleteVisible(false);
            SetCursorState(true);
            UpdateStatus();
        }

        public void CloseMinigame()
        {
            CancelInvoke(nameof(CloseMinigame));
            SetPanelActive(false);
            ReleaseCurrentObject();
            SetCursorState(false);
        }

        public void UpdateStatus()
        {
            int totalSwitchCount = switches != null ? switches.Length : 0;
            int activeSwitchCount = 0;

            if (switches != null)
            {
                for (int i = 0; i < switches.Length; i++)
                {
                    if (switches[i] != null && switches[i].IsOn)
                    {
                        activeSwitchCount++;
                    }
                }
            }

            if (statusText != null)
            {
                statusText.color = Color.white;
                statusText.text = $"Aktif: {activeSwitchCount} / {totalSwitchCount}";
            }

            if (totalSwitchCount > 0 && activeSwitchCount == totalSwitchCount)
            {
                if (statusText != null)
                {
                    statusText.text = "✓ Sistem Aktif!";
                    statusText.color = Color.green;
                }

                ShowTaskComplete();
                CancelInvoke(nameof(CloseMinigame));
                Invoke(nameof(CloseMinigame), 1.5f);
            }
        }

        private void ResetAllSwitches()
        {
            if (switches == null)
            {
                return;
            }

            for (int i = 0; i < switches.Length; i++)
            {
                if (switches[i] != null)
                {
                    switches[i].ResetSwitch();
                }
            }
        }

        private void ShowTaskComplete()
        {
            if (taskCompleteText == null)
            {
                return;
            }

            taskCompleteText.text = "Görev Tamamlandı!";
            taskCompleteText.color = Color.green;
            taskCompleteText.gameObject.SetActive(true);

            CancelInvoke(nameof(HideTaskComplete));
            Invoke(nameof(HideTaskComplete), 2f);
        }

        private void HideTaskComplete()
        {
            SetTaskCompleteVisible(false);
        }

        private void SetPanelActive(bool isActive)
        {
            if (minigamePanel != null)
            {
                minigamePanel.SetActive(isActive);
            }
        }

        private void SetTaskCompleteVisible(bool isVisible)
        {
            if (taskCompleteText != null)
            {
                taskCompleteText.gameObject.SetActive(isVisible);
            }
        }

        private void SetCursorState(bool isUnlocked)
        {
            Cursor.visible = isUnlocked;
            Cursor.lockState = isUnlocked ? CursorLockMode.None : CursorLockMode.Locked;
        }

        private void ReleaseCurrentObject()
        {
            if (currentObject != null)
            {
                currentObject.ReleaseObject();
                currentObject = null;
            }
        }
    }
}
