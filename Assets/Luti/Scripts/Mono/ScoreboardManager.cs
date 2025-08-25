using System.Collections.Generic;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;
using UnityEngine.UIElements;
using Unity.Collections;

/// <summary>
/// SIMPLIFIED: ScoreboardManager that reads directly from PlayerStats - no ResourceManager needed
/// </summary>
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

    // Simple tracking
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

        if (Time.time - lastUpdateTime >= updateInterval)
        {
            UpdateStatsDisplay();
            lastUpdateTime = Time.time;
        }
    }

    private void ForceUpdateDisplay()
    {
        UpdateStatsDisplay();
    }

    private void InitializeUI()
    {
        if (uiDocument == null)
        {
            Debug.LogError("ScoreboardManager: UIDocument is null!");
            return;
        }

        var root = uiDocument.rootVisualElement;
        scoreboardContainer = root.Q<VisualElement>("scoreboard-container");
        resource1Display = root.Q<Label>("resource1-display");
        resource2Display = root.Q<Label>("resource2-display");
        playersContainer = root.Q<VisualElement>("players-container");

        if (scoreboardContainer == null)
        {
            Debug.LogError("ScoreboardManager: Missing scoreboard-container!");
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
    }

    /// <summary>
    /// SIMPLIFIED: Read everything directly from PlayerStats - no ResourceManager needed
    /// </summary>
    private void UpdateStatsDisplay()
    {
        if (clientWorld == null || !clientWorld.IsCreated) return;

        var entityManager = clientWorld.EntityManager;

        // Clear players container
        playersContainer?.Clear();

        // Query PlayerStats entities
        using var query = entityManager.CreateEntityQuery(ComponentType.ReadOnly<PlayerStats>());

        if (query.IsEmpty)
        {
            // Show fallback values
            if (resource1Display != null) resource1Display.text = "?";
            if (resource2Display != null) resource2Display.text = "?";
            return;
        }

        var allStats = query.ToComponentDataArray<PlayerStats>(Allocator.Temp);
        var localPlayerId = GetLocalPlayerId();

        // Find local player and update resources
        PlayerStats localPlayerStats = default;
        bool hasLocalPlayer = false;

        // Create player list for scoring
        var playerDisplayData = new List<(PlayerStats stats, bool isLocal)>();

        for (int i = 0; i < allStats.Length; i++)
        {
            var stats = allStats[i];
            bool isLocal = (stats.playerId == localPlayerId);

            if (isLocal)
            {
                localPlayerStats = stats;
                hasLocalPlayer = true;
            }

            playerDisplayData.Add((stats, isLocal));
        }

        // Update resource displays from local player stats
        if (hasLocalPlayer)
        {
            if (resource1Display != null) resource1Display.text = localPlayerStats.resource1.ToString();
            if (resource2Display != null) resource2Display.text = localPlayerStats.resource2.ToString();
        }
        else
        {
            // No local player found
            if (resource1Display != null) resource1Display.text = "?";
            if (resource2Display != null) resource2Display.text = "?";
        }

        // Sort by total score (descending) - highest score first
        playerDisplayData.Sort((a, b) => b.stats.totalScore.CompareTo(a.stats.totalScore));

        // Create player score entries (max 2 players)
        int playerCount = 0;
        foreach (var (stats, isLocal) in playerDisplayData)
        {
            if (playerCount >= 2) break;

            CreatePlayerScoreEntry(stats, isLocal, playerCount + 1);
            playerCount++;
        }

        allStats.Dispose();
    }

    private void CreatePlayerScoreEntry(PlayerStats stats, bool isLocal, int position)
    {
        var playerEntry = new VisualElement();
        playerEntry.AddToClassList("player-entry");
        if (isLocal) playerEntry.AddToClassList("local-player");

        string playerName = isLocal ? "You" : $"Player {stats.playerId}";
        var playerLabel = new Label($"{playerName}: {stats.totalScore}");
        playerLabel.AddToClassList("player-score");
        if (position == 1) playerLabel.AddToClassList("first-place");

        playerEntry.Add(playerLabel);
        playersContainer?.Add(playerEntry);
    }

    private int GetLocalPlayerId()
    {
        if (clientWorld == null || !clientWorld.IsCreated) return -1;

        var entityManager = clientWorld.EntityManager;

        // First try to find using GhostOwnerIsLocal
        using var ghostOwnerQuery = entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<GhostOwner>(),
            ComponentType.ReadOnly<GhostOwnerIsLocal>()
        );

        if (!ghostOwnerQuery.IsEmpty)
        {
            var ghostOwners = ghostOwnerQuery.ToComponentDataArray<GhostOwner>(Allocator.Temp);
            if (ghostOwners.Length > 0)
            {
                int localId = ghostOwners[0].NetworkId;
                ghostOwners.Dispose();
                return localId;
            }
            ghostOwners.Dispose();
        }

        // Fallback
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

        return -1;
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
}