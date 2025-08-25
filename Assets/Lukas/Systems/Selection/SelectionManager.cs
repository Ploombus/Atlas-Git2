using System;
using Managers;
using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;
using Unity.Physics;
using Unity.Mathematics;
using Unity.NetCode;

public class SelectionManager : MonoBehaviour
{
    public static SelectionManager Instance { get; private set; } //singleton

    public event EventHandler OnSelectionAreaStart;
    public event EventHandler OnSelectionAreaEnd;

    Vector2 selectionStartMousePosition;

    private float lastClickTime;
    private Vector2 lastClickPosition;

    bool hasRecordedClick = false;
    private float doubleClickThreshold = 0.3f;
    const float maxDoubleClickDistance = 6f;
    const float DRAG_SELECT_PADDING_PX = 0.1f; // small drag sellection pad so it still counts

    bool selectionStarted; // guard to ignore stray MouseUp

    Camera mainCamera;

    private void Awake()
    {
        Instance = this;
        mainCamera = Camera.main;
    }

    private void Update()
    {
        if (CheckGameplayStateAccess.GetGameplayState(WorldManager.GetClientWorld()))
        {
            if (Input.GetMouseButtonDown(0))
            {
                if (UIUtility.IsPointerOverUI()) { return; }

                selectionStarted = true; // start a selection attempt
                selectionStartMousePosition = Input.mousePosition;
                OnSelectionAreaStart?.Invoke(this, EventArgs.Empty);
            }

            if (Input.GetMouseButtonUp(0))
            {
                //if (UIUtility.IsPointerOverUI()) { selectionStarted = false; return; }
                if (!selectionStarted) return; // ignore stray MouseUp

                Vector2 selectionEndMousePosition = Input.mousePosition;
                EntityManager entityManager = WorldManager.GetClientWorld().EntityManager;

                // -------- Deselect EVERYTHING first (your original behavior) --------
                EntityQuery entityQuery = new EntityQueryBuilder(Allocator.Temp).WithAll<Selected>().Build(entityManager);
                NativeArray<Entity> entityArray = entityQuery.ToEntityArray(Allocator.Temp);
                for (int i = 0; i < entityArray.Length; i++)
                {
                    // If it's a building that was selected, hide UI
                    if (entityManager.HasComponent<Building>(entityArray[i]) && entityManager.IsComponentEnabled<Selected>(entityArray[i]))
                    {
                        BuildingUI.Instance.HideBuildingUI();
                    }
                    entityManager.SetComponentEnabled<Selected>(entityArray[i], false);
                }

                // Compute rect + click/drag threshold
                Rect selectionAreaRect = GetSelectionAreaRect();
                float selectionAreaSize = selectionAreaRect.width + selectionAreaRect.height;
                float multipleSelectionSizeMin = 30f;

                // -------- SINGLE SELECTION / DOUBLE-CLICK BRANCH --------
                bool isSingleSelection = selectionAreaSize < multipleSelectionSizeMin;
                if (isSingleSelection)
                {
                    // Physics raycast to find what we clicked
                    entityQuery = entityManager.CreateEntityQuery(typeof(PhysicsWorldSingleton));
                    PhysicsWorldSingleton physicsWorldSingleton = entityQuery.GetSingleton<PhysicsWorldSingleton>();
                    CollisionWorld collisionWorld = physicsWorldSingleton.CollisionWorld;

                    UnityEngine.Ray cameraRay = Camera.main.ScreenPointToRay(Input.mousePosition);

                    int selectableLayer = 6; // Sixth layer
                    RaycastInput raycastInput = new RaycastInput
                    {
                        Start = cameraRay.GetPoint(0f),
                        End = cameraRay.GetPoint(9999f),
                        Filter = new CollisionFilter
                        {
                            BelongsTo = ~0u,
                            CollidesWith = 1u << selectableLayer,
                            GroupIndex = 0,
                        }
                    };

                    if (collisionWorld.CastRay(raycastInput, out Unity.Physics.RaycastHit raycastHit))
                    {
                        // Always select the directly clicked entity if it supports Selected
                        if (entityManager.HasComponent<Selected>(raycastHit.Entity))
                        {
                            entityManager.SetComponentEnabled<Selected>(raycastHit.Entity, true);
                        }

                        // Determine the "category" of the clicked entity:
                        //   Building  -> Buildings
                        //   Unit (and not Building) -> Units
                        //   else -> Others (e.g., Trees)
                        bool hitIsBuilding = entityManager.HasComponent<Building>(raycastHit.Entity);
                        bool hitIsUnit = !hitIsBuilding && entityManager.HasComponent<Unit>(raycastHit.Entity);

                        // Owner filter for double-click: if we double-click our unit/building, select only same owner.
                        bool ownerFilterEnabled = (hitIsBuilding || hitIsUnit) && entityManager.HasComponent<GhostOwner>(raycastHit.Entity);
                        int clickedOwnerId = 0;
                        if (ownerFilterEnabled)
                        {
                            clickedOwnerId = entityManager.GetComponentData<GhostOwner>(raycastHit.Entity).NetworkId;
                        }

                        // -------- DOUBLE-CLICK: select only SAME CATEGORY (and SAME OWNER if applicable) on screen --------
                        bool isDoubleClick =
                            hasRecordedClick &&
                            (Time.time - lastClickTime) <= doubleClickThreshold &&
                            (lastClickPosition - selectionStartMousePosition).sqrMagnitude <= (maxDoubleClickDistance * maxDoubleClickDistance);

                        if (isDoubleClick)
                        {
                            mainCamera = Camera.main;

                            entityQuery = new EntityQueryBuilder(Allocator.Temp)
                                .WithPresent<LocalToWorld, Selected>() // anything that can be selected
                                .Build(entityManager);

                            entityArray = entityQuery.ToEntityArray(Allocator.Temp);
                            var localToWorldArray = entityQuery.ToComponentDataArray<LocalToWorld>(Allocator.Temp);

                            for (int i = 0; i < entityArray.Length; i++)
                            {
                                var e = entityArray[i];

                                // Must be on screen
                                Vector3 viewportPos = mainCamera.WorldToViewportPoint(localToWorldArray[i].Position);
                                bool onScreen =
                                    viewportPos.z > 0 &&
                                    viewportPos.x > 0 && viewportPos.x < 1 &&
                                    viewportPos.y > 0 && viewportPos.y < 1;

                                if (!onScreen) continue;

                                // Same category check using Unit/Building tags
                                bool eIsBuilding = entityManager.HasComponent<Building>(e);
                                bool eIsUnit = !eIsBuilding && entityManager.HasComponent<Unit>(e);
                                bool sameCategory =
                                    (hitIsBuilding && eIsBuilding) ||
                                    (hitIsUnit && eIsUnit) ||
                                    (!hitIsBuilding && !hitIsUnit && !eIsBuilding && !eIsUnit); // both "Other"

                                if (!sameCategory) continue;

                                // Same owner (only when filtering is enabled and both have GhostOwner)
                                bool ownerMatch = true;
                                if (ownerFilterEnabled)
                                {
                                    if (entityManager.HasComponent<GhostOwner>(e))
                                    {
                                        var eOwner = entityManager.GetComponentData<GhostOwner>(e).NetworkId;
                                        ownerMatch = (eOwner == clickedOwnerId);
                                    }
                                    else
                                    {
                                        ownerMatch = false; // clicked had owner; this one doesn't -> exclude
                                    }
                                }

                                if (ownerMatch && entityManager.HasComponent<Selected>(e))
                                {
                                    entityManager.SetComponentEnabled<Selected>(e, true);
                                }
                            }
                            entityArray.Dispose();
                            localToWorldArray.Dispose();
                        }
                    }
                }

                // -------- DRAG SELECTION BRANCH --------
                entityQuery = new EntityQueryBuilder(Allocator.Temp)
                    .WithAll<LocalTransform>()
                    .WithPresent<Selected>()
                    .Build(entityManager);

                entityArray = entityQuery.ToEntityArray(Allocator.Temp);
                NativeArray<LocalTransform> localTransformArray = entityQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

                if (isSingleSelection == false)
                {
                    // Find my NetworkId (used to detect "my" ownership via GhostOwner)
                    int myNetId = -1;
                    bool myNetIdValid = false;
                    {
                        var idQuery = entityManager.CreateEntityQuery(typeof(NetworkId));
                        using (var ids = idQuery.ToComponentDataArray<NetworkId>(Allocator.Temp))
                        {
                            if (ids.Length > 0)
                            {
                                myNetId = ids[0].Value;
                                myNetIdValid = true;
                            }
                        }
                    }

                    // Pass 1: detect whether there are my units or my buildings in the rect
                    bool hasMyUnitsInRect = false;
                    bool hasMyBuildingsInRect = false;

                    for (int i = 0; i < localTransformArray.Length; i++)
                    {
                        if (!ScreenRectOverlapsEntity(selectionAreaRect, entityManager, entityArray[i], localTransformArray[i], Camera.main)) continue;

                        var e = entityArray[i];

                        bool isBuilding = entityManager.HasComponent<Building>(e);
                        bool isUnit = !isBuilding && entityManager.HasComponent<Unit>(e);

                        bool isMine = false;
                        if (myNetIdValid && entityManager.HasComponent<GhostOwner>(e))
                        {
                            var owner = entityManager.GetComponentData<GhostOwner>(e);
                            isMine = (owner.NetworkId == myNetId);
                        }

                        if (isMine && isUnit) { hasMyUnitsInRect = true; break; } // highest priority; we can early-out
                        if (isMine && isBuilding) { hasMyBuildingsInRect = true; }
                    }

                    // Decide selection policy according to your rule:
                    // 1) If my units present -> select only my units
                    // 2) else if my buildings present -> select only my buildings
                    // 3) else -> select all (any ownership/type)
                    bool selectOnlyMyUnits = hasMyUnitsInRect;
                    bool selectOnlyMyBuildings = !hasMyUnitsInRect && hasMyBuildingsInRect;

                    // Pass 2: apply selection according to policy
                    for (int i = 0; i < localTransformArray.Length; i++)
                    {
                        if (!ScreenRectOverlapsEntity(selectionAreaRect, entityManager, entityArray[i], localTransformArray[i], Camera.main)) continue;

                        var e = entityArray[i];

                        bool isBuilding = entityManager.HasComponent<Building>(e);
                        bool isUnit = !isBuilding && entityManager.HasComponent<Unit>(e);

                        bool isMine = false;
                        if (myNetIdValid && entityManager.HasComponent<GhostOwner>(e))
                        {
                            var owner = entityManager.GetComponentData<GhostOwner>(e);
                            isMine = (owner.NetworkId == myNetId);
                        }

                        bool pass =
                            (selectOnlyMyUnits && isMine && isUnit) ||
                            (selectOnlyMyBuildings && isMine && isBuilding) ||
                            (!selectOnlyMyUnits && !selectOnlyMyBuildings); // select-all fallback

                        if (pass)
                        {
                            entityManager.SetComponentEnabled<Selected>(e, true);

                            // If this entity is a building, show its UI (your original behavior)
                            if (entityManager.HasComponent<Building>(e))
                            {
                                BuildingUI.Instance.ShowBuildingUI(e);
                            }
                        }
                    }
                }

                // -------- finalize click bookkeeping --------
                lastClickPosition = selectionStartMousePosition;
                OnSelectionAreaEnd?.Invoke(this, EventArgs.Empty);

                entityArray.Dispose();
                lastClickTime = Time.time;
                lastClickPosition = selectionStartMousePosition;
                hasRecordedClick = true;
                selectionStarted = false; // end selection session
            }
        }
    }

