using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using TMPro;

public class KillSystem : NetworkBehaviour
{
    [Header("Ayarlar")]
    public float killRange = 3.0f;
    public float cooldownTime = 30.0f;
    
    private float currentCooldown = 0f;
    private FirstPersonController targetToKill;
    private FirstPersonController fpc; // Local Player Controller referansı

    private void Awake()
    {
        // Kendi bedenimizdeki FirstPersonController'ı bulalım
        fpc = GetComponent<FirstPersonController>();
    }

    private void Start()
    {
        // Başlangıç modunda UI'ı RoleManager'dan kapatıyoruz
        if (IsOwner && RoleManager.Instance != null)
        {
            if (RoleManager.Instance.killText != null) RoleManager.Instance.killText.gameObject.SetActive(false);
            if (RoleManager.Instance.cooldownText != null) RoleManager.Instance.cooldownText.gameObject.SetActive(false);
        }
    }

    private void Update()
    {
        // Sadece kendi karakterimizin kontrolünü yapıyoruz
        if (!IsOwner) return;
        
        // Eğer RoleManager tam yüklenmediyse bekle
        if (RoleManager.Instance == null) return;

        // Katil değilse veya ölüyse UI gizlensin (Rol henüz gelmemişse de gizler)
        if (RoleManager.Instance.GetLocalPlayerRole() != PlayerRole.Impostor || fpc.isDead.Value)
        {
            if (RoleManager.Instance.killText != null) RoleManager.Instance.killText.gameObject.SetActive(false);
            if (RoleManager.Instance.cooldownText != null) RoleManager.Instance.cooldownText.gameObject.SetActive(false);
            return;
        }

        // Cooldown Aktif ise Müşteriyi (Katili) geri sayım ile bilgilendir
        if (currentCooldown > 0f)
        {
            currentCooldown -= Time.deltaTime;
            
            // E - Öldür yazısını gizle, sadece sayacı göster
            if (RoleManager.Instance.killText != null) RoleManager.Instance.killText.gameObject.SetActive(false);
            
            if (RoleManager.Instance.cooldownText != null) 
            {
                RoleManager.Instance.cooldownText.gameObject.SetActive(true);
                RoleManager.Instance.cooldownText.text = Mathf.Ceil(currentCooldown).ToString() + "s";
            }
            
            // Bekleme süresi bitene kadar aşağı geçip hedef bulmasını/öldürmesini engelle
            return; 
        }

        // Süre bittiyse sayacı gizle
        if (RoleManager.Instance.cooldownText != null) RoleManager.Instance.cooldownText.gameObject.SetActive(false);

        // 1. Hedef Bulma (OverlapSphere ile optimize tarama)
        FindTarget();

        // 2. Hedef Varsa Geri Bildirim ve Etkileşim
        if (targetToKill != null)
        {
            if (RoleManager.Instance.killText != null) 
            {
                RoleManager.Instance.killText.gameObject.SetActive(true);
                RoleManager.Instance.killText.text = "E - Öldür";
            }

            // KLAVYEDEN E Tuşuna Basıldıysa Öldür
            if (Input.GetKeyDown(KeyCode.E))
            {
                ExecuteKill();
            }
        }
        else
        {
            // Yakında kimse yoksa UI'ı gizle
            if (RoleManager.Instance.killText != null) RoleManager.Instance.killText.gameObject.SetActive(false);
        }
    }

    private void FindTarget()
    {
        targetToKill = null;
        
        // Physics.OverlapSphere ile etrafımızdaki her şeyi tarıyoruz
        Collider[] hits = Physics.OverlapSphere(transform.position, killRange);
        float closestDistance = float.MaxValue;

        foreach (Collider hit in hits)
        {
            // Tag ile de ayrıştırılabilir ancak direkt FirstPersonController arıyoruz
            FirstPersonController otherFpc = hit.GetComponent<FirstPersonController>();
            if (otherFpc == null) otherFpc = hit.GetComponentInParent<FirstPersonController>();

            // Başka bir oyuncu bulunduysa, o kişi kendimiz değilse, ölü değilse ve aktif olarak bağlantılıysa
            if (otherFpc != null && otherFpc != fpc && !otherFpc.isDead.Value && otherFpc.IsSpawned)
            {
                // En yakındakini seçmek için mesafe hesabı (İç içe çok kişi varsa en yakın olanı seçer)
                float distance = Vector3.Distance(transform.position, otherFpc.transform.position);
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    targetToKill = otherFpc;
                }
            }
        }
    }

    private void ExecuteKill()
    {
        if (targetToKill == null) return;

        // Ağ üzerinden ölümü tetikle. (FirstPersonController NetworkBehaviour olduğu için NetworkObjectId özelliğine doğrudan sahiptir)
        ulong targetNetId = targetToKill.NetworkObjectId;
        
        // Bu komut host'u uyarır
        KillPlayerServerRpc(targetNetId);

        // Katili bekleme süresine (cooldown) sok
        currentCooldown = cooldownTime;
        targetToKill = null;
        
        Debug.Log("Bir köylüyü katlettiniz!");
    }

    // AĞ FONKSİYONLARI (Network Sync)

    [ServerRpc]
    private void KillPlayerServerRpc(ulong targetNetworkObjectId)
    {
        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(targetNetworkObjectId, out NetworkObject netObj))
        {
            FirstPersonController victim = netObj.GetComponent<FirstPersonController>();
            if (victim != null)
            {
                victim.deathCause.Value = FirstPersonController.PlayerDeathCause.ImpostorKill;
                victim.corpseHidden.Value = false;
                victim.isDead.Value = true;
            }
        }
        KillPlayerClientRpc(targetNetworkObjectId);
    }

    [ClientRpc]
    private void KillPlayerClientRpc(ulong targetNetworkObjectId)
    {
        // Tüm oyuncuların bilgisayarında çalışan kısım
        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(targetNetworkObjectId, out NetworkObject netObj))
        {
            FirstPersonController victim = netObj.GetComponent<FirstPersonController>();
            if (victim != null)
            {
                // Die fonksiyonu FirstPersonController içerisinde "isDead = true" yapıp hareketi sıfırlıyor.
                victim.Die();
            }
        }
    }
}
