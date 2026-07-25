using System.Collections.Generic;
using GuildFactionAddon;
using HarmonyLib;

namespace IsekaiGuildLootTweaks
{
    // Boost the shared bonus XP a completed guild contract pays out. The board sets each entry's xpReward
    // (the TOTAL XP split among all participants when the target dies) in RollDailyEntries; that value flows
    // into the quest part and is what gets distributed on completion. We scale it once per fresh roll
    // (entries are cleared and rebuilt each refresh, so there is no compounding), which raises both the
    // reward shown on the board and the XP actually awarded.
    //
    // This shares the RollDailyEntries method with GuildQuestRankRange's transpiler (rank band); a Postfix
    // and a Transpiler coexist fine - the transpiler shapes the entries, then this runs on the result.
    [HarmonyPatch(typeof(GuildQuestBoardWorldComponent), "RollDailyEntries")]
    public static class Patch_GuildBoard_XpRewardBoost
    {
        // Multiplier on contract completion XP. 1.0 = vanilla Isekai amount.
        private const float XpRewardMultiplier = 3f;

        private static readonly AccessTools.FieldRef<GuildQuestBoardWorldComponent, List<GuildQuestBoardEntry>> EntriesRef =
            AccessTools.FieldRefAccess<GuildQuestBoardWorldComponent, List<GuildQuestBoardEntry>>("entries");

        [HarmonyPostfix]
        public static void Postfix(GuildQuestBoardWorldComponent __instance)
        {
            List<GuildQuestBoardEntry> entries = EntriesRef(__instance);
            if (entries == null)
            {
                return;
            }

            foreach (GuildQuestBoardEntry entry in entries)
            {
                if (entry != null)
                {
                    entry.xpReward *= XpRewardMultiplier;
                }
            }
        }
    }
}
