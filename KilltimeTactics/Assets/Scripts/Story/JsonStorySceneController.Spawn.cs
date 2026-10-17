using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Killtime.Tactics;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Units;
using Killtime.Tactics.TurnSystem;
using Killtime.Core.Character;
using Killtime.Core.Dice;
using Killtime.Core.Inventory;
using Killtime.CameraSystem;
using Killtime.UI;
using Killtime.Audio;
using Killtime.Story.Data;


namespace Killtime.Story.Scenes
{
    public partial class JsonStorySceneController
    {
        private void SpawnActors()
        {
            State._spawnedActors.Clear();
            State._playerParty.Clear();

            for (int i = 0; i < State._sceneData.Actors.Count; i++)
            {
                var actorData = State._sceneData.Actors[i];
                if (actorData == null) continue;
                if (!actorData.SpawnInitially && !string.IsNullOrEmpty(actorData.SpawnOnNodeId)) continue;

                SpawnSingleActor(actorData);
            }
            SeparateStackedUnits();
        }

        /// <summary>
        /// Détecte l'acteur John (Passeur du Creuset) sans fichier disque :
        /// couvre "john.json", DisplayName "John" et modèles "John" / "Operative_John".
        /// Logique : <see cref="SceneActorService.IsJohnActor"/>.
        /// </summary>
        private static bool IsJohnActor(SceneActorSpawnData actorData) => SceneActorService.IsJohnActor(actorData);

        /// <summary>
        /// Détecte l'actrice Erika de Cleya sans fichier disque :
        /// couvre "erika.json", DisplayName "Erika" et modèles "Erika" / "Cleyan_Erika".
        /// Logique : <see cref="SceneActorService.IsErikaActor"/>.
        /// </summary>
        private static bool IsErikaActor(SceneActorSpawnData actorData) => SceneActorService.IsErikaActor(actorData);

        /// <summary>
        /// Détecte le Commandant Vance sans fichier disque :
        /// couvre "vance.json", DisplayName exact "Vance"/"Commandant Vance"
        /// ("Adjudant de Vance" n'est PAS Vance) et modèles exacts
        /// "Vance" / "Rebel_Commander" / "Commander_Vance".
        /// Logique : <see cref="SceneActorService.IsVanceActor"/>.
        /// </summary>
        private static bool IsVanceActor(SceneActorSpawnData actorData) => SceneActorService.IsVanceActor(actorData);

