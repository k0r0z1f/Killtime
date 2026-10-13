using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Killtime.Tactics;
using Killtime.Tactics.Grid;

namespace Killtime.UI
{
    /// <summary>
    /// Tout le rendu OnGUI / onglets (extrait sans modification depuis MapEditorDevWindow.cs).
    /// </summary>
    public partial class MapEditorDevWindow
    {
        protected override void DrawContent()
        {

            GUILayout.Space(6);
            int prevTab = _selectedTab;
            _selectedTab = GUILayout.Toolbar(_selectedTab, _tabTitles);
            if (_selectedTab != prevTab)
            {
                if (_selectedTab == 2)
                {
                    _activeBrush = MapBrushType.PlaceProp3D;
                    _statusMessage = "Mode Objets 3D actif : cliquez sur la carte pour poser l'objet sélectionné.";
                }
                else if (_selectedTab == 1)
                {
                    _activeBrush = MapBrushType.Inspect;
                    _statusMessage = "Mode Sols & Relief actif : choisissez une texture ou un outil de sculpture.";
                }
            }
            GUILayout.Space(6);

            if (!string.IsNullOrEmpty(_statusMessage))
            {
                GUI.color = Color.cyan;
                GUILayout.Label($"ℹ️ {_statusMessage}", GUI.skin.box);
                GUI.color = Color.white;
            }

            _scrollPos = GUILayout.BeginScrollView(_scrollPos);

            switch (_selectedTab)
            {
                case 0: DrawTerrainTab(); break;
                case 1: DrawGroundAndSculptTab(); break;
                case 2: DrawProps3DTab(); break;
                case 3: DrawStorageTab(); break;
                case 4: DrawGridSettingsTab(); break;
            }

            GUILayout.EndScrollView();

            if (GUI.changed)
            {
                try { CaptureDevPrefs(); } catch { /* ignore */ }
            }
        }

        private void DrawTerrainTab()
        {
            GUILayout.Label("<b>1. Pinceaux de Sol & Couvertures Géométriques :</b>");
            GUILayout.BeginVertical(GUI.skin.box);

            DrawBrushOption(MapBrushType.Inspect, "🔍 Inspecter", "Affiche l'état complet (sol, couverture, plafond, objets).");
            DrawBrushOption(MapBrushType.ClearGround, "⬛ Sol Dégagé", "Restaure un sol plat et praticable.");
            DrawBrushOption(MapBrushType.HalfCover, "📦 Demi-Couverture", "Muret bas : cible à moitié visible, -1 à l'attaque (Livre VI §25.3).");
            DrawBrushOption(MapBrushType.ThreeQuartersCover, "🧱 Barricade Haute", "Couvert aux 3/4 : seul 1/4 visible, -2 à l'attaque (Livre VI §25.3).");
            DrawBrushOption(MapBrushType.FullCover, "🏛️ Couverture Totale", "Mur / pilier : cible non visible, attaque directe impossible.");
            DrawBrushOption(MapBrushType.ImpassableHole, "🕳️ Gouffre / Vide", "Supprime le passage au sol.");

            GUILayout.EndVertical();

            GUILayout.Space(8);
            GUILayout.Label("<b>2. Gestion des Dalles de Plafond (Transparent / Semi-Transparent) :</b>");
            GUILayout.BeginVertical(GUI.skin.box);

            DrawBrushOption(MapBrushType.AddCeiling, "🏛️ Poser Dalle Plafond", "Place un plafond transparent (semi-transparent en édition).");
            DrawBrushOption(MapBrushType.RemoveCeiling, "🚫 Gomme Plafond", "Supprime le plafond sur les cellules peintes.");

            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("🏛️ Couvrir Toute la Grille"))
            {
                _grid?.SetAllCeilings(true);
                _gridVisualizer?.RefreshObstacles();
                _statusMessage = "Plafond étendu sur l'intégralité de la grille.";
            }
            if (GUILayout.Button("🚫 Retirer Tous les Plafonds"))
            {
                _grid?.SetAllCeilings(false);
                _gridVisualizer?.RefreshObstacles();
                _statusMessage = "Tous les plafonds ont été retirés.";
            }
            GUILayout.EndHorizontal();

