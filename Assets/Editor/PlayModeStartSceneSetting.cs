using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public class PlayModeStartSceneSetting
{
    private const string ToggleKey = "PlayModeStartFromMainMenu";
    private const string MainMenuScenePath = "Assets/Scenes/MainMenu.unity";

    static PlayModeStartSceneSetting()
    {
        // Editör her yüklendiğinde ayarı uygula
        EditorApplication.delayCall += ApplySetting;
    }

    [MenuItem("Tools/Her Zaman Main Menu'den Başlat")]
    public static void ToggleAction()
    {
        bool isEnabled = EditorPrefs.GetBool(ToggleKey, true);
        EditorPrefs.SetBool(ToggleKey, !isEnabled);
        ApplySetting();
    }

    [MenuItem("Tools/Her Zaman Main Menu'den Başlat", true)]
    public static bool ToggleActionValidate()
    {
        bool isEnabled = EditorPrefs.GetBool(ToggleKey, true);
        Menu.SetChecked("Tools/Her Zaman Main Menu'den Başlat", isEnabled);
        return true;
    }

    private static void ApplySetting()
    {
        bool isEnabled = EditorPrefs.GetBool(ToggleKey, true);
        
        if (isEnabled)
        {
            SceneAsset myWantedStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(MainMenuScenePath);
            if (myWantedStartScene != null)
                EditorSceneManager.playModeStartScene = myWantedStartScene;
            else
                Debug.LogWarning("MainMenu.unity bulunamadı: " + MainMenuScenePath);
        }
        else
        {
            EditorSceneManager.playModeStartScene = null;
        }
    }
}
