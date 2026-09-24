using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Tactics.Units;
using Killtime.Tactics.TurnSystem;
using Killtime.Tactics.DuoTech;
using Killtime.Core.Character;
using Killtime.Core.Combat;

namespace Killtime.Tactics.CombatUI
{
    /// <summary>
    /// Duo-Tech (Lucas + Mina) — registre des actions jumelées.
    ///
    /// RÈGLES VERROUILLÉES (brainstorm) :
    /// 1. Action UNIQUE : ne consomme l'action principale de personne
    ///    (pas de RegisterAttack, pas de AttacksThisTurn).
    /// 2. Déclaration à tout moment pendant le tour perso de Lucas OU Mina.
    /// 3. Paiement IMMÉDIAT des deux : initiateur paie 1 PA de départ + son coût
    ///    de rôle, partenaire paie son coût de rôle de suite — même s'il n'a pas
    ///    encore joué ce round. Pas assez de PA = refusé. Pas de dette.
    /// 4. Pas d'expiration : une fois payé, le tissage reste armé jusqu'à
    ///    exécution ou annulation (PA perdus dans les deux cas).
    /// 5. Dessin complet avant résolution : T1 Mina (vecteur gardé) -> onde en
    ///    loop à vitesse réelle -> T2 Lucas en anticipation. Raté = effet
    ///    dégradé, PA déjà payés perdus (pas de refund).
    /// 6. Limite : 1 duo par personnage par round, initiateur OU partenaire
    ///    (tampon LastDuoTechRound, remis à zéro à chaque nouveau combat).
    ///
    /// La géométrie pure vit dans DuoTechGeometry (testable sans Unity).
    /// Ce registre fait le lien avec les CharacterStats / l'arène / le menu.
    /// </summary>
    public enum DuoTechId
    {
        SillageIgne,
        LacetNytharite,
        FournaiseRetardement
    }

    [Serializable]
    public sealed class DuoTechDef
    {
        public DuoTechId Id;
        public string Name;
        public string Description;
        public int DeclareCost = 1;
        public int MinaCost;
        public int LucasCost;

        public int TotalCost => DeclareCost + MinaCost + LucasCost;
    }

    public static class DuoTechRegistry
    {
        // --- Coûts verrouillés (brainstorm §2) ---
        // Sillage 8 | Lacet 5 | Fournaise 7 (départ 1 inclus).
        public static DuoTechDef GetDef(DuoTechId id)
        {
            switch (id)
            {
                case DuoTechId.SillageIgne:
                    return new DuoTechDef
                    {
                        Id = id,
                        Name = "🔥 Sillage Igné",
                        Description = "Mina dash en ligne par 2-4 ennemis alignés, Lucas détonne le sillage. Friendly fire si T2 colle l'onde.",
                        MinaCost = 3,
                        LucasCost = 4
                    };
                case DuoTechId.LacetNytharite:
                    return new DuoTechDef
                    {
                        Id = id,
                        Name = "🪢 Lacet de Nytharite",
                        Description = "Mina trace un lasso autour d'une cible mobile, Lucas ferme. Immobilisation 1 tour, 0 dégât.",
                        MinaCost = 2,
                        LucasCost = 2
                    };
                case DuoTechId.FournaiseRetardement:
                    return new DuoTechDef
                    {
                        Id = id,
                        Name = "♨️ Fournaise à Retardement",
                        Description = "Triangle autour d'un ennemi isolé : implosion + Étourdi + -2 PA.",
                        MinaCost = 3,
                        LucasCost = 3
                    };
                default:
                    return null;
            }
        }

        // =====================================================================
        // IDENTITÉ (complémentarité Lucas / Mina exigée)
        // =====================================================================

