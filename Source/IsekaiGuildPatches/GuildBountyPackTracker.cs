using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using IsekaiLeveling.Quests;
using RimWorld;
using Verse;

namespace IsekaiGuildPatches
{
    // Remembers each bounty quest's rolled pack size (questId -> size). The size is decided ONCE, at
    // quest creation, so the title/description can reflect it; the spawner then reuses the stored size
    // instead of re-rolling, and it survives a save/load while the offer waits in the Available list.
    // RimWorld auto-instantiates every GameComponent (with a Game ctor) for each game.
    public class GuildBountyPackTracker : GameComponent
    {
        private Dictionary<int, int> packSizeByQuest = new Dictionary<int, int>();

        public GuildBountyPackTracker(Game game)
        {
        }

        public static GuildBountyPackTracker Get()
        {
            return Current.Game?.GetComponent<GuildBountyPackTracker>();
        }

        public void Set(int questId, int size) => packSizeByQuest[questId] = size;
        public bool TryGet(int questId, out int size) => packSizeByQuest.TryGetValue(questId, out size);
        public void Clear(int questId) => packSizeByQuest.Remove(questId);

        public override void ExposeData()
        {
            base.ExposeData();
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                PruneStale();
            }
            Scribe_Collections.Look(ref packSizeByQuest, "guildBountyPackSizes", LookMode.Value, LookMode.Value);
            if (packSizeByQuest == null)
            {
                packSizeByQuest = new Dictionary<int, int>();
            }
        }

        // Drop entries whose quest is no longer a live offer, so the table can't grow without bound over
        // a long game (accepted quests clear themselves at spawn; declined/expired ones are pruned here).
        private void PruneStale()
        {
            QuestManager qm = Find.QuestManager;
            if (qm == null)
            {
                return;
            }
            HashSet<int> live = new HashSet<int>(qm.QuestsListForReading
                .Where(q => q.State == QuestState.NotYetAccepted)
                .Select(q => q.id));
            foreach (int key in packSizeByQuest.Keys.Where(k => !live.Contains(k)).ToList())
            {
                packSizeByQuest.Remove(key);
            }
        }
    }

    // Shared pack-size roll: ~35% lone at any rank, else a rank-scaled pack.
    internal static class PackRules
    {
        public static int Roll(QuestRank rank)
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
            return Math.Max(2, Rand.RangeInclusive(b - 1, b + 1));
        }
    }

    // At creation, decide the pack size once, record it by quest id, and tag a pack quest's title +
    // description so it reads differently from a lone mark. CreateHuntQuest has just done
    // Find.QuestManager.Add, so the new quest is the last one in the manager.
    [HarmonyPatch(typeof(IncidentWorker_IsekaiHunt), nameof(IncidentWorker_IsekaiHunt.CreateHuntQuest))]
    public static class Patch_TagPackQuest
    {
        public static void Postfix(QuestRank rank)
        {
            Quest quest = Find.QuestManager?.QuestsListForReading?.LastOrDefault();
            if (quest == null || !quest.PartsListForReading.Any(p => p is QuestPart_IsekaiLocalHunt))
            {
                return;
            }

            int size = PackRules.Roll(rank);
            GuildBountyPackTracker.Get()?.Set(quest.id, size);
            if (size <= 1)
            {
                return; // lone target: leave the title/description as-is
            }

            quest.name = quest.name + " " + "IsekaiGuildPatches_PackTag".Translate().ToString();
            quest.description = quest.description + "\n\n" + "IsekaiGuildPatches_PackNote".Translate().ToString();
        }
    }
}
