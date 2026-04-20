using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEditor;

public class CreateVoteCardPrefab
{
    [MenuItem("Tools/Create PlayerVoteCard Prefab")]
    public static void Create()
    {
        // Kök
        var root = new GameObject("PlayerVoteCard");
        var cardRT = root.AddComponent<RectTransform>();
        cardRT.sizeDelta = new Vector2(160, 80);
        var cardBg = root.AddComponent<Image>();
        cardBg.color = new Color(0.15f, 0.15f, 0.35f, 1f);
        var btn = root.AddComponent<Button>();
        var pvc = root.AddComponent<PlayerVoteCard>();
        btn.targetGraphic = cardBg;

        // İsim yazısı
        var nameObj = new GameObject("NameText");
        nameObj.transform.SetParent(root.transform, false);
        var nameRT = nameObj.AddComponent<RectTransform>();
        nameRT.anchorMin = new Vector2(0, 0.4f);
        nameRT.anchorMax = new Vector2(1f, 1f);
        nameRT.offsetMin = new Vector2(8, 0);
        nameRT.offsetMax = new Vector2(-8, -4);
        var nameTMP = nameObj.AddComponent<TextMeshProUGUI>();
        nameTMP.text = "Oyuncu";
        nameTMP.fontSize = 18;
        nameTMP.alignment = TextAlignmentOptions.Center;
        nameTMP.color = Color.white;

        // Alt etiket
        var lbl = new GameObject("VoteLabel");
        lbl.transform.SetParent(root.transform, false);
        var lblRT = lbl.AddComponent<RectTransform>();
        lblRT.anchorMin = new Vector2(0, 0f);
        lblRT.anchorMax = new Vector2(1f, 0.4f);
        lblRT.offsetMin = new Vector2(4, 2);
        lblRT.offsetMax = new Vector2(-4, 0);
        var lblTMP = lbl.AddComponent<TextMeshProUGUI>();
        lblTMP.text = "Oy Ver";
        lblTMP.fontSize = 14;
        lblTMP.alignment = TextAlignmentOptions.Center;
        lblTMP.color = new Color(0.9f, 0.8f, 0.2f, 1f);

        // Oy kullandı göstergesi
        var vi = new GameObject("VotedIndicator");
        vi.transform.SetParent(root.transform, false);
        var viRT = vi.AddComponent<RectTransform>();
        viRT.anchorMin = new Vector2(0.75f, 0.6f);
        viRT.anchorMax = new Vector2(1f, 1f);
        viRT.offsetMin = Vector2.zero;
        viRT.offsetMax = Vector2.zero;
        var viImg = vi.AddComponent<Image>();
        viImg.color = new Color(0.2f, 0.9f, 0.2f, 1f);
        vi.SetActive(false);

        // Referanslar
        pvc.nameText = nameTMP;
        pvc.background = cardBg;
        pvc.voteButton = btn;
        pvc.votedIndicator = vi;

        // Klasör
        if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
            AssetDatabase.CreateFolder("Assets", "Prefabs");
        if (!AssetDatabase.IsValidFolder("Assets/Prefabs/MeetingSystem"))
            AssetDatabase.CreateFolder("Assets/Prefabs", "MeetingSystem");

        string path = "Assets/Prefabs/MeetingSystem/PlayerVoteCard.prefab";
        bool ok;
        PrefabUtility.SaveAsPrefabAsset(root, path, out ok);
        Object.DestroyImmediate(root);

        if (ok) Debug.Log("[CreateVoteCardPrefab] Prefab olusturuldu: " + path);
        else Debug.LogError("[CreateVoteCardPrefab] Prefab olusturulamadi!");

        AssetDatabase.Refresh();
    }
}
