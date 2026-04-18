// ============================================================================
// RandomEventController.cs
// Her oyuncunun üzerinde bulunur. 30 saniyelik döngülerle random event atar.
// Server-authoritative: Event seçimi ve teleport sunucu tarafında yapılır.
// Local efektler (ekran ters çevirme, input ters çevirme) client tarafında.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Unity.Netcode;

/// <summary>
/// Her oyuncunun prefab'ına eklenir.
/// - Server event seçer ve NetworkVariable ile tüm client'lara bildirir.
/// - Her client kendi local efektlerini uygular (ekran çevirme, input çevirme).
/// - Teleport gibi pozisyon değişiklikleri server tarafından yapılır.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class RandomEventController : NetworkBehaviour
{
    // =========================================================================
    // Kullanıcının isteği üzerine Random Event sistemi tamamen deaktif edilmiştir.
    // Prefab'larda referans hatası ("Missing Script") çıkmaması için scriptin 
    // içi boşaltılmış ancak sınıf ismi korunmuştur.
    // =========================================================================
}
