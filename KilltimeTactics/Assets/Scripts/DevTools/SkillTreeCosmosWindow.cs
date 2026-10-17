using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Core.Character;
using Killtime.Core.Character.Classes;
using Killtime.Core.Dice;
using Killtime.Tactics.Units;

namespace Killtime.UI
{
    public partial class SkillTreeCosmosWindow : FloatingWindow<SkillTreeCosmosWindow>
    {
        protected override int WindowId => 896;
        protected override string Title => "Voûte Céleste // Matrice des Compétences";
        protected override Vector2 MinSize => new Vector2(760f, 520f);
        protected override Rect DefaultRect => new Rect(
            Mathf.Max(15f, (Screen.width - 1060f) * 0.5f),
            Mathf.Max(25f, (Screen.height - 740f) * 0.5f),
            Mathf.Min(1060f, Screen.width - 30f),
            Mathf.Min(740f, Screen.height - 50f));

        private CharacterSheet _sheet;
        private TacticalUnit _boundUnit;

        private Vector2 _canvasPan = Vector2.zero;
        private float _canvasZoom = 0.68f;
        private bool _isPanning = false;
        private Vector2 _lastMousePos;

        private CosmosNode _selectedNode;
        private CosmosNode _hoveredNode;
        private string _statusFeedback = "Explorez la voûte céleste (Glisser Clic-Gauche pour déplacer, Molette pour zoomer).";

        // Cache par frame (assigné dans DrawContent) : évite ~600 lookups catalogue par frame.
        private CharacterClassDefinition _frameGuide;
        // Positions starfield pré-calculées (déterministes) : évite 120 Sin/Cos par frame.
        private static Vector2[] _starfieldPos;

        private static Texture2D _pixelTex;
        private static Texture2D _starCircleTex;
        private static Texture2D _starGlowTex;
        private static Texture2D _starDiamondTex;

        private static GUIStyle _nodeLabelStyle;
        private static GUIStyle _tooltipHeaderStyle;
        private static GUIStyle _tooltipDescStyle;
        private static GUIStyle _tooltipRuleStyle;
        private static GUIStyle _constellationTitleStyle;

        private readonly List<CosmosSector> _sectors = new();
        private readonly List<CosmosNode> _allNodes = new();
        private readonly List<CosmosConduit> _conduits = new();

        private bool IsCurrentMina => MinaCharacter.IsMina(_sheet);
        private bool IsCurrentLucas => LucasCharacter.IsLucas(_sheet);
        private bool IsCurrentJohn => JohnCharacter.IsJohn(_sheet);
        private bool IsCurrentErika => ErikaCharacter.IsErika(_sheet);
        private bool IsCurrentVance => VanceCharacter.IsVance(_sheet);

        private bool _showHiddenCheats = false;

        public static void OpenForCharacter(CharacterSheet sheet, TacticalUnit unit = null)
        {
            Open();
            if (Instance != null)
            {
                Instance.BindCharacter(sheet, unit);
            }
        }

        public void BindCharacter(CharacterSheet sheet, TacticalUnit unit = null)
        {
            _sheet = sheet;
            _boundUnit = unit;
            _selectedNode = null;
            BuildProceduralCosmology();
            _statusFeedback = sheet != null
                ? $"Constellation synchronisée : {sheet.Name} {(MinaCharacter.IsMina(sheet) ? MinaCharacter.DisplayTag : "")}{(LucasCharacter.IsLucas(sheet) ? LucasCharacter.DisplayTag : "")}{(JohnCharacter.IsJohn(sheet) ? JohnCharacter.DisplayTag : "")}{(ErikaCharacter.IsErika(sheet) ? ErikaCharacter.DisplayTag : "")}{(VanceCharacter.IsVance(sheet) ? VanceCharacter.DisplayTag : "")} | Réserve : {sheet.AvailableXP} XP."
                : "Aucune fiche active.";
        }

        protected override void OnAwake()
        {
            InitProceduralTextures();
            BuildProceduralCosmology();
        }

        protected override void OnOpened()
        {
            if (_sheet == null)
            {
                var unit = FindAnyObjectByType<TacticalUnit>();
                if (unit != null)
                {
                    BindCharacter(unit.GetOrBuildSheet(), unit);
                }
            }
        }
    }
}
