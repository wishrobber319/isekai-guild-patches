using System.Collections.Generic;
using System.Linq;
using GuildFactionAddon;
using GuildFactionAddon.Patches;
using HarmonyLib;
using IsekaiLeveling;
using IsekaiLeveling.Quests;
using Verse;

namespace GuildQuestRankRange
{
    // The guild board picks each daily quest's rank via GuildQuestBoardWorldComponent.RollRank
    // (a level-capped weighted roll). We replace the [F..cap] bound with the party's own range
    // [lowestColonistRank - 1, highestColonistRank + 1], keep the add-on's original weighting
    // (lower ranks common, ranks C+ skewed up by guild goodwill), and honor Isekai's
    // "Minimum Quest Rank" setting as a floor.
    [HarmonyPatch(typeof(GuildQuestBoardWorldComponent), "RollRank")]
    public static class Patch_GuildQuestBoard_RollRank
    {
        // QuestRank enum order: F=0, E=1, D=2, C=3, B=4, A=5, S=6, SS=7, SSS=8.
        private const int MinRankIndex = 0;
        private const int MaxRankIndex = 8;
        private const int GoodwillSkewFromRank = 3; // QuestRank.C and above get the goodwill bonus

        public static bool Prefix(ref QuestRank __result)
        {
            Map map = Find.Maps?.FirstOrDefault(m => m != null && m.IsPlayerHome);
            if (map == null) return true; // no home map: let the original logic run

            List<Pawn> pawns = IsekaiComponent.GetIsekaiPawnsOnMap(map);
            if (pawns == null || pawns.Count == 0) return true;

            int highest = int.MinValue;
            int lowest = int.MaxValue;
            foreach (Pawn pawn in pawns)
            {
                int level = IsekaiComponent.GetCached(pawn)?.Level ?? 1;
                int rank = RankIndexFromLevel(level);
                if (rank > highest) highest = rank;
                if (rank < lowest) lowest = rank;
            }

            if (highest == int.MinValue) return true; // safety: nothing usable

            int low = Clamp(lowest - 1, MinRankIndex, MaxRankIndex);
            int high = Clamp(highest + 1, MinRankIndex, MaxRankIndex);

            // Honor Isekai's "Minimum Quest Rank" floor (0 = All, so a no-op).
            int minQuestRank = IsekaiLevelingSettings.Settings?.MinQuestRank ?? 0;
            if (low < minQuestRank) low = Clamp(minQuestRank, MinRankIndex, MaxRankIndex);
            if (high < low) high = low;

            __result = (QuestRank)WeightedRoll(low, high);
            return false; // skip the original weighted roll
        }

        // Mirrors the add-on's original RollRank curve, but over [low, high] instead of [F, cap]:
        // base weight 50 - i*5 (F most common ... SSS least), with ranks C+ multiplied by a goodwill
        // bonus of up to x2 at +100 goodwill.
        private static int WeightedRoll(int low, int high)
        {
            int goodwill = GuildFactionUtility.GetGuildFaction()?.PlayerGoodwill ?? 0;
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
