using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using System.Collections.Generic;
using Unity.Entities;

public class LeaveGameButton : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Button leaveButton;

    // Scene to load when leaving (matches your MenuScene.unity)
    private const string MENU_SCENE_NAME = "MenuScene";

    private void Start()
    {
        // If no button assigned, try to find it on this GameObject
        if (leaveButton == null)
        {
            leaveButton = GetComponent<Button>();
        }

        // If still no button found, try to find it in children
        if (leaveButton == null)
        {
            leaveButton = GetComponentInChildren<Button>();
        }

        // Subscribe to button click if we found a button
        if (leaveButton != null)
        {
            leaveButton.onClick.AddListener(OnLeaveButtonClicked);
            Debug.Log("SafeLeaveGameButton: Successfully connected to leave button");
        }
    }

    private void OnLeaveButtonClicked()
    {
        Debug.Log("Leave button clicked - returning to main menu");
        LoadMainMenu();
    }

    private void LoadMainMenu()
    {
        try
        {
            // Load the main menu scene
            SceneManager.LoadScene(MENU_SCENE_NAME);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to load {MENU_SCENE_NAME}: {e.Message}");

            // Fallback: try loading by build index 0 (usually main menu)
            try
            {
                Debug.Log("Attempting fallback to scene index 0");
                SceneManager.LoadScene(0);
            }
            catch (System.Exception fallbackException)
            {
                Debug.LogError($"Fallback scene load also failed: {fallbackException.Message}");
            }
        }
    }

    private void OnDestroy()
    {
        // Safely unsubscribe from button events
        // Check for null before unsubscribing to prevent NullReferenceException
        if (leaveButton != null)
        {
            try
            {
                leaveButton.onClick.RemoveListener(OnLeaveButtonClicked);
            }
            catch (System.Exception e)
            {
                // Silently handle any cleanup errors - don't spam console during shutdown
                Debug.LogWarning($"SafeLeaveGameButton cleanup warning: {e.Message}");
            }
        }
    }

    private void OnDisable()
    {
        // Additional safety - also cleanup when disabled
        if (leaveButton != null)
        {
            try
            {
                leaveButton.onClick.RemoveListener(OnLeaveButtonClicked);
            }
            catch (System.Exception)
            {
                // Silently handle - object might be destroyed already
            }
        }
    }

    // Public method that can be called from other scripts or UI events
    public void LeaveGame()
    {
        LoadMainMenu();
    }
}
