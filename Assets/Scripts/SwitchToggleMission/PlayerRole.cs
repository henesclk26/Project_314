using UnityEngine;

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
            // Prefer the project's authoritative role system if present.
            // This keeps SwitchToggleMission in sync with RoleManager (Crewmate/Impostor).
            if (RoleManager.Instance != null)
            {
                return RoleManager.Instance.GetLocalPlayerRole() == global::PlayerRole.Crewmate;
            }

            return role == PlayerRoleType.Villager;
        }
    }
}
