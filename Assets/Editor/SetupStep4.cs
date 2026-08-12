using UnityEngine;
using UnityEditor;
using Unity.Netcode;
using System.Collections.Generic;

public class SetupStep4
{
    [MenuItem("Project 314/Setup Step 4")]
    public static void Execute()
    {
        // Find main Managers object or create it
        GameObject managersObj = GameObject.Find("Managers");
        if (managersObj == null)
        {
            managersObj = new GameObject("Managers");
            managersObj.AddComponent<NetworkObject>();
        }

        // Ensure RoleManager
        if (managersObj.GetComponent<RoleManager>() == null)
            managersObj.AddComponent<RoleManager>();

        // Ensure MatchFlowManager
        if (managersObj.GetComponent<MatchFlowManager>() == null)
            managersObj.AddComponent<MatchFlowManager>();

        // Ensure MissionManager is present somewhere
        MissionManager mm = Object.FindFirstObjectByType<MissionManager>();
        if (mm != null && mm.gameObject.GetComponent<TaskManager>() == null)
        {
            mm.gameObject.AddComponent<TaskManager>();
        }
        else if (mm == null && managersObj.GetComponent<TaskManager>() == null)
        {
            managersObj.AddComponent<TaskManager>();
        }

        TaskManager taskManager = Object.FindFirstObjectByType<TaskManager>();

        if (taskManager != null)
        {
            // Ensure NetworkObject
            if (taskManager.GetComponent<NetworkObject>() == null)
            {
                taskManager.gameObject.AddComponent<NetworkObject>();
            }

            // Create TaskDefinition Assets
            System.IO.Directory.CreateDirectory("Assets/ScriptableObjects/Tasks");
            
            string[] basicTasks =
            {
                "MissionComputer",
                "WaveFrequency",
                "CircuitMission",
                "PressureTerminal",
                "ReactorTerminal"
            };
            List<TaskDefinition> tasks = new List<TaskDefinition>();

            foreach (string t in basicTasks)
            {
                string path = $"Assets/ScriptableObjects/Tasks/{t}.asset";
                TaskDefinition def = AssetDatabase.LoadAssetAtPath<TaskDefinition>(path);
                if (def == null)
                {
                    def = ScriptableObject.CreateInstance<TaskDefinition>();
                    def.TaskID = t;
                    def.DisplayName = t == "MissionComputer" ? "Bilgisayar Görevi" :
                                      t == "WaveFrequency" ? "Frekans Ayarı" : "Devre Tamiri";
                    def.RoomName = "Ana İstasyon";
                    def.IsCooperative = t == "PressureTerminal" || t == "ReactorTerminal";
                    def.RequiredVillagers = def.IsCooperative ? 3 : 1;
                    def.MinLivingVillagersToOffer = def.RequiredVillagers;
                    def.KillerCanReceive = false;
                    AssetDatabase.CreateAsset(def, path);
                }
                else if (t == "PressureTerminal" || t == "ReactorTerminal")
                {
                    def.IsCooperative = true;
                    def.RequiredVillagers = 3;
                    def.MinLivingVillagersToOffer = 3;
                    def.KillerCanReceive = false;
                    EditorUtility.SetDirty(def);
                }
                tasks.Add(def);
            }

            taskManager.AvailableTasks = tasks;
            EditorUtility.SetDirty(taskManager);
        }

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
        Debug.Log("Step 4 Setup Complete!");
    }
}
