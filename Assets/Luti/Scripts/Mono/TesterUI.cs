using Unity.Entities;
using Unity.NetCode;
using UnityEngine;
using UnityEngine.UIElements;
using Managers;

public class TesterUI : MonoBehaviour
{
    public static TesterUI Instance { get; private set; } // ADDED: Static instance

    [SerializeField] private UIDocument _TesterUI;

    // UI Elements
    private Button spawnerButton;
    private Button addResource1Button;
    private Button addResource2Button;
    private IntegerField resource1Input;
    private IntegerField resource2Input;
    private VisualElement buildingUI;
    private Button spawnUnitButton;

    // State tracking
    private bool buildMode = false;
    private Entity selectedBuilding = Entity.Null;
    private bool isOwnerOfSelectedBuilding = false;
    private int cachedResource1Cost = 0;
    private int cachedResource2Cost = 0;
    private bool canAffordCurrent = false;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void Start()
    {
        InitializeUI();
        SubscribeToEvents();
    }

    private void InitializeUI()
    {
        if (_TesterUI == null)
        {
            Debug.LogError("TesterUI: UIDocument is null!");
            return;
        }

        var root = _TesterUI.rootVisualElement;

        spawnerButton = root.Q<Button>("SpawnerButton");
        addResource1Button = root.Q<Button>("AddResource1Button");
        addResource2Button = root.Q<Button>("AddResource2Button");
        resource1Input = root.Q<IntegerField>("Resource1Input");
        resource2Input = root.Q<IntegerField>("Resource2Input");

        spawnerButton.RegisterCallback<ClickEvent>(StartBuildMode);
        //addResource1Button.RegisterCallback<ClickEvent>(AddResource1Amount);
        //addResource2Button.RegisterCallback<ClickEvent>(AddResource2Amount);

        buildingUI = root.Q<VisualElement>("BuildingUIPanel");
        buildingUI.style.display = DisplayStyle.None;

        spawnUnitButton = root.Q<Button>("UnitButton");
        spawnUnitButton.RegisterCallback<ClickEvent>(OnSpawnUnitClicked);

        resource1Input.RegisterCallback<ChangeEvent<int>>((evt) => { resource1Input.value = evt.newValue; });
        resource2Input.RegisterCallback<ChangeEvent<int>>((evt) => { resource2Input.value = evt.newValue; });

        UpdateSpawnerButtonStyle();
    }

    private void SubscribeToEvents()
    {
        BuildingUIEvents.OnBuildingSelected += HandleBuildingSelected;
        BuildingUIEvents.OnBuildingDeselected += HandleBuildingDeselected;
        BuildingUIEvents.OnSpawnCostUpdated += HandleSpawnCostUpdated;
        BuildingUIEvents.OnResourcesUpdated += HandleResourcesUpdated;
        BuildingUIEvents.OnSpawnValidated += HandleSpawnValidation;
    }

    private void OnDestroy()
    {
        // Unsubscribe from events
        BuildingUIEvents.OnBuildingSelected -= HandleBuildingSelected;
        BuildingUIEvents.OnBuildingDeselected -= HandleBuildingDeselected;
        BuildingUIEvents.OnSpawnCostUpdated -= HandleSpawnCostUpdated;
        BuildingUIEvents.OnResourcesUpdated -= HandleResourcesUpdated;
        BuildingUIEvents.OnSpawnValidated -= HandleSpawnValidation;

        if (Instance == this)
        {
            Instance = null;
        }
    }

    // Event Handlers
    private void HandleBuildingSelected(BuildingSelectedEventData data)
    {
        isOwnerOfSelectedBuilding = data.IsOwned;
        selectedBuilding = data.BuildingEntity;
        buildingUI.style.display = DisplayStyle.Flex;

        // Update button text with costs
        if (data.Resource1Cost > 0 || data.Resource2Cost > 0)
        {
            string costText = $"Base Unit\nCost: ";
            if (data.Resource1Cost > 0) costText += $"R1:{data.Resource1Cost} ";
            if (data.Resource2Cost > 0) costText += $"R2:{data.Resource2Cost}";
            spawnUnitButton.text = costText;
        }
        else
        {
            spawnUnitButton.text = "Base Unit";
        }
    }

    private void HandleBuildingDeselected()
    {
        isOwnerOfSelectedBuilding = false;
        selectedBuilding = Entity.Null;
        buildingUI.style.display = DisplayStyle.None;
    }

    private void HandleSpawnCostUpdated(SpawnCostUIData data)
    {
        if (data.BuildingEntity != selectedBuilding) return;

        cachedResource1Cost = data.Resource1Cost;
        cachedResource2Cost = data.Resource2Cost;
        canAffordCurrent = data.CanAfford;

        UpdateUnitButtonDisplay();
        UpdateUnitButtonState();
    }

    private void HandleResourcesUpdated(ResourceUIData data)
    {
        canAffordCurrent = data.CanAffordCurrent;
        UpdateUnitButtonState();
    }

    private void HandleSpawnValidation(SpawnValidationData data)
    {
        if (!data.Success)
        {
            Debug.Log($"Spawn failed: {data.Message}. Server will handle any refunds.");
        }
    }

