using RimWorld;
using UnityEngine;
using Verse;

namespace CEOverpenetration;

internal static class ObstacleMaterial
{
    private const float WoodyBase = 5f;
    private const float MetallicBase = 12f;
    private const float StonyBase = 15f;
    private const float BagValue = 20f;
    private const float CurtainValue = 0.5f;
    private const float TreeValue = 15f;
    private const float MinTreeGrowth = 0.2f;
    private const float DoorFactor = 0.5f;
    private const float WornFloor = 0.3f;
    private const float ThinValue = 6f;

    public static bool IsObstacle(Thing thing)
    {
        return thing is Building || thing is Plant plant && plant.def.plant != null && plant.def.plant.IsTree;
    }

    public static bool IsThin(Thing thing)
    {
        var value = BaseValue(thing);
        return value >= 0f && value <= ThinValue;
    }

    public static float Value(Thing thing, float hitPointsFraction, OverpenetrationSettings settings)
    {
        var value = BaseValue(thing);
        if (value < 0f)
        {
            return -1f;
        }
        if (settings.damagedWalls)
        {
            value *= Mathf.Lerp(WornFloor, 1f, hitPointsFraction);
        }
        return value * settings.wallStrength;
    }

    private static float BaseValue(Thing thing)
    {
        var extension = thing.def.GetModExtension<PenetrationExtension>();
        if (extension != null)
        {
            if (extension.impenetrable)
            {
                return -1f;
            }
            if (extension.material >= 0f)
            {
                return extension.material;
            }
        }
        if (thing is Plant plant)
        {
            return TreeValue * Mathf.Max(plant.Growth, MinTreeGrowth);
        }
        if (thing is Mineable || !thing.def.useHitPoints || !thing.def.destroyable)
        {
            return -1f;
        }
        var stuff = thing.Stuff;
        var categories = stuff?.stuffProps?.categories;
        if (categories == null)
        {
            return -1f;
        }
        var door = thing is Building_Door;
        if (categories.Contains(StuffCategoryDefOf.Fabric) || categories.Contains(StuffCategoryDefOf.Leathery))
        {
            return door ? CurtainValue : BagValue;
        }
        var baseValue = categories.Contains(StuffCategoryDefOf.Stony) ? StonyBase
            : categories.Contains(StuffCategoryDefOf.Metallic) ? MetallicBase
            : categories.Contains(StuffCategoryDefOf.Woody) ? WoodyBase
            : -1f;
        if (baseValue < 0f)
        {
            return -1f;
        }
        var value = baseValue * stuff.stuffProps.statFactors.GetStatFactorFromList(StatDefOf.MaxHitPoints);
        return door ? value * DoorFactor : value;
    }

    public static string Describe(Thing thing, float hitPointsFraction)
    {
        var extension = thing.def.GetModExtension<PenetrationExtension>();
        if (extension != null && (extension.impenetrable || extension.material >= 0f))
        {
            return extension.impenetrable ? "set impenetrable by a mod" : $"value set by a mod, condition {hitPointsFraction:P0}";
        }
        if (thing is Plant plant)
        {
            return $"tree, growth {plant.Growth:P0}, condition {hitPointsFraction:P0}";
        }
        if (thing is Mineable)
        {
            return "rock";
        }
        if (!thing.def.useHitPoints || !thing.def.destroyable)
        {
            return "indestructible";
        }
        return thing.Stuff == null ? "no material" : $"{thing.Stuff.defName}, condition {hitPointsFraction:P0}";
    }
}
