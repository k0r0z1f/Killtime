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
    /// RD-086 — Synergie Tri-Fusion & Ancrage Tellurique de Thomas (Livres IV & V).
    /// Thomas-0 = ancre tellurique physique : réflexion des ondes cinétiques de
    /// Mina, absorption du contrecoup thermodynamique de Lucas (cf. ThomasCharacter).
    /// 3 synergies table (résolution MJ aux dés via TableOutcome, comme RD-085) :
    /// Rempart Tellurique (Thomas + Mina), Zéro Tectonique (Thomas + Lucas),
    /// Tri-Fusion (ultime, trio synchronisé).
    /// Tissage souris hors périmètre : comme RD-085, le placement est validé par
    /// le MJ ; le WeaveController n'est pas branché (couleurs/résolutions weave
    /// inchangées, cas défaut = pas de surlignage).
    /// </summary>
    public static partial class DuoTechRegistry
    {
        // =====================================================================
        // IDENTITÉ THOMAS (miroir IsLucasMember / IsMinaMember)
        // =====================================================================

        public static bool IsThomasMember(CharacterStats s)
        {
            if (s == null) return false;
            if (s.Sheet != null) return ThomasCharacter.IsThomas(s.Sheet);
            return !string.IsNullOrEmpty(s.Name)
                && s.Name.IndexOf("Thomas", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool IsTrioMember(CharacterStats s)
            => IsLucasMember(s) || IsMinaMember(s) || IsThomasMember(s);

        /// <summary>
        /// Rempart Tellurique = 1 Thomas + 1 Mina (ni Lucas, ni doublon).
        /// </summary>
        public static bool AreThomasMina(CharacterStats a, CharacterStats b)
        {
            if (a == null || b == null || ReferenceEquals(a, b)) return false;
            return (IsThomasMember(a) && IsMinaMember(b))
                || (IsMinaMember(a) && IsThomasMember(b));
        }

        /// <summary>
        /// Zéro Tectonique = 1 Thomas + 1 Lucas (ni Mina, ni doublon).
        /// </summary>
        public static bool AreThomasLucas(CharacterStats a, CharacterStats b)
        {
            if (a == null || b == null || ReferenceEquals(a, b)) return false;
            return (IsThomasMember(a) && IsLucasMember(b))
                || (IsLucasMember(a) && IsThomasMember(b));
        }

        /// <summary>
        /// Tri-Fusion = 3 participants distincts : 1 Thomas + 1 Mina + 1 Lucas.
        /// </summary>
        public static bool AreTrio(CharacterStats a, CharacterStats b, CharacterStats c)
        {
            if (a == null || b == null || c == null) return false;
            if (ReferenceEquals(a, b) || ReferenceEquals(b, c) || ReferenceEquals(a, c)) return false;
            var set = new HashSet<CharacterStats> { a, b, c };
            bool hasThomas = false, hasMina = false, hasLucas = false;
            foreach (var s in set)
            {
                if (IsThomasMember(s)) hasThomas = true;
                else if (IsMinaMember(s)) hasMina = true;
                else if (IsLucasMember(s)) hasLucas = true;
                else return false;
            }
            return hasThomas && hasMina && hasLucas;
        }

        /// <summary>
        /// Coût de rôle étendu à Thomas (les 9 classiques gardent ThomasCost = 0 :
        /// un Thomas ne peut pas les tisser, CanDeclarePair refuse).
        /// </summary>
        public static int RoleCostFor(DuoTechDef def, CharacterStats s)
        {
            if (def == null || s == null) return int.MaxValue;
            if (IsLucasMember(s)) return def.LucasCost;
            if (IsMinaMember(s)) return def.MinaCost;
            if (IsThomasMember(s)) return def.ThomasCost;
            return int.MaxValue;
        }

        // =====================================================================
        // DÉCLARATION PAIRES THOMAS + TRIO (atomique, sans dette)
        // =====================================================================

        private static bool CheckPairLimit(CharacterStats a, CharacterStats b, int currentRound, out string why)
        {
            why = string.Empty;
            if (currentRound > 0)
            {
                if (a.LastDuoTechRound >= currentRound)
                {
                    why = $"{a.Name} a déjà tissé ce round (1 duo max).";
                    return false;
                }
                if (b.LastDuoTechRound >= currentRound)
                {
                    why = $"{b.Name} a déjà tissé ce round (1 duo max).";
                    return false;
                }
            }
            return true;
        }

        private static bool CheckPairPA(CharacterStats initiator, CharacterStats partner,
            DuoTechDef def, int initNeed, int partNeed, out string why)
        {
            why = string.Empty;
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
        /// Paires Thomas : Rempart (Thomas+Mina) et Zéro (Thomas+Lucas).
        /// Les 9 classiques restent sur CanDeclare (Lucas+Mina exigé).
        /// </summary>
        public static bool CanDeclarePair(CharacterStats initiator, CharacterStats partner,
            DuoTechDef def, int currentRound, out string why)
        {
            why = string.Empty;
            if (def == null) { why = "Duo inconnu."; return false; }
            if (initiator == null || partner == null) { why = "Binôme incomplet."; return false; }

            bool ok;
            switch (def.Id)
            {
                case DuoTechId.RempartTellurique:
                    ok = AreThomasMina(initiator, partner);
                    if (!ok) why = "Rempart Tellurique = 1 Thomas + 1 Mina.";
                    break;
                case DuoTechId.ZeroTectonique:
                    ok = AreThomasLucas(initiator, partner);
                    if (!ok) why = "Zéro Tectonique = 1 Thomas + 1 Lucas.";
                    break;
                default:
                    return CanDeclare(initiator, partner, def, currentRound, out why);
            }
            if (!ok) return false;
            if (!initiator.IsAlive) { why = $"{initiator.Name} est hors de combat."; return false; }
            if (!partner.IsAlive) { why = $"{partner.Name} est hors de combat."; return false; }
            if (!CheckPairLimit(initiator, partner, currentRound, out why)) return false;

            int initNeed = def.DeclareCost + RoleCostFor(def, initiator);
            int partNeed = RoleCostFor(def, partner);
            return CheckPairPA(initiator, partner, def, initNeed, partNeed, out why);
        }

        public static bool TryDeclarePairPayment(CharacterStats initiator, CharacterStats partner,
            DuoTechDef def, int currentRound)
        {
            if (def == null) return false;
            if (def.Id != DuoTechId.RempartTellurique && def.Id != DuoTechId.ZeroTectonique)
                return TryDeclarePayment(initiator, partner, def, currentRound);
            if (!CanDeclarePair(initiator, partner, def, currentRound, out _)) return false;

            int initNeed = def.DeclareCost + RoleCostFor(def, initiator);
            int partNeed = RoleCostFor(def, partner);
            if (!initiator.ConsumeActionPoints(initNeed)) return false;
            if (!partner.ConsumeActionPoints(partNeed))
            {
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

        /// <summary>
        /// Ultime Tri-Fusion : les TROIS héros paient immédiatement (initiateur =
        /// départ + son rôle, les deux autres = leur rôle). Échec du 2e/3e débit
        /// = remboursement des précédents. Les trois sont tamponnés au round.
        /// </summary>
        public static bool CanDeclareTrio(CharacterStats initiator, CharacterStats partnerA,
            CharacterStats partnerB, DuoTechDef def, int currentRound, out string why)
        {
            why = string.Empty;
            if (def == null) { why = "Duo inconnu."; return false; }
            if (def.Id != DuoTechId.TriFusion) { why = "CanDeclareTrio réservé à la Tri-Fusion."; return false; }
            if (initiator == null || partnerA == null || partnerB == null)
            {
                why = "Trio incomplet (Thomas + Mina + Lucas exigés).";
                return false;
            }
            if (!AreTrio(initiator, partnerA, partnerB))
            {
                why = "Tri-Fusion = 1 Thomas + 1 Mina + 1 Lucas.";
                return false;
            }
            if (!initiator.IsAlive) { why = $"{initiator.Name} est hors de combat."; return false; }
            if (!partnerA.IsAlive) { why = $"{partnerA.Name} est hors de combat."; return false; }
            if (!partnerB.IsAlive) { why = $"{partnerB.Name} est hors de combat."; return false; }

            if (currentRound > 0)
            {
                if (initiator.LastDuoTechRound >= currentRound)
                {
                    why = $"{initiator.Name} a déjà tissé ce round (1 duo max).";
                    return false;
                }
                if (partnerA.LastDuoTechRound >= currentRound)
                {
                    why = $"{partnerA.Name} a déjà tissé ce round (1 duo max).";
                    return false;
                }
                if (partnerB.LastDuoTechRound >= currentRound)
                {
                    why = $"{partnerB.Name} a déjà tissé ce round (1 duo max).";
                    return false;
                }
            }

            int initNeed = def.DeclareCost + RoleCostFor(def, initiator);
            int needA = RoleCostFor(def, partnerA);
            int needB = RoleCostFor(def, partnerB);
            if (initiator.CurrentActionPoints < initNeed)
            {
                why = $"{initiator.Name} : {initiator.CurrentActionPoints} PA < {initNeed} requis.";
                return false;
            }
            if (partnerA.CurrentActionPoints < needA)
            {
                why = $"{partnerA.Name} : {partnerA.CurrentActionPoints} PA < {needA} requis (paiement immédiat).";
                return false;
            }
            if (partnerB.CurrentActionPoints < needB)
            {
                why = $"{partnerB.Name} : {partnerB.CurrentActionPoints} PA < {needB} requis (paiement immédiat).";
                return false;
            }
            return true;
        }

        public static bool TryDeclareTrioPayment(CharacterStats initiator, CharacterStats partnerA,
            CharacterStats partnerB, DuoTechDef def, int currentRound)
        {
            if (!CanDeclareTrio(initiator, partnerA, partnerB, def, currentRound, out _)) return false;

            int initNeed = def.DeclareCost + RoleCostFor(def, initiator);
            int needA = RoleCostFor(def, partnerA);
            int needB = RoleCostFor(def, partnerB);

            if (!initiator.ConsumeActionPoints(initNeed)) return false;
            if (!partnerA.ConsumeActionPoints(needA))
            {
                initiator.CurrentActionPoints = Math.Min(
                    initiator.MaxActionPoints,
                    initiator.CurrentActionPoints + initNeed);
                return false;
            }
            if (!partnerB.ConsumeActionPoints(needB))
            {
                initiator.CurrentActionPoints = Math.Min(
                    initiator.MaxActionPoints,
                    initiator.CurrentActionPoints + initNeed);
                partnerA.CurrentActionPoints = Math.Min(
                    partnerA.MaxActionPoints,
                    partnerA.CurrentActionPoints + needA);
                return false;
            }

            if (currentRound > 0)
            {
                initiator.LastDuoTechRound = currentRound;
                partnerA.LastDuoTechRound = currentRound;
                partnerB.LastDuoTechRound = currentRound;
            }
            return true;
        }

        // =====================================================================
        // RÉSOLUTIONS TABLE (Parfaite / Semi / Ratée — MJ aux dés)
        // =====================================================================

        /// <summary>
        /// Rempart Tellurique : Parfaite = 4 bruts + Déstabilisé sur la zone +
        /// barricade rocheuse (Cover Half, posée sur la grille par le MJ) ;
        /// Semi = 2 bruts ; Ratée = l'onde se disperse (PA perdus).
        /// </summary>
        public static void ResolveRempart(
            List<CharacterStats> targets, DuoTechOutcome outcome, Action<string> log)
        {
            if (outcome == DuoTechOutcome.Miss)
            {
                log?.Invoke("🛡️ <b>Rempart Tellurique</b> [Ratée] : l'onde se disperse dans le sol (PA perdus).");
                return;
            }
            if (targets == null || targets.Count == 0)
            {
                log?.Invoke("🛡️ <b>Rempart Tellurique</b> : personne dans l'onde (PA perdus).");
                return;
            }
            if (outcome == DuoTechOutcome.Perfect)
            {
                log?.Invoke($"🛡️ <b>Rempart Tellurique</b> [Parfaite] : {targets.Count} cible(s) + barricade rocheuse (Cover Half, MJ) !");
                for (int i = 0; i < targets.Count; i++)
                {
                    var t = targets[i];
                    if (t == null || !t.IsAlive) continue;
                    DealBrut(t, 4, log, "Rempart");
                    if (!t.IsAlive) continue;
                    t.ApplyStatus(StatusEffect.Destabilise, 1);
                    log?.Invoke($"Rempart : {t.Name} est [Déstabilisé] 1 tour.");
                }
            }
            else
            {
                log?.Invoke($"🛡️ <b>Rempart Tellurique</b> [Semi] : {targets.Count} cible(s), onde partielle.");
                for (int i = 0; i < targets.Count; i++)
                {
                    var t = targets[i];
                    if (t == null || !t.IsAlive) continue;
                    DealBrut(t, 2, log, "Rempart");
                }
            }
        }

        /// <summary>
        /// Zéro Tectonique : Parfaite = 4 bruts + Immobilisé (faille gelante) ;
        /// Semi = 2 bruts + Ralenti ; Ratée = la faille se referme (PA perdus).
        /// </summary>
        public static void ResolveZero(
            List<CharacterStats> targets, DuoTechOutcome outcome, Action<string> log)
        {
            if (outcome == DuoTechOutcome.Miss)
            {
                log?.Invoke("🕳️ <b>Zéro Tectonique</b> [Ratée] : la faille se referme avant de geler (PA perdus).");
                return;
            }
            if (targets == null || targets.Count == 0)
            {
                log?.Invoke("🕳️ <b>Zéro Tectonique</b> : personne sur la faille (PA perdus).");
                return;
            }
            if (outcome == DuoTechOutcome.Perfect)
            {
                log?.Invoke($"🕳️ <b>Zéro Tectonique</b> [Parfaite] : {targets.Count} cible(s) figée(s) !");
                for (int i = 0; i < targets.Count; i++)
                {
                    var t = targets[i];
                    if (t == null || !t.IsAlive) continue;
                    DealBrut(t, 4, log, "Zéro");
                    if (!t.IsAlive) continue;
                    t.ApplyStatus(StatusEffect.Immobilise, 1);
                    log?.Invoke($"Zéro : {t.Name} est [Immobilisé] 1 tour (faille gelante).");
                }
            }
            else
            {
                log?.Invoke($"🕳️ <b>Zéro Tectonique</b> [Semi] : {targets.Count} cible(s), gel partiel.");
                for (int i = 0; i < targets.Count; i++)
                {
                    var t = targets[i];
                    if (t == null || !t.IsAlive) continue;
                    DealBrut(t, 2, log, "Zéro");
                    if (!t.IsAlive) continue;
                    t.ApplyStatus(StatusEffect.Ralenti, 1);
                    log?.Invoke($"Zéro : {t.Name} est [Ralenti] 1 tour.");
                }
            }
        }

        /// <summary>
        /// Tri-Fusion (ultime) : Parfaite = 8 bruts + À Terre + EnFeu ;
        /// Semi = 5 bruts ; Ratée = contrecoup : +1 Essoufflement chacun
        /// (whiff punit, comme l'Uppercut — PA déjà payés perdus).
        /// </summary>
        public static void ResolveTriFusion(
            CharacterStats mina, CharacterStats lucas, CharacterStats thomas,
            List<CharacterStats> targets, DuoTechOutcome outcome, Action<string> log)
        {
            switch (outcome)
            {
                case DuoTechOutcome.Perfect:
                    if (targets == null || targets.Count == 0)
                    {
                        log?.Invoke("☄️ <b>Tri-Fusion</b> [Parfaite] : convergence dans le vide (PA perdus).");
                        return;
                    }
                    log?.Invoke($"☄️ <b>Tri-Fusion</b> [Parfaite] : convergence totale sur {targets.Count} cible(s) !");
                    for (int i = 0; i < targets.Count; i++)
                    {
                        var t = targets[i];
                        if (t == null || !t.IsAlive) continue;
                        DealBrut(t, 8, log, "Tri-Fusion");
                        if (!t.IsAlive) continue;
                        t.ApplyStatus(StatusEffect.ATerre, 1);
                        t.ApplyStatus(StatusEffect.EnFeu, 1);
                        log?.Invoke($"Tri-Fusion : {t.Name} est [À Terre] et [En Feu] !");
                    }
                    break;
                case DuoTechOutcome.Partial:
                    if (targets == null || targets.Count == 0)
                    {
                        log?.Invoke("☄️ <b>Tri-Fusion</b> [Semi] : convergence dans le vide (PA perdus).");
                        return;
                    }
                    log?.Invoke($"☄️ <b>Tri-Fusion</b> [Semi] : {targets.Count} cible(s), convergence partielle.");
                    for (int i = 0; i < targets.Count; i++)
                    {
                        var t = targets[i];
                        if (t == null || !t.IsAlive) continue;
                        DealBrut(t, 5, log, "Tri-Fusion");
                    }
                    break;
                default:
                    log?.Invoke("☄️ <b>Tri-Fusion</b> [Ratée] : contrecoup — le vide se referme, +1 Essoufflement chacun (PA perdus).");
                    DealWhiffTrio(mina, log, "Tri-Fusion (contrecoup)");
                    DealWhiffTrio(lucas, log, "Tri-Fusion (contrecoup)");
                    DealWhiffTrio(thomas, log, "Tri-Fusion (contrecoup)");
                    break;
            }
        }

        /// <summary>
        /// Contrecoup trio (miroir du whiff Uppercut) : +1 Essoufflement sans
        /// dépasser le plafond de Constitution ; au plafond, Ralenti 1 tour.
        /// </summary>
        private static void DealWhiffTrio(CharacterStats s, Action<string> log, string label)
        {
            if (s == null || !s.IsAlive) return;
            int maxEss = s.Attributes.Constitution;
            s.Essoufflement = Math.Min(maxEss, s.Essoufflement + 1);
            log?.Invoke($"{label} : {s.Name} +1 Essoufflement ({s.Essoufflement}/{maxEss}).");
            if (s.Essoufflement >= maxEss)
                s.ApplyStatus(StatusEffect.Ralenti, 1);
        }
    }
}
