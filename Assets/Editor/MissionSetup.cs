using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using UnityEditor.SceneManagement;

public class MissionSetup
{
    [MenuItem("Tools/Setup Mission")]
    public static void Setup()
    {
        // 1. Create ScriptableObjects
        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder("Assets/Resources/Mission"))
            AssetDatabase.CreateFolder("Assets/Resources", "Mission");

        ComputerData emailData = ScriptableObject.CreateInstance<ComputerData>();
        emailData.computerName = "GALACTIC MAIL";
        emailData.computerType = ComputerType.Email;
        emailData.emails = new System.Collections.Generic.List<EmailData>();
        
        EmailData em1 = new EmailData();
        em1.sender = "Commander Shepard";
        em1.subject = "Mission briefing";
        em1.time = "13:00";
        em1.body = "Report to the CIC for an urgent mission update. The Reapers are moving. Also, the armory code is 7465.";
        
        EmailData em2 = new EmailData();
        em2.sender = "SYSTEM";
        em2.subject = "Critical update";
        em2.time = "12:45";
        em2.body = "System maintenance scheduled for 0200 hours.";
        
        emailData.emails.Add(em1);
        emailData.emails.Add(em2);

        AssetDatabase.CreateAsset(emailData, "Assets/Resources/Mission/EmailComputerData.asset");

        ComputerData pwdData = ScriptableObject.CreateInstance<ComputerData>();
        pwdData.computerName = "SECURITY TERMINAL";
        pwdData.computerType = ComputerType.Password;
        pwdData.correctPassword = "7465";
        pwdData.successMessage = "ACCESS GRANTED";

        AssetDatabase.CreateAsset(pwdData, "Assets/Resources/Mission/PasswordComputerData.asset");
        AssetDatabase.SaveAssets();

        // 2. Scene Setup
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (scene.name != "sci-fi-map")
        {
            Debug.LogError("Please open sci-fi-map scene before running this.");
            return;
        }

        // Find or create UI Object
        GameObject uiObj = GameObject.Find("ComputerUI");
        if (uiObj == null)
        {
            uiObj = new GameObject("ComputerUI");
            var uiDoc = uiObj.AddComponent<UIDocument>();
            var uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UI Toolkit/ComputerScreen.uxml");
            var panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/UI Toolkit/PanelSettings.asset");
            uiDoc.visualTreeAsset = uxml;
            uiDoc.panelSettings = panelSettings;
            uiDoc.sortingOrder = 50; // Above everything
            
            uiObj.AddComponent<ComputerUIManager>();
        }

        // 3. Attach to Computers
        GameObject comp1 = GameObject.Find("MissionComputer"); // Password
        if (comp1 != null)
        {
            if (comp1.GetComponent<Collider>() == null) comp1.AddComponent<BoxCollider>();
            var interact = comp1.GetComponent<ComputerInteractable>();
            if (interact == null) interact = comp1.AddComponent<ComputerInteractable>();
            interact.data = pwdData;
            interact.interactionRange = 4f;
        }
        else
        {
            Debug.LogWarning("MissionComputer not found!");
        }

        GameObject comp2 = GameObject.Find("MissionComputer2"); // Email
        if (comp2 != null)
        {
            if (comp2.GetComponent<Collider>() == null) comp2.AddComponent<BoxCollider>();
            var interact = comp2.GetComponent<ComputerInteractable>();
            if (interact == null) interact = comp2.AddComponent<ComputerInteractable>();
            interact.data = emailData;
            interact.interactionRange = 4f;
        }
        else
        {
            Debug.LogWarning("MissionComputer2 not found!");
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        
        Debug.Log("Mission setup complete!");
    }
}
