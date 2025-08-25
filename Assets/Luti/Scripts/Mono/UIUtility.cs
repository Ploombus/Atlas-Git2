
using UnityEngine.EventSystems;

/// <summary>
/// Utility class for UI-related operations
/// </summary>
public static class UIUtility
{
    /// <summary>
    /// Check if the mouse pointer is currently over a UI element
    /// </summary>
    public static bool IsPointerOverUI()
    {
        return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
    }
}