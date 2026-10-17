using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Core.Character;
using Killtime.Core.Inventory;
using Killtime.Story.Data;

namespace Killtime.Story.Scenes
{
    /// <summary>
    /// Service pur du moteur de dialogue (sans état Unity).
    /// Logique extraite de JsonStorySceneController.Dialogue (move-only) :
    /// calcul d'entrée de nœud + octroi d'objet de récompense.
    /// </summary>
    public static class SceneDialogueService
    {
        // Entrée du nœud = première carte dialogue sans lien entrant (routage explicite).
        // Repli sur 0 si aucune (cycle) ou liste vide.
        public static int FindEntryDialogueIndex(SceneNodeData dataNode)
        {
            if (dataNode == null || dataNode.Dialogues == null || dataNode.Dialogues.Count == 0) return 0;
            if (dataNode.Dialogues.Count == 1) return 0;
            var incoming = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < dataNode.Dialogues.Count; i++)
            {
                var l = dataNode.Dialogues[i];
                if (l == null) continue;
                if (!string.IsNullOrEmpty(l.NextLineId)) incoming.Add(l.NextLineId);
                if (l.Choices != null)
                {
                    for (int c = 0; c < l.Choices.Count; c++)
                    {
                        var ch = l.Choices[c];
                        if (ch == null) continue;
                        if (!string.IsNullOrEmpty(ch.NextLineId)) incoming.Add(ch.NextLineId);
                        var chal = ch.Challenge;
                        if (chal != null && chal.HasChallenge)
                        {
                            if (!string.IsNullOrEmpty(chal.SuccessNextLineId)) incoming.Add(chal.SuccessNextLineId);
                            if (!string.IsNullOrEmpty(chal.FailureNextLineId)) incoming.Add(chal.FailureNextLineId);
                        }
                    }
                }
                if (l.AutoSkillCheck != null && l.AutoSkillCheck.HasAutoCheck)
                {
                    if (!string.IsNullOrEmpty(l.AutoSkillCheck.SuccessNextLineId)) incoming.Add(l.AutoSkillCheck.SuccessNextLineId);
                    if (!string.IsNullOrEmpty(l.AutoSkillCheck.FailureNextLineId)) incoming.Add(l.AutoSkillCheck.FailureNextLineId);
                }
            }
            if (dataNode.Consequences != null)
            {
                for (int k = 0; k < dataNode.Consequences.Count; k++)
                {
                    var cons = dataNode.Consequences[k];
                    if (cons == null) continue;
                    if (!string.IsNullOrEmpty(cons.NextLineId)) incoming.Add(cons.NextLineId);
                }
            }
            for (int i = 0; i < dataNode.Dialogues.Count; i++)
            {
                var l = dataNode.Dialogues[i];
                if (l == null || string.IsNullOrEmpty(l.LineId)) continue;
                if (!incoming.Contains(l.LineId)) return i;
            }
            return 0;
        }

        public static void GrantRewardItem(CharacterSheet sheet, string itemName, ItemType itemType, int quantity, ref string rewardsSummary)
        {
            if (sheet == null || string.IsNullOrEmpty(itemName)) return;
            int qty = Mathf.Max(1, quantity);
            var catalogDef = ArmoryCatalog.GetByName(itemName);
            InventoryItem rewardItem;
            if (catalogDef != null)
            {
                rewardItem = catalogDef.Clone();
                rewardItem.Quantity = qty;
                rewardItem.IsEquipped = false;
            }
            else
            {
                rewardItem = new InventoryItem
                {
                    Name = itemName,
                    Type = itemType,
                    PriceCE = 100,
                    Quantity = qty
                };
            }
            sheet.AddItem(rewardItem);
            rewardsSummary += qty > 1 ? $"[{itemName} x{qty}]  " : $"[{itemName}]  ";
        }
    }
}
