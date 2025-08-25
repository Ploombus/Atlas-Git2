using UnityEngine;
using UnityEngine.UIElements;
using Unity.Entities;

public class Chat : MonoBehaviour
{
    //public static Chat Instance { get; private set; }

    [SerializeField] UIDocument uiDocument;
    private VisualElement root;
    private VisualElement chat;
    private VisualElement logContainer;

    private void OnEnable()
    {
        DontDestroyOnLoad(gameObject);

        root = uiDocument.rootVisualElement;
        chat = root.Q<VisualElement>("Chat");

        chat.style.display = DisplayStyle.Flex;

        if (root != null)
        {
            InitUI();
            Application.logMessageReceived += HandleLog;
            Debug.Log("Chat Enabled - press 'C' to hide/show chat.");
        }
    }
    private void InitUI()
    {
        var sendButton = root.Q<VisualElement>("SendButton");
        sendButton.RegisterCallback<ClickEvent>(SendButton);

        logContainer = root.Q<VisualElement>("LogContainer");
    }
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.C))
        {
            var inputField = root.Q<VisualElement>("InputField");
            if (inputField.panel.focusController.focusedElement != inputField)
            {
                ChatToggle();
            }
        }
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (chat.style.display.value == DisplayStyle.Flex)
            {
                chat.style.display = DisplayStyle.None;
            }
        }

        if (Input.GetKeyDown(KeyCode.Return))
        {
            if (chat.style.display.value == DisplayStyle.Flex)
            {
                SendEnter();
            }
        }
    }

    private void SendButton(ClickEvent evt) { SendMessage(); }
    private void SendEnter() { SendMessage(); }
    public void SendMessage()
    {
        var inputField = root.Q<TextField>("InputField");
        string userInput = inputField.value;

        if (string.IsNullOrWhiteSpace(userInput)) return;

        //Setting the world to client plus a failsafe
        World world = Managers.WorldManager.GetClientWorld();

        if (world == null)
        {
            Debug.LogError("No client world found for chat!");
            return;
        }

        var entityManager = world.EntityManager;

        Entity requestEntity = entityManager.CreateEntity();
        entityManager.AddComponentData(requestEntity, new RequestMessage
        {
            message = userInput
        });

        inputField.Focus();
        ClearInputField();
    }

    private void ChatToggle()
    {
        var chat = root.Q<VisualElement>("Chat");
        var inputField = root.Q<TextField>("InputField");
        var chatIsEnabled = chat.style.display.value;

        if (chatIsEnabled == DisplayStyle.Flex)
        {
            chat.style.display = DisplayStyle.None;
        }
        else
        {
            chat.style.display = DisplayStyle.Flex;
            inputField.schedule.Execute(() =>
            {
                inputField.Focus();
            }).ExecuteLater(1);
        }
    }

    public void ClearInputField()
    {
        var inputField = root.Q<TextField>("InputField");
        inputField.value = "";
        inputField.SelectRange(0, 0);
    }

    private void HandleLog(string logString, string stackTrace, LogType type)
    {
        if (logContainer == null) return;

        Label logLabel = new Label($"[{type}] {logString}");
        logLabel.style.whiteSpace = WhiteSpace.Normal;
        logLabel.style.unityTextAlign = TextAnchor.UpperLeft;

        logContainer.Insert(0, logLabel);

    }
}