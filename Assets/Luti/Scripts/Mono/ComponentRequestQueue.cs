using Unity.Entities;
using System.Collections.Generic;

public struct AddComponentRequest
{
    // Add data if needed — for now it's just a signal
}

public static class ComponentRequestQueue
{
    public static List<AddComponentRequest> BuildingModeStart = new();
    public static List<AddComponentRequest> BuildingModeEnd = new();
    public static List<AddComponentRequest> Requests = new();
}