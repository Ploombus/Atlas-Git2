using UnityEngine;
using UnityEngine.UIElements;
using Managers;
using Unity.Entities;
using Unity.NetCode;
using Unity.Mathematics;
using Unity.Collections;
public class UIPanel : MonoBehaviour
{

    [SerializeField] UIDocument uiDocument;
    [SerializeField] Texture2D lockpng;
    [SerializeField] Texture2D unlockpng;
    [SerializeField] Texture2D loosepng;
    [SerializeField] Texture2D tightpng;
    private VisualElement root;
    bool lockT;
    bool formationT;
    bool attackMoveT;
    bool spawnMyUnitT;
    bool spawnEnemyUnitT;
    Color baseColor;

    private void OnEnable()
    {
        lockT = false;
        FormationUIState.IsLocked = lockT;
        formationT = true;
        attackMoveT = false;
        baseColor = new Color32(59, 50, 42, 255);

        root = uiDocument.rootVisualElement;
        var lockFormation = root.Q<VisualElement>("LockFormation");
        var formations = root.Q<VisualElement>("Formations");
        var attackMove = root.Q<VisualElement>("AttackMove");
        var spawnUnit = root.Q<VisualElement>("SpawnUnit");
        var spawnEnemy = root.Q<VisualElement>("SpawnEnemy");
        var agressive = root.Q<Button>("Aggressive");
        var defensive = root.Q<Button>("Defensive");
        var holdGround = root.Q<Button>("StandGround");

        lockFormation.RegisterCallback<ClickEvent>(LockFormationButton);
        formations.RegisterCallback<ClickEvent>(FormationsButton);
        attackMove.RegisterCallback<ClickEvent>(AttackMove);
        spawnUnit.RegisterCallback<ClickEvent>(SpawnMyUnit);
        spawnEnemy.RegisterCallback<ClickEvent>(SpawnEnemyUnit);
        agressive.RegisterCallback<ClickEvent>(SetAggressive);
        defensive.RegisterCallback<ClickEvent>(SetDefensive);
        holdGround.RegisterCallback<ClickEvent>(SetHoldGround);
    }

