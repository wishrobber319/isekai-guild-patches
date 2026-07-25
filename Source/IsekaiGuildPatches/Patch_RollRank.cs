using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using GuildFactionAddon;
using HarmonyLib;
using IsekaiLeveling;
using IsekaiLeveling.Quests;
using Verse;

namespace GuildQuestRankRange
{
    // The guild board rolls each daily quest's rank via a private static helper RollRank(maxRank, goodwill),
    // called from RollDailyEntries. A Prefix on RollRank had NO EFFECT: Mono inlines that small helper into
    // RollDailyEntries, so the patch never fired (see the inlining gotcha - patch the loop-bearing caller,
    // not the tiny helper). Instead we TRANSPILE RollDailyEntries and redirect its RollRank call to our own
    // roll, which bounds the rank to the party's band:
    //   floor   = the party's AVERAGE rank (so one low-level recruit can't drag the board down to F),
    //   ceiling = the strongest colonist's rank + 1 (a stretch challenge).
    // We keep the add-on's original weighting (lower ranks common, C+ skewed up by goodwill) and honor
    // Isekai's "Minimum Quest Rank" floor. The rest of RollDailyEntries (pawn + reward selection) then uses
    // our rank unchanged. Falls back to the add-on's own roll when there is no party to measure.
    [HarmonyPatch(typeof(GuildQuestBoardWorldComponent), "RollDailyEntries")]
    public static class Patch_GuildQuestBoard_RollDailyEntries
    {
        // QuestRank enum order: F=0, E=1, D=2, C=3, B=4, A=5, S=6, SS=7, SSS=8.
        private const int MinRankIndex = 0;
        private const int MaxRankIndex = 8;
        private const int GoodwillSkewFromRank = 3; // QuestRank.C and above get the goodwill bonus

        private static readonly MethodInfo OriginalRollRank =
            AccessTools.Method(typeof(GuildQuestBoardWorldComponent), "RollRank");

        private static string lastBandLog;

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo replacement = AccessTools.Method(typeof(Patch_GuildQuestBoard_RollDailyEntries), nameof(ClampedRollRank));
            foreach (CodeInstruction ins in instructions)
            {
                // Redirect the (inlined-in-place) RollRank(maxRank, goodwill) call to our bounded roll.
                if (OriginalRollRank != null && ins.Calls(OriginalRollRank))
                {
                    ins.opcode = OpCodes.Call;
                    ins.operand = replacement;
                }
                yield return ins;
            }
        }

        // Same signature as RollRank so it drops straight into the call site: (maxRank, goodwill) on the
        // stack, QuestRank returned.
        public static QuestRank ClampedRollRank(QuestRank maxRank, int goodwill)
        {
            Map map = Find.Maps?.FirstOrDefault(m => m != null && m.IsPlayerHome);
            if (map == null) return OriginalRoll(maxRank, goodwill);

            List<Pawn> pawns = IsekaiComponent.GetIsekaiPawnsOnMap(map);
            if (pawns == null || pawns.Count == 0) return OriginalRoll(maxRank, goodwill);

            int highest = int.MinValue;
            long sumLevel = 0L;
            int count = 0;
            foreach (Pawn pawn in pawns)
            {
                int level = IsekaiComponent.GetCached(pawn)?.Level ?? 1;
                int rank = RankIndexFromLevel(level);
                if (rank > highest) highest = rank;
                sumLevel += level;
                count++;
            }
            if (highest == int.MinValue || count == 0) return OriginalRoll(maxRank, goodwill);

            // Floor on the party's AVERAGE rank, ceiling on the strongest + 1.
            int averageRank = RankIndexFromLevel((int)(sumLevel / count));
            int low = Clamp(averageRank, MinRankIndex, MaxRankIndex);
            int high = Clamp(highest + 1, MinRankIndex, MaxRankIndex);

            // Honor Isekai's "Minimum Quest Rank" floor (0 = All, so a no-op).
            int minQuestRank = IsekaiLevelingSettings.Settings?.MinQuestRank ?? 0;
            if (low < minQuestRank) low = Clamp(minQuestRank, MinRankIndex, MaxRankIndex);
            if (high < low) high = low;

            // One de-duped line so the band is verifiable without Dev Mode.
            string line = $"[Isekai Guild] Quest rank band [{(QuestRank)low}..{(QuestRank)high}] " +
                          $"(party avg {(QuestRank)averageRank}, top {(QuestRank)highest}, {count} pawns)";
            if (line != lastBandLog)
            {
                lastBandLog = line;
                Log.Message(line);
            }

            return (QuestRank)WeightedRoll(low, high, goodwill);
        }

        // No party to measure: fall back to the add-on's own roll. RollRank still exists (only the call site
        // in RollDailyEntries was redirected), so reflection reaches the real method.
        private static QuestRank OriginalRoll(QuestRank maxRank, int goodwill)
        {
            if (OriginalRollRank != null)
            {
                return (QuestRank)OriginalRollRank.Invoke(null, new object[] { maxRank, goodwill });
            }
            return maxRank;
        }

        // Mirrors the add-on's original RollRank curve, but over [low, high]: base weight 50 - i*5
        // (lower ranks common), with ranks C+ multiplied by a goodwill bonus of up to x2 at +100 goodwill.
        private static int WeightedRoll(int low, int high, int goodwill)
        {
            float goodwillBonus = 1f + ClampF(goodwill, 0f, 100f) * 0.01f;

            float total = 0f;
            for (int i = low; i <= high; i++) total += RankWeight(i, goodwillBonus);

            float roll = Rand.Range(0f, total);
            float cumulative = 0f;
            for (int i = low; i <= high; i++)
            {
                cumulative += RankWeight(i, goodwillBonus);
                if (roll <= cumulative) return i;
            }
            return high;
        }

        private static float RankWeight(int rankIndex, float goodwillBonus)
        {
            float weight = 50f - rankIndex * 5f;
            if (rankIndex >= GoodwillSkewFromRank) weight *= goodwillBonus;
            return weight;
        }

        private static float ClampF(float value, float min, float max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        // Canonical Isekai level -> rank mapping (matches IsekaiComponent.GetRankFromLevel).
        private static int RankIndexFromLevel(int level)
        {
            if (level >= 401) return 8; // SSS
            if (level >= 201) return 7; // SS
            if (level >= 101) return 6; // S
            if (level >= 51) return 5;  // A
            if (level >= 26) return 4;  // B
            if (level >= 18) return 3;  // C
            if (level >= 11) return 2;  // D
            if (level >= 6) return 1;   // E
            return 0;                   // F
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }
}
