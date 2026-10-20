using System.Collections.Generic;
using UnityEngine;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Units;
using Killtime.Tactics.Arena;
using Killtime.Core.Character;
using Killtime.Core.Combat;

namespace Killtime.Tactics
{
    // RD-196 : Titans multi-cases + boss 1-clic (Livres V §22.3, XI §45-46).
    // Spawn générique Rosette7/Colossus19 + 3 boss du catalogue, 3 coups
    // Titanesques 1-clic (saisie 2 PA, piétinement 3 PA, souffle 3 PA),
    // vérification empreinte/périmètre et rechargement bossAI JSON.
    // Les dégâts reprennent le registre joueur (CombatActionRegistry.Attack)
    // + crochet réactions boss RD-087 (TitanBossMechanics, tracé log).
    public partial class CombatDevArena : MonoBehaviour, IArenaHost
    {
        public const int TitanStompAPCost = 3;
        public const int TitanBreathAPCost = 3;
        public const int TitanGrabAPCost = 2;

        /// <summary>Construit une fiche Titan générique de test (0 règle modifiée).</summary>
        public static CharacterSheet BuildTitanTestSheet(TitanFootprintType footprint)
        {
            bool colossus = footprint == TitanFootprintType.Colossus19;
            var sheet = new CharacterSheet
            {
                Name = colossus ? "Titan Test (Colossus19)" : "Titan Test (Rosette7)",
                BaseAttributes = new Attributes(7, 3, 8, 3, 2, 1, 1, 4, 2),
                BaseArmor = 2,
                Profile = CharacterProfileType.PnjBoss,
                Footprint = footprint,
                ModelPrefabName = colossus ? "TitanColossus" : "TitanRosette",
                Species = SpeciesType.Humain,
                Gender = "Colosse",
                Age = 500,
                LoreNotes = "[RD-196] Gabarit de test arène (pas de lore)."
            };
            sheet.GetSkill(SkillType.MainsNues).TrainingLevel = 2;
            sheet.GetSkill(SkillType.Athletisme).TrainingLevel = 1;
            sheet.UnlockedSpecializations.Add("Poigne Titanesque (test)");
            return sheet;
        }

        /// <summary>
        /// Spawne un Titan générique sur la plus proche ancre à empreinte libre.
        /// Log la vérification empreinte (cases occupées) + périmètre.
        /// </summary>
        public TacticalUnit SpawnTitanDummy(TitanFootprintType footprint, HexCoordinates at, bool isPlayer = false)
        {
            if (footprint != TitanFootprintType.Rosette7 && footprint != TitanFootprintType.Colossus19)
                footprint = TitanFootprintType.Rosette7;
            var sheet = BuildTitanTestSheet(footprint);
            return SpawnTitanUnit(sheet, footprint, at, isPlayer);
        }

        /// <summary>Spawne un boss du Livre XI : 0 Bête, 1 Minulican, 2 Disciple.</summary>
        public TacticalUnit SpawnTitanBoss(int bossIndex, HexCoordinates at, bool isPlayer = false)
        {
            CharacterSheet sheet = bossIndex switch
            {
                1 => TitanBossCatalog.BuildMinulicanSheet(),
                2 => TitanBossCatalog.BuildDiscipleDeTerreSheet(),
                _ => TitanBossCatalog.BuildTheBeastSheet(),
            };
            var unit = SpawnTitanUnit(sheet, sheet.Footprint, at, isPlayer);
            if (unit == null) return null;
            var p = TitanBossAIConfig.GetParams(unit.Stats);
            Log($"👑 bossAI : balayage {p.sweepDamage} dégâts / charge {p.chargeCost} PA / poing {p.fistDamage}.");
            return unit;
        }

