using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using IsekaiLeveling.MobRanking;
using IsekaiLeveling.Quests;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace IsekaiGuildPatches
{
    // Shared state for the pack patches: a reader for the base quest part's private target pawn, and a
    // weak map from each bounty target -> the assault lord its pack shares. Weak so it never keeps dead
    // pawns alive; not persisted, so after a mid-fight save/load the flee-on-leader-death simply no-ops
    // (the pack keeps assaulting, i.e. the pre-flee behaviour) - a harmless degradation.
    internal static class LocalHuntPackState
    {
        internal static readonly AccessTools.FieldRef<QuestPart_IsekaiLocalHunt, Pawn> Target =
            AccessTools.FieldRefAccess<QuestPart_IsekaiLocalHunt, Pawn>("spawnedCreature");

        internal static readonly ConditionalWeakTable<Pawn, Lord> PackLords =
            new ConditionalWeakTable<Pawn, Lord>();
    }

    // Give some bounties a PACK, for a mix of pack and single-target fights.
    //
    // The base local hunt spawns exactly ONE target. Here we roll a rank-scaled pack size and spawn
    // that many extra hostiles of the same kind, each rank-scaled/elite like the target and joined to
    // the target's own LordJob_AssaultColony, so the whole pack marches on the colony together.
    //
    // Completion is left to the base mod: it fires on the bounty TARGET's death (the reward is for the
    // mark). Escorts are ordinary pawns in an ordinary saved lord, so this survives save/load for free.
    [HarmonyPatch(typeof(QuestPart_IsekaiLocalHunt), "SpawnCreatureOnMap")]
    public static class Patch_LocalHuntPack
    {
        public static void Postfix(QuestPart_IsekaiLocalHunt __instance, Map map)
        {
            Pawn main = LocalHuntPackState.Target(__instance);
            if (main == null || !main.Spawned || map == null)
            {
                return;
            }

            // Use the size decided (and shown in the title) at quest creation; fall back to a fresh roll
            // only if there's no record (e.g. a non-guild hunt, or a pre-existing quest from before this
            // update). Consume the record once spawned.
            int packSize;
            GuildBountyPackTracker tracker = GuildBountyPackTracker.Get();
            int questId = __instance.quest?.id ?? -1;
            if (tracker != null && questId != -1 && tracker.TryGet(questId, out packSize))
            {
                tracker.Clear(questId);
            }
            else
            {
                packSize = PackRules.Roll(__instance.rank);
            }
            if (packSize <= 1)
            {
                return; // lone target
            }

            Faction faction = main.Faction ?? IncidentWorker_IsekaiHunt.GetHostileFactionForBounty();
            Lord lord = main.GetLord();
            for (int i = 1; i < packSize; i++)
            {
                SpawnEscort(__instance.creatureKind, __instance.rank, faction, map, main.Position, lord);
            }

            // Remember the pack's shared lord so we can rout the survivors when the target dies.
            if (lord != null)
            {
                LocalHuntPackState.PackLords.Remove(main);
                LocalHuntPackState.PackLords.Add(main, lord);
            }
        }

        // Spawn one extra pack member: same kind, same hostile faction, rank-scaled/elite like the
        // target (mirrors the base spawn's stat treatment), placed near the target and added to its lord.
        private static void SpawnEscort(PawnKindDef kind, QuestRank rank, Faction faction, Map map, IntVec3 near, Lord lord)
        {
            if (kind == null)
            {
                return;
            }

            PawnGenerationRequest req = new PawnGenerationRequest(kind, faction, PawnGenerationContext.NonPlayer,
                map.Tile, forceGenerateNewPawn: true, mustBeCapableOfViolence: true,
                developmentalStages: DevelopmentalStage.Adult);
            Pawn p = PawnGenerator.GeneratePawn(req);
            if (p == null)
            {
                return;
            }

            IncidentWorker_IsekaiHunt.EnsureCombatCapable(p);
            MobRankComponent mob = p.TryGetComp<MobRankComponent>();
            if (mob != null)
            {
                mob.SetRankOverride((MobRankTier)rank);
                mob.SetEliteOverride(true);
            }
            if (p.RaceProps.Humanlike)
            {
                IncidentWorker_IsekaiHunt.ForceBountyPawnRank(p, rank);
            }
            p.health.Reset();
            IncidentWorker_IsekaiHunt.RemoveIncapacitatingConditions(p);

            if (!CellFinder.TryFindRandomCellNear(near, map, 10,
                    (IntVec3 c) => c.Standable(map) && !c.Fogged(map), out IntVec3 cell))
            {
                cell = near;
            }
            GenSpawn.Spawn(p, cell, map, Rot4.Random);
            lord?.AddPawn(p);
        }
    }

    // Kill the leader, break the pack: when the bounty target dies, any surviving pack members drop the
    // assault and sprint off the nearest map edge, like a routed raid. Runs off the base quest part's
    // own target-death hook (which is also where it completes the quest).
    [HarmonyPatch(typeof(QuestPart_IsekaiLocalHunt), "Notify_PawnKilled")]
    public static class Patch_PackFleeOnTargetDeath
    {
        public static void Postfix(QuestPart_IsekaiLocalHunt __instance, Pawn pawn)
        {
            if (pawn == null || pawn != LocalHuntPackState.Target(__instance))
            {
                return; // only when the bounty TARGET (the leader) dies
            }
            if (!LocalHuntPackState.PackLords.TryGetValue(pawn, out Lord lord) || lord == null)
            {
                return; // lone target, or no pack recorded (e.g. post-load)
            }
            LocalHuntPackState.PackLords.Remove(pawn);

            List<Pawn> survivors = new List<Pawn>();
            foreach (Pawn p in lord.ownedPawns)
            {
                if (p != null && p != pawn && !p.Dead && p.Spawned)
                {
                    survivors.Add(p);
                }
            }
            if (survivors.Count == 0)
            {
                return;
            }

            Map map = survivors[0].Map;
            if (map == null)
            {
                return;
            }
            LordMaker.MakeNewLord(survivors[0].Faction,
                new LordJob_ExitMapBest(LocomotionUrgency.Sprint, false, true),
                map, survivors);
        }
    }
}
