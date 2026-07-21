using HarmonyLib;
using Verse;

namespace IsekaiGuildPatches
{
    // Single bootstrap for the merged mod. One Harmony.PatchAll() covers every [HarmonyPatch] in this
    // assembly, across all three namespaces (GuildQuestRankRange, GuildQuestsInVanillaTab,
    // IsekaiGuildLootTweaks). Each of the three source mods carried its own StaticConstructorOnStartup
    // + PatchAll; those were removed on merge, since calling PatchAll more than once would apply every
    // patch two or three times.
    [StaticConstructorOnStartup]
    public static class IsekaiGuildPatchesMod
    {
        static IsekaiGuildPatchesMod()
        {
            new Harmony("wishRobber.isekaiguildpatches").PatchAll();
        }
    }
}
