using System;
using Killtime.UI;

namespace Killtime.Tactics.Arena
{
    /// <summary>
    /// Carte active de l'arène (état pur, sans dépendance Unity).
    /// Register/Clear déplacés depuis CombatDevArena (l.835-847).
    /// L'event OnLoadedMapRegistered RESTE dans la façade (VTT abonné dessus) :
    /// la façade appelle le store puis émet son propre event → API inchangée.
    /// RestoreLoadedMap reste en façade pour cette phase (dépend de
    /// MapEditorDevWindow + LoadUnitsFromMap + audio) et migrera avec
    /// ArenaUnitStore en phase 2.
    /// </summary>
    public sealed class ArenaMapStore
    {
        public TacticalMapSaveData CurrentLoadedMap { get; private set; }
        public string CurrentLoadedMapPath { get; private set; }
        public bool HasLoadedMap => CurrentLoadedMap != null;

        public void Register(TacticalMapSaveData mapData, string mapPath = null)
        {
            CurrentLoadedMap = mapData;
            CurrentLoadedMapPath = mapPath;
        }

        public void Clear()
        {
            CurrentLoadedMap = null;
            CurrentLoadedMapPath = null;
        }
    }
}
