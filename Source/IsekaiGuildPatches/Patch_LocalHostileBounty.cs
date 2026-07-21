using GuildFactionAddon;
using HarmonyLib;
using IsekaiLeveling.Quests;
using Verse;

namespace IsekaiGuildPatches
{
    // Reshape every Isekai guild quest into a LOCAL, HOSTILE "Bounty":
    //   1. Force local spawns (never a remote world site) by failing the hunt-site tile search, which
    //      drops CreateHuntQuest into its home-map branch (QuestPart_IsekaiLocalHunt). That branch
    //      already picks a random map-edge entry cell, so targets arrive from the edge.
    //   2. Force isBounty = true. In QuestPart_IsekaiLocalHunt the ONLY branch that gives the target a
    //      hostile faction + a LordJob_AssaultColony (i.e. it actually attacks) is the isBounty branch;
    //      otherwise creatures spawn factionless and just wander, or manhunter-and-calm. Forcing the
    //      flag also names the vanilla quest "Bounty".
    //   3. Relabel the guild board so every listing shows as a Bounty too, matching the quests.
    // (1) and (2) sit on the base Isekai CreateHuntQuest path, so they cover both the board-mirrored
    // quests and any direct Isekai hunt. Note: the local branch spawns a single target, so what used to
    // be a multi-target world expedition/raid becomes one (rank-scaled, elite) local target.

    // 1. Force LOCAL: make the world-site tile search always fail -> CreateHuntQuest falls back home.
    [HarmonyPatch(typeof(IncidentWorker_IsekaiHunt), "TryFindHuntSiteTile")]
    public static class Patch_ForceLocalHunt
    {
        public static bool Prefix(ref int tile, ref bool __result)
        {
            tile = -1;
            __result = false;
            return false; // skip the real search; CreateHuntQuest reads this as "no site -> local"
        }
    }

    // 1b. Our ForceLocal makes every B+ hunt fall back to the home map on purpose, so swallow the
    // now-expected "could not find suitable tile" warning it would otherwise log each time.
    [HarmonyPatch(typeof(Log), nameof(Log.Warning), typeof(string))]
    public static class Patch_SuppressFallbackWarning
    {
        public static bool Prefix(string text)
        {
            return text == null || !text.Contains("Could not find suitable tile for");
        }
    }

    // 2. Force BOUNTY: hostile + assault-lord spawn, and a "Bounty" quest name.
    [HarmonyPatch(typeof(IncidentWorker_IsekaiHunt), nameof(IncidentWorker_IsekaiHunt.CreateHuntQuest))]
    public static class Patch_ForceBounty
    {
        public static void Prefix(ref bool isBounty)
        {
            isBounty = true;
        }
    }

    // 3. Guild board: every listing's kind label reads "Bounty".
    [HarmonyPatch(typeof(GuildQuestBoardEntry), "QuestKindLabel", MethodType.Getter)]
    public static class Patch_BoardKindLabel
    {
        public static void Postfix(ref string __result)
        {
            __result = "GuildFaction_QuestBoard_KindBounty".Translate();
        }
    }

    // 3b. Guild board: the one-line flavor text reads as a bounty too, so a row is consistent.
    [HarmonyPatch(typeof(GuildQuestBoardEntry), "ShortDescription", MethodType.Getter)]
    public static class Patch_BoardFlavor
    {
        public static void Postfix(GuildQuestBoardEntry __instance, ref string __result)
        {
            string target = __instance?.PawnKind?.LabelCap.ToString() ?? "creature";
            __result = "GuildFaction_QuestBoard_FlavorBounty".Translate(target);
        }
    }
}