        public TacticalUnit SpawnSingleActor(SceneActorSpawnData actorData)
        {
            if (actorData == null) return null;
            if (State._spawnedActors.TryGetValue(actorData.ActorId, out var existing) && existing != null)
                return existing;

            CharacterSheet sheet = null;
            string charFolder = Path.Combine(Application.persistentDataPath, "Characters");

            if (!string.IsNullOrEmpty(actorData.CharacterSheetFileName))
            {
                string sheetPath = Path.Combine(charFolder, actorData.CharacterSheetFileName.EndsWith(".json") ? actorData.CharacterSheetFileName : $"{actorData.CharacterSheetFileName}.json");
                if (File.Exists(sheetPath))
                {
                    sheet = CharacterStorageService.LoadCharacter(sheetPath);
                    // Répare les objets hors-catalogue des fiches disque (ex. scène 01 :
                    // "Carabine d'Assaut Résistance") vers leurs équivalents Armurerie/Marché.
                    if (sheet != null && ArmoryCatalog.MigrateLegacyScene01Items(sheet) > 0)
                        CharacterStorageService.SaveCharacter(sheet);
                }
            }

            if (sheet == null && actorData.EmbeddedSheet != null && !string.IsNullOrEmpty(actorData.EmbeddedSheet.Name))
            {
                sheet = JsonUtility.FromJson<CharacterSheet>(JsonUtility.ToJson(actorData.EmbeddedSheet));
            }

            // Fallback héroïque (sans JSON) : les scènes 01/02 référencent "john.json" /
            // "Operative_John" sans fichier disque — on résout la fiche héroïque intégrée
            // (voir JohnCharacter, même pattern que Mina/Lucas/Thomas dans le dev-spawn).
            // Aucun john.json à générer : le code reconnaît John directement.
            if (sheet == null && IsJohnActor(actorData))
            {
                sheet = JohnCharacter.BuildHeroicSheet();
            }

            // Fallback héroïque (sans JSON) : même pattern pour Erika de Cleya
            // ("erika.json" / "Cleyan_Erika" sans fichier disque).
            if (sheet == null && IsErikaActor(actorData))
            {
                sheet = ErikaCharacter.BuildHeroicSheet();
            }

            // Fallback héroïque (sans JSON) : même pattern pour le Commandant Vance
            // ("vance.json" / "Vance" / "Rebel_Commander" / "Commander_Vance" sans fichier disque).
            if (sheet == null && IsVanceActor(actorData))
            {
                sheet = VanceCharacter.BuildHeroicSheet();
            }

            if (sheet == null)
            {
                sheet = new CharacterSheet
                {
                    Name = actorData.DisplayName,
                    BaseAttributes = new Attributes(3, 3, 3, 3, 2, 2, 1, 2, 0),
                    BaseArmor = actorData.BaseArmor,
                    Profile = actorData.IsPlayer ? CharacterProfileType.HerosPJ : CharacterProfileType.PnjNormal,
                    ModelPrefabName = actorData.ModelPrefabName
                };
            }

            // Arme de la carte : clone catalogue si le nom existe (catégorie/prix/poids
            // réels), équivalent officiel si ancien nom hors-catalogue, sinon placeholder.
            // Dotation déséquipée (au joueur de s'équiper) ; ajoutée seulement si la fiche
            // n'a aucune arme en poche — jamais de doublon avec l'inventaire existant.
            if (!string.IsNullOrEmpty(actorData.EquippedWeaponName) && !ArmoryCatalog.SheetHasWeapon(sheet))
            {
                var weapon = ArmoryCatalog.ResolveSceneWeapon(actorData.EquippedWeaponName, actorData.ActorId, equipped: false);
                sheet.AddItem(weapon);
                if (sheet.GetSkill(weapon.AssociatedSkill).TrainingLevel == 0)
                    sheet.GetSkill(weapon.AssociatedSkill).TrainingLevel = 1;
            }

            var coords = new HexCoordinates(actorData.Q, actorData.R);
            var go = new GameObject($"Actor_{actorData.ActorId}");
            var unit = go.AddComponent<TacticalUnit>();
            unit.InitializeFromSheet(sheet, coords, State._grid, actorData.IsPlayer);

            // Spawn refusé + fallback épuisé : pas de fantôme sans position
            // logique (invisible aux contrôles d'occupation, traversable).
            if (!unit.HasLogicalPosition)
            {
                Debug.LogError($"[JsonStorySceneController] Spawn impossible pour '{actorData.ActorId}' vers ({coords.Q}, {coords.R}) : zone saturée — acteur ignoré.");
                CombatHUD.Instance?.AddAdvancedLog($"<color=red>[SCÈNE] Spawn impossible pour '{actorData.DisplayName}' : zone saturée.</color>", LogCategory.MovementAndTurns, "[SCÈNE]", Color.red);
                Destroy(go);
                return null;
            }

            var visual = unit.GetComponent<TacticalUnitVisual>();
            if (visual != null)
            {
                visual.SetColor(actorData.PrimaryColor, actorData.AccentColor);
                visual.SetCombatStance(actorData.StartInCombatStance);
            }

            if (actorData.FacingAngle != 0f)
            {
                unit.transform.rotation = Quaternion.Euler(0f, actorData.FacingAngle, 0f);
            }

            State._spawnedActors[actorData.ActorId] = unit;

            if (actorData.IsPlayer)
            {
                State._playerParty.Add(unit);
            }

            return unit;
        }

        public void SpawnActorsForNode(string nodeId, bool isCombat = false)
        {
            if (State._sceneData == null || State._sceneData.Actors == null || string.IsNullOrEmpty(nodeId)) return;

            var dataNode = State._sceneData.FindNode(nodeId);
            bool isCombatNode = isCombat || (dataNode != null && dataNode.TriggerCombatOnEnter) || (State._turnManager != null && !State._turnManager.IsInExploration);

            for (int i = 0; i < State._sceneData.Actors.Count; i++)
            {
                var actorData = State._sceneData.Actors[i];
                if (actorData == null) continue;
                if (State._spawnedActors.ContainsKey(actorData.ActorId)) continue;
                if (!string.Equals(actorData.SpawnOnNodeId, nodeId, StringComparison.OrdinalIgnoreCase)) continue;

                var unit = SpawnSingleActor(actorData);
                if (unit != null)
                {
                    // Cinématiques liées à l'acteur (spawn différé) : non-bloquantes.
                    FireLinkedCinematics(actorData.CinematicIds);
                    // Cartes son 🔊 : carte instantanée → début et fin ensemble.
                    FireLinkedSounds(actorData.ActorId, SoundTriggerPoint.AtLinkedStart);
                    FireLinkedSounds(actorData.ActorId, SoundTriggerPoint.AtLinkedEnd);
                    EvaluateLinkedAmbiences(actorData.ActorId);
                    State._turnManager?.RegisterUnit(unit);
                    if (!actorData.IsPlayer && isCombatNode)
                    {
                        State._arena?.RegisterHostileUnit(unit);
                        State._arena?.SelectTarget(unit);
                    }
                }
            }
            SeparateStackedUnits();
        }

