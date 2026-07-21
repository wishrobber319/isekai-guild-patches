using System.Collections.Generic;
using HarmonyLib;
using IsekaiLeveling.Quests;
using RimWorld;
using Verse;

namespace IsekaiGuildLootTweaks
{
    // Postfix IncidentWorker_IsekaiHunt.GenerateLootRewards (base Isekai mod) to: strip the generic
    // weapon/armour/component/drug loot, and add more Isekai-flavoured rewards (mana cores, forge
    // materials, rebirth orbs, star fragments). We let the original build its list, then edit it -
    // this leaves food/medicine/materials/valuables/runes/artifacts untouched. The silver reward is
    // inserted by the caller AFTER this returns, so it is never at risk here.
    [HarmonyPatch(typeof(IncidentWorker_IsekaiHunt), "GenerateLootRewards")]
    public static class Patch_GenerateLootRewards
    {
        public static void Postfix(List<Thing> __result, QuestRank rank)
        {
            if (__result == null)
            {
                return;
            }

            // --- Remove generic weapons / armour / components / drugs ---
            // Never strip custom Isekai items (runes, cores, masks, scrolls, orbs all start "Isekai_").
            __result.RemoveAll(t =>
            {
                ThingDef d = t.def;
                if (d.defName.StartsWith("Isekai_"))
                {
                    return false;
                }
                return d.IsWeapon
                    || d.IsApparel
                    || d == ThingDefOf.ComponentIndustrial
                    || d == ThingDefOf.ComponentSpacer
                    || d.IsDrug;
            });

            int r = (int)rank;

            // --- Mana essence (C+) ---
            // We no longer hand out mana cores; instead the essence payout already INCLUDES the essence
            // those cores would have distilled into at the forge (3 Small->1, 1 Mana->2, 1 Big->5,
            // 1 Huge->12). So each rank's amount = base essence + distill-equivalent of the old cores:
            //   C: 2 + (2 Small=0.67)  -> 3      S:  8 + (3 Mana=6)  -> 14
            //   B: 3 + (3 Small=1)     -> 4      SS: 12 + (2 Big=10) -> 22
            //   A: 5 + (2 Mana=4)      -> 9      SSS:16 + (3 Huge=36)-> 52
            if (r >= (int)QuestRank.C)
            {
                int essence;
                switch (rank)
                {
                    case QuestRank.C:   essence = 3; break;
                    case QuestRank.B:   essence = 4; break;
                    case QuestRank.A:   essence = 9; break;
                    case QuestRank.S:   essence = 14; break;
                    case QuestRank.SS:  essence = 22; break;
                    case QuestRank.SSS: essence = 52; break;
                    default:            essence = 0; break;
                }
                AddThing(__result, "Isekai_ManaEssence", essence);
            }
            if (r >= (int)QuestRank.A)
            {
                int reinforcement;
                switch (rank)
                {
                    case QuestRank.A:   reinforcement = 1; break;
                    case QuestRank.S:   reinforcement = 2; break;
                    case QuestRank.SS:  reinforcement = 3; break;
                    case QuestRank.SSS: reinforcement = 4; break;
                    default:            reinforcement = 0; break;
                }
                AddThing(__result, "Isekai_ReinforcementCore", reinforcement);
            }

            // --- Rebirth orb OR star fragment: flat 25% on A+ quests, one or the other (50/50) ---
            if (r >= (int)QuestRank.A && Rand.Chance(0.25f))
            {
                AddThing(__result, Rand.Bool ? "Isekai_RespecOrb" : "Isekai_StarFragment", 1);
            }

            // --- Healing potion (vanilla MechSerumHealer; "Healing potion" if the Medieval Fantasy
            //     Themed Quest Rewards reskin is active), S+ only ---
            if (r >= (int)QuestRank.S)
            {
                float healChance;
                switch (rank)
                {
                    case QuestRank.S:   healChance = 0.10f; break;
                    case QuestRank.SS:  healChance = 0.20f; break;
                    case QuestRank.SSS: healChance = 0.30f; break;
                    default:            healChance = 0f; break;
                }
                if (Rand.Chance(healChance))
                {
                    AddThing(__result, "MechSerumHealer", 1);
                }
            }
        }

        private static void AddThing(List<Thing> rewards, string defName, int count)
        {
            if (string.IsNullOrEmpty(defName) || count <= 0)
            {
                return;
            }
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (def == null)
            {
                return;
            }
            Thing thing = ThingMaker.MakeThing(def);
            thing.stackCount = count;
            rewards.Add(thing);
        }
    }
}
