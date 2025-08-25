using UnityEngine;
using Unity.Mathematics;
using Unity.Collections;
using Unity.Transforms;
using System.Collections.Generic;

public struct FormationStrategy
{
    public FormationGenerator Generator;             // manual drag
    public FormationAutoGenerator AutoMimicGenerator; // auto mimic
    public float DefaultMargin;
}

public delegate NativeArray<float3> FormationGenerator(
    float3 targetPosition,
    float3 cursorPosition,
    NativeArray<float3> currentPositions,
    int unitCount,
    float angleOffset,
    Allocator allocator
);
public delegate NativeArray<float3> FormationAutoGenerator(
    float3 targetPosition,
    int unitCount,
    int columnCount,
    float angleOffset,
    float margin,
    Allocator allocator
);

public enum Formations
{
    Locked,
    Tight,
    Loose,
}


public static class FormationLibrary
{
    public static FormationGenerator Get(Formations type)
    {
        return type switch
        {
            Formations.Locked => LockedFormation,
            Formations.Tight => TightFormation,
            Formations.Loose => LooseFormation,
            _ => throw new System.ArgumentOutOfRangeException(nameof(type), $"No formation logic for {type}")
        };
    }

    private static readonly Dictionary<Formations, FormationStrategy> _strategies = new()
    {
        [Formations.Tight] = new FormationStrategy
        {
            Generator = TightFormation,
            AutoMimicGenerator = GenerateGrid,
            DefaultMargin = 1.0f
        },
        [Formations.Loose] = new FormationStrategy
        {
            Generator = LooseFormation,
            AutoMimicGenerator = GenerateStaggeredGrid,
            DefaultMargin = 2f
        },
        // Add more here
    };
        public static NativeArray<float3> GenerateAutoMimic(
        Formations formation,
        float3 targetPosition,
        int unitCount,
        float angleOffset,
        Allocator allocator)
    {
        if (!_strategies.TryGetValue(formation, out var strategy))
            throw new System.Exception($"Formation not supported: {formation}");

        float margin = strategy.DefaultMargin;
        int columnCount = math.max(1, (int)(math.sqrt(unitCount) * 1.5f * margin / margin));

        return strategy.AutoMimicGenerator(
            targetPosition,
            unitCount,
            columnCount,
            angleOffset,
            margin,
            allocator
        );
    }




    public static NativeArray<float3> GenerateGrid(
        float3 targetPosition,
        int unitCount,
        int columnCount,
        float angleOffset,
        float margin,
        Allocator allocator)
    {
        NativeArray<float3> positionArray = new NativeArray<float3>(unitCount, allocator);

        if (unitCount == 0) return positionArray;
        if (unitCount == 1)
        {
            positionArray[0] = targetPosition;
            return positionArray;
        }

        int rowCount = (int)math.ceil(unitCount / (float)columnCount);

        float gridWidth = (columnCount - 1) * margin;
        float gridHeight = (rowCount - 1) * margin;

        // Center offset
        float3 offsetToCenter = new float3(gridWidth * 0.5f, 0f, -gridHeight * 0.5f);

        int index = 0;
        for (int row = 0; row < rowCount && index < unitCount; row++)
        {
            for (int col = 0; col < columnCount && index < unitCount; col++)
            {
                float3 localPos = new float3(col * margin, 0f, -row * margin);
                float3 centered = localPos - offsetToCenter;
                float3 rotated = math.rotate(quaternion.RotateY(-angleOffset), centered);
                positionArray[index++] = targetPosition + rotated;
            }
        }

        return positionArray;
    }

