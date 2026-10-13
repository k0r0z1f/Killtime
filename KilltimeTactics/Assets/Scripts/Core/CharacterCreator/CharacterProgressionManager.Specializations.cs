using System;
using System.Collections.Generic;
using Killtime.Core.Arcanotech;

namespace Killtime.Core.Character
{
    public partial class CharacterProgressionManager
    {
        // CharacterProgressionManager.Specializations.cs — Orchestrateur : EnsureRegistryBuilt delegue aux 4 catalogues.

        public static void EnsureRegistryBuilt()
        {
            if (_registryBuilt) return;
            _registryBuilt = true;

            RegisterMeleeCatalog();
            RegisterMobilityCatalog();
            RegisterSupportCatalog();
            RegisterMagicCatalog();
        }
    }
}
