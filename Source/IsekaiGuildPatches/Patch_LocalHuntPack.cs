using System;
using HarmonyLib;
using IsekaiLeveling.MobRanking;
using IsekaiLeveling.Quests;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace IsekaiGuildPatches
{
    // Give some bounties a PACK, for a mix of pack and single-target fights.
    //
    // The base local hunt spawns exactly ONE target. Here we roll a rank-scaled pack size and spawn
    // that many extra hostiles of the same kind, each rank-scaled/elite like the target and joined to
    // the target's own LordJob_AssaultColony, so the whole pack marches on the colony together.
    //
    // Completion is left to the base mod: it fires on the bounty TARGET's death (the reward is for the
    // mark), and the rest of the pack is simply the fight around it. Escorts are ordinary pawns in an
    // ordinary saved lord, so this needs no bookkeeping and survives save/load for free.
    [HarmonyPatch(typeof(QuestPart_IsekaiLocalHunt), "SpawnCreatureOnMap")]
    public static class Patch_LocalHuntPack
    {
        private static readonly AccessTools.FieldRef<QuestPart_IsekaiLocalHunt, Pawn> SpawnedCreatureRef =
            AccessTools.FieldRefAccess<QuestPart_IsekaiLocalHunt, Pawn>("spawnedCreature");

        public static void Postfix(QuestPart_IsekaiLocalHunt __instance, Map map)
        {
            Pawn main = SpawnedCreatureRef(__instance);
            if (main == null || !main.Spawned || map == null)
            {
                return;
            }

            int packSize = RollPackSize(__instance.rank);
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
        }

        // A genuine mix: ~35% of bounties come alone at any rank; the rest bring a rank-scaled pack.
        private static int RollPackSize(QuestRank rank)
        {
            if (Rand.Chance(0.35f))
            {
                return 1;
            }

            int b;
            switch (rank)
            {
                case QuestRank.F:
                case QuestRank.E: b = 2; break;
                case QuestRank.D:
                case QuestRank.C: b = 3; break;
                case QuestRank.B: b = 4; break;
                case QuestRank.A: b = 6; break;
                case QuestRank.S: b = 8; break;
                case QuestRank.SS: b = 10; break;
                case QuestRank.SSS: b = 12; break;
                default: b = 2; break;
            }
            return Rand.RangeInclusive(Math.Max(2, b - 1), b + 1);
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
}
