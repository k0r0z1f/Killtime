using System.Collections.Generic;

namespace Killtime.Core.Character.Classes
{
    /// <summary>
    /// Registre central des archétypes de base (un fichier par classe sous Classes/).
    /// Le créateur pioche ici pour le chargement rapide ; la Voûte Céleste pioche
    /// ici pour le highlight du guide de build (ActiveClassId sur la fiche).
    /// </summary>
    public static class CharacterClassCatalog
    {
        private static List<CharacterClassDefinition> _cached;

        public static List<CharacterClassDefinition> GetAll()
        {
            if (_cached != null) return _cached;
            _cached = new List<CharacterClassDefinition>
            {
                ClasseGuerrierLame.Build(),
                ClasseTireurPrecision.Build(),
                ClasseMageElementaliste.Build(),
                ClasseMoineCinetique.Build(),
                ClasseMedecinChirurgien.Build(),
                ClasseIngenieurArcanotech.Build(),
                ClasseOmbreInfiltratrice.Build(),
                ClasseChefTacticien.Build(),
            };
            return _cached;
        }

        public static CharacterClassDefinition GetById(string classId)
        {
            if (string.IsNullOrWhiteSpace(classId)) return null;
            var all = GetAll();
            for (int i = 0; i < all.Count; i++)
                if (all[i] != null && all[i].ClassId == classId.Trim())
                    return all[i];
            return null;
        }

        /// <summary>Guide actif d'une fiche (null si aucun ou désactivé).</summary>
        public static CharacterClassDefinition GetActiveGuide(CharacterSheet sheet)
        {
            if (sheet == null || !sheet.BuildGuideEnabled) return null;
            if (string.IsNullOrWhiteSpace(sheet.ActiveClassId)) return null;
            return GetById(sheet.ActiveClassId);
        }

        public static void ClearGuide(CharacterSheet sheet)
        {
            if (sheet == null) return;
            sheet.ActiveClassId = "";
            sheet.BuildGuideEnabled = false;
        }
    }
}
