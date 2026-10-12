using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Animations;
using Killtime.Core.Character;
using Killtime.Core.Character.Classes;
using Killtime.Core.Arcanotech;
using Killtime.Core.Inventory;
using Killtime.DevTools.Character;
using Killtime.Tactics;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Units;
using Killtime.Tactics.TurnSystem;

namespace Killtime.UI
{
    public partial class CharacterDevWindow
    {
        // ================= TAB 4 : SPAWN SUR LA GRILLE =================
        private void DrawMapInsertionTab()
        {
            GUILayout.Label("<b>Insérer le Personnage sur la Grille Hexagonale 3D :</b>");
            GUILayout.BeginVertical(GUI.skin.box);

            GUILayout.Label($"Personnage actif : <b>{_currentSheet.Name}</b> ({_currentSheet.Species})");
            string appliedModel = string.IsNullOrEmpty(_currentSheet.ModelPrefabName) ? "Avatar Procédural" : _currentSheet.ModelPrefabName;
            GUILayout.Label($"Modèle 3D appliqué : <b><color=#00E5FF>{appliedModel}</color></b>");
            string footprintColor = _currentSheet.Footprint != TitanFootprintType.Single ? "#FF3B5C" : "#00E5FF";
            GUILayout.Label($"Gabarit tactique : <b><color={footprintColor}>{_currentSheet.Footprint}</color></b>");

            GUILayout.BeginHorizontal();
            GUILayout.Label("Coordonnée Hexagonale Q :", GUILayout.Width(180));
            _spawnQStr = GUILayout.TextField(_spawnQStr, GUILayout.Width(60));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Coordonnée Hexagonale R :", GUILayout.Width(180));
            _spawnRStr = GUILayout.TextField(_spawnRStr, GUILayout.Width(60));
            GUILayout.EndHorizontal();

            _spawnAsPlayer = GUILayout.Toggle(_spawnAsPlayer, "Contrôlé par le Joueur (Bleu) / Sinon Ennemi (Rouge)");

            GUILayout.Space(10);
            GUI.backgroundColor = new Color(0.2f, 0.6f, 0.9f);
            if (GUILayout.Button("⚡ INSERER L'UNITÉ SUR LA GRILLE", GUILayout.Height(38)))
            {
                SpawnCharacterOnGrid();
            }
            GUI.backgroundColor = Color.white;

            GUILayout.EndVertical();
        }
        private void DrawRelationshipsTab()
        {
            if (_currentSheet == null) return;

            _social.EnsureLoaded(_currentSheet);

            GUILayout.BeginHorizontal(GUI.skin.box);
            GUILayout.Label($"Matrice Sociale de : <b>{_currentSheet.Name}</b> (ID: {_currentSheet.SheetId[..Math.Min(8, _currentSheet.SheetId.Length)]}) | Liens actifs : <b>{_currentSocialGraph.Relationships.Count}</b>", GUILayout.ExpandWidth(true));
            GUI.backgroundColor = new Color(0.2f, 0.75f, 0.4f);
            if (GUILayout.Button("💾 Sauvegarder Relations", GUILayout.Width(180), GUILayout.Height(24)))
            {
                _statusMessage = _social.Save(_currentSheet);
            }
            GUI.backgroundColor = Color.white;
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            GUILayout.Label("<b>1. Introduire un Nouveau Personnage dans ce Graphe :</b>");
            GUILayout.BeginVertical(GUI.skin.box);

            var candidates = _social.CollectCandidates(_currentSheet);

            if (candidates.Count == 0)
            {
                GUILayout.Label("<color=gray><i>Aucun autre personnage sauvegardé sur le disque pour établir un lien.</i></color>");
            }
            else
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Rencontrer :", GUILayout.Width(90));
                for (int i = 0; i < candidates.Count; i++)
                {
                    var c = candidates[i];
                    bool alreadyLinked = _currentSocialGraph.GetRelationship(c.SheetId) != null;
                    GUI.enabled = !alreadyLinked;
                    if (GUILayout.Button($"{c.Name} {(alreadyLinked ? "(Lié)" : "+")}", GUILayout.Height(22)))
                    {
                        _statusMessage = _social.Introduce(_currentSheet, c);
                    }
                    GUI.enabled = true;
                }
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label("Connu depuis :", GUILayout.Width(90));
                _newRelationKnownSince = GUILayout.TextField(_newRelationKnownSince, GUILayout.Width(180));
                GUILayout.Label("Lieu :", GUILayout.Width(45));
                _newRelationLocation = GUILayout.TextField(_newRelationLocation, GUILayout.ExpandWidth(true));
                GUILayout.EndHorizontal();
            }
            GUILayout.EndVertical();