        public static bool IsLucasMember(CharacterStats s)
        {
            if (s == null) return false;
            if (s.Sheet != null) return LucasCharacter.IsLucas(s.Sheet);
            return !string.IsNullOrEmpty(s.Name)
                && s.Name.IndexOf("Lucas", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool IsMinaMember(CharacterStats s)
        {
            if (s == null) return false;
            if (s.Sheet != null) return MinaCharacter.IsMina(s.Sheet);
            return !string.IsNullOrEmpty(s.Name)
                && s.Name.IndexOf("Mina", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool IsDuoMember(CharacterStats s) => IsLucasMember(s) || IsMinaMember(s);

        /// <summary>
        /// Les deux moitiés doivent être complémentaires : un Lucas + une Mina.
        /// </summary>
        public static bool AreComplementary(CharacterStats a, CharacterStats b)
        {
            if (a == null || b == null || ReferenceEquals(a, b)) return false;
            return (IsLucasMember(a) && IsMinaMember(b))
                || (IsMinaMember(a) && IsLucasMember(b));
        }

        public static int RoleCost(DuoTechDef def, CharacterStats s)
        {
            if (def == null || s == null) return int.MaxValue;
            if (IsLucasMember(s)) return def.LucasCost;
            if (IsMinaMember(s)) return def.MinaCost;
            return int.MaxValue;
        }

        // =====================================================================
        // DÉCLARATION + PAIEMENT IMMÉDIAT (atomique, sans dette)
        // =====================================================================

        public static bool CanDeclare(CharacterStats initiator, CharacterStats partner, DuoTechDef def, int currentRound, out string why)
        {
            why = string.Empty;
            if (def == null) { why = "Duo inconnu."; return false; }
            if (initiator == null || partner == null) { why = "Binôme incomplet."; return false; }
            if (!AreComplementary(initiator, partner)) { why = "Duo-Tech = 1 Lucas + 1 Mina."; return false; }
            if (!initiator.IsAlive) { why = $"{initiator.Name} est hors de combat."; return false; }
            if (!partner.IsAlive) { why = $"{partner.Name} est hors de combat."; return false; }

            // Limite : 1 duo par personnage par round (initiateur OU partenaire).
            // currentRound <= 0 = hors combat (exploration) : pas de limite.
            if (currentRound > 0)
            {
                if (initiator.LastDuoTechRound >= currentRound)
                {
                    why = $"{initiator.Name} a déjà tissé ce round (1 duo max).";
                    return false;
                }
                if (partner.LastDuoTechRound >= currentRound)
                {
                    why = $"{partner.Name} a déjà tissé ce round (1 duo max).";
                    return false;
                }
            }

            int initNeed = def.DeclareCost + RoleCost(def, initiator);
            int partNeed = RoleCost(def, partner);
            if (initiator.CurrentActionPoints < initNeed)
            {
                why = $"{initiator.Name} : {initiator.CurrentActionPoints} PA < {initNeed} requis.";
                return false;
            }
            if (partner.CurrentActionPoints < partNeed)
            {
                why = $"{partner.Name} : {partner.CurrentActionPoints} PA < {partNeed} requis (paiement immédiat).";
                return false;
            }
            return true;
        }

        /// <summary>
        /// Débite initiateur (départ + rôle) puis partenaire (rôle). Échec du
        /// second = remboursement du premier. Succès = duo armé, sans expiration,
        /// et les DEUX participants sont tamponnés au round courant (1 duo max).
        /// Ne touche PAS aux actions principales (action unique).
        /// </summary>
        public static bool TryDeclarePayment(CharacterStats initiator, CharacterStats partner, DuoTechDef def, int currentRound)
        {
            if (!CanDeclare(initiator, partner, def, currentRound, out _)) return false;

            int initNeed = def.DeclareCost + RoleCost(def, initiator);
            int partNeed = RoleCost(def, partner);

            if (!initiator.ConsumeActionPoints(initNeed)) return false;
            if (!partner.ConsumeActionPoints(partNeed))
            {
                // Remboursement (Ralenti x2 déjà appliqué à la consommation :
                // on restaure au brut, plafond au max — pas de création de PA).
                initiator.CurrentActionPoints = Math.Min(
                    initiator.MaxActionPoints,
                    initiator.CurrentActionPoints + initNeed);
                return false;
            }

            if (currentRound > 0)
            {
                initiator.LastDuoTechRound = currentRound;
                partner.LastDuoTechRound = currentRound;
            }
            return true;
        }

        // =====================================================================
        // RÉSOLUTION (appelée par le WeaveController après T2 complet)
        // =====================================================================

        private static void DealBrut(CharacterStats target, int dmg, Action<string> log, string label)
        {
            if (target == null || !target.IsAlive) return;
            int remaining = target.CurrentHealth - dmg;
            if (remaining <= 0)
            {
                target.EvaluateFatalBlow(BodyPart.Torse, dmg);
                log?.Invoke($"{label} : {target.Name} prend {dmg} bruts (létal, {target.LastFatalBlowResolution}).");
            }
            else
            {
                target.CurrentHealth = remaining;
                log?.Invoke($"{label} : {target.Name} prend {dmg} bruts ({remaining} PV).");
            }
        }

        /// <summary>
        /// Sillage Igné : dash Mina (4 bruts à chaque cible de la ligne) puis feu
        /// Lucas selon synchro. Risky = friendly fire 3 bruts sur Mina.
        /// </summary>
        public static void ResolveSillage(
            CharacterStats mina, CharacterStats lucas,
            List<CharacterStats> targets, WeaveSyncGrade grade, Action<string> log)
        {
            const int dashDmg = 4;
            int fireDmg = grade switch
            {
                WeaveSyncGrade.Perfect => 3,
                WeaveSyncGrade.Risky => 2,
                WeaveSyncGrade.Late => 1,
                _ => 0
            };

            log?.Invoke($"🔥 <b>Sillage Igné</b> [{grade}] : dash {dashDmg} + feu {fireDmg}.");
            if (targets != null)
            {
                for (int i = 0; i < targets.Count; i++)
                {
                    var t = targets[i];
                    if (t == null || !t.IsAlive) continue;
                    DealBrut(t, dashDmg + fireDmg, log, "Sillage");
                    if (fireDmg > 0 && grade == WeaveSyncGrade.Perfect)
                    {
                        t.ApplyStatus(StatusEffect.EnFeu, 1);
                        log?.Invoke($"Sillage : {t.Name} est [En Feu] !");
                    }
                }
            }
            if (grade == WeaveSyncGrade.Risky && mina != null && mina.IsAlive)
            {
                DealBrut(mina, 3, log, "Sillage (friendly fire)");
                log?.Invoke("Sillage : le trait de Lucas a léché Mina — T2 trop collé à l'onde !");
            }
            if (grade == WeaveSyncGrade.Miss)
                log?.Invoke("Sillage : aucun croisement — le feu de Lucas passe à côté.");
        }

        /// <summary>
        /// Lacet : 0 dégât, Immobilisé 1 tour sur la cible si boucle fermée.
        /// </summary>
        public static void ResolveLacet(
            CharacterStats target, bool loopClosed, Action<string> log)
        {
            if (target == null || !target.IsAlive) return;
            if (!loopClosed)
            {
                log?.Invoke("🪢 <b>Lacet</b> : boucle non fermée — la cible glisse hors du lasso (PA perdus).");
                return;
            }
            target.ApplyStatus(StatusEffect.Immobilise, 1);
            log?.Invoke($"🪢 <b>Lacet</b> : {target.Name} est [Immobilisé] 1 tour !");
        }

        /// <summary>
        /// Fournaise : 5 bruts + Étourdi 1 tour + -2 PA sur l'isolé si triangle fermé.
        /// </summary>
        public static void ResolveFournaise(
            CharacterStats target, bool triangleClosed, Action<string> log)
        {
            if (target == null || !target.IsAlive) return;
            if (!triangleClosed)
            {
                log?.Invoke("♨️ <b>Fournaise</b> : triangle non fermé — implosion éventée (PA perdus).");
                return;
            }
            DealBrut(target, 5, log, "Fournaise");
            if (!target.IsAlive) return;
            target.ApplyStatus(StatusEffect.Etourdi, 1);
            target.CurrentActionPoints = Math.Max(0, target.CurrentActionPoints - 2);
            log?.Invoke($"Fournaise : {target.Name} est [Étourdi] 1 tour et perd 2 PA !");
        }

        // =====================================================================
        // MENU CONTEXTUEL (Unity) — déclaration + ouverture du tissage
        // =====================================================================

        private static List<TacticalUnit> GetAllUnits()
        {
            return new List<TacticalUnit>(
                UnityEngine.Object.FindObjectsByType<TacticalUnit>(FindObjectsInactive.Exclude));
        }

        private static TacticalUnit FindPartner(TacticalUnit actor)
        {
            if (actor?.Stats == null) return null;
            bool actorIsLucas = IsLucasMember(actor.Stats);
            bool actorIsMina = IsMinaMember(actor.Stats);
            if (!actorIsLucas && !actorIsMina) return null;

            var all = GetAllUnits();
            for (int i = 0; i < all.Count; i++)
            {
                var u = all[i];
                if (u == null || u == actor || u.Stats == null || !u.Stats.IsAlive) continue;
                if (actorIsLucas && IsMinaMember(u.Stats)) return u;
                if (actorIsMina && IsLucasMember(u.Stats)) return u;
            }
            return null;
        }

        private static bool IsActiveTurn(TacticalUnit actor)
        {
            var tm = UnityEngine.Object.FindAnyObjectByType<TurnManager>();
            if (tm == null) return true;
            if (tm.IsInExploration) return true;
            return tm.ActiveUnit == actor;
        }

        /// <summary>
        /// Round courant pour la limite "1 duo par personnage par round".
        /// 0 = hors combat (exploration) : pas de limite.
        /// Lu au moment du clic (pas à l'ouverture du menu) pour rester frais.
        /// </summary>
        private static int CurrentDuoRound()
        {
            var tm = UnityEngine.Object.FindAnyObjectByType<TurnManager>();
            if (tm == null || tm.IsInExploration) return 0;
            return tm.CurrentRound;
        }

        private static void BeginWeave(DuoTechId id, TacticalUnit initiator, CombatDevArena arena)
        {
            var partner = FindPartner(initiator);
            var def = GetDef(id);
            if (partner == null || def == null)
            {
                arena?.Log("Duo-Tech : partenaire introuvable (il faut Lucas + Mina en vie sur la carte).");
                return;
            }
            var weave = UnityEngine.Object.FindAnyObjectByType<DuoTechWeaveController>();
            if (weave != null && weave.IsWeaving)
            {
                arena?.Log("Duo-Tech : un tissage est déjà en cours — terminez-le ou annulez (clic droit). Aucun PA débité.");
                return;
            }
            if (!TryDeclarePayment(initiator.Stats, partner.Stats, def, CurrentDuoRound()))
            {
                CanDeclare(initiator.Stats, partner.Stats, def, CurrentDuoRound(), out string why);
                arena?.Log($"Duo-Tech refusé : {why}");
                var vis = initiator.GetComponent<TacticalUnitVisual>();
                vis?.SpawnFloatingText($"Duo refusé : {why}", Color.red);
                return;
            }

            if (weave == null)
            {
                var go = new GameObject("DuoTechWeave");
                weave = go.AddComponent<DuoTechWeaveController>();
            }
            arena?.Log($"{def.Name} armé ({def.TotalCost} PA payés, sans expiration) : tracez T1 (Mina), suivez l'onde, tracez T2 (Lucas) !");
            arena?.RecordChronoSnapshot($"{def.Name} armé : {initiator.Stats.Name} + {partner.Stats.Name}");
            weave.BeginSession(def, initiator, partner, arena);
        }

        /// <summary>
        /// Actions Duo-Tech pour le menu contextuel : visibles quand l'acteur est
        /// Lucas ou Mina, que c'est son tour, et que le partenaire est en vie.
        /// L'ActionPointCost affiché = 0 car le débit réel (départ + rôles, sur les
        /// DEUX fiches) est fait dans BeginWeave — CanExecute ne sait checker
        /// qu'une seule fiche, CanDeclare fait le vrai contrôle.
        /// </summary>
        public static List<CombatAction> GetDuoTechActions(
            TacticalUnit actor, TacticalUnit target, CombatDevArena arena)
        {
            var actions = new List<CombatAction>();
            if (actor?.Stats == null || target == null) return actions;
            if (!IsDuoMember(actor.Stats)) return actions;
            if (!IsActiveTurn(actor)) return actions;

            var partner = FindPartner(actor);
            if (partner == null) return actions;

            bool isEnemy = target.IsPlayerControlled != actor.IsPlayerControlled;

            var sillage = GetDef(DuoTechId.SillageIgne);
            if (isEnemy && target.Stats != null && target.Stats.IsAlive)
            {
                actions.Add(new CombatAction(
                    $"🔥 Sillage Igné (duo {sillage.TotalCost} PA)",
                    "Mina dash en ligne par les ennemis alignés, Lucas détonne le sillage. Paiement immédiat des deux, tissage souris (T1 + onde + T2). Action unique.",
                    ActionCategory.TechniquesDeSpecialisation,
                    0,
                    (act, tgt) => CanDeclare(act.Stats, FindPartner(act)?.Stats, sillage, CurrentDuoRound(), out _),
                    (act, tgt) => BeginWeave(DuoTechId.SillageIgne, act, arena)
                ));
            }

            var lacet = GetDef(DuoTechId.LacetNytharite);
            if (isEnemy && target.Stats != null && target.Stats.IsAlive)
            {
                actions.Add(new CombatAction(
                    $"🪢 Lacet de Nytharite (duo {lacet.TotalCost} PA)",
                    "Lasso de Mina autour d'une cible mobile, Lucas ferme : Immobilisé 1 tour. Tissage souris.",
                    ActionCategory.TechniquesDeSpecialisation,
                    0,
                    (act, tgt) => CanDeclare(act.Stats, FindPartner(act)?.Stats, lacet, CurrentDuoRound(), out _),
                    (act, tgt) => BeginWeave(DuoTechId.LacetNytharite, act, arena)
                ));
            }

            var fournaise = GetDef(DuoTechId.FournaiseRetardement);
            if (isEnemy && target.Stats != null && target.Stats.IsAlive)
            {
                actions.Add(new CombatAction(
                    $"♨️ Fournaise (duo {fournaise.TotalCost} PA)",
                    "Triangle autour d'un ennemi isolé : 5 bruts + Étourdi + -2 PA. Tissage souris.",
                    ActionCategory.TechniquesDeSpecialisation,
                    0,
                    (act, tgt) => CanDeclare(act.Stats, FindPartner(act)?.Stats, fournaise, CurrentDuoRound(), out _),
                    (act, tgt) => BeginWeave(DuoTechId.FournaiseRetardement, act, arena)
                ));
            }

            return actions;
        }
    }
}
