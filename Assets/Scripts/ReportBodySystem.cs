using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Hayatta olan bir oyuncu, öldürülmüş bir oyuncunun cesedinin yanına geldiğinde
/// "E - Cesedi Bildir" yazısı gösterir. E'ye basılırsa toplantı başlatılır.
/// Oyuncu prefabına (KillSystem gibi) eklenir.
/// </summary>
public class ReportBodySystem : NetworkBehaviour
{
    [Header("Ayarlar")]
    public float reportRange = 3.5f;

    private FirstPersonController nearbyDeadBody;
    private FirstPersonController fpc;

    private void Awake()
    {
        fpc = GetComponent<FirstPersonController>();
    }

    private void Start()
    {
        if (IsOwner && RoleManager.Instance != null && RoleManager.Instance.reportBodyText != null)
        {
            RoleManager.Instance.reportBodyText.gameObject.SetActive(false);
        }
    }

    private void Update()
    {
        if (!IsOwner) return;
        if (fpc == null || RoleManager.Instance == null) return;

        // Ana menüdeyse çalışmasın
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "MainMenu") return;

        // Ölüyse bildirim yapamaz
        if (fpc.isDead.Value)
        {
            HideReportPrompt();
            return;
        }

        // Toplantı aktifse çalışmasın
        if (MeetingManager.Instance != null && MeetingManager.Instance.IsMeetingActive)
        {
            HideReportPrompt();
            return;
        }

        // Yakında ölü bir oyuncu (ceset) var mı?
        SearchForDeadBody();

        if (nearbyDeadBody != null)
        {
            ShowReportPrompt();

            if (Input.GetKeyDown(KeyCode.F))
            {
                SubmitReport();
            }
        }
        else
        {
            HideReportPrompt();
        }
    }

    private void SearchForDeadBody()
    {
        nearbyDeadBody = null;

        Collider[] hits = Physics.OverlapSphere(transform.position, reportRange);
        float closestDistance = float.MaxValue;

        foreach (Collider hit in hits)
        {
            FirstPersonController otherFpc = hit.GetComponent<FirstPersonController>();
            if (otherFpc == null) otherFpc = hit.GetComponentInParent<FirstPersonController>();

            // Başka bir oyuncu, ölü olmalı ve networkte aktif olmalı
            if (otherFpc != null && otherFpc != fpc && otherFpc.isDead.Value && otherFpc.IsSpawned)
            {
                float distance = Vector3.Distance(transform.position, otherFpc.transform.position);
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    nearbyDeadBody = otherFpc;
                }
            }
        }
    }

    private void SubmitReport()
    {
        if (nearbyDeadBody == null) return;

        // Toplantıyı başlat (MeetingTrigger üzerinden ServerRpc)
        MeetingTrigger.Singleton?.RequestMeetingServerRpc(NetworkManager.Singleton.LocalClientId);

        nearbyDeadBody = null;
        HideReportPrompt();

        Debug.Log("Bir ceset bildirildi! Toplantı başlatılıyor...");
    }

    private void ShowReportPrompt()
    {
        if (RoleManager.Instance != null && RoleManager.Instance.reportBodyText != null)
        {
            RoleManager.Instance.reportBodyText.gameObject.SetActive(true);
            RoleManager.Instance.reportBodyText.text = "F - Cesedi Bildir";
        }
    }

    private void HideReportPrompt()
    {
        if (RoleManager.Instance != null && RoleManager.Instance.reportBodyText != null)
        {
            RoleManager.Instance.reportBodyText.gameObject.SetActive(false);
        }
    }
}
