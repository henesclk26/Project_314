// ============================================================================
// RandomEventType.cs
// Tüm random event türlerini tanımlayan enum.
// Yeni event eklemek için buraya yeni bir satır eklemeniz yeterlidir.
// ============================================================================

/// <summary>
/// Oyundaki tüm random event türlerini tanımlar.
/// None = Aktif event yok (varsayılan / geçiş durumu).
/// Yeni event eklemek için bu enum'a yeni bir değer ekleyin.
/// </summary>
public enum RandomEventType
{
    None = 0,
    ReverseMovement = 1,
    InvertScreen = 2,
    RandomTeleport = 3
}