    public void Update()
    {
        World world = null;
        try { world = WorldManager.GetClientWorld(); } catch { /* not ready */ }
        if (world == null || !world.IsCreated) return;
        if (!CheckGameplayStateAccess.GetGameplayState(world)) return;

        if (spawnMyUnitT)
        {
            if (Input.GetMouseButtonDown(0) && !UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
            {
                var mousePosition = MouseWorldPosition.Instance.GetPosition();
                SpawnUnitRpcRequest(mousePosition, 1);
            }
        }
        if (spawnEnemyUnitT)
        {
            if (Input.GetMouseButtonDown(0) && !UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
            {
                var mousePosition = MouseWorldPosition.Instance.GetPosition();
                SpawnUnitRpcRequest(mousePosition, -1);
            }
        }

        var em = WorldManager.GetClientWorld().EntityManager;
        var q = em.CreateEntityQuery(ComponentType.ReadOnly<ShowStancesRequest>());
        if (!q.IsEmptyIgnoreFilter)
        {
            using var reqs = q.ToComponentDataArray<ShowStancesRequest>(Allocator.Temp);
            int aggressiveCount = 0, defensiveCount = 0, holdGroundCount = 0;
            for (int i = 0; i < reqs.Length; i++)
            {
                aggressiveCount += reqs[i].aggressive;
                defensiveCount += reqs[i].defensive;
                holdGroundCount += reqs[i].holdGround;
            }

            bool aggr = aggressiveCount > 0;
            bool deff = defensiveCount > 0;
            bool hold = holdGroundCount > 0;

            em.DestroyEntity(q);
            ShowStances(aggr, deff, hold);
        }
        q = em.CreateEntityQuery(ComponentType.ReadOnly<HideStancesRequest>());
        if (!q.IsEmptyIgnoreFilter)
        {
            HideStances();
            em.DestroyEntity(q);
        }
        
    }

    public void LockFormationButton(ClickEvent evt)
    {
        lockT = !lockT;

        var lockFormation = root.Q<VisualElement>("LockFormation");

        if (lockT)
        {
            lockFormation.style.backgroundColor = Color.red;
            lockFormation.style.backgroundImage = new StyleBackground(lockpng);
        }
        else
        {
            lockFormation.style.backgroundColor = baseColor;
            lockFormation.style.backgroundImage = new StyleBackground(unlockpng);
        }

        FormationUIState.IsLocked = lockT;
    }
    public void FormationsButton(ClickEvent evt)
    {
        formationT = !formationT;

        var formations = root.Q<VisualElement>("Formations");
        formations.style.backgroundImage = new StyleBackground(formationT ? tightpng : loosepng);
        FormationUIState.SelectedFormation = formationT ? Formations.Tight : Formations.Loose;
    }
    public void AttackMove(ClickEvent evt)
    {
        attackMoveT = !attackMoveT;

        var attackMove = root.Q<VisualElement>("AttackMove");

        if (attackMoveT)
        {
            attackMove.style.backgroundColor = Color.red;
        }
        else
        {
            attackMove.style.backgroundColor = baseColor;
        }
        AttackMoveUIState.IsAttackMove = attackMoveT;
    }

    public void ShowStances(bool aggr, bool deff, bool hold)
    {
        var stancesContainer = root.Q<VisualElement>("StancesContainer");
        var aggressive = root.Q<Button>("Aggressive");
        var defensive = root.Q<Button>("Defensive");
        var holdGround = root.Q<Button>("StandGround");

        if (aggr)
        {
            aggressive.style.backgroundColor = Color.red;
        }
        else
        {
            aggressive.style.backgroundColor = baseColor;
        }
        if (deff)
        {
            defensive.style.backgroundColor = Color.red;
        }
        else
        {
            defensive.style.backgroundColor = baseColor;
        }
        if (hold)
        {
            holdGround.style.backgroundColor = Color.red;
        }
        else
        {
            holdGround.style.backgroundColor = baseColor;
        }

        stancesContainer.style.display = DisplayStyle.Flex;
    }
    public void HideStances()
    {
        var stancesContainer = root.Q<VisualElement>("StancesContainer");

        stancesContainer.style.display = DisplayStyle.None;
    }
    public void SetAggressive(ClickEvent evt)
    {
        ApplyStanceRequest(Stance.Aggressive);

        var aggressive = root.Q<Button>("Aggressive");
        var defensive = root.Q<Button>("Defensive");
        var holdGround = root.Q<Button>("StandGround");

        aggressive.style.backgroundColor = Color.red;
        defensive.style.backgroundColor = baseColor;
        holdGround.style.backgroundColor = baseColor;
    }
    public void SetDefensive(ClickEvent evt)
    {
        ApplyStanceRequest(Stance.Defensive);

        var aggressive = root.Q<Button>("Aggressive");
        var defensive = root.Q<Button>("Defensive");
        var holdGround = root.Q<Button>("StandGround");
        
        aggressive.style.backgroundColor = baseColor;
        defensive.style.backgroundColor = Color.red;
        holdGround.style.backgroundColor = baseColor;
    }
    public void SetHoldGround(ClickEvent evt)
    {
        ApplyStanceRequest(Stance.HoldGround);
        
        var aggressive = root.Q<Button>("Aggressive");
        var defensive = root.Q<Button>("Defensive");
        var holdGround = root.Q<Button>("StandGround");
        
        aggressive.style.backgroundColor = baseColor;
        defensive.style.backgroundColor = baseColor;
        holdGround.style.backgroundColor = Color.red;
    }

    private void ApplyStanceRequest(Stance stance)
    {
        var world = WorldManager.GetClientWorld();
        if (world == null || !world.IsCreated) return;

        var em = world.EntityManager;
        var q = em.CreateEntityQuery(
            ComponentType.ReadOnly<UnitTargetsNetcode>(),
            ComponentType.ReadOnly<GhostOwnerIsLocal>(),
            ComponentType.ReadOnly<Selected>());

        using var ents = q.ToEntityArray(Unity.Collections.Allocator.Temp);
        for (int i = 0; i < ents.Length; i++)
        {
            var e = ents[i];
            if (!em.IsComponentEnabled<Selected>(e)) continue;

            var net = em.GetComponentData<UnitTargetsNetcode>(e);
            net.requestStance = stance;          // set; DO NOT clear locally
            em.SetComponentData(e, net);
        }
    }

    public void SpawnMyUnit(ClickEvent evt)
    {
        var spawnUnit = root.Q<VisualElement>("SpawnUnit");
        var spawnEnemy = root.Q<VisualElement>("SpawnEnemy");

        spawnMyUnitT = !spawnMyUnitT;
        spawnEnemyUnitT = false;
        spawnEnemy.style.backgroundColor = baseColor;

        if (spawnMyUnitT)
        {
            spawnUnit.style.backgroundColor = Color.red;
        }
        else
        {
            spawnUnit.style.backgroundColor = baseColor;
        }

    }
    public void SpawnEnemyUnit(ClickEvent evt)
    {
        var spawnUnit = root.Q<VisualElement>("SpawnUnit");
        var spawnEnemy = root.Q<VisualElement>("SpawnEnemy");
        
        spawnEnemyUnitT = !spawnEnemyUnitT;
        spawnMyUnitT = false;
        spawnUnit.style.backgroundColor = baseColor;

        if (spawnEnemyUnitT)
        {
            spawnEnemy.style.backgroundColor = Color.red;
        }
        else
        {
            spawnEnemy.style.backgroundColor = baseColor;
        }
    }

    public void SpawnUnitRpcRequest(Vector3 position, int owner)
    {
        var em = WorldManager.GetClientWorld().EntityManager;
        var rpc = em.CreateEntity();
        em.AddComponentData(rpc, new SpawnUnitRpc { position = position, owner = owner });
        em.AddComponentData(rpc, new SendRpcCommandRequest());
    }

}

public static class FormationUIState
{
    public static Formations SelectedFormation = Formations.Tight;
    public static bool IsLocked = false;
}
public static class AttackMoveUIState
{
    public static bool IsAttackMove = false;
}
public struct SpawnUnitRpc : IRpcCommand
{
    public float3 position;
    public int owner;
}