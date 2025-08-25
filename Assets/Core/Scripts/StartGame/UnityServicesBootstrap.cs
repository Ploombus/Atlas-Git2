using UnityEngine;
using System;
using Unity.Services;
using Unity.Services.Core;
using Unity.Services.Authentication;

public class UnityServicesBootstrap : MonoBehaviour
{
    private async void Awake()
    {
        if (Unity.Services.Core.UnityServices.State == Unity.Services.Core.ServicesInitializationState.Uninitialized)
        {
            try
            {
                await UnityServices.InitializeAsync();
                AuthenticationService.Instance.SignedIn += () =>
                {
                    Debug.Log($"Unity Services signed in : {AuthenticationService.Instance.PlayerId}");
                };
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Debug.LogError("Failed to initialize Unity Services.");
            }
        }

        if (!Unity.Services.Authentication.AuthenticationService.Instance.IsSignedIn)
        {
            try
            {
                await Unity.Services.Authentication.AuthenticationService.Instance.SignInAnonymouslyAsync();
                Debug.Log("[Bootstrap] Signed in anonymously.");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                Debug.LogError("Failed to sign in.");
            }
        }
    }
}