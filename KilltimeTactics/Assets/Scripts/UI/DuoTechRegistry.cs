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
        UppercutThermobarique,
        RempartTellurique,
        ZeroTectonique,
        TriFusion
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
        public int ThomasCost;

        public int TotalCost => DeclareCost + MinaCost + LucasCost + ThomasCost;

        // Table Livre VI §25.5 : ND de synchro + binôme de caracs testées
        // (1er = rôle cinétique/physique Mina, 2nd = rôle calcul Lucas).
        public int SyncND = 5;
        public string SyncAttrs = "AGI+INT";
        public string Condition = "";
    }

    /// <summary>
    /// Façade : les types vivent ici, le contenu vit dans les partiels
    /// (Defs, Roles, Classics, Extended, Menu) — extrait sans modification.
    /// </summary>
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
    public static partial class DuoTechRegistry
    {
    }
}
