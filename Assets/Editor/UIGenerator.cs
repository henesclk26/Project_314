#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEngine.EventSystems;
using TMPro;

public class UIGenerator
{
    [MenuItem("Tools/Generate Lobby UI")]
    public static void Generate()
    {
        var canvasObj = GameObject.Find("Canvas");
        if (canvasObj == null) {
            canvasObj = new GameObject("Canvas");
            var canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasObj.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasObj.AddComponent<GraphicRaycaster>();
        }

        if (Object.FindAnyObjectByType<EventSystem>() == null) {
            var esObj = new GameObject("EventSystem");
            esObj.AddComponent<EventSystem>();
            // Handle Input System correctly
            esObj.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        }

        var managerObj = GameObject.Find("NetworkManager");
        if (managerObj == null) {
            managerObj = new GameObject("NetworkManager");
        }
        var multiManager = managerObj.GetComponent<MultiplayerManager>();
        if (multiManager == null) multiManager = managerObj.AddComponent<MultiplayerManager>();
        var uiManager = managerObj.GetComponent<LobbyUIManager>();
        if (uiManager == null) uiManager = managerObj.AddComponent<LobbyUIManager>();

        System.Func<string, GameObject> CreatePanel = (name) => {
            var p = new GameObject(name);
            p.transform.SetParent(canvasObj.transform, false);
            var pRect = p.AddComponent<RectTransform>();
            pRect.anchorMin = Vector2.zero; pRect.anchorMax = Vector2.one;
            pRect.offsetMin = Vector2.zero; pRect.offsetMax = Vector2.zero;
            var img = p.AddComponent<Image>();
            img.color = new Color(0.1f, 0.1f, 0.15f, 1f);
            return p;
        };

        var selectionPanel = CreatePanel("SelectionPanel");
        var privateGamePanel = CreatePanel("PrivateGamePanel");
        var publicGamePanel = CreatePanel("PublicGamePanel");
        var inLobbyPanel = CreatePanel("InLobbyPanel");

        uiManager.selectionPanel = selectionPanel;
        uiManager.privateGamePanel = privateGamePanel;
        uiManager.publicGamePanel = publicGamePanel;
        uiManager.inLobbyPanel = inLobbyPanel;

        System.Func<GameObject, string, Vector2, Vector2, Button> CreateBtn = (parent, text, pos, size) => {
            var bGo = new GameObject("Btn_" + text);
            bGo.transform.SetParent(parent.transform, false);
            var rect = bGo.AddComponent<RectTransform>();
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;
            var img = bGo.AddComponent<Image>();
            img.color = new Color(0.9f, 0.9f, 0.9f, 1f);
            var btn = bGo.AddComponent<Button>();
            var tGo = new GameObject("Text");
            tGo.transform.SetParent(bGo.transform, false);
            var tRect = tGo.AddComponent<RectTransform>();
            tRect.anchorMin = Vector2.zero; tRect.anchorMax = Vector2.one;
            tRect.offsetMin = Vector2.zero; tRect.offsetMax = Vector2.zero;
            var tComp = tGo.AddComponent<TextMeshProUGUI>();
            tComp.text = text;
            tComp.color = Color.black;
            tComp.alignment = TextAlignmentOptions.Center;
            return btn;
        };

        uiManager.navToPrivateBtn = CreateBtn(selectionPanel, "Private Game", new Vector2(-150, 0), new Vector2(250, 80));
        uiManager.navToPublicBtn = CreateBtn(selectionPanel, "Public Game", new Vector2(150, 0), new Vector2(250, 80));

        uiManager.backFromPrivateBtn = CreateBtn(privateGamePanel, "Back", new Vector2(0, -300), new Vector2(150, 50));
        uiManager.createPrivateBtn = CreateBtn(privateGamePanel, "Host Private", new Vector2(-120, 100), new Vector2(200, 50));
        uiManager.joinPrivateBtn = CreateBtn(privateGamePanel, "Join Private", new Vector2(120, -100), new Vector2(200, 50));

        uiManager.backFromPublicBtn = CreateBtn(publicGamePanel, "Back", new Vector2(0, -300), new Vector2(150, 50));
        uiManager.createPublicBtn = CreateBtn(publicGamePanel, "Host Public", new Vector2(0, 200), new Vector2(200, 50));
        uiManager.refreshLobbiesBtn = CreateBtn(publicGamePanel, "Refresh Lobbies", new Vector2(0, 50), new Vector2(200, 50));

        uiManager.leaveLobbyBtn = CreateBtn(inLobbyPanel, "Leave Lobby", new Vector2(0, -100), new Vector2(200, 50));
        var lobbyTextGo = new GameObject("StatusText");
        lobbyTextGo.transform.SetParent(inLobbyPanel.transform, false);
        var ltRect = lobbyTextGo.AddComponent<RectTransform>();
        ltRect.anchoredPosition = new Vector2(0, 100);
        ltRect.sizeDelta = new Vector2(500, 100);
        uiManager.currentLobbyInfoText = lobbyTextGo.AddComponent<TextMeshProUGUI>();
        uiManager.currentLobbyInfoText.text = "In Lobby...";
        uiManager.currentLobbyInfoText.alignment = TextAlignmentOptions.Center;

        var pfGo = new GameObject("LobbyEntryPrefab");
        var pImg = pfGo.AddComponent<Image>();
        pImg.color = new Color(0.2f, 0.2f, 0.2f, 1f);
        var plRect = pfGo.AddComponent<RectTransform>();
        plRect.sizeDelta = new Vector2(600, 80);

        var t1Go = new GameObject("NameText"); t1Go.transform.SetParent(pfGo.transform, false);
        var t1 = t1Go.AddComponent<TextMeshProUGUI>(); t1.text = "Room Name"; t1.GetComponent<RectTransform>().anchoredPosition = new Vector2(-150, 0); t1.alignment = TextAlignmentOptions.Left;

        var t2Go = new GameObject("PlayersText"); t2Go.transform.SetParent(pfGo.transform, false);
        var t2 = t2Go.AddComponent<TextMeshProUGUI>(); t2.text = "0/14"; t2.GetComponent<RectTransform>().anchoredPosition = new Vector2(50, 0);

        var jBtn = CreateBtn(pfGo, "Join", new Vector2(200, 0), new Vector2(100, 50));

        var ent = pfGo.AddComponent<LobbyListEntry>();
        ent.lobbyNameText = t1;
        ent.playerCountText = t2;
        ent.joinBtn = jBtn;

        uiManager.lobbyEntryPrefab = pfGo;
        pfGo.SetActive(false);

        var svGo = new GameObject("LobbyContainer");
        svGo.transform.SetParent(publicGamePanel.transform, false);
        var svRect = svGo.AddComponent<RectTransform>();
        svRect.anchoredPosition = new Vector2(0, -100);
        svRect.sizeDelta = new Vector2(650, 250);
        var vlg = svGo.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 10;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlHeight = false; vlg.childControlWidth = false;
        uiManager.lobbyListContainer = svGo.transform;

        System.Func<GameObject, string, Vector2, TMP_InputField> CreateInput = (parent, placeholder, pos) => {
            var parentObj = new GameObject("InputField");
            parentObj.transform.SetParent(parent.transform, false);
            var pRect = parentObj.AddComponent<RectTransform>();
            pRect.anchoredPosition = pos; pRect.sizeDelta = new Vector2(300, 50);
            parentObj.AddComponent<Image>().color = Color.white;
            var inp = parentObj.AddComponent<TMP_InputField>();
            var textGo = new GameObject("Text"); textGo.transform.SetParent(parentObj.transform, false);
            var tRect = textGo.AddComponent<RectTransform>(); tRect.anchorMin = Vector2.zero; tRect.anchorMax = Vector2.one;
            var tComp = textGo.AddComponent<TextMeshProUGUI>(); tComp.color = Color.black; 
            tComp.text = placeholder; // For visual testing immediately
            inp.textComponent = tComp;
            return inp;
        };

        uiManager.lobbyNameInput = CreateInput(privateGamePanel, "Enter Lobby Name...", new Vector2(-120, 170));
        uiManager.joinCodeInput = CreateInput(privateGamePanel, "Enter Join Code...", new Vector2(120, -30));

        var sliderGo = new GameObject("Slider");
        sliderGo.transform.SetParent(privateGamePanel.transform, false);
        var sImg = sliderGo.AddComponent<Image>(); sImg.color = Color.black;
        var sr = sliderGo.GetComponent<RectTransform>(); sr.anchoredPosition = new Vector2(-120, 240); sr.sizeDelta = new Vector2(250, 20);
        var fillObj = new GameObject("Fill Area"); fillObj.transform.SetParent(sliderGo.transform, false); 
        var fillRt = fillObj.AddComponent<RectTransform>(); fillRt.anchorMin = Vector2.zero; fillRt.anchorMax = Vector2.one;
        var fillItem = new GameObject("Fill"); fillItem.transform.SetParent(fillObj.transform, false); 
        var fiRt = fillItem.AddComponent<RectTransform>(); fiRt.anchorMin = Vector2.zero; fiRt.anchorMax = Vector2.one;
        fillItem.AddComponent<Image>().color = Color.green;
        var s = sliderGo.AddComponent<Slider>(); s.fillRect = fiRt;
        uiManager.maxPlayersSlider = s;

        privateGamePanel.SetActive(false);
        publicGamePanel.SetActive(false);
        inLobbyPanel.SetActive(false);

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
        Debug.Log("Lobby UI Generatated Successfully!");
    }
}
#endif
