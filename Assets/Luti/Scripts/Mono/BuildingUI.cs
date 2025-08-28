using Unity.Entities;
using Unity.NetCode;
using UnityEngine;
using UnityEngine.UIElements;
using Managers;

public class BuildingUI : MonoBehaviour
{
    public static BuildingUI Instance { get; private set; }

    [SerializeField] private UIDocument _BuildingUI;

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
        if (_BuildingUI == null)
        {
            Debug.LogError("BuildingUI: UIDocument is null!");
            return;
        }

        var root = _BuildingUI.rootVisualElement;
        if (root == null)
        {
            Debug.LogError("BuildingUI: Root visual element is null!");
            return;
        }

        // Find UI elements
        spawnerButton = root.Q<Button>("SpawnerButton");
        addResource1Button = root.Q<Button>("AddResource1");
        addResource2Button = root.Q<Button>("AddResource2");
        resource1Input = root.Q<IntegerField>("Resource1Input");
        resource2Input = root.Q<IntegerField>("Resource2Input");
        buildingUI = root.Q<VisualElement>("BuildingUIPanel");
        spawnUnitButton = root.Q<Button>("UnitButton");

        // Register callbacks if elements exist
        spawnerButton?.RegisterCallback<ClickEvent>(StartBuildMode);
        addResource1Button?.RegisterCallback<ClickEvent>(AddResource1Amount);
        addResource2Button?.RegisterCallback<ClickEvent>(AddResource2Amount);
        spawnUnitButton?.RegisterCallback<ClickEvent>(OnSpawnUnitClicked);

        // Setup building UI
        if (buildingUI != null)
        {
            buildingUI.style.display = DisplayStyle.None;
            Debug.Log("[BuildingUI] UI initialized and hidden");
        }
        else
        {
            Debug.LogError("[BuildingUI] buildingUI panel not found!");
        }

        // Setup input field callbacks
        resource1Input?.RegisterCallback<ChangeEvent<int>>((evt) => { resource1Input.value = evt.newValue; });
        resource2Input?.RegisterCallback<ChangeEvent<int>>((evt) => { resource2Input.value = evt.newValue; });

        UpdateSpawnerButtonStyle();
    }

    private void SubscribeToEvents()
    {
        // Subscribe to BuildingUIEvents
        BuildingUIEvents.OnBuildingSelected += HandleBuildingSelected;
        BuildingUIEvents.OnBuildingDeselected += HandleBuildingDeselected;
        BuildingUIEvents.OnSpawnCostUpdated += HandleSpawnCostUpdated;
        BuildingUIEvents.OnResourcesUpdated += HandleResourcesUpdated;
        BuildingUIEvents.OnSpawnValidated += HandleSpawnValidation;
    }

    private void OnDestroy()
    {
        // Unsubscribe from BuildingUIEvents
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

        selectedBuilding = data.BuildingEntity;

        // Determine ownership by comparing NetworkIds
        isOwnerOfSelectedBuilding = (data.OwnerNetworkId == data.LocalPlayerNetworkId) &&
                                   (data.OwnerNetworkId != -999);

        cachedResource1Cost = data.Resource1Cost;
        cachedResource2Cost = data.Resource2Cost;


        // Only show building UI if player owns the building AND it has spawn capability
        if (isOwnerOfSelectedBuilding && data.HasSpawnCapability)
        {
            if (buildingUI != null)
            {
                buildingUI.style.display = DisplayStyle.Flex;
            }
            else
            {
                Debug.LogError("[BuildingUI] buildingUI element is null!");
            }
            UpdateUnitButtonDisplay();
            UpdateAffordabilityUI();
        }
        else
        {
            if (buildingUI != null)
            {
                buildingUI.style.display = DisplayStyle.None;
            }
        }
    }

    private void HandleBuildingDeselected()
    {
        selectedBuilding = Entity.Null;
        isOwnerOfSelectedBuilding = false;
        if (buildingUI != null)
        {
            buildingUI.style.display = DisplayStyle.None;
        }
        cachedResource1Cost = 0;
        cachedResource2Cost = 0;
    }

    private void HandleSpawnCostUpdated(SpawnCostUIData data)
    {
        if (data.BuildingEntity != selectedBuilding) return;

        cachedResource1Cost = data.Resource1Cost;
        cachedResource2Cost = data.Resource2Cost;
        canAffordCurrent = data.CanAfford;

        UpdateUnitButtonDisplay();
        UpdateAffordabilityUI();
    }

    private void HandleResourcesUpdated(ResourceUIData data)
    {
        canAffordCurrent = data.CanAffordCurrent;
        UpdateAffordabilityUI();
    }

    private void HandleSpawnValidation(SpawnValidationData data)
    {
        // Handle spawn validation results silently
    }

    private void UpdateUnitButtonDisplay()
    {
        if (spawnUnitButton == null) return;

        // Show "Base Unit" with cost info
        if (cachedResource1Cost > 0 || cachedResource2Cost > 0)
        {
            string costText = "Base Unit\nCost: ";
            if (cachedResource1Cost > 0) costText += $"R1:{cachedResource1Cost} ";
            if (cachedResource2Cost > 0) costText += $"R2:{cachedResource2Cost}";
            spawnUnitButton.text = costText;
        }
        else
        {
            spawnUnitButton.text = "Base Unit";
        }
    }

    private void UpdateAffordabilityUI()
    {
        if (spawnUnitButton == null) return;

        // Update button state and style based on affordability
        if (canAffordCurrent)
        {
            spawnUnitButton.SetEnabled(true);
            spawnUnitButton.RemoveFromClassList("unaffordable");
        }
        else
        {
            spawnUnitButton.SetEnabled(false);
            spawnUnitButton.AddToClassList("unaffordable");
        }
    }

    // Public methods for external access (called by SelectionManager)
    // These are kept for backward compatibility but just trigger events
    public void ShowBuildingUI(Entity buildingEntity)
    {
        // The BuildingUISystem will handle this through events
    }

    public void HideBuildingUI()
    {
        // The BuildingUISystem will handle this through events
    }

    public Entity GetSelectedBuilding()
    {
        return selectedBuilding;
    }

    public IPanel GetRootPanel()
    {
        return _BuildingUI?.rootVisualElement?.panel;
    }

    // UI Actions
    private void StartBuildMode(ClickEvent clickEvent)
    {
        buildMode = !buildMode;
        UpdateSpawnerButtonStyle();
    }

    private void UpdateSpawnerButtonStyle()
    {
        if (spawnerButton == null) return;

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

    // Resource button handlers
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

        Debug.Log($"Sent resource RPC: +{resource1ToAdd} R1, +{resource2ToAdd} R2");
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

            // Affordability check using PlayerStatsUtils
            if (!PlayerStatsUtils.CanAfford(cachedResource1Cost, cachedResource2Cost))
            {
                Debug.Log("Cannot afford to spawn unit");
                return;
            }

            // Send spawn request
            var clientWorld = WorldManager.GetClientWorld();
            if (clientWorld == null || !clientWorld.IsCreated) return;

            var em = clientWorld.EntityManager;
            var rpc = em.CreateEntity();
            em.AddComponentData(rpc, new SpawnUnitFromBuildingRpc
            {
                buildingEntity = selectedBuilding
            });
            em.AddComponentData(rpc, new SendRpcCommandRequest());

        }
    }
}