using UnityEngine;

namespace Killtime.Audio
{
    /// <summary>
    /// Canaux de mixage. Mappés vers l'AudioMixer optionnel,
    /// sinon appliqués en volume direct sur les sources.
    /// </summary>
    public enum SoundCategory
    {
        Master = 0,
        Music = 1,
        Ambience = 2,
        SFX = 3,
        UI = 4,
        Cinematic = 5
    }
}
