using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;

namespace SwitchToggleMission
{
    public class PlayerRole : MonoBehaviour
    {
        public enum PlayerRoleType
        {
            Villager,
            Killer
        }

        public PlayerRoleType role = PlayerRoleType.Villager;

        public bool IsVillager()
        {
            return role == PlayerRoleType.Villager;
        }
    }
}