        /// <summary>
        /// Socle spawn Titan : ancre résolue à empreinte libre (jamais de fantôme
        /// sans position), refus loggé si l'empreinte ne tient nulle part à portée.
        /// </summary>
        private TacticalUnit SpawnTitanUnit(CharacterSheet sheet, TitanFootprintType footprint, HexCoordinates at, bool isPlayer)
        {
            if (_grid == null) EnsureDependencies();
            HexCoordinates anchor = at;
            if (_grid != null && !_grid.IsFootprintFree(at, footprint))
            {
                if (!_grid.TryFindNearestFreeFootprint(at, footprint, out var free, 12))
                { Log($"⛔ Spawn {sheet.Name} impossible : aucune ancre à empreinte {footprint} libre autour de ({at.Q},{at.R})."); return null; }
                anchor = free;
            }
            var unit = _unitStore != null ? _unitStore.SpawnCustomCharacter(sheet, anchor, isPlayer) : null;
            if (unit == null) { Log($"⛔ Spawn {sheet.Name} impossible en ({anchor.Q},{anchor.R})."); return null; }
            Log($"🦣 <b>{sheet.Name}</b> déployé en ({unit.CurrentCoords.Q},{unit.CurrentCoords.R}) — {VerifyTitanFootprint(unit)}.");
            RecordChronoSnapshot($"Spawn Titan {sheet.Name}");
            return unit;
        }

        /// <summary>Vérification pure (sans log) : cases occupées + périmètre.</summary>
        public static string DescribeTitanFootprint(HexCoordinates anchor, TitanFootprintType footprint)
        {
            int cells = TitanFootprint.GetHexCount(footprint);
            int perimeter = TitanFootprint.GetPerimeterCoordinates(anchor, footprint).Count;
            return $"empreinte {footprint} : {cells} cases + {perimeter} périmètre";
        }

        public string VerifyTitanFootprint(TacticalUnit unit)
        {
            if (unit == null || unit.Stats == null) return "unité invalide";
            int occupied = unit.OccupiedCoords != null ? unit.OccupiedCoords.Count : 0;
            int expected = TitanFootprint.GetHexCount(unit.FootprintType);
            int perimeter = TitanFootprint.GetPerimeterCoordinates(unit.CurrentCoords, unit.FootprintType).Count;
            string tag = occupied == expected ? "✓" : "⚠️";
            return $"{tag} {unit.FootprintType} : {occupied}/{expected} occupées + {perimeter} périmètre";
        }

        /// <summary>Recharge le bestiaire + bossAI JSON (touche B / F6).</summary>
        public void ReloadTitanBossAI()
        {
            BestiaryCatalog.InvalidateCache();
            var all = BestiaryCatalog.GetAll();
            Log($"📖 <b>Bestiaire rechargé</b> : {all.Count} entrées ({BestiaryCatalog.LoadedFrom}) — bossAI JSON repris en compte.");
            RecordChronoSnapshot("Rechargement bestiaire/bossAI");
        }

        // --- Coups Titanesques 1-clic (mêmes coûts/effets que le registre) ---

        public bool ExecuteTitanGrab1Click(TacticalUnit actor, TacticalUnit target)
        {
            if (actor == null || actor.Stats == null || target == null || target.Stats == null) return false;
            if (!actor.Stats.IsAlive || !target.Stats.IsAlive) return false;
            int dist = TitanFootprint.MinDistanceBetweenUnits(actor.CurrentCoords, actor.FootprintType, target.CurrentCoords, target.FootprintType);
            if (!TitanFootprint.CanTitanGrab(actor.FootprintType, target.FootprintType, dist))
            { Log($"⛔ Saisie impossible ({actor.Stats.Name} -> {target.Stats.Name}) : contact périmètre requis, gabarit inférieur uniquement."); return false; }
            if (actor.Stats.CurrentActionPoints < TitanGrabAPCost)
            { Log($"⛔ Saisie impossible ({actor.Stats.Name} : {actor.Stats.CurrentActionPoints} PA, coût {TitanGrabAPCost})."); return false; }
            if (!actor.Stats.ConsumeActionPoints(TitanGrabAPCost)) return false;

            int grabDmg = Mathf.Max(3, actor.Stats.Attributes.Force);
            ApplyTitanBrutalDamage(target, grabDmg);
            if (target.Stats.IsAlive)
            {
                GrappleState.SetGrapple(actor.Stats, target.Stats);
                target.Stats.ApplyStatus(StatusEffect.Destabilise, 1);
                target.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText($"-{grabDmg} SAISIE [IMMOBILISÉ]", Color.yellow);
            }
            else OnTitanVictimDown(target);
            actor.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText("✊ SAISIE TITANESQUE", Color.cyan);
            Log($"✊ <b>{actor.Stats.Name}</b> saisit <b>{target.Stats.Name}</b> (-{TitanGrabAPCost} PA) : {grabDmg} broyage + Immobilisé en lutte !");
            RecordChronoSnapshot($"Saisie Titanesque : {actor.Stats.Name} -> {target.Stats.Name}");
            try { _turnManager?.CheckCombatOver(); } catch { /* jamais bloquant */ }
            return true;
        }