            if (_gridVisualizer != null)
            {
                GUILayout.Space(6);
                GUILayout.BeginHorizontal();
                GUILayout.Label($"Opacité Plafond : {_gridVisualizer.CeilingOpacity * 100f:0}%", GUILayout.Width(170));
                _gridVisualizer.CeilingOpacity = GUILayout.HorizontalSlider(_gridVisualizer.CeilingOpacity, 0.05f, 0.80f);
                GUILayout.EndHorizontal();

                _gridVisualizer.ShowCeilingInGame = GUILayout.Toggle(_gridVisualizer.ShowCeilingInGame, "👁️ Afficher le Plafond en Jeu (Semi-Transparent)");
            }

            GUILayout.EndVertical();

            GUILayout.Space(8);
            GUILayout.Label("<b>3. Actions Globales :</b>");
            GUILayout.BeginHorizontal();

            if (GUILayout.Button("🧹 Vider Tous les Obstacles"))
            {
                _grid?.ClearAllCovers();
                ClearAllPlacedProps();
                _arena?.ClearLoadedMap();
                _gridVisualizer?.RefreshObstacles();
                _statusMessage = "Carte entièrement dégagée.";
            }

            if (GUILayout.Button("🔄 Rafraîchir les Visuels"))
            {
                _gridVisualizer?.RefreshObstacles();
                _statusMessage = "Rendu 3D synchronisé.";
            }

            GUILayout.EndHorizontal();

            if (_inspectedNode != null)
            {
                GUILayout.Space(8);
                GUILayout.Label("<b>4. Données de la Case Inspectée :</b>");
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label($"Position : <b>({_inspectedNode.Coordinates.Q}, {_inspectedNode.Coordinates.R})</b>");
                GUILayout.Label($"Couverture : {_inspectedNode.Cover}");
                GUILayout.Label($"Praticable : {_inspectedNode.IsWalkable}");
                GUILayout.Label($"Plafond : {(_inspectedNode.HasCeiling ? "<color=#00E5FF>OUI (Actif)</color>" : "NON")}");
                GUILayout.Label($"Occupé : {_inspectedNode.IsOccupied}");

                var unitOnNode = _arena != null ? _arena.GetUnitAtCoords(_inspectedNode.Coordinates) : null;
                if (unitOnNode != null && unitOnNode.Stats != null)
                {
                    string fColor = unitOnNode.IsPlayerControlled ? "#00E5FF" : "#FF5555";
                    string fName = unitOnNode.IsPlayerControlled ? "Joueur" : "Ennemi";
                    GUILayout.Label($"Avatar : <b>{unitOnNode.Stats.Name}</b> [<color={fColor}>{fName}</color>] (PV: {unitOnNode.Stats.CurrentHealth}/{unitOnNode.Stats.MaxHealth})");
                }

                var inspectedProps = GetPropsAt(_inspectedNode.Coordinates);
                for (int pIdx = 0; pIdx < inspectedProps.Count; pIdx++)
                {
                    var prop = inspectedProps[pIdx];
                    GUILayout.Label($"Objet 3D : <color=#00E5FF>{prop.PrefabName}</color> [{prop.PlacementType}] (Rot: {prop.RotationY:0}°, Échelle: {prop.Scale:0.##})");
                }
                GUILayout.EndVertical();
            }
        }

