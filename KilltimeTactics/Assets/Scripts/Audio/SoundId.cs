namespace Killtime.Audio
{
    /// <summary>
    /// Identifiants stables de tous les sons du jeu.
    /// Le procédural fournit un fallback pour chaque entrée :
    /// le jeu sonne même sans aucun .wav importé.
    /// </summary>
    public enum SoundId
    {
        None = 0,

        // --- UI ---
        UI_Click = 100,
        UI_Hover = 101,
        UI_Open = 102,
        UI_Close = 103,
        UI_Denied = 104,
        UI_Toggle = 105,
        UI_Filter = 106,

        // --- VATS / Ciblage ---
        VATS_Open = 120,
        VATS_Close = 121,
        VATS_TargetChange = 122,
        VATS_Lock = 123,
        VATS_Fire = 124,

        // --- Mouvement / Grille ---
        Move_Footstep = 200,
        Move_Dash = 201,
        Move_Denied = 202,
        Move_PathTick = 203,
        Turn_Start = 210,
        Turn_End = 211,
        Round_Start = 212,

        // --- Combat mêlée / tir ---
        Attack_Whoosh = 300,
        Attack_Impact_Hit = 301,
        Attack_Impact_Crit = 302,
        Attack_Miss = 303,
        Defense_Parry = 304,
        Defense_Dodge = 305,
        Defense_Block = 306,
        Armor_Absorb = 307,
        Trauma_Shock = 308,
        Status_Expired = 309,
        Weapon_Laser_Fire = 310,
        Weapon_Laser_Impact = 311,
        Grenade_Pin = 312,
        Grenade_Throw = 313,
        Grenade_Bounce = 314,
        Grenade_Explosion_Frag = 315,
        Grenade_Explosion_Heavy = 316,
        Grenade_Flash = 317,
        Grenade_Smoke = 318,
        Grenade_Shrapnel = 319,

        // --- Vitalité ---
        Hurt_Light = 320,
        Hurt_Heavy = 321,
        KO_Fall = 322,
        Death_Instant = 323,
        Miracle_Saved = 324,
        LastBreath = 325,
        Heal = 326,
        Breath_Emergency = 327,
        Launcher_Thump = 328,
        Grenade_Gas = 329,

        // --- Dés / PA / Chrono ---
        Dice_Roll = 340,
        PA_Consume = 341,
        PA_Refill = 342,
        Rewind_Time = 343,
        Snapshot_Tick = 344,

        // --- Cinématique ---
        Cinematic_WhooshIn = 360,
        Cinematic_WhooshOut = 361,
        SlowMo_Enter = 362,
        SlowMo_Exit = 363,
        Impact_DeepBoom = 364,

        // --- Spawns / Arène ---
        Spawn_Deploy = 380,
        Arena_Reset = 381,
        Victory_Stinger = 382,
        Defeat_Stinger = 383,

        // --- Nytharite / Psychique (Livre IV, prêt pour la suite) ---
        Spell_Charge = 400,
        Spell_Cast = 401,
        Spell_Fizzle = 402,
        Psychic_Whisper = 403,
    }

    /// <summary>Humeurs musicales adaptatives.</summary>
    public enum MusicMood
    {
        None = 0,
        Explore = 1,
        Combat = 2,
        Tension = 3,
        Victory = 4,
        Defeat = 5,
        CombatBoss = 6
    }

    /// <summary>
    /// Intensité graduée de la musique adaptative.
    /// Calm = nappe seule, Tense = + basse/arpège, Intense = + batterie pleine.
    /// Quantifiée en 3 variantes procédurales (léger sur WebGL, pas de streaming).
    /// </summary>
    public enum MusicIntensity
    {
        Calm = 0,
        Tense = 1,
        Intense = 2
    }
}
