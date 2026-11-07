using System.Collections.Generic;
using CombatExtended;
using Verse;

namespace CEOverpenetration;

public class FollowUpState : IExposable
{
    public float penetration;
    public float damage;
    public int passes;
    public Thing passedThing;

    public void ExposeData()
    {
        Scribe_Values.Look(ref penetration, "penetration");
        Scribe_Values.Look(ref damage, "damage");
        Scribe_Values.Look(ref passes, "passes");
        Scribe_References.Look(ref passedThing, "passedThing");
    }
}

public class PenetrationTracker : GameComponent
{
    private static PenetrationTracker instance;

    private Dictionary<ProjectileCE, FollowUpState> states = new Dictionary<ProjectileCE, FollowUpState>();
    private List<ProjectileCE> keys;
    private List<FollowUpState> values;

    public PenetrationTracker(Game game)
    {
        instance = this;
    }

    public static FollowUpState Get(ProjectileCE projectile)
    {
        var tracker = instance;
        if (tracker == null || tracker.states.Count == 0 || projectile == null)
        {
            return null;
        }
        return tracker.states.TryGetValue(projectile, out var state) ? state : null;
    }

    public static void Set(ProjectileCE projectile, FollowUpState state)
    {
        if (instance != null)
        {
            instance.states[projectile] = state;
        }
    }

    public static void Remove(ProjectileCE projectile)
    {
        if (instance != null && instance.states.Count > 0)
        {
            instance.states.Remove(projectile);
        }
    }

    public override void GameComponentTick()
    {
        if (states.Count > 0 && Find.TickManager.TicksGame % 2500 == 0)
        {
            Prune();
        }
    }

    private void Prune()
    {
        states.RemoveAll(pair => pair.Key == null || pair.Key.Destroyed || pair.Value == null);
    }

    public override void ExposeData()
    {
        if (Scribe.mode == LoadSaveMode.Saving)
        {
            Prune();
        }
        Scribe_Collections.Look(ref states, "states", LookMode.Reference, LookMode.Deep, ref keys, ref values);
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            states ??= new Dictionary<ProjectileCE, FollowUpState>();
            Prune();
            foreach (var pair in states)
            {
                pair.Key.DamageAmount = pair.Value.damage;
            }
        }
    }
}