        private void DrawProps3DTab()
        {
            GUILayout.Label("<b>1. Modèles Disponibles (Assets/Resources/Objects) :</b>");
            GUILayout.BeginVertical(GUI.skin.box);

            GUILayout.BeginHorizontal();
            GUILayout.Label($"Total trouvé(s) : <b>{_availablePropNames.Count}</b>");
            if (GUILayout.Button("🔄 Scanner le Dossier", GUILayout.Width(150)))
            {
                RefreshAvailableProps();
                _statusMessage = $"{_availablePropNames.Count} modèle(s) 3D chargé(s).";
            }
            GUILayout.EndHorizontal();

            if (_availablePropNames.Count == 0)
            {
                GUILayout.Label("<color=#FF6B6B>Aucun préfab dans Assets/Resources/Objects.\nGlissez vos modèles 3D (.prefab) dans ce dossier.</color>");
            }
            else
            {
                _propsScrollPos = GUILayout.BeginScrollView(_propsScrollPos, GUILayout.Height(120));
                for (int i = 0; i < _availablePropNames.Count; i++)
                {
                    bool isSelected = (_selectedPropIndex == i);
                    GUI.backgroundColor = isSelected ? new Color(0.1f, 0.7f, 1f) : Color.white;
                    if (GUILayout.Button($"📦 {_availablePropNames[i]}"))
                    {
                        _selectedPropIndex = i;
                        _activeBrush = MapBrushType.PlaceProp3D;
                        _statusMessage = $"Prêt à poser : {_availablePropNames[i]} [{_propPlacementType}] (Cliquez sur la carte)";
                    }
                    GUI.backgroundColor = Color.white;
                }
                GUILayout.EndScrollView();
            }

            GUILayout.EndVertical();

            if (_availablePropNames.Count > 0)
            {
                string activePropName = _availablePropNames[_selectedPropIndex];

                GUILayout.Space(6);
                GUILayout.Label($"<b>2. Configuration du Placement & Ancrage : <color=#00E5FF>{activePropName}</color></b>");
                GUILayout.BeginVertical(GUI.skin.box);

                GUILayout.BeginHorizontal();
                GUILayout.Label("Type d'Ancrage :", GUILayout.Width(130));
                var prevType = _propPlacementType;
                _propPlacementType = (PropPlacementType)GUILayout.Toolbar((int)_propPlacementType, Enum.GetNames(typeof(PropPlacementType)));
                if (_propPlacementType != prevType)
                {
                    if (_propPlacementType == PropPlacementType.Plafond)
                    {
                        _propIsWalkable = true;
                        _propCover = CoverType.None;
                        _propHeightOffset = 0f;
                    }
                    else if (_propPlacementType == PropPlacementType.Plancher)
                    {
                        _propIsWalkable = false;
                        _propCover = CoverType.Full;
                        _propHeightOffset = 0f;
                    }
                }
                GUILayout.EndHorizontal();

                if (_propPlacementType == PropPlacementType.Plafond)
                {
                    GUI.color = new Color(0.0f, 0.95f, 1.0f);
                    GUILayout.Label("<i>⚓ Ancrage sous plafond actif. Le plafond s'éclaire en semi-transparent. Pose requiert une dalle de plafond.</i>");
                    GUI.color = Color.white;
                }

                GUILayout.BeginHorizontal();
                GUILayout.Label($"Décalage Hauteur (Y) : {_propHeightOffset:0.##}m", GUILayout.Width(170));
                _propHeightOffset = GUILayout.HorizontalSlider(_propHeightOffset, -2.0f, 4.0f);
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label($"Orientation Y : {_propRotationY:0}°", GUILayout.Width(170));
                _propRotationY = GUILayout.HorizontalSlider(_propRotationY, 0f, 360f);
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label("Pivoter (Hex 60°) :", GUILayout.Width(130));
                if (GUILayout.Button("0°")) _propRotationY = 0f;
                if (GUILayout.Button("60°")) _propRotationY = 60f;
                if (GUILayout.Button("120°")) _propRotationY = 120f;
                if (GUILayout.Button("180°")) _propRotationY = 180f;
                if (GUILayout.Button("240°")) _propRotationY = 240f;
                if (GUILayout.Button("300°")) _propRotationY = 300f;
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label($"Échelle : {_propScale:0.##}x", GUILayout.Width(170));
                _propScale = GUILayout.HorizontalSlider(_propScale, 0.2f, 3.0f);
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label("Couverture conférée :", GUILayout.Width(130));
                _propCover = (CoverType)GUILayout.Toolbar((int)_propCover, Enum.GetNames(typeof(CoverType)));
                GUILayout.EndHorizontal();

                _propIsWalkable = GUILayout.Toggle(_propIsWalkable, "Praticable (Les unités peuvent traverser cette case)");

                GUILayout.Space(6);
                GUILayout.BeginHorizontal();

                bool isPlacing = (_activeBrush == MapBrushType.PlaceProp3D);
                GUI.backgroundColor = isPlacing ? new Color(0.2f, 0.85f, 0.4f) : Color.white;
                if (GUILayout.Button(isPlacing ? "🎯 MODE PLACEMENT ACTIF" : "Activer Pinceau de Pose", GUILayout.Height(32)))
                {
                    _activeBrush = MapBrushType.PlaceProp3D;
                }

                bool isErasing = (_activeBrush == MapBrushType.EraseProp3D);
                GUI.backgroundColor = isErasing ? new Color(0.9f, 0.25f, 0.25f) : Color.white;
                if (GUILayout.Button(isErasing ? "🗑️ MODE SUPPRESSION ACTIF" : "Gomme d'Objets 3D", GUILayout.Height(32)))
                {
                    _activeBrush = MapBrushType.EraseProp3D;
                }
                GUI.backgroundColor = Color.white;

                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
            }

            GUILayout.Space(6);
            GUILayout.Label($"<b>3. Objets 3D Déployés ({_placedPropRecords.Count}) :</b>");
            if (_placedPropRecords.Count == 0)
            {
                GUILayout.Label("<i>Aucun objet 3D posé pour l'instant.</i>");
            }
            else
            {
                GUILayout.BeginVertical(GUI.skin.box);
                foreach (var kvp in new Dictionary<string, MapPropData>(_placedPropRecords))
                {
                    string propKey = kvp.Key;
                    var data = kvp.Value;

                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"• <b>{data.PrefabName}</b> en ({data.Q}, {data.R}) [{data.PlacementType} | {data.Cover}]");
                    GUI.backgroundColor = Color.red;
                    if (GUILayout.Button("Suppr.", GUILayout.Width(60)))
                    {
                        RemovePropAt(new HexCoordinates(data.Q, data.R), data.PlacementType);
                    }
                    GUI.backgroundColor = Color.white;
                    GUILayout.EndHorizontal();
                }
                GUILayout.EndVertical();
            }
        }

