using HarmonyLib;
using IsekaiLeveling.Quests;

namespace GuildQuestRankRange
{
    // Suppress Isekai's own guild-hunt STORYTELLER incident so the Guild Add-on board is the single source
    // of daily guild quests.
    //
    // IncidentWorker_IsekaiHunt does double duty: the board (GuildQuestBoardWorldComponent.RollDailyEntries)
    // calls its STATIC CreateHuntQuest to build the 5 daily board entries, but the same class ALSO fires as
    // a random storyteller event a few times a day and auto-generates EXTRA hunts via DetermineRankForColony
    // - a weighted roll skewed hard toward low ranks (F=50, E=45, ...) up to a colony cap, ignoring the
    // board's party-rank band (see Patch_GuildQuestBoard_RollDailyEntries). Those extra low-rank hunts are
    // the "outliers" showing up beside the board's 5.
    //
    // We stop the storyteller from firing this incident (CanFireNowSub -> false). Only the board's 5
    // rank-banded quests remain. The static CreateHuntQuest path used on accept is not touched.
    [HarmonyPatch(typeof(IncidentWorker_IsekaiHunt), "CanFireNowSub")]
    public static class Patch_SuppressIsekaiHuntIncident
    {
        public static bool Prefix(ref bool __result)
        {
            __result = false;
            return false; // never let the storyteller fire the guild-hunt incident
        }
    }
}