    public static NativeArray<float3> GenerateStaggeredGrid(
        float3 targetPosition,
        int unitCount,
        int columnCount,
        float angleOffset,
        float margin,
        Allocator allocator)
    {
        NativeArray<float3> positionArray = new NativeArray<float3>(unitCount, allocator);

        if (unitCount == 0) return positionArray;
        if (unitCount == 1)
        {
            positionArray[0] = targetPosition;
            return positionArray;
        }

        int rowCount = (int)math.ceil(unitCount / (float)columnCount);
        float gridWidth = (columnCount - 1 + 0.5f) * margin;
        float gridHeight = (rowCount - 1) * margin;

        if (columnCount <= 0) columnCount = 1;

        // Center offset
        float3 offsetToCenter = new float3(gridWidth * 0.5f, 0f, -gridHeight * 0.5f);

        int index = 0;
        for (int row = 0; row < rowCount && index < unitCount; row++)
        {
            for (int col = 0; col < columnCount && index < unitCount; col++)
            {
                float xOffset = col * margin;
                if (row % 2 == 1)
                    xOffset += margin * 0.5f;

                float3 localPos = new float3(xOffset, 0f, -row * margin);
                float3 centered = localPos - offsetToCenter;
                float3 rotated = math.rotate(quaternion.RotateY(-angleOffset), centered);
                positionArray[index++] = targetPosition + rotated;
            }
        }

        return positionArray;
    }


    public static NativeArray<float3> LockedFormation(
        float3 targetPosition,
        float3 cursorPosition,
        NativeArray<float3> currentPositions,
        int unitCount,
        float angleOffset,
        Allocator allocator)
    {
        int count = currentPositions.Length;
        NativeArray<float3> output = new NativeArray<float3>(count, allocator);

        // Calculate original center
        float3 center = float3.zero;
        for (int i = 0; i < count; i++)
            center += currentPositions[i];
        center /= math.max(1, count);

        // Offset positions from center, reapply at new position
        for (int i = 0; i < count; i++)
        {
            float3 offset = currentPositions[i] - center;
            output[i] = targetPosition + offset;
        }

        return output;
    }

    public static NativeArray<float3> TightFormation(
        float3 targetPosition,
        float3 cursorPosition,
        NativeArray<float3> currentPositions,
        int unitCount,
        float angleOffset,
        Allocator allocator)
    {
        NativeArray<float3> positionArray = new NativeArray<float3>(unitCount, allocator);

        if (unitCount == 0) { return positionArray; }
        positionArray[0] = targetPosition;
        if (unitCount == 1) { return positionArray; }

        float margin = 1f;
        float formationWidth = math.length(cursorPosition - targetPosition);
        int columnCount = math.max(1, (int)(formationWidth / margin));

        int positionIndex = 0;
        int row = 0;

        while (positionIndex < unitCount)
        {
            for (int col = 0; col < columnCount && positionIndex < unitCount; col++)
            {
                float3 pos = targetPosition + math.rotate(
                    quaternion.RotateY(-angleOffset),
                    new float3(col * margin, 0f, -row * margin)
                );
                positionArray[positionIndex++] = pos;
            }
            row++;
        }

        return positionArray;
    }

    public static NativeArray<float3> LooseFormation(
        float3 targetPosition,
        float3 cursorPosition,
        NativeArray<float3> currentPositions,
        int unitCount,
        float angleOffset,
        Allocator allocator)
    {
        NativeArray<float3> positionArray = new NativeArray<float3>(unitCount, allocator);

        if (unitCount == 0) return positionArray;
        if (unitCount == 1)
        {
            positionArray[0] = targetPosition;
            return positionArray;
        }

        float margin = 2f;
        float formationWidth = math.length(cursorPosition - targetPosition);
        int columnCount = math.max(1, (int)(formationWidth / margin));
        int rowCount = (int)math.ceil(unitCount / (float)columnCount);

        int positionIndex = 0;
        for (int row = 0; row < rowCount && positionIndex < unitCount; row++)
        {
            for (int col = 0; col < columnCount && positionIndex < unitCount; col++)
            {
                float xOffset = col * margin;
                if (row % 2 == 1)
                    xOffset += margin * 0.5f;

                float3 localPos = new float3(xOffset, 0f, -row * margin);

                float3 rotated = math.rotate(quaternion.RotateY(-angleOffset), localPos);
                positionArray[positionIndex++] = targetPosition + rotated;
            }
        }

        return positionArray;
    }


}