        private void DrawBrushOption(MapBrushType brush, string title, string description)
        {
            bool isSelected = (_activeBrush == brush);
            GUI.backgroundColor = isSelected ? new Color(0.1f, 0.7f, 1f) : Color.white;

            if (GUILayout.Button($"<b>{title}</b> — <color=#A0B0C0>{description}</color>", GUI.skin.button, GUILayout.Height(28)))
            {
                _activeBrush = brush;
                _statusMessage = $"Pinceau actif : {title}";
            }

            GUI.backgroundColor = Color.white;
        }

        private void DrawStorageTab()
        {
            GUILayout.Label("<b>Sauvegarde & Exportation de la Carte Active :</b>");
            GUILayout.BeginVertical(GUI.skin.box);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Nom de la Carte :", GUILayout.Width(130));
            _mapNameInput = GUILayout.TextField(_mapNameInput);
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            GUI.backgroundColor = new Color(0.2f, 0.7f, 0.4f);
            if (GUILayout.Button("💾 Sauvegarder la Carte (Sol + Plafond + Objets 3D + Avatars)", GUILayout.Height(34)))
            {
                SaveCurrentMap(_mapNameInput);
            }
            GUI.backgroundColor = Color.white;

            GUILayout.EndVertical();

            GUILayout.Space(8);
            GUILayout.Label("<b>Cartes Enregistrées sur le Disque :</b>");

            var files = GetSavedMapFiles();
            if (files.Count == 0)
            {
                GUILayout.Label("<i>Aucune carte enregistrée dans le dossier Maps.</i>");
            }

            foreach (var file in files)
            {
                string fileName = Path.GetFileNameWithoutExtension(file);
                string summary = "";
                try
                {
                    string jsonPreview = File.ReadAllText(file);
                    var previewData = JsonUtility.FromJson<TacticalMapSaveData>(jsonPreview);
                    if (previewData != null)
                    {
                        int pCount = previewData.PlacedProps != null ? previewData.PlacedProps.Count : 0;
                        int uCount = previewData.PlacedUnits != null ? previewData.PlacedUnits.Count : 0;
                        int lxCount = previewData.PlacedPixies != null ? previewData.PlacedPixies.Count : 0;
                        summary = $"<color=#88AACC>[{uCount} PJ/PNJ | {pCount} Props | {lxCount} Lum.]</color>";
                    }
                }
                catch {}

                GUILayout.BeginHorizontal(GUI.skin.box);
                GUILayout.Label($"<b>{fileName}</b> {summary}", GUILayout.Width(280));

                GUI.backgroundColor = Color.cyan;
                if (GUILayout.Button("Charger", GUILayout.Width(75)))
                {
                    LoadMap(file);
                }

                GUI.backgroundColor = Color.red;
                if (GUILayout.Button("Suppr.", GUILayout.Width(60)))
                {
                    DeleteMap(file);
                }
                GUI.backgroundColor = Color.white;

                GUILayout.EndHorizontal();
            }
        }

