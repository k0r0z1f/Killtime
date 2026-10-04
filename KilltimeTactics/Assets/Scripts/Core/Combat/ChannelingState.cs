using System.Collections.Generic;
using Killtime.Core.Character;
using Killtime.Core.Rules;

namespace Killtime.Core.Combat
{
    /// <summary>
    /// Canalisation / Action en Progression (RD-048, Livre VI §24.3) :
    /// un combattant étale son effort sur plusieurs temps (ajuster un tir
    /// dévastateur, canaliser un sort de la 5e Force) pour frapper plus fort.
    ///
    /// - Entrée pendant son propre tour (coût config, 1 PA par défaut) avec un
    ///   libellé ("Coup préparé", nom du sort...). La canalisation persiste
    ///   jusqu'à exécution, annulation ou interruption.
    /// - Tant qu'il canalise, le combattant défend avec une punition de
    ///   progression (-1 par défaut, CoreRulesConfig.ChannelDefensePenalty).
    /// - Quand il attaque en canalisant, la canalisation est consommée et
    ///   confère +2 au jet (config ChannelAttackBonus, comme la Préparation).
    /// - S'il subit des dégâts nets supérieurs à son encaissement pendant sa
    ///   progression, la canalisation est INTERROMPUE (perdue, sans bonus).
    /// - Rompue aussi si neutralisé (étourdi/paralysé/sonné/inconscient/mort).
    ///
    /// Keyée sur CharacterStats (couche Core, joueur + IA, sans Unity).
    /// </summary>
    public static class ChannelingState
    {
        private sealed class Channel
        {
            public CharacterStats Caster;
            public string Label;
        }

        private static readonly Dictionary<CharacterStats, Channel> _channels = new();

        /// <summary>Vrai si la canalisation est invalidée par les statuts
        /// (même assiette que le guet RD-030 + mort).</summary>
        public static bool IsCancelledByStatus(CharacterStats caster)
        {
            if (caster == null) return true;
            if (!caster.IsAlive) return true;
            var fx = caster.ActiveStatus;
            if (fx.HasFlag(StatusEffect.Etourdi)) return true;
            if (fx.HasFlag(StatusEffect.Paralyse)) return true;
            if (fx.HasFlag(StatusEffect.Sonne)) return true;
            if (fx.HasFlag(StatusEffect.Inconscient)) return true;
            return false;
        }

        /// <summary>Vrai si le lanceur canalise actuellement (et valide).</summary>
        public static bool IsChanneling(CharacterStats caster)
        {
            if (caster == null) return false;
            if (!_channels.TryGetValue(caster, out var c) || c == null) return false;
            if (IsCancelledByStatus(caster)) { _channels.Remove(caster); return false; }
            return true;
        }

        public static bool TryGetLabel(CharacterStats caster, out string label)
        {
            label = null;
            if (!IsChanneling(caster)) return false;
            label = _channels[caster].Label;
            return true;
        }

        /// <summary>
        /// Entre en canalisation : débite le coût et enregistre le libellé.
        /// Refuse si déjà en canalisation, PA insuffisants ou neutralisé.
        /// </summary>
        public static bool TryBeginChannel(CharacterStats caster, string label, int apCost, out string error)
        {
            error = null;
            if (caster == null) { error = "Canalisation impossible : sans lanceur."; return false; }
            if (!caster.IsAlive) { error = $"{caster.Name} est hors de combat : canalisation impossible."; return false; }
            if (IsCancelledByStatus(caster))
            {
                error = $"{caster.Name} est neutralisé (étourdi/paralysé) : canalisation impossible.";
                return false;
            }
            if (_channels.ContainsKey(caster))
            {
                error = $"{caster.Name} canalise déjà « {_channels[caster].Label} » : exécutez ou annulez d'abord.";
                return false;
            }
            if (!caster.ConsumeActionPoints(apCost))
            {
                error = $"{caster.Name} n'a pas assez de PA ({caster.CurrentActionPoints}/{apCost}) pour canaliser !";
                return false;
            }
            _channels[caster] = new Channel
            {
                Caster = caster,
                Label = string.IsNullOrWhiteSpace(label) ? "Coup en progression" : label.Trim()
            };
            return true;
        }

