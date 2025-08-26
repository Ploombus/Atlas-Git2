using System;
using System.Collections.Generic;

/// <summary>
/// Static events class for victory screen communication
/// </summary>
public static class VictoryScreenEvents
{
    public static event Action<VictoryScreenData> OnShowVictoryScreen;
    public static event Action OnHideVictoryScreen;

    public static void ShowVictoryScreen(VictoryScreenData data)
    {
        OnShowVictoryScreen?.Invoke(data);
    }

    public static void HideVictoryScreen()
    {
        OnHideVictoryScreen?.Invoke();
    }
}

/// <summary>
/// Data structure containing all information needed for victory screen display
/// </summary>
public struct VictoryScreenData
{
    public int winnerPlayerId;
    public bool isLocalPlayerWinner;
    public List<PlayerStats> finalScores;

    public string GetVictoryMessage()
    {
        if (winnerPlayerId == -1)
        {
            // Single player lost
            return "GAME OVER";
        }

        return isLocalPlayerWinner ? "VICTORY!" : "DEFEAT";
    }

    public string GetWinnerText()
    {
        if (winnerPlayerId == -1)
            return "No units remaining and insufficient resources";

        return $"Player {winnerPlayerId} Wins!";
    }
}