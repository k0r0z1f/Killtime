using System;
using Killtime.Core.Character;
using Killtime.Story.Data;

namespace Killtime.Story.Scenes
{
    /// <summary>
    /// Service pur d'identification des acteurs héroïques (sans état Unity).
    /// Logique extraite de JsonStorySceneController.Spawn (move-only) :
    /// détection sans fichier disque via CharacterSheetFileName / DisplayName /
    /// ModelPrefabName / fiche embarquée. Aucun accès à la scène.
    /// </summary>
    public static class SceneActorService
    {
        /// <summary>
        /// Détecte l'acteur John (Passeur du Creuset) sans fichier disque :
        /// couvre "john.json", DisplayName "John" et modèles "John" / "Operative_John".
        /// </summary>
        public static bool IsJohnActor(SceneActorSpawnData actorData)
        {
            if (actorData == null) return false;
            if (!string.IsNullOrEmpty(actorData.CharacterSheetFileName)
                && actorData.CharacterSheetFileName.IndexOf("john", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (!string.IsNullOrEmpty(actorData.DisplayName)
                && actorData.DisplayName.IndexOf("john", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (!string.IsNullOrEmpty(actorData.ModelPrefabName)
                && actorData.ModelPrefabName.IndexOf("john", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (actorData.EmbeddedSheet != null && JohnCharacter.IsJohn(actorData.EmbeddedSheet))
                return true;
            return false;
        }

        /// <summary>
        /// Détecte l'actrice Erika de Cleya sans fichier disque :
        /// couvre "erika.json", DisplayName "Erika" et modèles "Erika" / "Cleyan_Erika".
        /// </summary>
        public static bool IsErikaActor(SceneActorSpawnData actorData)
        {
            if (actorData == null) return false;
            if (!string.IsNullOrEmpty(actorData.CharacterSheetFileName)
                && actorData.CharacterSheetFileName.IndexOf("erika", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (!string.IsNullOrEmpty(actorData.DisplayName)
                && actorData.DisplayName.IndexOf("erika", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (!string.IsNullOrEmpty(actorData.ModelPrefabName)
                && actorData.ModelPrefabName.IndexOf("erika", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (actorData.EmbeddedSheet != null && ErikaCharacter.IsErika(actorData.EmbeddedSheet))
                return true;
            return false;
        }

        /// <summary>
        /// Détecte le Commandant Vance sans fichier disque :
        /// couvre "vance.json", DisplayName exact "Vance"/"Commandant Vance"
        /// ("Adjudant de Vance" n'est PAS Vance) et modèles exacts
        /// "Vance" / "Rebel_Commander" / "Commander_Vance".
        /// </summary>
        public static bool IsVanceActor(SceneActorSpawnData actorData)
        {
            if (actorData == null) return false;
            if (!string.IsNullOrEmpty(actorData.CharacterSheetFileName)
                && actorData.CharacterSheetFileName.IndexOf("vance", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (!string.IsNullOrEmpty(actorData.DisplayName)
                && (actorData.DisplayName.Equals("Vance", StringComparison.OrdinalIgnoreCase)
                    || actorData.DisplayName.Equals("Commandant Vance", StringComparison.OrdinalIgnoreCase)))
                return true;
            if (!string.IsNullOrEmpty(actorData.ModelPrefabName)
                && (actorData.ModelPrefabName.Equals("vance", StringComparison.OrdinalIgnoreCase)
                    || actorData.ModelPrefabName.Equals("rebel_commander", StringComparison.OrdinalIgnoreCase)
                    || actorData.ModelPrefabName.Equals("commander_vance", StringComparison.OrdinalIgnoreCase)))
                return true;
            if (actorData.EmbeddedSheet != null && VanceCharacter.IsVance(actorData.EmbeddedSheet))
                return true;
            return false;
        }
    }
}
