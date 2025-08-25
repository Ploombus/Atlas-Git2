using System.Collections.Generic;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;
using UnityEngine.UIElements;
using Unity.Collections;

public class ScoreboardManager : MonoBehaviour
{
    public static ScoreboardManager Instance { get; private set; }

    [Header("UI References")]
    [SerializeField] private UIDocument uiDocument;

    [Header("Settings")]
    [SerializeField] private bool showScoreboard = true;
    [SerializeField] private float updateInterval = 0.1f;

    // UI Elements
    private VisualElement scoreboardContainer;
    private Label resource1Display;
    private Label resource2Display;
    private VisualElement playersContainer;

    // Tracking
    private float lastUpdateTime;
    private World clientWorld;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }
    }

    private void Start()
    {
        InitializeUI();
        FindClientWorld();

        PlayerStatsUIEvents.OnAllPlayerStatsUpdated += ForceUpdateDisplay;
    }

    private void OnDestroy()
    {
        PlayerStatsUIEvents.OnAllPlayerStatsUpdated -= ForceUpdateDisplay;

        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Update()
    {
        if (!showScoreboard) return;

        // Update at regular intervals
        if (Time.time - lastUpdateTime >= updateInterval)
        {
            UpdateScoreboardDisplay();
            lastUpdateTime = Time.time;
        }
    }

    private void ForceUpdateDisplay()
    {
        UpdateScoreboardDisplay();
    }

    private void InitializeUI()
    {
        if (uiDocument == null)
        {
            Debug.LogError("ScoreboardManager: UIDocument is null!");
            enabled = false;
            return;
        }

        var root = uiDocument.rootVisualElement;
        if (root == null)
        {
            Debug.LogError("ScoreboardManager: Root visual element is null!");
            enabled = false;
            return;
        }

        scoreboardContainer = root.Q<VisualElement>("scoreboard-container");
        resource1Display = root.Q<Label>("resource1-display");
        resource2Display = root.Q<Label>("resource2-display");
        playersContainer = root.Q<VisualElement>("players-container");

        if (scoreboardContainer == null)
        {
            Debug.LogError("ScoreboardManager: Missing required UI elements in UXML!");
            enabled = false;
            return;
        }

        SetScoreboardVisibility(showScoreboard);
    }

    private void FindClientWorld()
    {
        foreach (var world in World.All)
        {
            if (world.IsClient())
            {
                clientWorld = world;
                break;
            }
        }

        if (clientWorld == null)
        {
            Debug.LogWarning("ScoreboardManager: No client world found!");
        }
    }

    private void UpdateScoreboardDisplay()
    {
        if (clientWorld == null || !clientWorld.IsCreated)
        {
            DisplayFallbackValues();
            return;
        }

        var allPlayerStats = GetAllPlayerStats();
        if (allPlayerStats == null || allPlayerStats.Count == 0)
        {
            DisplayFallbackValues();
            return;
        }

        UpdateLocalPlayerResources(allPlayerStats);

        UpdatePlayersScoreboard(allPlayerStats);
    }

    private List<PlayerStatsData> GetAllPlayerStats()
    {
        var entityManager = clientWorld.EntityManager;

        using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<PlayerStats>());

        if (query.IsEmpty)
        {
            return null;
        }

        var statsArray = query.ToComponentDataArray<PlayerStats>(Allocator.Temp);
        var playerDataList = new List<PlayerStatsData>();

        var localPlayerId = GetLocalPlayerId();

        for (int i = 0; i < statsArray.Length; i++)
        {
            var stats = statsArray[i];

            if (stats.playerId >= 0)
            {
                var playerData = new PlayerStatsData
                {
                    stats = stats,
                    isLocalPlayer = (stats.playerId == localPlayerId)
                };

                playerDataList.Add(playerData);
            }
        }

        statsArray.Dispose();
        return playerDataList;
    }

    private void UpdateLocalPlayerResources(List<PlayerStatsData> allPlayerStats)
    {
        // Find local player's data
        PlayerStats? localStats = null;

        foreach (var playerData in allPlayerStats)
        {
            if (playerData.isLocalPlayer)
            {
                localStats = playerData.stats;
                break;
            }
        }

        if (localStats.HasValue)
        {
            // Display current resource values
            if (resource1Display != null)
                resource1Display.text = localStats.Value.resource1.ToString();

            if (resource2Display != null)
                resource2Display.text = localStats.Value.resource2.ToString();
        }
        else
        {
            // No local player found - show question marks
            if (resource1Display != null) resource1Display.text = "?";
            if (resource2Display != null) resource2Display.text = "?";
        }
    }

    private void UpdatePlayersScoreboard(List<PlayerStatsData> allPlayerStats)
    {
        // Clear existing entries
        if (playersContainer != null)
        {
            playersContainer.Clear();
        }
        else
        {
            return;
        }

        // Sort players by total score (descending - highest first)
        allPlayerStats.Sort((a, b) => b.stats.totalScore.CompareTo(a.stats.totalScore));

        // Display up to 4 players (or however many you want)
        int maxPlayersToShow = Mathf.Min(4, allPlayerStats.Count);

        for (int i = 0; i < maxPlayersToShow; i++)
        {
            var playerData = allPlayerStats[i];
            CreatePlayerScoreEntry(playerData.stats, playerData.isLocalPlayer, i + 1);
        }
    }

    private void CreatePlayerScoreEntry(PlayerStats stats, bool isLocalPlayer, int position)
    {
        var playerEntry = new VisualElement();
        playerEntry.AddToClassList("player-entry");

        if (isLocalPlayer)
        {
            playerEntry.AddToClassList("local-player");
        }

        // Create player name and score text
        string playerName = isLocalPlayer ? "You" : $"Player {stats.playerId}";
        string scoreText = $"{playerName}: {stats.totalScore}";

        var playerLabel = new Label(scoreText);
        playerLabel.AddToClassList("player-score");

        // Highlight first place
        if (position == 1)
        {
            playerLabel.AddToClassList("first-place");
        }

        playerEntry.Add(playerLabel);
        playersContainer.Add(playerEntry);
    }

    private int GetLocalPlayerId()
    {
        if (clientWorld == null || !clientWorld.IsCreated)
            return -1;

        var entityManager = clientWorld.EntityManager;

        // Method 1: Try using GhostOwnerIsLocal (most reliable)
        using var ghostQuery = entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<GhostOwner>(),
            ComponentType.ReadOnly<GhostOwnerIsLocal>()
        );

        if (!ghostQuery.IsEmpty)
        {
            var ghostOwners = ghostQuery.ToComponentDataArray<GhostOwner>(Allocator.Temp);
            if (ghostOwners.Length > 0)
            {
                var localId = ghostOwners[0].NetworkId;
                ghostOwners.Dispose();
                return localId;
            }
            ghostOwners.Dispose();
        }

        // Method 2: Fallback to NetworkStreamConnection
        using var connectionQuery = entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<NetworkStreamConnection>(),
            ComponentType.ReadOnly<NetworkId>()
        );

        if (!connectionQuery.IsEmpty)
        {
            var networkIds = connectionQuery.ToComponentDataArray<NetworkId>(Allocator.Temp);
            if (networkIds.Length > 0)
            {
                var localId = networkIds[0].Value;
                networkIds.Dispose();
                return localId;
            }
            networkIds.Dispose();
        }

        return -1; // No local player ID found
    }

    private void DisplayFallbackValues()
    {
        if (resource1Display != null) resource1Display.text = "?";
        if (resource2Display != null) resource2Display.text = "?";

        // Clear players list
        if (playersContainer != null)
        {
            playersContainer.Clear();
        }
    }

    public void SetScoreboardVisibility(bool visible)
    {
        showScoreboard = visible;
        if (scoreboardContainer != null)
        {
            scoreboardContainer.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }

    public void ToggleScoreboard()
    {
        SetScoreboardVisibility(!showScoreboard);
    }

    private struct PlayerStatsData
    {
        public PlayerStats stats;
        public bool isLocalPlayer;
    }
}