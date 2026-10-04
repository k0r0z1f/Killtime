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
        FournaiseRetardement,
        TrempeInversee,
        RailgunArtisanale,
        CisailleEntropique,
        Parallaxe,
        MirageDeuxTemps,
        UppercutThermobarique
    }

    /// <summary>
    /// Issue table (Livre VI §25.5.2) : double épreuve de synchro.
    /// Les deux réussissent = Parfaite ; un seul = Partiel (Semi) ; aucun = Ratée.
    /// Le tissage souris (WeaveController) ne produit que Parfaite/Ratée
    /// (binaire, comme Lacet/Fournaise) : le Partiel n'existe qu'à table (dés).
    /// </summary>
    public enum DuoTechOutcome
    {
        Perfect,
        Partial,
        Miss
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

        // Table Livre VI §25.5 : ND de synchro + binôme de caracs testées
        // (1er = rôle cinétique/physique Mina, 2nd = rôle calcul Lucas).
        public int SyncND = 5;
        public string SyncAttrs = "AGI+INT";
        public string Condition = "";
    }

    public static class DuoTechRegistry
    {
        // --- Coûts verrouillés (Livre VI §25.5, Cinétique + Calcul + départ 1) ---
        // Sillage 8 | Lacet 5 | Fournaise 7 | Trempe 9 | Railgun 11 |
        // Cisaille 9 | Parallaxe 6 | Mirage 4 | Uppercut 9.
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
                        LucasCost = 4,
                        SyncND = 5,
                        SyncAttrs = "AGI+INT",
                        Condition = "2 à 4 ennemis alignés"
                    };
                case DuoTechId.LacetNytharite:
                    return new DuoTechDef
                    {
                        Id = id,
                        Name = "🪢 Lacet de Nytharite",
                        Description = "Mina trace un lasso autour d'une cible mobile, Lucas ferme. Immobilisation 1 tour, 0 dégât.",
                        MinaCost = 2,
                        LucasCost = 2,
                        SyncND = 5,
                        SyncAttrs = "AGI+INT",
                        Condition = "Cible ayant bougé ce round"
                    };
                case DuoTechId.FournaiseRetardement:
                    return new DuoTechDef
                    {
                        Id = id,
                        Name = "♨️ Fournaise à Retardement",
                        Description = "Triangle autour d'un ennemi isolé : implosion + Étourdi + -2 PA.",
                        MinaCost = 3,
                        LucasCost = 3,
                        SyncND = 5,
                        SyncAttrs = "AGI+INT",
                        Condition = "Ennemi isolé (rien à ≤ 2 cases)"
                    };
                case DuoTechId.TrempeInversee:
                    return new DuoTechDef
                    {
                        Id = id,
                        Name = "❄️ Trempe Inversée",
                        Description = "Gel croisé sur ennemis groupés : Ralenti 1t + armures fissurées (-2 absorption jusqu'à la fin du round suivant). Semi = Ralenti seul.",
                        MinaCost = 4,
                        LucasCost = 4,
                        SyncND = 6,
                        SyncAttrs = "AGI+INT",
                        Condition = "Ennemis groupés (≤ 2 cases d'un point)"
                    };
                case DuoTechId.RailgunArtisanale:
                    return new DuoTechDef
                    {
                        Id = id,
                        Name = "🛤️ Railgun Artisanale",
                        Description = "Couloir vide 4+ cases : 8 perforants (ignore la moitié de l'armure) sur la cible dure. Semi = 5 bruts.",
                        MinaCost = 5,
                        LucasCost = 5,
                        SyncND = 7,
                        SyncAttrs = "FOR+INT",
                        Condition = "Couloir vide de 4+ cases, 1 cible dure au bout"
                    };
                case DuoTechId.CisailleEntropique:
                    return new DuoTechDef
                    {
                        Id = id,
                        Name = "✖️ Cisaille Entropique",
                        Description = "Axes croisés en X : 5 bruts + Saignement à l'intersection. Semi = 3 bruts.",
                        MinaCost = 4,
                        LucasCost = 4,
                        SyncND = 6,
                        SyncAttrs = "AGI+INT",
                        Condition = "Deux axes ennemis qui se croisent (X)"
                    };
                case DuoTechId.Parallaxe:
                    return new DuoTechDef
                    {
                        Id = id,
                        Name = "⧉ Parallaxe",
                        Description = "Opposition 180° : la cible ne peut pas se défendre (score 0) face à 4 bruts. Semi = défense à -2.",
                        MinaCost = 3,
                        LucasCost = 2,
                        SyncND = 5,
                        SyncAttrs = "AGI+INT",
                        Condition = "Partenaires opposés à 180° autour de la cible"
                    };
                case DuoTechId.MirageDeuxTemps:
                    return new DuoTechDef
                    {
                        Id = id,
                        Name = "🌫️ Mirage à Deux Temps",
                        Description = "Défensif : replacement 3 cases gratuites + réaction défensive gratuite (0 PA) jusqu'au prochain tour. Semi = replacement seul.",
                        MinaCost = 2,
                        LucasCost = 1,
                        SyncND = 4,
                        SyncAttrs = "AGI+INT",
                        Condition = "Aucune (défensif)"
                    };
                case DuoTechId.UppercutThermobarique:
                    return new DuoTechDef
                    {
                        Id = id,
                        Name = "⬆️ Uppercut Thermobarique",
                        Description = "Cible entravée à ≤ 50% PV : 7 bruts + À Terre. Semi = 4 bruts. Ratée = +1 Essoufflement chacun (whiff punit).",
                        MinaCost = 4,
                        LucasCost = 4,
                        SyncND = 6,
                        SyncAttrs = "FOR+INT",
                        Condition = "Cible entravée (étourdie, ralentie ou gelée) à ≤ 50% PV"
                    };
                default:
                    return null;
            }
        }

        /// <summary>
        /// Les 9 définitions officielles (Livre VI §25.5) dans l'ordre de la table.
        /// </summary>
        public static List<DuoTechDef> GetAllDefs()
        {
            return new List<DuoTechDef>
            {
                GetDef(DuoTechId.SillageIgne),
                GetDef(DuoTechId.LacetNytharite),
                GetDef(DuoTechId.FournaiseRetardement),
                GetDef(DuoTechId.TrempeInversee),
                GetDef(DuoTechId.RailgunArtisanale),
                GetDef(DuoTechId.CisailleEntropique),
                GetDef(DuoTechId.Parallaxe),
                GetDef(DuoTechId.MirageDeuxTemps),
                GetDef(DuoTechId.UppercutThermobarique)
            };
        }

        /// <summary>
        /// Issue table (§25.5.2) depuis les deux épreuves de synchro :
        /// les deux réussissent = Parfaite, une seule = Semi, aucune = Ratée.
        /// </summary>
        public static DuoTechOutcome TableOutcome(bool firstSuccess, bool secondSuccess)
        {
            if (firstSuccess && secondSuccess) return DuoTechOutcome.Perfect;
            if (firstSuccess || secondSuccess) return DuoTechOutcome.Partial;
            return DuoTechOutcome.Miss;
        }

        /// <summary>
        /// Pont tissage souris → table : le tissage ne produit que Parfaite/Ratée
        /// (binaire, comme Lacet/Fournaise). Perfect tissage = Parfaite table,
        /// Risky/Late/Miss tissage = Ratée table (le Semi n'existe qu'aux dés).
        /// Réservé aux futurs tisseurs qui voudraient brancher la synchro
        /// temporelle sur les 6 nouveaux (actuellement binaire par condition).
        /// </summary>
        public static DuoTechOutcome WeaveGradeToOutcome(WeaveSyncGrade grade)
        {
            return grade == WeaveSyncGrade.Perfect ? DuoTechOutcome.Perfect : DuoTechOutcome.Miss;
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
        // RD-085 — ARMURE FISSURÉE (Trempe Inversée, Livre VI §25.5)
        // -2 absorption jusqu'à la fin du round suivant. Suivi dans ce registre :
        // ApplyArmorFissure débite, RestoreArmorFissure rembourse (MJ / TurnManager
        // à la fin du round suivant). Testable sans Unity.
        // =====================================================================

        private static readonly Dictionary<CharacterStats, int> _armorFissures = new();

        public static void ApplyArmorFissure(CharacterStats target, int amount = 2)
        {
            if (target == null || amount <= 0) return;
            // Enregistre le débit RÉEL (plancher 0) pour un remboursement exact.
            int before = target.BaseArmorAbsorption;
            target.BaseArmorAbsorption = Math.Max(0, before - amount);
            int debited = before - target.BaseArmorAbsorption;
            if (debited <= 0) return;
            _armorFissures.TryGetValue(target, out int prev);
            _armorFissures[target] = prev + debited;
        }

        public static bool HasArmorFissure(CharacterStats target)
        {
            return target != null
                && _armorFissures.TryGetValue(target, out int v) && v > 0;
        }

        public static void RestoreArmorFissure(CharacterStats target)
        {
            if (target == null) return;
            if (_armorFissures.TryGetValue(target, out int v) && v > 0)
            {
                target.BaseArmorAbsorption += v;
                _armorFissures.Remove(target);
            }
        }

        /// <summary>
        /// Restaure TOUTES les fissures suivies (fin d'effet groupée), puis
        /// vide le suivi. Ne fait jamais perdre d'armure : sans suivi, rien.
        /// </summary>
        public static void ClearAllArmorFissures()
        {
            foreach (var kvp in _armorFissures)
            {
                if (kvp.Key != null && kvp.Value > 0)
                    kvp.Key.BaseArmorAbsorption += kvp.Value;
            }
            _armorFissures.Clear();
        }

        /// <summary>
        /// Dégâts perforants (Railgun Parfaite) : raw moins la moitié de l'armure
        /// totale (base + portée, arrondi inférieur), plancher 0. Le reste suit la
        /// voie brute (létal via EvaluateFatalBlow, sinon PV directs).
        /// </summary>
        private static void DealPerforant(CharacterStats target, int raw, Action<string> log, string label)
        {
            if (target == null || !target.IsAlive) return;
            int totalArmor = Math.Max(0, target.BaseArmorAbsorption + target.GetWornArmorBonus());
            int net = Math.Max(0, raw - totalArmor / 2);
            if (net <= 0)
            {
                log?.Invoke($"{label} : {raw} perforants absorbés par {totalArmor} armure (moitié = {totalArmor / 2}) — 0 net.");
                return;
            }
            log?.Invoke($"{label} : {raw} perforants - {totalArmor / 2} (moitié de {totalArmor} armure) = {net} nets.");
            DealBrut(target, net, log, label);
        }

        /// <summary>
        /// Whiff Uppercut (Ratée) : +1 Essoufflement chacun, sans dépasser le
        /// plafond de Constitution ; au plafond, Ralenti 1 tour (cohérent avec
        /// TriggerRedlineAP). PA déjà payés perdus (pas de refund).
        /// </summary>
        private static void DealWhiffEssoufflement(CharacterStats s, Action<string> log, string label)
        {
            if (s == null || !s.IsAlive) return;
            int maxEss = s.Attributes.Constitution;
            s.Essoufflement = Math.Min(maxEss, s.Essoufflement + 1);
            log?.Invoke($"{label} : {s.Name} +1 Essoufflement ({s.Essoufflement}/{maxEss}).");
            if (s.Essoufflement >= maxEss)
                s.ApplyStatus(StatusEffect.Ralenti, 1);
        }

        /// <summary>
        /// Condition Uppercut (Livre VI §25.5) : cible entravée (étourdie,
        /// ralentie ou gelée — gelée lue comme Immobilisé/Sonné/Paralysé, les
        /// entraves dures du compendium) ET à ≤ 50% PV.
        /// </summary>
        public static bool IsEntraveeForUppercut(CharacterStats target)
        {
            if (target == null || !target.IsAlive) return false;
            return target.ActiveStatus.HasFlag(StatusEffect.Etourdi)
                || target.ActiveStatus.HasFlag(StatusEffect.Ralenti)
                || target.ActiveStatus.HasFlag(StatusEffect.Immobilise)
                || target.ActiveStatus.HasFlag(StatusEffect.Sonne)
                || target.ActiveStatus.HasFlag(StatusEffect.Paralyse);
        }

        public static bool IsUppercutCondition(CharacterStats target)
        {
            if (!IsEntraveeForUppercut(target)) return false;
            return target.CurrentHealth * 2 <= target.MaxHealth;
        }

        // =====================================================================
        // RD-085 — RÉSOLUTIONS TABLE (Parfaite / Semi / Ratée, Livre VI §25.5)
        // Le WeaveController appelle en binaire (Perfect/Miss) après validation
        // de placement ; le Semi n'existe qu'aux dés (TableOutcome partiel).
        // =====================================================================

        /// <summary>
        /// Trempe Inversée : Parfaite = Ralenti 1t + armures fissurées (-2
        /// absorption jusqu'à fin round suivant) ; Semi = Ralenti seul ;
        /// Ratée = le gel ne prend pas.
        /// </summary>
        public static void ResolveTrempe(
            List<CharacterStats> targets, DuoTechOutcome outcome, Action<string> log)
        {
            if (outcome == DuoTechOutcome.Miss)
            {
                log?.Invoke("❄️ <b>Trempe Inversée</b> [Ratée] : le gel ne prend pas (PA perdus).");
                return;
            }
            if (targets == null || targets.Count == 0)
            {
                log?.Invoke("❄️ <b>Trempe Inversée</b> : aucun groupé dans la zone (PA perdus).");
                return;
            }
            string tag = outcome == DuoTechOutcome.Perfect ? "Parfaite" : "Semi";
            log?.Invoke($"❄️ <b>Trempe Inversée</b> [{tag}] : {targets.Count} cible(s).");
            for (int i = 0; i < targets.Count; i++)
            {
                var t = targets[i];
                if (t == null || !t.IsAlive) continue;
                t.ApplyStatus(StatusEffect.Ralenti, 1);
                if (outcome == DuoTechOutcome.Perfect)
                {
                    ApplyArmorFissure(t, 2);
                    log?.Invoke($"Trempe : {t.Name} est [Ralenti] 1 tour, armure fissurée -2 (restaurer +2 fin round suivant) !");
                }
                else
                {
                    log?.Invoke($"Trempe : {t.Name} est [Ralenti] 1 tour.");
                }
            }
        }

        /// <summary>
        /// Railgun Artisanale : Parfaite = 8 perforants (moitié d'armure) sur la
        /// cible dure ; Semi = 5 bruts, armure normale ; Ratée = couloir refermé.
        /// </summary>
        public static void ResolveRailgun(
            CharacterStats target, DuoTechOutcome outcome, Action<string> log)
        {
            if (target == null || !target.IsAlive) return;
            switch (outcome)
            {
                case DuoTechOutcome.Perfect:
                    log?.Invoke("🛤️ <b>Railgun Artisanale</b> [Parfaite] : décharge à plein régime !");
                    DealPerforant(target, 8, log, "Railgun");
                    break;
                case DuoTechOutcome.Partial:
                    log?.Invoke("🛤️ <b>Railgun Artisanale</b> [Semi] : bobines à demi-charge.");
                    DealBrut(target, 5, log, "Railgun");
                    break;
                default:
                    log?.Invoke("🛤️ <b>Railgun Artisanale</b> [Ratée] : le couloir se referme (PA perdus).");
                    break;
            }
        }

        /// <summary>
        /// Cisaille Entropique : Parfaite = 5 bruts + Saignement sur chaque cible
        /// à l'intersection ; Semi = 3 bruts ; Ratée = branches désynchronisées.
        /// </summary>
        public static void ResolveCisaille(
            List<CharacterStats> targets, DuoTechOutcome outcome, Action<string> log)
        {
            if (outcome == DuoTechOutcome.Miss)
            {
                log?.Invoke("✖️ <b>Cisaille Entropique</b> [Ratée] : les branches se désynchronisent (PA perdus).");
                return;
            }
            if (targets == null || targets.Count == 0)
            {
                log?.Invoke("✖️ <b>Cisaille Entropique</b> : personne à l'intersection (PA perdus).");
                return;
            }
            if (outcome == DuoTechOutcome.Perfect)
            {
                log?.Invoke($"✖️ <b>Cisaille Entropique</b> [Parfaite] : {targets.Count} cible(s) cisaillée(s) !");
                for (int i = 0; i < targets.Count; i++)
                {
                    var t = targets[i];
                    if (t == null || !t.IsAlive) continue;
                    DealBrut(t, 5, log, "Cisaille");
                    if (!t.IsAlive) continue;
                    t.ApplyStatus(StatusEffect.Saignement, 1);
                    log?.Invoke($"Cisaille : {t.Name} [Saignement] !");
                }
            }
            else
            {
                log?.Invoke($"✖️ <b>Cisaille Entropique</b> [Semi] : {targets.Count} cible(s), coupe partielle.");
                for (int i = 0; i < targets.Count; i++)
                {
                    var t = targets[i];
                    if (t == null || !t.IsAlive) continue;
                    DealBrut(t, 3, log, "Cisaille");
                }
            }
        }

        /// <summary>
        /// Parallaxe : Parfaite = cible à 0 en défense face à 4 bruts ;
        /// Semi = défense à -2 face aux mêmes 4 bruts (canon littéral) ;
        /// Ratée = la cible esquive entre les deux temps.
        /// </summary>
        public static void ResolveParallaxe(
            CharacterStats target, DuoTechOutcome outcome, Action<string> log)
        {
            if (target == null || !target.IsAlive) return;
            switch (outcome)
            {
                case DuoTechOutcome.Perfect:
                    log?.Invoke("⧉ <b>Parallaxe</b> [Parfaite] : la cible ne peut pas se défendre (score 0) !");
                    DealBrut(target, 4, log, "Parallaxe");
                    break;
                case DuoTechOutcome.Partial:
                    log?.Invoke("⧉ <b>Parallaxe</b> [Semi] : défense adverse à -2 !");
                    DealBrut(target, 4, log, "Parallaxe");
                    break;
                default:
                    log?.Invoke("⧉ <b>Parallaxe</b> [Ratée] : la cible esquive entre les deux temps (PA perdus).");
                    break;
            }
        }

        /// <summary>
        /// Mirage à Deux Temps (défensif) : Parfaite = replacement 3 cases +
        /// réaction défensive gratuite (0 PA) jusqu'au prochain tour de Mina ;
        /// Semi = replacement seul ; Ratée = pré-vision brouillée. Le déplacement
        /// grille est exécuté par le WeaveController (téléport) ; ici : statuts.
        /// </summary>
        public static void ResolveMirage(
            CharacterStats mina, DuoTechOutcome outcome, Action<string> log)
        {
            switch (outcome)
            {
                case DuoTechOutcome.Perfect:
                    log?.Invoke("🌫️ <b>Mirage à Deux Temps</b> [Parfaite] : replacement 3 cases + réaction défensive gratuite (0 PA) jusqu'au prochain tour !");
                    mina?.GrantFreeDefensiveReaction();
                    break;
                case DuoTechOutcome.Partial:
                    log?.Invoke("🌫️ <b>Mirage à Deux Temps</b> [Semi] : replacement 3 cases (sans réaction gratuite).");
                    break;
                default:
                    log?.Invoke("🌫️ <b>Mirage à Deux Temps</b> [Ratée] : la pré-vision se brouille (PA perdus).");
                    break;
            }
        }

        /// <summary>
        /// Uppercut Thermobarique : Parfaite = 7 bruts + À Terre ; Semi = 4 bruts ;
        /// Ratée = PA perdus + 1 Essoufflement chacun (whiff punit, même au tissage).
        /// </summary>
        public static void ResolveUppercut(
            CharacterStats mina, CharacterStats lucas,
            CharacterStats target, DuoTechOutcome outcome, Action<string> log)
        {
            switch (outcome)
            {
                case DuoTechOutcome.Perfect:
                    if (target == null || !target.IsAlive) return;
                    log?.Invoke("⬆️ <b>Uppercut Thermobarique</b> [Parfaite] : détonation ascendante !");
                    DealBrut(target, 7, log, "Uppercut");
                    if (!target.IsAlive) return;
                    target.ApplyStatus(StatusEffect.ATerre, 1);
                    log?.Invoke($"Uppercut : {target.Name} est [À Terre] !");
                    break;
                case DuoTechOutcome.Partial:
                    if (target == null || !target.IsAlive) return;
                    log?.Invoke("⬆️ <b>Uppercut Thermobarique</b> [Semi] : percussion partielle.");
                    DealBrut(target, 4, log, "Uppercut");
                    break;
                default:
                    log?.Invoke("⬆️ <b>Uppercut Thermobarique</b> [Ratée] : whiff — le vide se referme, +1 Essoufflement chacun (PA perdus).");
                    DealWhiffEssoufflement(mina, log, "Uppercut (whiff)");
                    DealWhiffEssoufflement(lucas, log, "Uppercut (whiff)");
                    break;
            }
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

            // --- RD-085 : les 6 Duo-Techs Livre VI §25.5 (placement validé au
            // tissage par le WeaveController + MJ, pas dans CanDeclare) ---
            var trempe = GetDef(DuoTechId.TrempeInversee);
            if (isEnemy && target.Stats != null && target.Stats.IsAlive)
            {
                actions.Add(new CombatAction(
                    $"❄️ Trempe Inversée (duo {trempe.TotalCost} PA)",
                    "Gel croisé sur ennemis groupés (≤ 2 cases) : Ralenti 1t + armures fissurées -2. Tissage souris.",
                    ActionCategory.TechniquesDeSpecialisation,
                    0,
                    (act, tgt) => CanDeclare(act.Stats, FindPartner(act)?.Stats, trempe, CurrentDuoRound(), out _),
                    (act, tgt) => BeginWeave(DuoTechId.TrempeInversee, act, arena)
                ));
            }

            var railgun = GetDef(DuoTechId.RailgunArtisanale);
            if (isEnemy && target.Stats != null && target.Stats.IsAlive)
            {
                actions.Add(new CombatAction(
                    $"🛤️ Railgun Artisanale (duo {railgun.TotalCost} PA)",
                    "Couloir vide 4+ cases : 8 perforants (moitié armure) sur la cible dure. Tissage souris.",
                    ActionCategory.TechniquesDeSpecialisation,
                    0,
                    (act, tgt) => CanDeclare(act.Stats, FindPartner(act)?.Stats, railgun, CurrentDuoRound(), out _),
                    (act, tgt) => BeginWeave(DuoTechId.RailgunArtisanale, act, arena)
                ));
            }

            var cisaille = GetDef(DuoTechId.CisailleEntropique);
            if (isEnemy && target.Stats != null && target.Stats.IsAlive)
            {
                actions.Add(new CombatAction(
                    $"✖️ Cisaille Entropique (duo {cisaille.TotalCost} PA)",
                    "Axes croisés en X : 5 bruts + Saignement à l'intersection. Tissage souris.",
                    ActionCategory.TechniquesDeSpecialisation,
                    0,
                    (act, tgt) => CanDeclare(act.Stats, FindPartner(act)?.Stats, cisaille, CurrentDuoRound(), out _),
                    (act, tgt) => BeginWeave(DuoTechId.CisailleEntropique, act, arena)
                ));
            }

            var parallaxe = GetDef(DuoTechId.Parallaxe);
            if (isEnemy && target.Stats != null && target.Stats.IsAlive)
            {
                actions.Add(new CombatAction(
                    $"⧉ Parallaxe (duo {parallaxe.TotalCost} PA)",
                    "Opposition 180° : cible à 0 en défense face à 4 bruts. Tissage souris.",
                    ActionCategory.TechniquesDeSpecialisation,
                    0,
                    (act, tgt) => CanDeclare(act.Stats, FindPartner(act)?.Stats, parallaxe, CurrentDuoRound(), out _),
                    (act, tgt) => BeginWeave(DuoTechId.Parallaxe, act, arena)
                ));
            }

            var mirage = GetDef(DuoTechId.MirageDeuxTemps);
            if (!isEnemy && target.Stats != null && target.Stats.IsAlive)
            {
                actions.Add(new CombatAction(
                    $"🌫️ Mirage à Deux Temps (duo {mirage.TotalCost} PA)",
                    "Défensif : replacement 3 cases + réaction défensive gratuite (0 PA). Tissage souris.",
                    ActionCategory.TechniquesDeSpecialisation,
                    0,
                    (act, tgt) => CanDeclare(act.Stats, FindPartner(act)?.Stats, mirage, CurrentDuoRound(), out _),
                    (act, tgt) => BeginWeave(DuoTechId.MirageDeuxTemps, act, arena)
                ));
            }

            var uppercut = GetDef(DuoTechId.UppercutThermobarique);
            if (isEnemy && target.Stats != null && target.Stats.IsAlive)
            {
                actions.Add(new CombatAction(
                    $"⬆️ Uppercut Thermobarique (duo {uppercut.TotalCost} PA)",
                    "Cible entravée ≤ 50% PV : 7 bruts + À Terre (whiff = +1 ESS chacun). Tissage souris.",
                    ActionCategory.TechniquesDeSpecialisation,
                    0,
                    (act, tgt) => CanDeclare(act.Stats, FindPartner(act)?.Stats, uppercut, CurrentDuoRound(), out _),
                    (act, tgt) => BeginWeave(DuoTechId.UppercutThermobarique, act, arena)
                ));
            }

            return actions;
        }
    }
}
