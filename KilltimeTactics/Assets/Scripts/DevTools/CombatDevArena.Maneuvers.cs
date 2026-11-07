using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Units;
using Killtime.Tactics.TurnSystem;
using Killtime.Audio;
using Killtime.Core.Character;
using Killtime.Core.Combat;
using Killtime.Core.Dice;
using Killtime.Core.Chrono;
using Killtime.Core.Inventory;
using Killtime.Core.Rules;
using Killtime.CameraSystem;
using Killtime.WebGL;
using Killtime.UI;
using Killtime.Tactics.Arena;

namespace Killtime.Tactics
{
    // Lutte, bousculade, charge, assaut groupe, poursuite (RD-152).
    public partial class CombatDevArena : MonoBehaviour, IArenaHost
    {
        /// <summary>
        /// RD-039 : Exécute une prise de lutte au corps-à-corps (2 PA).
        /// Duel opposé aveugle Athlétisme vs Athlétisme. Succès = cible Immobilisée et saisie en lutte.
        /// </summary>
        public void ExecuteGrapple(TacticalUnit actor, TacticalUnit target)
        {
            _melee.ExecuteGrapple(actor, target);
        }

        /// <summary>
        /// RD-039 : Tente de se libérer d'une étreinte de lutte (2 PA).
        /// Duel opposé aveugle Athlétisme/MainsNues/Acrobatie (victime) vs Athlétisme (grappler).
        /// Succès = prise brisée et statut Immobilisé dissipé.
        /// </summary>
        public void ExecuteGrappleEscape(TacticalUnit victim, TacticalUnit grappler)
        {
            _melee.ExecuteGrappleEscape(victim, grappler);
        }

        /// <summary>
        /// RD-039 : Étranglement au contact sur cible saisie ou immobilisée (2 PA).
        /// Duel opposé aveugle Athlétisme vs Endurance. Succès = Asphyxie (2 tours, DoT + drain PA).
        /// </summary>
        public void ExecuteStrangulation(TacticalUnit actor, TacticalUnit target)
        {
            _melee.ExecuteStrangulation(actor, target);
        }

        /// <summary>
        /// RD-040 : Exécute une Bousculade générique ou un Coup de bouclier (2 PA).
        /// Duel opposé aveugle de Force (Athlétisme / Mains Nues / Défense Corporelle).
        /// Victoire : recul forcé d'1 case (Knockback). Si un mur ou un obstacle bloque le recul, la cible chute À Terre.
        /// </summary>
        public void ExecuteShove(TacticalUnit attacker, TacticalUnit defender, bool isShieldBash, int attackerBonusAP = 0, int attackerPE = 0)
        {
            _melee.ExecuteShove(attacker, defender, isShieldBash, attackerBonusAP, attackerPE);
        }

        /// <summary>
        /// RD-039 : Vrai si le dragger peut traîner le corps de la victime d'1 case.
        /// </summary>
        public bool CanDragBody(TacticalUnit dragger, TacticalUnit victim)
        {
            return _melee.CanDragBody(dragger, victim);
        }

        /// <summary>
        /// RD-039 : Traîne le corps d'une cible saisie, immobilisée ou inconsciente d'1 case (2 PA).
        /// Le porteur recule ou pivote vers une case libre adjacente et tire la victime
        /// dans la case qu'il vient de libérer.
        /// </summary>
        public bool ExecuteDragBody(TacticalUnit dragger, TacticalUnit victim)
        {
            return _melee.ExecuteDragBody(dragger, victim);
        }

        /// <summary>Refoulement radial : repousse la cible sur le voisin le plus loin du souffle.</summary>
        public bool TryApplyBlastKnockback(TacticalUnit defender, HexCoordinates blastCoords)
        {
            return _melee.TryApplyBlastKnockback(defender, blastCoords);
        }

        /// <summary>
        /// Exécute une manœuvre de Charge (RD-038, Livre V) :
        /// - Déplacement d'au moins 3 cases vers un voisin libre de la cible.
        /// - Débite les PA de déplacement (+ surcoût éventuel de manœuvre).
        /// - À l'arrivée au contact, exécute immédiatement l'attaque de mêlée (+2 dégâts, -1 défense jusqu'au prochain tour personnel).
        /// - Bloque le tir pour le reste du tour en cours.
        /// </summary>
        public void ExecuteChargeAttack(TacticalUnit actor, TacticalUnit target, BodyPart targetedPart, int attackerBonusAP = 0, int attackerPE = 0)
        {
            _melee.ExecuteChargeAttack(actor, target, targetedPart, attackerBonusAP, attackerPE);
        }

        /// <summary>
        /// RD-084 : Collecte les participants d'un assaut groupé : alliés de
        /// l'acteur (même camp) vivants, avec assez de PA, à portée de la cible
        /// pour la compétence partagée (contact ≤ 1 en mêlée, portée d'arme au tir).
        /// </summary>
        public List<TacticalUnit> CollectCombinedAttackers(TacticalUnit actor, TacticalUnit target, SkillType attackSkill)
        {
            return _melee.CollectCombinedAttackers(actor, target, attackSkill);
        }

        /// <summary>
        /// RD-084 : Résout la compétence partagée de l'assaut (mêlée au contact,
        /// Ballistique à distance si l'acteur a une arme à distance).
        /// </summary>
        public bool TryResolveCombinedSkill(TacticalUnit actor, TacticalUnit target, out SkillType attackSkill)
        {
            return _melee.TryResolveCombinedSkill(actor, target, out attackSkill);
        }

        /// <summary>
        /// RD-084 : Exécute un assaut groupé (Livre V §21.2). Les participants
        /// sont synchronisés sur l'initiative du membre le plus lent, leurs
        /// résultats offensifs sommés face à l'unique défense de la cible.
        /// </summary>
        public void ExecuteCombinedAttack(TacticalUnit actor, TacticalUnit target, BodyPart targetedPart = BodyPart.Torse)
        {
            _melee.ExecuteCombinedAttack(actor, target, targetedPart);
        }

        /// <summary>
        /// RD-084 : Exécute un round de course-poursuite (Livre V §21.1) :
        /// concours d'Athlétisme proie vs poursuivants. Succès proie = distance
        /// maintenue (≥ 1 case), faillite = terrain gagné. À 4 faillites cumulées :
        /// arrêt et capture immédiate (Immobilisé).
        /// </summary>
        public void ExecutePursuitRound(TacticalUnit prey, List<TacticalUnit> pursuers)
        {
            _melee.ExecutePursuitRound(prey, pursuers);
        }

        public bool TryApplyKnockback(TacticalUnit attacker, TacticalUnit defender)
        {
            return _melee.TryApplyKnockback(attacker, defender);
        }
    }
}
