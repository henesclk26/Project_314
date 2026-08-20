#if UNITY_EDITOR
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// One-click editor preparation and audit for the demo map. It intentionally
/// touches only the static Building environment; player, doors, lights and
/// network objects remain dynamic.
/// </summary>
public static class Project314OptimizationTools
{
    private const string MainScenePath = "Assets/Scenes/sci-fi-map.unity";

    [MenuItem("Project 314/Optimization/Prepare Static Environment and Bake Occlusion")]
    public static void PrepareStaticEnvironment()
    {
        Scene scene = SceneManager.GetSceneByPath(MainScenePath);
        if (!scene.IsValid() || !scene.isLoaded)
        {
            scene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
        }

        GameObject building = null;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == "Building")
            {
                building = root;
                break;
            }
        }

        if (building == null)
        {
            Debug.LogError("[Project314 Optimization] Building root bulunamadı.");
            return;
        }

        int marked = 0;
        foreach (Renderer renderer in building.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null || renderer.GetComponentInParent<Animator>() != null)
                continue;

            GameObject target = renderer.gameObject;
            StaticEditorFlags flags = GameObjectUtility.GetStaticEditorFlags(target);
            flags |= StaticEditorFlags.BatchingStatic |
                     StaticEditorFlags.OccluderStatic |
                     StaticEditorFlags.OccludeeStatic;
            GameObjectUtility.SetStaticEditorFlags(target, flags);
            marked++;
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        if (!StaticOcclusionCulling.isRunning)
        {
            bool started = StaticOcclusionCulling.Compute();
            Debug.Log($"[Project314 Optimization] {marked} Building renderer static işaretlendi; occlusion bake başlatıldı: {started}");
        }
        else
        {
            Debug.Log($"[Project314 Optimization] {marked} Building renderer static işaretlendi; mevcut occlusion bake devam ediyor.");
        }
    }

    [MenuItem("Project 314/Optimization/Write Performance Audit")]
    public static void WritePerformanceAudit()
    {
        Scene scene = SceneManager.GetActiveScene();
        Renderer[] renderers = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
        Camera[] cameras = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
        Light[] lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
        LODGroup[] lodGroups = Object.FindObjectsByType<LODGroup>(FindObjectsSortMode.None);

        int staticRenderers = 0;
        foreach (Renderer renderer in renderers)
        {
            if (renderer != null && renderer.gameObject.isStatic)
                staticRenderers++;
        }

        UniversalRenderPipelineAsset urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        StringBuilder report = new StringBuilder();
        report.AppendLine("Project 314 Performance Audit");
        report.AppendLine("Scene: " + scene.path);
        report.AppendLine("Generated: " + System.DateTime.Now.ToString("O"));
        report.AppendLine();
        report.AppendLine("Renderers: " + renderers.Length);
        report.AppendLine("Static renderers: " + staticRenderers);
        report.AppendLine("LOD groups: " + lodGroups.Length);
        report.AppendLine("Cameras: " + cameras.Length);
        report.AppendLine("Lights: " + lights.Length);
        report.AppendLine("Occlusion bake running: " + StaticOcclusionCulling.isRunning);
        report.AppendLine("Occlusion data size: " + StaticOcclusionCulling.umbraDataSize);
        report.AppendLine("SRP batcher: " + GraphicsSettings.useScriptableRenderPipelineBatching);
        report.AppendLine("Quality preset: " + QualitySettings.names[QualitySettings.GetQualityLevel()]);
        report.AppendLine("Texture mip streaming: " + QualitySettings.streamingMipmapsActive);
        report.AppendLine("MSAA: " + QualitySettings.antiAliasing);
        report.AppendLine("Target FPS: " + Application.targetFrameRate);
        report.AppendLine("Network tick rate: " + (NetworkTickRateForReport()));
        if (urp != null)
        {
            report.AppendLine("URP render scale: " + urp.renderScale);
            report.AppendLine("URP HDR: " + urp.supportsHDR);
            report.AppendLine("URP MSAA: " + urp.msaaSampleCount);
            report.AppendLine("URP shadow distance: " + urp.shadowDistance);
        }

        string folder = "Assets/Performance";
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "Project314PerformanceAudit.txt"), report.ToString());
        AssetDatabase.Refresh();
        Debug.Log("[Project314 Optimization] Audit yazıldı: Assets/Performance/Project314PerformanceAudit.txt");
    }

    [MenuItem("Project 314/Optimization/Create Conservative Environment LODs")]
    public static void CreateConservativeEnvironmentLods()
    {
        Scene scene = SceneManager.GetActiveScene();
        GameObject building = null;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == "Building")
            {
                building = root;
                break;
            }
        }

        if (building == null)
        {
            Debug.LogError("[Project314 Optimization] Building root bulunamadı.");
            return;
        }

        int created = 0;
        foreach (Renderer renderer in building.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null || renderer.GetComponentInParent<Animator>() != null ||
                renderer.GetComponent<LODGroup>() != null)
                continue;

            Bounds bounds = renderer.bounds;
            if (bounds.size.magnitude > 0.75f)
                continue;

            LODGroup group = renderer.gameObject.AddComponent<LODGroup>();
            group.SetLODs(new[]
            {
                new LOD(0.02f, new[] { renderer }),
                new LOD(0.005f, System.Array.Empty<Renderer>())
            });
            group.RecalculateBounds();
            created++;
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[Project314 Optimization] Küçük statik çevre parçaları için {created} konservatif LOD grubu oluşturuldu.");
    }

    private static int NetworkTickRateForReport()
    {
        if (Unity.Netcode.NetworkManager.Singleton != null &&
            Unity.Netcode.NetworkManager.Singleton.NetworkConfig != null)
            return (int)Unity.Netcode.NetworkManager.Singleton.NetworkConfig.TickRate;

        GameObject networkRoot = GameObject.Find("NetworkRoot");
        Unity.Netcode.NetworkManager sceneManager = networkRoot != null
            ? networkRoot.GetComponent<Unity.Netcode.NetworkManager>()
            : null;
        return sceneManager != null && sceneManager.NetworkConfig != null
            ? (int)sceneManager.NetworkConfig.TickRate
            : 0;
    }
}
#endif
