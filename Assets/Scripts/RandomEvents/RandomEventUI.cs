// ============================================================================
// RandomEventUI.cs
// Ekranın üst kısmında event status bar'ını gösterir.
// Sadece local oyuncunun (IsOwner) kendi event'ini gösterir.
// ============================================================================

using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Sahneye yerleştirilen bir Canvas altında çalışır.
/// Her oyuncunun kendi ekranında, aktif event'in adını ve kalan süresini gösterir.
/// Inspector'dan Image ve Text referansları atanmalıdır.
/// </summary>
public class RandomEventUI : MonoBehaviour
{
    // =========================================================================
    // INSPECTOR REFERANSLARI
    // =========================================================================

    [Header("UI Elemanları")]
    [Tooltip("Event süresini gösteren dolum bar'ı (Image, Type = Filled)")]
    public Image fillBar;

    [Tooltip("Event adını gösteren TextMeshPro text")]
    public TMP_Text eventNameText;

    [Tooltip("Bar'ın arka plan paneli (event yokken gizlenir)")]
    public GameObject barPanel;

    [Header("Renk Ayarları")]
    [Tooltip("Bar rengi: süre azaldıkça yeşilden kırmızıya geçer")]
    public Color fullColor = new Color(0.2f, 0.8f, 0.2f, 1f);    // Yeşil
    public Color emptyColor = new Color(0.9f, 0.2f, 0.2f, 1f);    // Kırmızı

    // =========================================================================
    // PUBLIC METOTLAR
    // =========================================================================

    /// <summary>
    /// RandomEventController tarafından her frame çağrılır.
    /// Event bar'ını ve event adını günceller.
    /// </summary>
    /// <param name="eventType">Aktif event türü</param>
    /// <param name="normalizedTime">Kalan süre (1 = tam dolu, 0 = bitti)</param>
    public void UpdateBar(RandomEventType eventType, float normalizedTime)
    {
        // Event yoksa UI'ı gizle
        if (eventType == RandomEventType.None)
        {
            if (barPanel != null) barPanel.SetActive(false);
            return;
        }

        // UI'ı göster
        if (barPanel != null) barPanel.SetActive(true);

        // Bar doluluk oranını güncelle
        if (fillBar != null)
        {
            fillBar.fillAmount = normalizedTime;
            // Süre azaldıkça renk yeşilden kırmızıya geçer
            fillBar.color = Color.Lerp(emptyColor, fullColor, normalizedTime);
        }

        // Event adını göster
        if (eventNameText != null)
        {
            eventNameText.text = GetEventDisplayName(eventType);
        }
    }

    // =========================================================================
    // YARDIMCI METOTLAR
    // =========================================================================

    /// <summary>
    /// Event türünü okunabilir bir isme çevirir.
    /// Yeni event eklerken buraya da karşılığını ekleyin.
    /// </summary>
    private string GetEventDisplayName(RandomEventType eventType)
    {
        switch (eventType)
        {
            case RandomEventType.ReverseMovement: return "⚠ REVERSE MOVEMENT";
            case RandomEventType.InvertScreen:    return "🔄 INVERTED SCREEN";
            case RandomEventType.RandomTeleport:  return "⚡ RANDOM TELEPORT";
            default:                              return "";
        }
    }
}