            GUILayout.Space(8);
            GUILayout.Label($"<b>2. Registre des Relations Déclarées ({_currentSocialGraph.Relationships.Count}) :</b>");

            _socialScroll = GUILayout.BeginScrollView(_socialScroll, GUILayout.Height(340));
            if (_currentSocialGraph.Relationships.Count == 0)
            {
                GUILayout.Label("<color=gray><i>Aucune entrée relationnelle enregistrée pour ce protagoniste.</i></color>");
            }

            for (int i = 0; i < _currentSocialGraph.Relationships.Count; i++)
            {
                var rel = _currentSocialGraph.Relationships[i];
                if (rel == null) continue;

                if (DrawRelationshipCard(rel, i)) break;
            }
            GUILayout.EndScrollView();
        }

        private bool DrawRelationshipCard(CharacterRelationshipEntry rel, int index)
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.BeginHorizontal();

            string linkGlyph = rel.LinkType switch
            {
                RelationshipLinkType.SymbioteLigneZero => "🧬",
                RelationshipLinkType.FraterniteDarmes => "🛡️",
                RelationshipLinkType.HierarchieMilitaire => "🎖️",
                RelationshipLinkType.DetteDhonneur => "⚖️",
                RelationshipLinkType.RivaliteMartiale => "⚔️",
                RelationshipLinkType.MefianceInstinctive => "👁️",
                RelationshipLinkType.HostiliteDeclaree => "💀",
                RelationshipLinkType.MentorEleve => "📜",
                _ => "🔗"
            };

            GUILayout.Label($"{linkGlyph} <b>{rel.TargetCharacterName}</b> <color=#94A3B8>[{rel.TargetSheetId[..Math.Min(8, rel.TargetSheetId.Length)]}]</color>", GUILayout.Width(220));

            rel.LinkType = (RelationshipLinkType)GUILayout.Toolbar((int)rel.LinkType, new[] { "Neutre", "Symbiote", "Frères d'armes", "Hiérarchie", "Dette", "Rival", "Méfiance", "Ennemi", "Mentor" }, GUILayout.ExpandWidth(true));

            GUI.backgroundColor = Color.red;
            if (GUILayout.Button("✕", GUILayout.Width(24), GUILayout.Height(18)))
            {
                _statusMessage = _social.RemoveAt(index);
                return true;
            }
            GUI.backgroundColor = Color.white;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            string affColor = rel.Affinity > 20 ? "#10B981" : (rel.Affinity < -20 ? "#EF4444" : "#FBBF24");
            GUILayout.Label($"Affinité : <color={affColor}><b>{rel.Affinity:+0;-0;0}</b></color>", GUILayout.Width(130));
            rel.Affinity = (int)GUILayout.HorizontalSlider(rel.Affinity, -100, 100);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            string trustColor = rel.Trust >= 70 ? "#38BDF8" : (rel.Trust <= 30 ? "#F87171" : "#E2E8F0");
            GUILayout.Label($"Confiance : <color={trustColor}><b>{rel.Trust}%</b></color>", GUILayout.Width(130));
            rel.Trust = (int)GUILayout.HorizontalSlider(rel.Trust, 0, 100);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Connu depuis :", GUILayout.Width(90));
            rel.KnownSince = GUILayout.TextField(rel.KnownSince, GUILayout.Width(160));
            GUILayout.Label("Lieu :", GUILayout.Width(45));
            rel.FirstMetLocation = GUILayout.TextField(rel.FirstMetLocation, GUILayout.Width(160));
            GUILayout.Label($"Interactions : <b>{rel.InteractionsCount}</b>", GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Notes :", GUILayout.Width(90));
            rel.Notes = GUILayout.TextField(rel.Notes);
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();

            return false;
        }

        private void SpawnCharacterOnGrid()
        {
            EnsureReferences();

            SpawnService.TrySpawn(
                _grid, _arena, _turnManager, _currentSheet, _spawnQStr, _spawnRStr, _spawnAsPlayer,
                out string message);
            _statusMessage = message;
        }
    }
}