        private void DrawGridSettingsTab()
        {
            GUILayout.Label("<b>Configuration de la Grille & Hauteur de Plafond :</b>");
            GUILayout.BeginVertical(GUI.skin.box);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Rayon de la Grille (HexRadius) :", GUILayout.Width(210));
            _gridRadiusInput = GUILayout.TextField(_gridRadiusInput, GUILayout.Width(60));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label($"Hauteur du Plafond : {_ceilingHeightInput:0.##}m", GUILayout.Width(210));
            _ceilingHeightInput = GUILayout.HorizontalSlider(_ceilingHeightInput, 2.0f, 8.0f);
            if (_grid != null && Mathf.Abs(_grid.CeilingHeight - _ceilingHeightInput) > 0.01f)
            {
                _grid.CeilingHeight = _ceilingHeightInput;
                _gridVisualizer?.RefreshObstacles();
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            if (GUILayout.Button("📐 Régénérer la Grille avec ce Rayon", GUILayout.Height(32)))
            {
                if (int.TryParse(_gridRadiusInput, out int newRadius) && newRadius >= 3 && newRadius <= 25)
                {
                    RegenerateGridWithRadius(newRadius);
                }
                else
                {
                    _statusMessage = "Rayon invalide (doit être entre 3 et 25).";
                }
            }

            GUILayout.EndVertical();
        }

        private void DrawGroundAndSculptTab()
        {
            GUILayout.Label("<b>1. Textures de Sol (Assets/Resources/Grounds) :</b>");
            GUILayout.BeginVertical(GUI.skin.box);

            GUILayout.BeginHorizontal();
            GUILayout.Label($"Textures détectées : <b>{_availableGroundTextures.Count}</b>");
            if (GUILayout.Button("🔄 Scanner Grounds", GUILayout.Width(125)))
            {
                RefreshAvailableGroundTextures();
                _statusMessage = $"{_availableGroundTextures.Count} texture(s) trouvée(s) sous Assets/Resources/Grounds.";
            }
            if (GUILayout.Button("🛡️ Anti-Grésillement (Mipmaps)", GUILayout.Width(180)))
            {
                if (Application.isPlaying)
                {
                    _statusMessage = "Arrêtez le mode Play dans Unity pour générer les Mipmaps sur disque.";
                }
                else
                {
#if UNITY_EDITOR
                    HexGridVisualizer.BatchFixAllGroundTextureAssets();
                    RefreshAvailableGroundTextures();
                    _gridVisualizer?.RefreshObstacles();
                    _statusMessage = "Mipmaps et Anisotropie 16x appliqués sur le disque.";
#else
                    _statusMessage = "Cette commande de réimportation est réservée à l'Éditeur Unity.";
#endif
                }
            }
            GUILayout.EndHorizontal();

            if (_groundCategories.Count > 1)
            {
                GUILayout.Space(4);
                _selectedCategoryIndex = GUILayout.Toolbar(_selectedCategoryIndex, _groundCategories.ToArray());
            }

            string activeCat = (_selectedCategoryIndex >= 0 && _selectedCategoryIndex < _groundCategories.Count) ? _groundCategories[_selectedCategoryIndex] : "Tous";

            List<GroundTextureRecord> filteredTextures = new();
            for (int i = 0; i < _availableGroundTextures.Count; i++)
            {
                if (activeCat == "Tous" || _availableGroundTextures[i].Category.Equals(activeCat, StringComparison.OrdinalIgnoreCase))
                {
                    filteredTextures.Add(_availableGroundTextures[i]);
                }
            }

            if (filteredTextures.Count == 0)
            {
                GUILayout.Label("<i>Aucune texture trouvée dans cette catégorie.</i>");
            }
            else
            {
                GUILayout.Space(6);
                _groundTexturesScrollPos = GUILayout.BeginScrollView(_groundTexturesScrollPos, GUILayout.Height(135));

                int cols = 4;
                for (int i = 0; i < filteredTextures.Count; i += cols)
                {
                    GUILayout.BeginHorizontal();
                    for (int c = 0; c < cols; c++)
                    {
                        int index = i + c;
                        if (index < filteredTextures.Count)
                        {
                            var rec = filteredTextures[index];
                            bool isSelected = (_availableGroundTextures.Count > _selectedGroundIndex && _availableGroundTextures[_selectedGroundIndex].RelativePath == rec.RelativePath);

                            GUI.backgroundColor = isSelected ? new Color(0.0f, 0.85f, 1.0f) : Color.white;
                            GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(110), GUILayout.Height(105));

                            if (rec.Texture != null)
                            {
                                Rect previewRect = GUILayoutUtility.GetRect(100, 70);
                                GUI.DrawTexture(previewRect, rec.Texture, ScaleMode.ScaleToFit);
                            }

                            if (GUILayout.Button(rec.DisplayName, GUILayout.Height(22)))
                            {
                                _selectedGroundIndex = _availableGroundTextures.IndexOf(rec);
                                _activeBrush = MapBrushType.PaintGroundTexture;
                                _statusMessage = $"Pinceau Texture Actif : {rec.RelativePath}";
                            }
                            GUILayout.EndVertical();
                            GUI.backgroundColor = Color.white;
                        }
                    }
                    GUILayout.EndHorizontal();
                }

                GUILayout.EndScrollView();
            }

            if (_availableGroundTextures.Count > 0 && _selectedGroundIndex < _availableGroundTextures.Count)
            {
                var activeRec = _availableGroundTextures[_selectedGroundIndex];
                GUILayout.Space(6);
                GUILayout.Label($"Texture Active : <b><color=#00E5FF>{activeRec.RelativePath}</color></b>");

                GUILayout.BeginHorizontal();
                bool isPainting = (_activeBrush == MapBrushType.PaintGroundTexture);
                GUI.backgroundColor = isPainting ? new Color(0.1f, 0.7f, 1f) : Color.white;
                if (GUILayout.Button("🖌️ Peindre cette Texture (Clic Carte)", GUILayout.Height(30)))
                {
                    _activeBrush = MapBrushType.PaintGroundTexture;
                }

                bool isClearing = (_activeBrush == MapBrushType.ClearGroundTexture);
                GUI.backgroundColor = isClearing ? new Color(0.9f, 0.3f, 0.3f) : Color.white;
                if (GUILayout.Button("🧹 Gomme Texture", GUILayout.Height(30)))
                {
                    _activeBrush = MapBrushType.ClearGroundTexture;
                }
                GUI.backgroundColor = Color.white;
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("🪣 Remplir Toute la Grille"))
                {
                    if (_grid != null)
                    {
                        foreach (var node in _grid.Nodes.Values)
                        {
                            node.GroundTexture = activeRec.RelativePath;
                        }
                        _gridVisualizer?.RefreshObstacles();
                        _statusMessage = $"Texture '{activeRec.RelativePath}' appliquée à l'ensemble de la grille.";
                    }
                }
                if (GUILayout.Button("🧹 Effacer Toutes les Textures"))
                {
                    if (_grid != null)
                    {
                        foreach (var node in _grid.Nodes.Values)
                        {
                            node.GroundTexture = "";
                        }
                        _gridVisualizer?.RefreshObstacles();
                        _statusMessage = "Toutes les textures ont été retirées.";
                    }
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Tiling UV Texture : {_groundTiling:0.##}x", GUILayout.Width(170));
            float newTiling = GUILayout.HorizontalSlider(_groundTiling, 0.25f, 4.0f);
            if (Mathf.Abs(newTiling - _groundTiling) > 0.01f)
            {
                _groundTiling = newTiling;
                if (_gridVisualizer != null) _gridVisualizer.GroundTiling = _groundTiling;
            }
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();

            GUILayout.Space(8);
            GUILayout.Label("<b>2. Sculpture du Relief (Altitude & Tranchées) :</b>");
            GUILayout.BeginVertical(GUI.skin.box);

            GUILayout.BeginHorizontal();
            DrawBrushOption(MapBrushType.ElevateGround, "▲ Surélever (+)", $"+{_sculptStep:0.##}m par clic");
            DrawBrushOption(MapBrushType.LowerGround, "▼ Creuser (-)", $"-{_sculptStep:0.##}m par clic");
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            DrawBrushOption(MapBrushType.SetElevation, "▬ Niveau Fixe", $"Hauteur = {_targetElevation:0.##}m");
            DrawBrushOption(MapBrushType.SmoothElevation, "≈ Lisser Relief", "Moyenne avec voisins");
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Pas d'Élévation : <b>{_sculptStep:0.##}m</b>", GUILayout.Width(170));
            _sculptStep = GUILayout.HorizontalSlider(_sculptStep, 0.10f, 1.50f);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label($"Hauteur Fixe Cible : <b>{_targetElevation:0.##}m</b>", GUILayout.Width(170));
            _targetElevation = GUILayout.HorizontalSlider(_targetElevation, -3.0f, 6.0f);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label($"Rayon Pinceau : <b>{_sculptRadius} case(s)</b>", GUILayout.Width(170));
            _sculptRadius = (int)GUILayout.HorizontalSlider(_sculptRadius, 1, 4);
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            bool newSmooth = GUILayout.Toggle(_smoothSlopeTerrain, "🌊 Mode Pentes Lisses Continues (Sinon Marches / Terrasses)");
            if (newSmooth != _smoothSlopeTerrain)
            {
                _smoothSlopeTerrain = newSmooth;
                if (_gridVisualizer != null) _gridVisualizer.SmoothSlopeTerrain = _smoothSlopeTerrain;
            }

            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Aplanir Tout le Sol (Y = 0)"))
            {
                if (_grid != null)
                {
                    foreach (var node in _grid.Nodes.Values)
                    {
                        node.Elevation = 0f;
                        node.WorldPosition = node.Coordinates.ToWorldPosition(_grid.HexRadius, 0.05f);
                    }
                    _gridVisualizer?.RefreshObstacles();
                    _statusMessage = "Tout le terrain a été aplani à Y = 0.";
                }
            }

            if (GUILayout.Button("🌋 Cratère Central"))
            {
                GenerateTerrainPreset(isCrater: true);
            }

            if (GUILayout.Button("⛰️ Colline Centrale"))
            {
                GenerateTerrainPreset(isCrater: false);
            }
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
        }
    }
}