    // UI Methods
    private void UpdateUnitButtonDisplay()
    {
        string costText = "Base Unit";
        if (cachedResource1Cost > 0 || cachedResource2Cost > 0)
        {
            costText += "\nCost: ";
            if (cachedResource1Cost > 0)
                costText += $"R1:{cachedResource1Cost} ";
            if (cachedResource2Cost > 0)
                costText += $"R2:{cachedResource2Cost}";
        }

        spawnUnitButton.text = costText;
    }

    private void UpdateUnitButtonState()
    {
        spawnUnitButton.SetEnabled(canAffordCurrent);

        if (canAffordCurrent)
        {
            spawnUnitButton.RemoveFromClassList("insufficient-resources");
            spawnUnitButton.tooltip = "";
        }
        else
        {
            spawnUnitButton.AddToClassList("insufficient-resources");

            // Calculate what's missing using PlayerStatsUtils
            PlayerStatsUtils.GetMissingResources(cachedResource1Cost, cachedResource2Cost,
                out int missingR1, out int missingR2);

            if (missingR1 > 0 || missingR2 > 0)
            {
                string missingText = "Missing: ";
                if (missingR1 > 0) missingText += $"R1:{missingR1} ";
                if (missingR2 > 0) missingText += $"R2:{missingR2}";
                spawnUnitButton.tooltip = missingText;
            }
        }
    }

    private void Update()
    {
        if (UIUtility.IsPointerOverUI()) return;

        if (Input.GetMouseButtonDown(0) && buildMode)
        {
            int buildCost = 0; // Buildings are free for now

            // Check if we can afford it (currently free, but structure ready for costs)
            if (PlayerStatsUtils.CanAfford(buildCost, buildCost))
            {
                SpawnBarracksRpcRequest(MouseWorldPosition.Instance.GetPosition(), 1);
                // Server will handle resource deduction if there are costs
            }
            else
            {
                PlayerStatsUtils.GetMissingResources(buildCost, buildCost,
                    out int missingR1, out int missingR2);

                if (missingR1 > 0)
                    Debug.Log($"You are missing {missingR1} Resource1.");
                if (missingR2 > 0)
                    Debug.Log($"You are missing {missingR2} Resource2.");
            }
        }
    }

    // Public methods for external access
    public void ShowBuildingUI(Entity buildingEntity)
    {
        selectedBuilding = buildingEntity;
    }

    public void HideBuildingUI()
    {
        selectedBuilding = Entity.Null;
    }

    public Entity GetSelectedBuilding()
    {
        return selectedBuilding;
    }

    public IPanel GetRootPanel()
    {
        return _TesterUI?.rootVisualElement?.panel;
    }

    // UI Actions
    private void StartBuildMode(ClickEvent clickEvent)
    {
        buildMode = !buildMode;
        UpdateSpawnerButtonStyle();
    }

    private void UpdateSpawnerButtonStyle()
    {
        if (buildMode)
        {
            spawnerButton.text = "Stop Building";
            spawnerButton.AddToClassList("active");
        }
        else
        {
            spawnerButton.text = "Build Barracks";
            spawnerButton.RemoveFromClassList("active");
        }
    }

    // Resource buttons now send RPCs to server instead of modifying local resources
    private void AddResource1Amount(ClickEvent clickEvent)
    {
        SendResourceRpc(resource1Input.value, 0);
    }

    private void AddResource2Amount(ClickEvent clickEvent)
    {
        SendResourceRpc(0, resource2Input.value);
    }

    private void SendResourceRpc(int resource1ToAdd, int resource2ToAdd)
    {
        var clientWorld = WorldManager.GetClientWorld();
        if (clientWorld == null || !clientWorld.IsCreated)
        {
            Debug.LogError("Client world not available for sending RPC");
            return;
        }

        var em = clientWorld.EntityManager;
        var rpc = em.CreateEntity();
        em.AddComponentData(rpc, new AddResourcesRpc
        {
            resource1ToAdd = resource1ToAdd,
            resource2ToAdd = resource2ToAdd
        });
        em.AddComponentData(rpc, new SendRpcCommandRequest());
    }

    private void OnSpawnUnitClicked(ClickEvent evt)
    {
        if (selectedBuilding != Entity.Null)
        {
            // Owner check
            if (!isOwnerOfSelectedBuilding)
            {
                Debug.Log("Cannot spawn units from buildings you don't own");
                return;
            }

            SendSpawnUnitRpc(selectedBuilding);
        }
    }

    private void SendSpawnUnitRpc(Entity buildingEntity)
    {
        var clientWorld = WorldManager.GetClientWorld();
        if (clientWorld == null || !clientWorld.IsCreated)
        {
            Debug.LogError("Client world not available for sending RPC");
            return;
        }

        var em = clientWorld.EntityManager;
        var rpc = em.CreateEntity();
        em.AddComponentData(rpc, new SpawnUnitFromBuildingRpc { buildingEntity = buildingEntity });
        em.AddComponentData(rpc, new SendRpcCommandRequest());
    }

    public void SpawnBarracksRpcRequest(Vector3 position, int owner)
    {
        var clientWorld = WorldManager.GetClientWorld();
        if (clientWorld == null || !clientWorld.IsCreated) return;

        var em = clientWorld.EntityManager;
        var rpcEntity = em.CreateEntity();
        em.AddComponentData(rpcEntity, new SpawnBarracksRpc
        {
            position = position,
            owner = owner
        });
        em.AddComponentData(rpcEntity, new SendRpcCommandRequest());
    }
}