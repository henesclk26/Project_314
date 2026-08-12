using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;
using Unity.Netcode;

public class SetupStep3
{
    [MenuItem("Project 314/Setup Step 3")]
    public static void Execute()
    {
        // 1. Setup MeetingManager GameObject
        GameObject meetingMgrObj = GameObject.Find("MeetingManager");
        if (meetingMgrObj == null) {
            meetingMgrObj = new GameObject("MeetingManager");
        }
        
        if (meetingMgrObj.GetComponent<NetworkObject>() == null)
            meetingMgrObj.AddComponent<NetworkObject>();
            
        if (meetingMgrObj.GetComponent<MeetingManager>() == null)
            meetingMgrObj.AddComponent<MeetingManager>();
            
        if (meetingMgrObj.GetComponent<MeetingUIManager>() == null)
            meetingMgrObj.AddComponent<MeetingUIManager>();
            
        var uiDoc = meetingMgrObj.GetComponent<UIDocument>();
        if (uiDoc == null)
            uiDoc = meetingMgrObj.AddComponent<UIDocument>();
            
        // Load UXML and assign
        var visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UI Toolkit/MeetingScreen.uxml");
        uiDoc.visualTreeAsset = visualTreeAsset;
        
        // 2. Setup MatchFlowManager ReportableBody Prefab
        var mfm = Object.FindFirstObjectByType<MatchFlowManager>();
        if (mfm != null && mfm.reportableBodyPrefab == null)
        {
            GameObject bodyPrefab = new GameObject("ReportableBody");
            bodyPrefab.AddComponent<NetworkObject>();
            bodyPrefab.AddComponent<ReportableBody>();
            
            GameObject capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            capsule.transform.parent = bodyPrefab.transform;
            capsule.transform.localPosition = new Vector3(0, 0.5f, 0);
            capsule.transform.localRotation = Quaternion.Euler(0, 0, 90);
            
            System.IO.Directory.CreateDirectory("Assets/Prefabs");
            GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(bodyPrefab, "Assets/Prefabs/ReportableBody.prefab");
            Object.DestroyImmediate(bodyPrefab);
            
            mfm.reportableBodyPrefab = savedPrefab;
            
            var netManager = Object.FindFirstObjectByType<NetworkManager>();
            if (netManager != null) {
                netManager.NetworkConfig.Prefabs.Add(new NetworkPrefab() { Prefab = savedPrefab });
                EditorUtility.SetDirty(netManager);
            }
            EditorUtility.SetDirty(mfm);
        }
        
        // 3. Create Emergency Button
        GameObject emButton = GameObject.Find("EmergencyButton");
        if (emButton == null)
        {
            emButton = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            emButton.name = "EmergencyButton";
            emButton.transform.localScale = new Vector3(0.5f, 0.1f, 0.5f);
            emButton.transform.position = new Vector3(0, 1, 0); 
            
            var col = emButton.GetComponent<Collider>();
            if (col == null) emButton.AddComponent<BoxCollider>();
            
            emButton.AddComponent<EmergencyButtonInteractable>();
            emButton.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Standard")) { color = Color.red };
        }
        
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
        Debug.Log("Step 3 Setup Complete!");
    }
}
