using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Core.Character;
using Killtime.Tactics.Grid;

namespace Killtime.Story.Data
{
    /// <summary>
    /// Une sortie "Parler à" 💬 d'une carte 👥 Acteur : si <see cref="SpeakerId"/>
    /// correspond à celui qui parle à l'acteur (ActorId OU DisplayName,
    /// insensible à la casse ; vide = n'importe qui), <see cref="TargetId"/> est
    /// enclenché — LineId de réplique ou NodeId de nœud.
    /// </summary>
    [Serializable]
    public class SceneActorTalkEntry
    {
        public string SpeakerId = "";
        public string TargetId = "";
    }

    [Serializable]
    public class SceneActorSpawnData
    {
        public string ActorId = "acteur";
        public string DisplayName = "Nouvel acteur";
        public string CharacterSheetFileName = "";
        public int Q = 0;
        public int R = -1;
        public bool IsPlayer = true;
        public float FacingAngle = 0f;
        public Color PrimaryColor = new Color(0.2f, 0.5f, 0.9f);
        public Color AccentColor = Color.cyan;
        public string ModelPrefabName = "";
        public string EquippedWeaponName = "";
        public int BaseArmor = 1;
        public bool StartInCombatStance = false;
        public bool SpawnInitially = true;
        public string SpawnOnNodeId = "";
        // Sorties "Parler à" 💬 : une entrée par interlocuteur possible. Quand on
        // utilise "Parler à" (menu contextuel carte) sur cet acteur, l'entrée dont
        // le Qui correspond à celui qui parle est enclenchée ; sinon l'entrée
        // sans Qui (défaut, n'importe qui). La cible est un LineId de réplique
        // OU un NodeId de nœud (le nœud est alors enclenché / rejoint).
        public List<SceneActorTalkEntry> TalkEntries = new();
        // Sortie cinématique non-bloquante : jouée (fire-and-forget) au spawn différé de l'acteur.
        public List<string> CinematicIds = new();
        public CharacterSheet EmbeddedSheet = new();
        // Position/taille dans le graphe nodal (éditeur uniquement, comme les Triggers).
        // 0/0 = jamais placé → l'éditeur auto-dispose sur la rangée acteurs au chargement.
        public float GraphPosX = 0f;
        public float GraphPosY = 0f;
        public float CardWidth = 340f;
        public float CardHeight = 208f;

        public string GetSummary()
        {
            string faction = IsPlayer ? "[PJ]" : "[PNJ]";
            string where = $"hex ({Q}, {R}) ∠{FacingAngle:0}°";
            string spawn = SpawnInitially ? "init" : (string.IsNullOrEmpty(SpawnOnNodeId) ? "(jamais spawné)" : $"si {SpawnOnNodeId}");
            return $"{faction} {where} · {spawn}";
        }
    }

}