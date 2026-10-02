using System.Collections.Generic;
using Killtime.Core.Character;

namespace Killtime.Core.Combat
{
    /// <summary>
    /// Réaction choisie face à un désengagement adverse (RD-031, Livre VI §24.4) :
    /// quand un ennemi rompt le contact (distance passe de ≤ portée à > portée),
    /// chaque voisin au contact peut dépenser son unique réaction du round pour :
    /// - Strike : frappe gratuite de mêlée (0 PA, duel aveugle auto) — base du hit-and-run ;
    /// - Follow : poursuite — suit d'1 case (coût config, en réaction à son déplacement) ;
    /// - Block : blocage — duel opposé qui, en cas de succès, annule le pas (sans
    ///   grapple permanent ; les PA du fuyard ne sont pas dépensés s'il ne bouge pas) ;
    /// - Trip : balayage — frappe gratuite visant les Jambes (À Terre + Ralenti si choc).
    /// </summary>
    public enum OpportunityReactionType
    {
        Strike = 0,
        Follow = 1,
        Block = 2,
        Trip = 3
    }

    /// <summary>
    /// Attaque d'opportunité / désengagement (RD-031) :
    /// - 1 réaction d'opportunité par round et par combattant (Livre VI §24 : une
    ///   réaction par tour en réponse à une offensive ennemie — ici un désengagement).
    /// - Posture de réaction persistante (choix du joueur/IA pendant son tour).
    /// - Désengagement sûr (Décrochage, Livre VI §26.2) : 1 PA pour que le prochain
    ///   déplacement ne provoque aucune réaction.
    /// - Exemption esquive : les spécialisations d'évitement (Roulade de Décrochage,
    ///   Acrobatie d'Évitement) permettent de quitter le contact sans provoquer.
    ///
    /// Keyé sur CharacterStats (couche Core, sans dépendance Unity), comme OverwatchState.
    /// </summary>
    public static class OpportunityState
    {
        /// <summary>Préfixes de spécialisations d'Esquive qui exemptent de provoquer
        /// (quitter le contact sans offrir de réaction). Comparaison par préfixe car
        /// les branches (ex: "Roulade de Décrochage : Pas Déphasé") héritent de l'esquive.</summary>
        public static readonly string[] DisengageExemptSpecPrefixes =
        {
            "Roulade de Décrochage",
            "Acrobatie d'Évitement"
        };

        private static readonly HashSet<CharacterStats> _reacted = new();
        private static readonly Dictionary<CharacterStats, OpportunityReactionType> _stances = new();
        private static readonly HashSet<CharacterStats> _safeDisengage = new();

        /// <summary>Vrai si la réaction d'opportunité est invalidée par les statuts
        /// (même assiette que le guet RD-030 : étourdi/paralysé/sonné/inconscient/mort).</summary>
        public static bool IsCancelledByStatus(CharacterStats unit)
        {
            if (unit == null) return true;
            if (!unit.IsAlive) return true;
            var fx = unit.ActiveStatus;
            if (fx.HasFlag(StatusEffect.Etourdi)) return true;
            if (fx.HasFlag(StatusEffect.Paralyse)) return true;
            if (fx.HasFlag(StatusEffect.Sonne)) return true;
            if (fx.HasFlag(StatusEffect.Inconscient)) return true;
            return false;
        }

