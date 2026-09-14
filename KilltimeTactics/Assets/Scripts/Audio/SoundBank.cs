using System.Collections.Generic;
using UnityEngine;

namespace Killtime.Audio
{
    /// <summary>
    /// Banque de sons adressable. Optionnelle : le manager fonctionne sans
    /// (100% procédural). Assignez vos .wav ici pour overrider au cas par cas.
    /// CreateAssetMenu : Killtime/Audio/SoundBank
    /// </summary>
    [CreateAssetMenu(fileName = "KilltimeSoundBank", menuName = "Killtime/Audio/SoundBank")]
    public class SoundBank : ScriptableObject
    {
        public List<SoundDefinition> Sounds = new();

        private Dictionary<SoundId, SoundDefinition> _lookup;

        public SoundDefinition Get(SoundId id)
        {
            if (_lookup == null) RebuildLookup();
            return _lookup.TryGetValue(id, out var def) ? def : null;
        }

        public void RebuildLookup()
        {
            _lookup = new Dictionary<SoundId, SoundDefinition>();
            if (Sounds == null) return;
            foreach (var s in Sounds)
            {
                if (s == null) continue;
                if (!_lookup.ContainsKey(s.Id))
                    _lookup.Add(s.Id, s);
            }
        }

        private void OnValidate()
        {
            RebuildLookup();
        }

        /// <summary>Pré-remplit la banque avec les 40+ ids et réglages par défaut.</summary>
        [ContextMenu("Remplir avec défauts")]
        public void FillWithDefaults()
        {
            Sounds.Clear();
            foreach (SoundId id in System.Enum.GetValues(typeof(SoundId)))
            {
                if (id == SoundId.None) continue;
                Sounds.Add(KilltimeAudioManager.CreateDefaultDefinition(id));
            }
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }
    }
}
