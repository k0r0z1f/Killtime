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
    /// Menu contextuel Unity : déclaration + ouverture du tissage souris. — extrait sans modification depuis DuoTechRegistry.cs.
    /// </summary>
    public static partial class DuoTechRegistry
    {
        // =====================================================================
        // MENU CONTEXTUEL (Unity) — déclaration + ouverture du tissage
        // =====================================================================

        private static List<TacticalUnit> GetAllUnits()
        {
            return new List<TacticalUnit>(TacticalUnitRegistry.AllUnits);
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

        private static TacticalUnit FindPartnerFor(TacticalUnit actor, DuoTechDef def)
        {
            if (actor?.Stats == null || def == null) return null;
            switch (def.Id)
            {
                case DuoTechId.RempartTellurique:
                    if (IsThomasMember(actor.Stats)) return FindHero(actor, true, false);
                    if (IsMinaMember(actor.Stats)) return FindHero(actor, false, false, true);
                    return null;
                case DuoTechId.ZeroTectonique:
                    if (IsThomasMember(actor.Stats)) return FindHero(actor, false, true);
                    if (IsLucasMember(actor.Stats)) return FindHero(actor, false, false, true);
                    return null;
                default:
                    return FindPartner(actor);
            }
        }

        private static TacticalUnit FindHero(TacticalUnit actor, bool wantMina, bool wantLucas, bool wantThomas = false)
        {
            var all = GetAllUnits();
            for (int i = 0; i < all.Count; i++)
            {
                var u = all[i];
                if (u == null || u == actor || u.Stats == null || !u.Stats.IsAlive) continue;
                if (wantMina && IsMinaMember(u.Stats)) return u;
                if (wantLucas && IsLucasMember(u.Stats)) return u;
                if (wantThomas && IsThomasMember(u.Stats)) return u;
            }
            return null;
        }

        /// <summary>
        /// Les deux partenaires manquants du trio pour l'acteur (RD-086).
        /// Ex : acteur Thomas → (Mina, Lucas). Faux si le trio est incomplet.
        /// </summary>
        private static bool FindTrioPartners(TacticalUnit actor, out TacticalUnit pA, out TacticalUnit pB)
        {
            pA = null;
            pB = null;
            if (actor?.Stats == null) return false;
            bool actorMina = IsMinaMember(actor.Stats);
            bool actorLucas = IsLucasMember(actor.Stats);
            bool actorThomas = IsThomasMember(actor.Stats);
            if (!actorMina && !actorLucas && !actorThomas) return false;

            TacticalUnit mina = actorMina ? actor : FindHero(actor, true, false);
            TacticalUnit lucas = actorLucas ? actor : FindHero(actor, false, true);
            TacticalUnit thomas = actorThomas ? actor : FindHero(actor, false, false, true);
            if (mina == null || lucas == null || thomas == null) return false;
            // pA/pB = les deux qui ne sont pas l'acteur.
            var others = new List<TacticalUnit>(2);
            if (mina != actor) others.Add(mina);
            if (lucas != actor) others.Add(lucas);
            if (thomas != actor) others.Add(thomas);
            if (others.Count != 2) return false;
            pA = others[0];
            pB = others[1];
            return true;
        }

        /// <summary>
        /// RD-086 — déclaration table (MJ) : paiement immédiat puis résolution
        /// aux dés via TableOutcome + Resolve*. Pas de tissage souris (le
        /// WeaveController ne gère que les paires Lucas/Mina) : l'issue est
        /// tranchée par le MJ et consignée au chrono.
        /// </summary>
        private static void BeginTablePair(DuoTechId id, TacticalUnit initiator, CombatDevArena arena)
        {
            var def = GetDef(id);
            if (def == null) return;
            var partner = FindPartnerFor(initiator, def);
            if (partner == null)
            {
                arena?.Log("Duo-Tech : partenaire introuvable (Rempart = Thomas+Mina, Zéro = Thomas+Lucas en vie sur la carte).");
                return;
            }
            if (!TryDeclarePairPayment(initiator.Stats, partner.Stats, def, CurrentDuoRound()))
            {
                CanDeclarePair(initiator.Stats, partner.Stats, def, CurrentDuoRound(), out string why);
                arena?.Log($"Duo-Tech refusé : {why}");
                var vis = initiator.GetComponent<TacticalUnitVisual>();
                vis?.SpawnFloatingText($"Duo refusé : {why}", Color.red);
                return;
            }
            arena?.Log($"{def.Name} armé ({def.TotalCost} PA payés, sans expiration) : issue à trancher aux dés par le MJ (TableOutcome → Resolve).");
            arena?.RecordChronoSnapshot($"{def.Name} armé : {initiator.Stats.Name} + {partner.Stats.Name}");
        }

        private static void BeginTableTrio(TacticalUnit initiator, CombatDevArena arena)
        {
            var def = GetDef(DuoTechId.TriFusion);
            if (def == null) return;
            if (!FindTrioPartners(initiator, out TacticalUnit pA, out TacticalUnit pB))
            {
                arena?.Log("Tri-Fusion : trio introuvable (il faut Thomas + Mina + Lucas en vie sur la carte).");
                return;
            }
            if (!TryDeclareTrioPayment(initiator.Stats, pA.Stats, pB.Stats, def, CurrentDuoRound()))
            {
                CanDeclareTrio(initiator.Stats, pA.Stats, pB.Stats, def, CurrentDuoRound(), out string why);
                arena?.Log($"Tri-Fusion refusée : {why}");
                var vis = initiator.GetComponent<TacticalUnitVisual>();
                vis?.SpawnFloatingText($"Trio refusé : {why}", Color.red);
                return;
            }
            arena?.Log($"{def.Name} armée ({def.TotalCost} PA payés, sans expiration) : issue à trancher aux dés par le MJ (TableOutcome → ResolveTriFusion).");
            arena?.RecordChronoSnapshot($"{def.Name} armée : {initiator.Stats.Name} + {pA.Stats.Name} + {pB.Stats.Name}");
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
        /// Lucas, Mina ou Thomas, que c'est son tour, et que le partenaire est en vie.
        /// L'ActionPointCost affiché = 0 car le débit réel (départ + rôles, sur les
        /// DEUX fiches) est fait dans BeginWeave — CanExecute ne sait checker
        /// qu'une seule fiche, CanDeclare fait le vrai contrôle.
        /// </summary>
        public static List<CombatAction> GetDuoTechActions(
            TacticalUnit actor, TacticalUnit target, CombatDevArena arena)
        {
            var actions = new List<CombatAction>();
            if (actor?.Stats == null || target == null) return actions;
            if (!IsDuoMember(actor.Stats) && !IsThomasMember(actor.Stats)) return actions;
            if (!IsActiveTurn(actor)) return actions;

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

            // --- RD-086 : synergies Thomas (résolution table MJ, sans tissage) ---
            var rempart = GetDef(DuoTechId.RempartTellurique);
            if (isEnemy && target.Stats != null && target.Stats.IsAlive
                && (IsThomasMember(actor.Stats) || IsMinaMember(actor.Stats)))
            {
                actions.Add(new CombatAction(
                    $"🛡️ Rempart Tellurique (duo {rempart.TotalCost} PA)",
                    "Thomas + Mina : onde de choc + barricade rocheuse (Cover Half, MJ). Paiement immédiat, issue aux dés.",
                    ActionCategory.TechniquesDeSpecialisation,
                    0,
                    (act, tgt) => CanDeclarePair(act.Stats, FindPartnerFor(act, rempart)?.Stats, rempart, CurrentDuoRound(), out _),
                    (act, tgt) => BeginTablePair(DuoTechId.RempartTellurique, act, arena)
                ));
            }

            var zero = GetDef(DuoTechId.ZeroTectonique);
            if (isEnemy && target.Stats != null && target.Stats.IsAlive
                && (IsThomasMember(actor.Stats) || IsLucasMember(actor.Stats)))
            {
                actions.Add(new CombatAction(
                    $"🕳️ Zéro Tectonique (duo {zero.TotalCost} PA)",
                    "Thomas + Lucas : faille de vide gelante (Immobilisé). Paiement immédiat, issue aux dés.",
                    ActionCategory.TechniquesDeSpecialisation,
                    0,
                    (act, tgt) => CanDeclarePair(act.Stats, FindPartnerFor(act, zero)?.Stats, zero, CurrentDuoRound(), out _),
                    (act, tgt) => BeginTablePair(DuoTechId.ZeroTectonique, act, arena)
                ));
            }

            var trifusion = GetDef(DuoTechId.TriFusion);
            if (isEnemy && target.Stats != null && target.Stats.IsAlive && IsTrioMember(actor.Stats))
            {
                actions.Add(new CombatAction(
                    $"☄️ Tri-Fusion (trio {trifusion.TotalCost} PA)",
                    "Ultime Thomas + Mina + Lucas : 8 bruts + À Terre + EnFeu (contrecoup ESS si ratée). Paiement trio immédiat, issue aux dés.",
                    ActionCategory.TechniquesDeSpecialisation,
                    0,
                    (act, tgt) =>
                    {
                        if (!FindTrioPartners(act, out TacticalUnit pA, out TacticalUnit pB)) return false;
                        return CanDeclareTrio(act.Stats, pA.Stats, pB.Stats, trifusion, CurrentDuoRound(), out _);
                    },
                    (act, tgt) => BeginTableTrio(act, arena)
                ));
            }

            return actions;
        }
    }
}