        /// <summary>Vrai si le fuyard quitte le contact sans provoquer (spé esquive).</summary>
        public static bool IsExemptFromProvoking(CharacterStats mover)
        {
            if (mover == null) return false;
            var sheet = mover.Sheet;
            if (sheet == null || sheet.UnlockedSpecializations == null) return false;
            for (int i = 0; i < sheet.UnlockedSpecializations.Count; i++)
            {
                string spec = sheet.UnlockedSpecializations[i];
                if (string.IsNullOrEmpty(spec)) continue;
                for (int p = 0; p < DisengageExemptSpecPrefixes.Length; p++)
                {
                    if (spec.StartsWith(DisengageExemptSpecPrefixes[p]))
                        return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Décide si un déplacement provoque des réactions : faux si spé esquive
        /// (gratuit, sans consommer) ou si un Décrochage a été payé (consommé ici).
        /// À appeler une fois au début du déplacement (TacticalUnit.MoveAlongPath).
        /// </summary>
        public static bool TryBeginProvokingMove(CharacterStats mover)
        {
            if (mover == null) return true;
            if (IsExemptFromProvoking(mover)) return false;
            if (_safeDisengage.Contains(mover))
            {
                _safeDisengage.Remove(mover);
                return false;
            }
            return true;
        }

        /// <summary>Vrai si un Décrochage payé attend d'être consommé par le prochain déplacement.</summary>
        public static bool HasSafeDisengage(CharacterStats mover)
        {
            return mover != null && _safeDisengage.Contains(mover);
        }

        /// <summary>
        /// Paie un Décrochage (Livre VI §26.2 : 1 PA, prochain déplacement sans réaction).
        /// Refuse si déjà armé, PA insuffisants ou statuts invalidants.
        /// </summary>
        public static bool RequestSafeDisengage(CharacterStats mover, int apCost, out string error)
        {
            error = null;
            if (mover == null) { error = "Décrochage impossible : sans combattant."; return false; }
            if (!mover.IsAlive) { error = $"{mover.Name} est hors de combat : décrochage impossible."; return false; }
            if (IsCancelledByStatus(mover))
            {
                error = $"{mover.Name} est neutralisé (étourdi/paralysé) : décrochage impossible.";
                return false;
            }
            if (_safeDisengage.Contains(mover))
            {
                error = $"{mover.Name} a déjà un décrochage armé.";
                return false;
            }
            if (!mover.ConsumeActionPoints(apCost))
            {
                error = $"{mover.Name} n'a pas assez de PA ({mover.CurrentActionPoints}/{apCost}) pour décrocher !";
                return false;
            }
            _safeDisengage.Add(mover);
            return true;
        }

        public static OpportunityReactionType GetStance(CharacterStats unit)
        {
            if (unit != null && _stances.TryGetValue(unit, out var stance))
                return stance;
            return OpportunityReactionType.Strike;
        }

        public static void SetStance(CharacterStats unit, OpportunityReactionType stance)
        {
            if (unit == null) return;
            _stances[unit] = stance;
        }

        public static string StanceLabel(OpportunityReactionType stance)
        {
            return stance switch
            {
                OpportunityReactionType.Follow => "Poursuite",
                OpportunityReactionType.Block => "Blocage",
                OpportunityReactionType.Trip => "Balayage",
                _ => "Frappe"
            };
        }

        /// <summary>Vrai si l'unité a déjà dépensé sa réaction d'opportunité ce round.</summary>
        public static bool HasReacted(CharacterStats unit)
        {
            return unit != null && _reacted.Contains(unit);
        }

        /// <summary>
        /// Vrai si l'unité peut réagir : vivante, non neutralisée, réaction du round
        /// encore disponible. Le coût PA (poursuite/blocage) est vérifié par l'appelant.
        /// </summary>
        public static bool CanReact(CharacterStats unit)
        {
            if (unit == null || !unit.IsAlive) return false;
            if (IsCancelledByStatus(unit)) return false;
            if (_reacted.Contains(unit)) return false;
            return true;
        }

        /// <summary>Consomme l'unique réaction d'opportunité du round. Faux si indisponible.</summary>
        public static bool TryConsumeReaction(CharacterStats unit)
        {
            if (!CanReact(unit)) return false;
            _reacted.Add(unit);
            return true;
        }

        public static void MarkReacted(CharacterStats unit)
        {
            if (unit == null) return;
            _reacted.Add(unit);
        }

        /// <summary>
        /// Annule une réaction consommée par erreur (ex : PA insuffisants révélés
        /// après coup par Ralenti/armure). Ne pas utiliser pour "rendre" une réaction jouée.
        /// </summary>
        public static void CancelReaction(CharacterStats unit)
        {
            if (unit == null) return;
            _reacted.Remove(unit);
        }

        /// <summary>À appeler au début du tour personnel : la réaction se recharge
        /// (1/round) ; le décrochage armé non consommé expire ; purge les invalides.</summary>
        public static void OnUnitTurnStart(CharacterStats unit)
        {
            if (unit != null)
            {
                _reacted.Remove(unit);
                _safeDisengage.Remove(unit);
            }
            if (_reacted.Count == 0 && _safeDisengage.Count == 0 && _stances.Count == 0) return;
            if (_reacted.Count > 0)
            {
                CharacterStats[] keys = new CharacterStats[_reacted.Count];
                _reacted.CopyTo(keys, 0);
                for (int i = 0; i < keys.Length; i++)
                {
                    var k = keys[i];
                    if (k == null || !k.IsAlive || IsCancelledByStatus(k))
                        _reacted.Remove(k);
                }
            }
            if (_safeDisengage.Count > 0)
            {
                CharacterStats[] keys = new CharacterStats[_safeDisengage.Count];
                _safeDisengage.CopyTo(keys, 0);
                for (int i = 0; i < keys.Length; i++)
                {
                    var k = keys[i];
                    if (k == null || !k.IsAlive || IsCancelledByStatus(k))
                        _safeDisengage.Remove(k);
                }
            }
        }

        /// <summary>Retire une unité qui quitte la carte (plus aucune réaction/posture).</summary>
        public static void RemoveUnit(CharacterStats unit)
        {
            if (unit == null) return;
            _reacted.Remove(unit);
            _stances.Remove(unit);
            _safeDisengage.Remove(unit);
        }

        public static void ClearAll()
        {
            _reacted.Clear();
            _stances.Clear();
            _safeDisengage.Clear();
        }
    }
}
