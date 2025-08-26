using System.Linq;
using Unity.Entities;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

public class VictoryScreenUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private UIDocument victoryScreenDocument;

    [Header("Victory Screen Settings")]
    [SerializeField] private bool autoHideAfterSeconds = false;
    [SerializeField] private float autoHideDelay = 15f; 

    private const string VICTORY_CONTAINER = "victory-container";
    private const string VICTORY_MESSAGE = "victory-message";
    private const string WINNER_TEXT = "winner-text";
    private const string SCORES_CONTAINER = "scores-container";
    private const string SCORES_CONTENT = "scores-content";
    private const string CLOSE_BUTTON = "close-button";

    private VisualElement victoryContainer;
    private Label victoryMessage;
    private Label winnerText;
    private VisualElement scoresContainer;
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
        scoresContainer = root.Q<VisualElement>(SCORES_CONTAINER);
        scoresContent = root.Q<VisualElement>(SCORES_CONTENT);
        closeButton = root.Q<Button>(CLOSE_BUTTON);

        // Setup close button
        if (closeButton != null)
        {
            closeButton.clicked += OnCloseButtonClicked;
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

        // Set victory/defeat message with appropriate styling
        if (victoryMessage != null)
        {
            victoryMessage.text = data.GetVictoryMessage();

            // Clear previous classes
            victoryMessage.RemoveFromClassList("victory");
            victoryMessage.RemoveFromClassList("defeat");
            victoryMessage.RemoveFromClassList("elimination");

            // Add appropriate class based on outcome
            if (data.winnerPlayerId == -1)
            {
                // Single player elimination
                victoryMessage.AddToClassList("elimination");
            }
            else if (data.isLocalPlayerWinner)
            {
                victoryMessage.AddToClassList("victory");
            }
            else
            {
                victoryMessage.AddToClassList("defeat");
            }
        }

        // Set winner/elimination explanation
        if (winnerText != null)
        {
            winnerText.text = data.GetWinnerText();
        }

        // Display final scores (even in single-player elimination)
        DisplayFinalScores(data.finalScores, data.winnerPlayerId);

        // Auto-hide if enabled
        if (autoHideAfterSeconds)
        {
            Invoke(nameof(HideVictoryScreen), autoHideDelay);
        }

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

    private void DisplayFinalScores(System.Collections.Generic.List<PlayerStats> finalScores, int winnerId)
    {
        if (scoresContent == null || finalScores == null)
            return;

        // Clear existing scores
        scoresContent.Clear();

        // Filter out invalid players and sort by total score (descending)
        var sortedScores = finalScores
            .Where(stats => stats.playerId >= 0)
            .OrderByDescending(stats => stats.totalScore)
            .ToList();

        if (sortedScores.Count == 0)
        {
            // No valid scores to display
            var noScoresLabel = new Label("No player statistics available");
            noScoresLabel.AddToClassList("no-scores-message");
            scoresContent.Add(noScoresLabel);
            return;
        }

        // Add title for scores section
        var scoresTitle = new Label("Final Statistics");
        scoresTitle.AddToClassList("scores-title");
        scoresContent.Add(scoresTitle);

        // Create score entries
        for (int i = 0; i < sortedScores.Count; i++)
        {
            var playerStats = sortedScores[i];
            var scoreEntry = CreateScoreEntry(playerStats, i + 1, winnerId);
            scoresContent.Add(scoreEntry);
        }

        // Add game end reason if single player elimination
        if (winnerId == -1 && sortedScores.Count == 1)
        {
            var reasonLabel = new Label("Game ended: No units remaining and insufficient resources");
            reasonLabel.AddToClassList("elimination-reason");
            scoresContent.Add(reasonLabel);
        }
    }

    private VisualElement CreateScoreEntry(PlayerStats playerStats, int rank, int winnerId)
    {
        var container = new VisualElement();
        container.AddToClassList("score-entry");

        // Add winner indicator
        if (winnerId != -1 && playerStats.playerId == winnerId)
        {
            container.AddToClassList("winner-entry");
        }

        // Rank (only show if more than one player)
        if (rank > 1 || winnerId != -1)
        {
            var rankLabel = new Label($"#{rank}");
            rankLabel.AddToClassList("rank-label");
            container.Add(rankLabel);
        }

        // Player name with status indicator
        string playerText = $"Player {playerStats.playerId}";
        if (winnerId == -1 && rank == 1)
        {
            playerText += " (Eliminated)";
        }
        else if (winnerId != -1 && playerStats.playerId == winnerId)
        {
            playerText += " (Winner)";
        }

        var playerLabel = new Label(playerText);
        playerLabel.AddToClassList("player-label");

        // Highlight local player
        if (PlayerStatsUtils.IsLocalPlayer(playerStats.playerId))
        {
            playerLabel.AddToClassList("local-player");
        }

        container.Add(playerLabel);

        // Scores section
        var scoresSection = new VisualElement();
        scoresSection.AddToClassList("scores-section");

        var totalScoreLabel = new Label($"Total Score: {playerStats.totalScore}");
        totalScoreLabel.AddToClassList("total-score");
        scoresSection.Add(totalScoreLabel);

        var resourceScoreLabel = new Label($"Resource Points - R1: {playerStats.resource1Score} | R2: {playerStats.resource2Score}");
        resourceScoreLabel.AddToClassList("resource-scores");
        scoresSection.Add(resourceScoreLabel);

        var finalResourcesLabel = new Label($"Final Resources: {playerStats.resource1} / {playerStats.resource2}");
        finalResourcesLabel.AddToClassList("final-resources");
        scoresSection.Add(finalResourcesLabel);

        container.Add(scoresSection);

        return container;
    }

    private void OnCloseButtonClicked()
    {
        HideVictoryScreen();

        // Example: Return to main menu
        // SceneManager.LoadScene("MainMenu");

    }
}