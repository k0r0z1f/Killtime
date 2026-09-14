using System;
using System.Collections.Generic;
using UnityEngine;

namespace Killtime.Audio
{
    /// <summary>
    /// Définition unitaire : clips réels (override) + réglages.
    /// Si Clips est vide, le ProceduralAudioFactory prend le relais (hybride).
    /// </summary>
    [Serializable]
    public class SoundDefinition
    {
        public SoundId Id = SoundId.UI_Click;
        public SoundCategory Category = SoundCategory.SFX;

        [Tooltip("Clips réels importés (.wav/.ogg). Si vide => synthèse procédurale.")]
        public List<AudioClip> Clips = new();

        [Range(0f, 1.5f)] public float Volume = 0.85f;
        [Range(0.5f, 2f)] public float PitchMin = 0.94f;
        [Range(0.5f, 2f)] public float PitchMax = 1.06f;
        [Tooltip("Anti-spam en secondes.")]
        [Range(0f, 1f)] public float Cooldown = 0.06f;
        [Range(0, 255)] public int Priority = 128;
        public bool Loop = false;

        [Tooltip("Spatialisation 3D (0 = 2D, 1 = 3D).")]
        [Range(0f, 1f)] public float SpatialBlend = 0f;
        [Tooltip("Distance max 3D.")]
        public float MaxDistance = 28f;
    }
}
