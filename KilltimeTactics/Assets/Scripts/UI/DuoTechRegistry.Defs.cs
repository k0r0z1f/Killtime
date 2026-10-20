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
    /// Définitions des 12 Duo-Techs (Livre V §21.5 + synergies Thomas RD-086) + issues table/tissage. — extrait sans modification depuis DuoTechRegistry.cs.
    /// </summary>
    public static partial class DuoTechRegistry
    {
        // --- Coûts verrouillés (Livre V §21.5, Cinétique + Calcul + départ 1) ---
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
                case DuoTechId.RempartTellurique:
                    return new DuoTechDef
                    {
                        Id = id,
                        Name = "🛡️ Rempart Tellurique",
                        Description = "Thomas + Mina : onde de choc radiale + barricade rocheuse (Cover Half à poser par le MJ). Parfaite = 4 bruts + Déstabilisé. Semi = 2 bruts.",
                        MinaCost = 3,
                        LucasCost = 0,
                        ThomasCost = 4,
                        SyncND = 6,
                        SyncAttrs = "FOR+AGI",
                        Condition = "Thomas + Mina (ancre tellurique + onde cinétique)"
                    };
                case DuoTechId.ZeroTectonique:
                    return new DuoTechDef
                    {
                        Id = id,
                        Name = "🕳️ Zéro Tectonique",
                        Description = "Thomas + Lucas : faille de vide gelant les hexagones. Parfaite = 4 bruts + Immobilisé. Semi = 2 bruts + Ralenti.",
                        MinaCost = 0,
                        LucasCost = 4,
                        ThomasCost = 4,
                        SyncND = 6,
                        SyncAttrs = "FOR+INT",
                        Condition = "Thomas + Lucas (ancre tellurique + calcul)"
                    };
                case DuoTechId.TriFusion:
                    return new DuoTechDef
                    {
                        Id = id,
                        Name = "☄️ Tri-Fusion",
                        Description = "Ultime trio synchronisé : les trois héros consomment ensemble. Parfaite = 8 bruts + À Terre + EnFeu. Semi = 5 bruts. Ratée = +1 Essoufflement chacun.",
                        MinaCost = 3,
                        LucasCost = 3,
                        ThomasCost = 4,
                        SyncND = 7,
                        SyncAttrs = "FOR+AGI+INT",
                        Condition = "Trio Thomas + Mina + Lucas (1 max par round chacun)"
                    };
                default:
                    return null;
            }
        }

        /// <summary>
        /// Les 12 définitions officielles (Livre V §21.5 + synergies Thomas RD-086) dans l'ordre de la table.
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
                GetDef(DuoTechId.UppercutThermobarique),
                GetDef(DuoTechId.RempartTellurique),
                GetDef(DuoTechId.ZeroTectonique),
                GetDef(DuoTechId.TriFusion)
            };
        }

        /// <summary>
        /// Issue table (§21.5.2) depuis les deux épreuves de synchro :
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

    }
}