    public Rect GetSelectionAreaRect()
    {
        Vector2 selectionEndMousePosition = Input.mousePosition;
        Vector2 lowerLeftCorner = new Vector2(
            Mathf.Min(selectionStartMousePosition.x, selectionEndMousePosition.x),
            Mathf.Min(selectionStartMousePosition.y, selectionEndMousePosition.y)
        );
        Vector2 upperRightCorner = new Vector2(
            Mathf.Max(selectionStartMousePosition.x, selectionEndMousePosition.x),
            Mathf.Max(selectionStartMousePosition.y, selectionEndMousePosition.y)
        );
        return new Rect(
            lowerLeftCorner.x,
            lowerLeftCorner.y,
            upperRightCorner.x - lowerLeftCorner.x,
            upperRightCorner.y - lowerLeftCorner.y
        );
    }

    static bool TryGetEntityScreenRect(EntityManager em, Entity e, in LocalTransform lt, Camera cam, out Rect rect)
    {
        // 1) Preferred: use gameplay footprint if available
        if (em.HasComponent<TargetingSize>(e))
        {
            float r = math.max(0f, em.GetComponentData<TargetingSize>(e).radius);
            // Sample a horizontal circle at entity position (ground footprint)
            float3 c = lt.Position;
            float3 px = new float3(r, 0f, 0f);
            float3 pz = new float3(0f, 0f, r);

            Vector3[] pts =
            {
                cam.WorldToScreenPoint((Vector3)(c + px)),
                cam.WorldToScreenPoint((Vector3)(c - px)),
                cam.WorldToScreenPoint((Vector3)(c + pz)),
                cam.WorldToScreenPoint((Vector3)(c - pz)),
                cam.WorldToScreenPoint((Vector3)c),
            };

            bool any = false;
            Vector2 scrMin = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 scrMax = new Vector2(float.NegativeInfinity, float.NegativeInfinity);

            for (int i = 0; i < pts.Length; i++)
            {
                var sp = pts[i];
                if (sp.z <= 0f) continue; // behind camera
                any = true;
                if (sp.x < scrMin.x) scrMin.x = sp.x;
                if (sp.y < scrMin.y) scrMin.y = sp.y;
                if (sp.x > scrMax.x) scrMax.x = sp.x;
                if (sp.y > scrMax.y) scrMax.y = sp.y;
            }

            if (any)
            {
                rect = Rect.MinMaxRect(
                    scrMin.x - DRAG_SELECT_PADDING_PX, scrMin.y - DRAG_SELECT_PADDING_PX,
                    scrMax.x + DRAG_SELECT_PADDING_PX, scrMax.y + DRAG_SELECT_PADDING_PX);
                return true;
            }
            // fall through to collider path if all samples were behind camera
        }

        // 2) Fallback: use collider's *horizontal footprint* (bottom face of AABB), not full 3D AABB
        if (em.HasComponent<PhysicsCollider>(e))
        {
            var pc = em.GetComponentData<PhysicsCollider>(e);
            if (pc.Value.IsCreated)
            {
                // World AABB of the collider at this pose
                var rt = new RigidTransform(lt.Rotation, lt.Position);
                Unity.Physics.Aabb aabb = pc.Value.Value.CalculateAabb(rt);

                // Project only the bottom face corners (XZ footprint) to avoid “tall tree” inflation
                float y = aabb.Min.y;
                float3[] corners =
                {
                    new float3(aabb.Min.x, y, aabb.Min.z),
                    new float3(aabb.Max.x, y, aabb.Min.z),
                    new float3(aabb.Min.x, y, aabb.Max.z),
                    new float3(aabb.Max.x, y, aabb.Max.z),
                };

                Vector2 scrMin = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
                Vector2 scrMax = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
                bool anyInFront = false;

                for (int i = 0; i < corners.Length; i++)
                {
                    Vector3 sp = cam.WorldToScreenPoint((Vector3)corners[i]);
                    if (sp.z <= 0f) continue; // behind camera
                    anyInFront = true;
                    if (sp.x < scrMin.x) scrMin.x = sp.x;
                    if (sp.y < scrMin.y) scrMin.y = sp.y;
                    if (sp.x > scrMax.x) scrMax.x = sp.x;
                    if (sp.y > scrMax.y) scrMax.y = sp.y;
                }

                if (anyInFront)
                {
                    rect = Rect.MinMaxRect(
                        scrMin.x - DRAG_SELECT_PADDING_PX, scrMin.y - DRAG_SELECT_PADDING_PX,
                        scrMax.x + DRAG_SELECT_PADDING_PX, scrMax.y + DRAG_SELECT_PADDING_PX);
                    return true;
                }
            }
        }

        // 3) Last resort: tiny screen-space box around projected position
        Vector3 center = cam.WorldToScreenPoint((Vector3)lt.Position);
        if (center.z > 0f)
        {
            const float fallbackRadiusPx = 10f;
            rect = Rect.MinMaxRect(center.x - fallbackRadiusPx, center.y - fallbackRadiusPx,
                                center.x + fallbackRadiusPx, center.y + fallbackRadiusPx);
            return true;
        }

        rect = default;
        return false;
    }

    static bool ScreenRectOverlapsEntity(Rect selectionRect, EntityManager em, Entity e, in LocalTransform lt, Camera cam)
    {
        if (!TryGetEntityScreenRect(em, e, lt, cam, out Rect entRect))
            return false;
        return selectionRect.Overlaps(entRect, true);
    }
}