        public bool ExecuteTitanStomp1Click(TacticalUnit actor, HexCoordinates impact)
        {
            if (actor == null || actor.Stats == null || !actor.Stats.IsAlive) return false;
            if (!TitanFootprint.IsTitan(actor.FootprintType)) { Log("⛔ Piétinement réservé aux Titans (Rosette7/Colossus19)."); return false; }
            if (TitanFootprint.MinDistance(impact, actor.CurrentCoords, actor.FootprintType) > 2)
            { Log($"⛔ Piétinement impossible : impact {impact} hors d'allonge (portée 2 depuis le corps colosse)."); return false; }
            if (actor.Stats.CurrentActionPoints < TitanStompAPCost)
            { Log($"⛔ Piétinement impossible ({actor.Stats.Name} : {actor.Stats.CurrentActionPoints} PA, coût {TitanStompAPCost})."); return false; }
            if (!actor.Stats.ConsumeActionPoints(TitanStompAPCost)) return false;

            var zone = TitanFootprint.GetStompZone(impact, actor.FootprintType);
            var zoneSet = new HashSet<HexCoordinates>(zone);
            int stompDmg = Mathf.Max(4, actor.Stats.Attributes.Force + 2);
            int hitCount = 0;
            var reacts = new List<string>();
            var all = TacticalUnitRegistry.AllUnits;
            for (int i = 0; i < all.Count; i++)
            {
                var u = all[i];
                if (u == null || u.Stats == null || !u.Stats.IsAlive || u == actor) continue;
                if (!TitanZoneHits(u, zoneSet)) continue;
                hitCount++;
                int dmg = stompDmg;
                var react = TitanBossMechanics.ResolveIncomingElementalDamage(u.Stats, TitanBossElement.Other, dmg, TitanBossState.CurrentRound);
                if (react.HasReaction)
                {
                    dmg = Mathf.Max(0, react.AdjustedDamage);
                    if (!string.IsNullOrEmpty(react.LogFragment)) reacts.Add(react.LogFragment);
                }
                ApplyTitanBrutalDamage(u, dmg);
                u.Stats.ApplyStatus(StatusEffect.ATerre, 1);
                u.Stats.ApplyStatus(StatusEffect.Destabilise, 1);
                u.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText($"-{dmg} PIÉTINÉ [À TERRE]", Color.red);
                if (!u.Stats.IsAlive) OnTitanVictimDown(u);
            }
            actor.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText($"💥 PIÉTINEMENT ({hitCount} touché(s))", new Color(1f, 0.4f, 0.1f));
            Log($"💥 <b>{actor.Stats.Name}</b> piétine {impact} (-{TitanStompAPCost} PA, {zone.Count} hex) : {hitCount} écrasé(s) ({stompDmg} choc, À Terre + Déstabilisé) !");
            for (int i = 0; i < reacts.Count; i++) Log($"   • {reacts[i]}");
            RecordChronoSnapshot($"Piétinement 1-clic : {actor.Stats.Name} ({zone.Count} hex)");
            try { _turnManager?.CheckCombatOver(); } catch { /* jamais bloquant */ }
            return true;
        }

