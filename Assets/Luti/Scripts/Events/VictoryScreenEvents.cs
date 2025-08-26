using System;
using System.Collections.Generic;

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

public struct VictoryScreenData
{
    public int winnerPlayerId;
    public bool isLocalPlayerWinner;
    public List<PlayerStats> finalScores;

    public string GetVictoryMessage()
    {
        if (winnerPlayerId == -1)
        {
            // Single player elimination - simple message
            return "GAME OVER";
        }

        return isLocalPlayerWinner ? "VICTORY!" : "DEFEAT";
    }

    public string GetWinnerText()
    {
        if (winnerPlayerId == -1)
        {
            return "Eliminated: No units remaining and Resource1 ≤ 10";
        }

        return $"Player {winnerPlayerId} Wins!";
    }

    public string GetEliminationReason()
    {
        return "A player was eliminated when they had no alive units and Resource1 dropped to 10 or below";
    }
}