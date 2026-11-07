using System;

namespace Killtime.Core.Arcanotech
{
    public enum PowerCategory
    {
        Offensif,
        Defensif,
        Utilitaire,
        Social
    }

    public enum ArcanotechActionType
    {
        ActionPrincipale,
        Reaction,
        ActionLibre
    }

    public enum ArcanotechModuleId
    {
        // Offensifs (Livre IV §19.2)
        Offensive_TestBonus,
        Offensive_DirectDamage,
        Offensive_DirectAbsoluteDamage,
        Offensive_DrainAP,
        Offensive_Debalance,
        Offensive_Knockdown,
        Offensive_ResidualDamage_Tier1,
        Offensive_Destabilise,
        Offensive_Immobilise,
        Offensive_AbsoluteDamageArea,
        Offensive_Ralenti,
        Offensive_ResidualDamage_Tier2,
        Offensive_Paralyse,
        Offensive_Sonne,
        Offensive_ResidualDamage_Tier3,

        // Défensifs & Utilitaires (Livre IV §19.2)
        Defensive_TestBonus,
        Defensive_ResidualResistance,
        Utility_Rapide,
        Utility_Survolte,
        Utility_EnTranse,
        Utility_EnVol,
        Utility_Accelere,
        Utility_Levitation
    }
}