        /// <summary>
        /// Consomme la canalisation pour frapper (bonus +2 au jet). Retourne
        /// vrai si une canalisation était active et a été consommée.
        /// </summary>
        public static bool ConsumeChannelBonus(CharacterStats caster)
        {
            if (caster == null) return false;
            if (!_channels.ContainsKey(caster)) return false;
            if (IsCancelledByStatus(caster)) { _channels.Remove(caster); return false; }
            _channels.Remove(caster);
            return true;
        }

        /// <summary>Annule la canalisation sans frapper (volontaire, sanction).</summary>
        public static void Cancel(CharacterStats caster)
        {
            if (caster == null) return;
            _channels.Remove(caster);
        }

        /// <summary>
        /// Modificateur défensif de progression (-1 si en canalisation, 0 sinon).
        /// Appliqué aux jets de défense du lanceur (BeginDuel / déclarations).
        /// </summary>
        public static int GetDefenseModifier(CharacterStats caster)
        {
            if (!IsChanneling(caster)) return 0;
            var cfg = CoreRulesConfig.Instance;
            return cfg != null ? cfg.ChannelDefensePenalty : -1;
        }

        /// <summary>
        /// Bonus offensif de progression (+2 si en canalisation, 0 sinon).
        /// Lu SANS consommer (la consommation a lieu à la résolution).
        /// </summary>
        public static int PeekAttackBonus(CharacterStats caster)
        {
            if (!IsChanneling(caster)) return 0;
            var cfg = CoreRulesConfig.Instance;
            return cfg != null ? cfg.ChannelAttackBonus : 2;
        }

        /// <summary>
        /// Notifie des dégâts nets subis (après armure/bouclier) : si le lanceur
        /// canalisait et que les dégâts dépassent strictement son encaissement,
        /// la canalisation est INTERROMPUE (retourne vrai + libellé perdu).
        /// La mort ou la neutralisation rompt aussi la canalisation (vrai +
        /// libellé). Sinon la canalisation survit (faux).
        /// </summary>
        public static bool NotifyDamage(CharacterStats caster, int finalDamageApplied, out string interruptedLabel)
        {
            interruptedLabel = null;
            if (caster == null) return false;
            if (!_channels.TryGetValue(caster, out var c) || c == null) return false;
            interruptedLabel = c.Label;
            if (!caster.IsAlive || IsCancelledByStatus(caster))
            {
                _channels.Remove(caster);
                return true;
            }
            if (finalDamageApplied > 0 && finalDamageApplied > caster.EncaissementThreshold)
            {
                _channels.Remove(caster);
                return true;
            }
            return false;
        }

        /// <summary>À appeler au début du tour personnel : purge uniquement les
        /// canalisations invalidées (mort/neutralisé). Une canalisation valide
        /// SURVIT au nouveau tour (effort étalé multi-tours).</summary>
        public static void OnUnitTurnStart(CharacterStats unit)
        {
            if (_channels.Count == 0) return;
            if (unit != null)
            {
                if (_channels.TryGetValue(unit, out var c))
                {
                    if (c == null || !unit.IsAlive || IsCancelledByStatus(unit))
                        _channels.Remove(unit);
                }
            }
            CharacterStats[] keys = new CharacterStats[_channels.Count];
            _channels.Keys.CopyTo(keys, 0);
            for (int i = 0; i < keys.Length; i++)
            {
                var k = keys[i];
                if (k == null || !_channels.TryGetValue(k, out var cc) || cc == null
                    || !k.IsAlive || IsCancelledByStatus(k))
                    _channels.Remove(k);
            }
        }

        /// <summary>Retire une unité qui quitte la carte (canalisation perdue).</summary>
        public static void RemoveUnit(CharacterStats unit)
        {
            if (unit == null) return;
            _channels.Remove(unit);
        }

        public static void ClearAll() => _channels.Clear();
    }
}
