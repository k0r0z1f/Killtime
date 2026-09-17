using System;
using UnityEngine;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Units;
using Killtime.Core.Character;
using Killtime.Audio;

namespace Killtime.Tactics
{
    public class TacticalInteractable : MonoBehaviour
    {
        [SerializeField] private string _objectName = "Objet Interactif";
        [SerializeField] private string _actionLabel = "Examiner";
        [SerializeField] private int _interactionRadius = 1;
        [SerializeField] private HexCoordinates _gridCoords;
        [SerializeField] private SkillType _requiredSkill = SkillType.IngenierieArcanotech;
        [SerializeField] private int _skillDifficultyThreshold = 0;
        [SerializeField] private bool _isOneShot = false;

        private bool _hasInteracted = false;
        public Action<TacticalUnit> OnInteractionTriggered;

        public HexCoordinates Coordinates => _gridCoords;
        public string ObjectName => _objectName;
        public string ActionLabel => _actionLabel;
        public bool HasInteracted => _hasInteracted;

        public void Configure(string objectName, string actionLabel, HexCoordinates coords, int radius = 1)
        {
            _objectName = objectName;
            _actionLabel = actionLabel;
            _gridCoords = coords;
            _interactionRadius = radius;
        }

        public bool CanInteract(TacticalUnit unit)
        {
            if (_isOneShot && _hasInteracted) return false;
            if (unit == null) return false;
            return unit.CurrentCoords.DistanceTo(_gridCoords) <= _interactionRadius;
        }

        public bool TryInteract(TacticalUnit unit, out string logMessage)
        {
            logMessage = "";
            if (!CanInteract(unit))
            {
                logMessage = $"{unit.Stats.Name} est trop éloigné de {_objectName}.";
                return false;
            }

            if (_skillDifficultyThreshold > 0)
            {
                var die = unit.Stats.GetSkillDie(_requiredSkill, isOffensive: true);
                int mod = unit.Stats.GetSkillModifier(_requiredSkill, isOffensive: true);
                var roller = new Core.Dice.DiceRoller();
                var roll = roller.Roll(die, mod);

                if (roll.Total < _skillDifficultyThreshold)
                {
                    logMessage = $"❌ Échec du test {SkillDefinitions.GetDisplayName(_requiredSkill)} par {unit.Stats.Name} ({roll.Total} vs SD {_skillDifficultyThreshold}).";
                    if (KilltimeAudioManager.Instance != null)
                        KilltimeAudioManager.Instance.PlayUI(SoundId.UI_Denied, 0.7f);
                    return false;
                }

                logMessage = $"✔ Succès {SkillDefinitions.GetDisplayName(_requiredSkill)} validé ({roll.Total} vs SD {_skillDifficultyThreshold}) !";
            }

            if (_isOneShot) _hasInteracted = true;
            OnInteractionTriggered?.Invoke(unit);

            if (KilltimeAudioManager.Instance != null)
                KilltimeAudioManager.Instance.PlayUI(SoundId.UI_Open, 0.8f);

            return true;
        }
    }
}