        public bool ExecuteTitanBreath1Click(TacticalUnit actor, HexCoordinates impact)
        {
            if (actor == null || actor.Stats == null || !actor.Stats.IsAlive) return false;
            if (!TitanFootprint.IsTitan(actor.FootprintType)) { Log("⛔ Souffle réservé aux Titans (Rosette7/Colossus19)."); return false; }
            if (TitanFootprint.MinDistance(impact, actor.CurrentCoords, actor.FootprintType) > 8)
            { Log($"⛔ Souffle impossible : impact {impact} hors de portée (8 depuis le corps colosse)."); return false; }
            if (actor.Stats.CurrentActionPoints < TitanBreathAPCost)
            { Log($"⛔ Souffle impossible ({actor.Stats.Name} : {actor.Stats.CurrentActionPoints} PA, coût {TitanBreathAPCost})."); return false; }
            if (!actor.Stats.ConsumeActionPoints(TitanBreathAPCost)) return false;

            var zone = TitanFootprint.GetBreathZone(impact, actor.FootprintType);
            var zoneSet = new HashSet<HexCoordinates>(zone);
            int mag = actor.Stats.Attributes.Magie > 0 ? actor.Stats.Attributes.Magie : actor.Stats.Attributes.Constitution;
            int breathDmg = Mathf.Max(5, mag + 2);
            int hitCount = 0;
            var reacts = new List<string>();
            var all = TacticalUnitRegistry.AllUnits;
            for (int i = 0; i < all.Count; i++)
            {
                var u = all[i];
                if (u == null || u.Stats == null || !u.Stats.IsAlive || u == actor) continue;
                if (!TitanZoneHits(u, zoneSet)) continue;
                hitCount++;
                int dmg = breathDmg;
                var react = TitanBossMechanics.ResolveIncomingElementalDamage(u.Stats, TitanBossElement.Fire, dmg, TitanBossState.CurrentRound);
                if (react.HasReaction)
                {
                    dmg = Mathf.Max(0, react.AdjustedDamage);
                    if (!string.IsNullOrEmpty(react.LogFragment)) reacts.Add(react.LogFragment);
                }
                ApplyTitanBrutalDamage(u, dmg);
                u.Stats.ApplyStatus(StatusEffect.EnFeu, 2);
                u.Stats.ApplyStatus(StatusEffect.Destabilise, 1);
                u.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText($"-{dmg} SOUFFLE [EN FEU]", new Color(1f, 0.45f, 0.1f));
                if (!u.Stats.IsAlive) OnTitanVictimDown(u);
            }
            actor.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText($"🔥 SOUFFLE ({hitCount} touché(s))", new Color(1f, 0.5f, 0.1f));
            Log($"🔥 <b>{actor.Stats.Name}</b> souffle sur {impact} (-{TitanBreathAPCost} PA, {zone.Count} hex) : {hitCount} calciné(s) ({breathDmg} feu, En Feu + Déstabilisé) !");
            for (int i = 0; i < reacts.Count; i++) Log($"   • {reacts[i]}");
            RecordChronoSnapshot($"Souffle 1-clic : {actor.Stats.Name} ({zone.Count} hex)");
            try { _turnManager?.CheckCombatOver(); } catch { /* jamais bloquant */ }
            return true;
        }

        private static bool TitanZoneHits(TacticalUnit u, HashSet<HexCoordinates> zoneSet)
        {
            var coords = u.OccupiedCoords;
            if (coords == null) return zoneSet.Contains(u.CurrentCoords);
            for (int c = 0; c < coords.Count; c++)
                if (zoneSet.Contains(coords[c])) return true;
            return false;
        }

        private void ApplyTitanBrutalDamage(TacticalUnit target, int dmg)
        {
            int rem = target.Stats.CurrentHealth - dmg;
            if (rem <= 0) target.Stats.EvaluateFatalBlow(BodyPart.Torse, dmg);
            else target.Stats.CurrentHealth = rem;
            target.GetComponent<TacticalUnitVisual>()?.TriggerHitFlash();
        }

        private void OnTitanVictimDown(TacticalUnit victim)
        {
            victim.GetComponent<TacticalUnitVisual>()?.TriggerFallingBackDeath();
            AutoTargetNextAlive();
        }
    }
}
