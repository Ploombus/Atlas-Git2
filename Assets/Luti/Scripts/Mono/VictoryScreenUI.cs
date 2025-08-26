using System.Linq;
using Unity.Entities;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// <summary>
/// MonoBehaviour that handles the victory screen UI display
/// Subscribe to VictoryScreenEvents and displays the final game results
/// </summary>
public class VictoryScreenUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private UIDocument victoryScreenDocument;

    [Header("Victory Screen Settings")]
    [SerializeField] private bool autoHideAfterSeconds = true;
    [SerializeField] private float autoHideDelay = 10f;

    // UI Element names (set these in your UXML)
    private const string VICTORY_CONTAINER = "victory-container";
    private const string VICTORY_MESSAGE = "victory-message";
    private const string WINNER_TEXT = "winner-text";
    private const string SCORES_CONTAINER = "scores-container";
    private const string SCORES_CONTENT = "scores-content";
    private const string CLOSE_BUTTON = "close-button";

    private VisualElement victoryContainer;
    private Label victoryMessage;
    private Label winnerText;
    private ScrollView scoresContainer;
    private VisualElement scoresContent;
    private Button closeButton;

    private void OnEnable()
    {
        VictoryScreenEvents.OnShowVictoryScreen += ShowVictoryScreen;
        VictoryScreenEvents.OnHideVictoryScreen += HideVictoryScreen;
    }

    private void OnDisable()
    {
        VictoryScreenEvents.OnShowVictoryScreen -= ShowVictoryScreen;
        VictoryScreenEvents.OnHideVictoryScreen -= HideVictoryScreen;
    }

    private void Start()
    {
        InitializeUI();
        HideVictoryScreen(); // Start hidden
    }

    private void InitializeUI()
    {
        if (victoryScreenDocument == null)
        {
            Debug.LogError("VictoryScreenUI: UIDocument not assigned!");
            return;
        }

        var root = victoryScreenDocument.rootVisualElement;

        victoryContainer = root.Q<VisualElement>(VICTORY_CONTAINER);
        victoryMessage = root.Q<Label>(VICTORY_MESSAGE);
        winnerText = root.Q<Label>(WINNER_TEXT);
        scoresContainer = root.Q<ScrollView>(SCORES_CONTAINER);
        scoresContent = root.Q<VisualElement>(SCORES_CONTENT);
        closeButton = root.Q<Button>(CLOSE_BUTTON);

        // Setup close button
        if (closeButton != null)
        {
            closeButton.clicked += HideVictoryScreen;
        }

        // Validate required elements
        if (victoryContainer == null)
        {
            Debug.LogError($"VictoryScreenUI: Could not find '{VICTORY_CONTAINER}' in UI document!");
        }
        if (scoresContent == null)
        {
            Debug.LogError($"VictoryScreenUI: Could not find '{SCORES_CONTENT}' in UI document!");
        }
    }

    private void ShowVictoryScreen(VictoryScreenData data)
    {
        if (victoryContainer == null)
        {
            Debug.LogError("VictoryScreenUI: UI not properly initialized!");
            return;
        }

        // Show the victory screen
        victoryContainer.style.display = DisplayStyle.Flex;

        // Set victory/defeat message
        if (victoryMessage != null)
        {
            victoryMessage.text = data.GetVictoryMessage();

            // Add appropriate styling based on victory/defeat
            victoryMessage.ClearClassList();
            if (data.isLocalPlayerWinner)
            {
                victoryMessage.AddToClassList("victory");
            }
            else
            {
                victoryMessage.AddToClassList("defeat");
            }
        }

        // Set winner text
        if (winnerText != null)
        {
            winnerText.text = data.GetWinnerText();
        }

        // Display final scores
        DisplayFinalScores(data.finalScores);

        // Auto-hide if enabled
        if (autoHideAfterSeconds)
        {
            Invoke(nameof(HideVictoryScreen), autoHideDelay);
        }

        Debug.Log($"Victory screen shown! Winner: Player {data.winnerPlayerId}, Local player won: {data.isLocalPlayerWinner}");
    }

    private void HideVictoryScreen()
    {
        if (victoryContainer != null)
        {
            victoryContainer.style.display = DisplayStyle.None;
        }

        // Cancel any pending auto-hide
        CancelInvoke(nameof(HideVictoryScreen));


    }

    private void DisplayFinalScores(System.Collections.Generic.List<PlayerStats> finalScores)
    {
        if (scoresContent == null || finalScores == null)
            return;

        // Clear existing scores
        scoresContent.Clear();

        // Sort players by total score (descending)
        var sortedScores = finalScores
            .Where(stats => stats.playerId >= 0) // Filter out invalid players
            .OrderByDescending(stats => stats.totalScore)
            .ToList();

        // Create score entries
        for (int i = 0; i < sortedScores.Count; i++)
        {
            var playerStats = sortedScores[i];
            var scoreEntry = CreateScoreEntry(playerStats, i + 1);
            scoresContent.Add(scoreEntry);
        }
    }

    private VisualElement CreateScoreEntry(PlayerStats playerStats, int rank)
    {
        var container = new VisualElement();
        container.AddToClassList("score-entry");

        // Rank
        var rankLabel = new Label($"#{rank}");
        rankLabel.AddToClassList("rank-label");
        container.Add(rankLabel);

        // Player name
        var playerLabel = new Label($"Player {playerStats.playerId}");
        playerLabel.AddToClassList("player-label");

        // Highlight local player
        if (PlayerStatsUtils.IsLocalPlayer(playerStats.playerId))
        {
            playerLabel.AddToClassList("local-player");
        }

        container.Add(playerLabel);

        // Scores
        var scoresContainer = new VisualElement();
        scoresContainer.AddToClassList("scores-section");

        var totalScoreLabel = new Label($"Total: {playerStats.totalScore}");
        totalScoreLabel.AddToClassList("total-score");
        scoresContainer.Add(totalScoreLabel);

        var resourceScoreLabel = new Label($"R1: {playerStats.resource1Score} | R2: {playerStats.resource2Score}");
        resourceScoreLabel.AddToClassList("resource-scores");
        scoresContainer.Add(resourceScoreLabel);

        var finalResourcesLabel = new Label($"Final Resources: {playerStats.resource1}/{playerStats.resource2}");
        finalResourcesLabel.AddToClassList("final-resources");
        scoresContainer.Add(finalResourcesLabel);

        container.Add(scoresContainer);

        return container;
    }
}