        /// <summary>
        /// Retrouve la fiche de spawn (carte 👥 Acteur) associée à une unité live.
        /// 1) lookup inverse State._spawnedActors, 2) repli par nom (ActorId / DisplayName).
        /// </summary>
        public SceneActorSpawnData FindActorDataForUnit(TacticalUnit unit)
        {
            if (unit == null || State._sceneData == null || State._sceneData.Actors == null) return null;
            foreach (var kvp in State._spawnedActors)
            {
                if (kvp.Value == unit)
                {
                    var d = State._sceneData.FindActor(kvp.Key);
                    if (d != null) return d;
                    break;
                }
            }
            if (unit.Stats != null && !string.IsNullOrWhiteSpace(unit.Stats.Name))
            {
                string n = unit.Stats.Name.Trim();
                for (int i = 0; i < State._sceneData.Actors.Count; i++)
                {
                    var a = State._sceneData.Actors[i];
                    if (a == null) continue;
                    if (string.Equals(a.ActorId?.Trim(), n, StringComparison.OrdinalIgnoreCase)
                        || (!string.IsNullOrWhiteSpace(a.DisplayName) && string.Equals(a.DisplayName.Trim(), n, StringComparison.OrdinalIgnoreCase)))
                        return a;
                }
            }
            return null;
        }

        /// <summary>
        /// Séparation anti-empilement au spawn : deux avatars vivants ne
        /// partagent jamais une case (données de scène avec mêmes Q,R, fallback
        /// partiel...). Les doublons sont relocalisés vers la case libre la
        /// plus proche, jamais laissés empilés.
        /// </summary>
        private void SeparateStackedUnits()
        {
            if (State._grid == null) return;
            var units = new List<TacticalUnit>(State._spawnedActors.Values);
            for (int i = 0; i < units.Count; i++)
            {
                var u = units[i];
                if (u == null || u.Stats == null || !u.Stats.IsAlive || !u.HasLogicalPosition) continue;
                bool stacked = false;
                for (int j = 0; j < units.Count; j++)
                {
                    if (i == j) continue;
                    var o = units[j];
                    if (o == null || o.Stats == null || !o.Stats.IsAlive || !o.HasLogicalPosition) continue;
                    var oc = o.OccupiedCoords;
                    for (int c = 0; c < oc.Count; c++)
                    {
                        if (u.Occupies(oc[c])) { stacked = true; break; }
                    }
                    if (stacked) break;
                }
                if (!stacked) continue;
                var from = u.CurrentCoords;
                // Les unités déjà relocalisées sont prises en compte via la
                // présence physique (TeleportTo refuse les cases occupées).
                if (State._grid.TryFindNearestFreeFootprint(from, u.FootprintType, out var free, 12, u)
                    && !free.Equals(from)
                    && u.TeleportTo(free, State._grid))
                {
                    string who = u.Stats != null ? u.Stats.Name : u.name;
                    Debug.Log($"[JsonStorySceneController] '{who}' séparé : ({from.Q}, {from.R}) ➔ ({free.Q}, {free.R}).");
                    CombatHUD.Instance?.AddAdvancedLog($"🔀 <b>{who}</b> replacé sur case libre ({free.Q}, {free.R}).", LogCategory.MovementAndTurns, "[SCÈNE]", Color.gray);
                }
                else
                {
                    Debug.LogWarning($"[JsonStorySceneController] Empilement non résolu en ({from.Q}, {from.R}) : aucune case libre à proximité.");
                }
            }
        }

        private void SpawnInteractables()
        {
            for (int i = 0; i < State._interactables.Count; i++)
            {
                if (State._interactables[i] != null) Destroy(State._interactables[i].gameObject);
            }
            State._interactables.Clear();

            for (int i = 0; i < State._sceneData.Interactables.Count; i++)
            {
                var data = State._sceneData.Interactables[i];
                if (data == null) continue;

                var go = new GameObject($"Interactable_{data.InteractableId}");
                var interactable = go.AddComponent<TacticalInteractable>();
                interactable.Configure(data.DisplayName, data.ActionLabel, new HexCoordinates(data.Q, data.R), data.Radius,
                    data.RequiredSkill, data.SkillThreshold, data.IsOneShot, data.InteractableId);

                // Legacy : l'interaction directe reste disponible pour les tests et
                // les appels non-routés par la fenêtre flottante générique.
                interactable.OnInteractionTriggered += (unit) =>
                {
                    HandleInteractableResolvedLegacy(unit, data);
                };

                State._interactables.Add(interactable);
            }
        }
    }
}
