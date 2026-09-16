using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Core.Character;
using Killtime.Core.Dice;
using Killtime.Tactics.Units;

namespace Killtime.UI
{
    public class SkillTreeCosmosWindow : FloatingWindow<SkillTreeCosmosWindow>
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

        private class CosmosSector
        {
            public string Name;
            public string Subtitle;
            public Color SectorColor;
            public Vector2 Position;
        }

        private class CosmosNode
        {
            public string Id;
            public string DisplayName;
            public bool IsSpecialization;
            public SkillType Skill;
            public string SpecializationName;
            public string Description;
            public string MechanicalEffect;
            public Vector2 Position;
            public Color NodeColor;
            public CosmosNode ParentSkillNode;
            public int XpCost;
        }

        private struct CosmosConduit
        {
            public CosmosNode From;
            public CosmosNode To;
            public Color ConduitColor;
        }

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
            _statusFeedback = sheet != null
                ? $"Constellation synchronisée : {sheet.Name} | Réserve : {sheet.AvailableXP} XP."
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

        private void BuildProceduralCosmology()
        {
            _sectors.Clear();
            _allNodes.Clear();
            _conduits.Clear();

            var sForce = AddSector("PILIER DU FER & DE L'IMPACT", "Attribut : Force (FOR)", new Color(0.95f, 0.25f, 0.35f), new Vector2(-460f, -280f));
            var sAgi = AddSector("PILIER DU VECTEUR & DU TIR", "Attribut : Agilité & Rapidité (AGI/RAP)", new Color(0.2f, 0.75f, 1.0f), new Vector2(0f, -440f));
            var sCon = AddSector("PILIER DU BASTION ORGANIQUE", "Attribut : Constitution (CON)", new Color(0.1f, 0.85f, 0.45f), new Vector2(460f, -280f));
            var sInt = AddSector("PILIER DU PRISME COGNITIF", "Attribut : Intelligence & Érudition (INT/ÉRU)", new Color(0.45f, 0.45f, 1.0f), new Vector2(-460f, 260f));
            var sCha = AddSector("PILIER DE L'ASCENDANT IMPÉRIAL", "Attribut : Charisme (CHA)", new Color(0.98f, 0.7f, 0.15f), new Vector2(0f, 440f));
            var sIns = AddSector("PILIER DU FLEUVE & DE L'INSTINCT", "Attribut : Instinct & Vision (INS)", new Color(0.9f, 0.45f, 0.1f), new Vector2(460f, 260f));
            var sMag = AddSector("LE FOYER DE LA CINQUIÈME FORCE", "Singularité Transdimensionnelle (MAG)", new Color(0.75f, 0.35f, 1.0f), new Vector2(0f, 0f));

            // --- PILIER 1 : FORCE ---
            var nMN = AddSkillNode(SkillType.MainsNues, sForce.Position + new Vector2(-70f, 50f), sForce.SectorColor,
                "Combinaison de la force musculaire et de la résonance cinétique corporelle.");
            AddSpecNode(nMN, "Arts Martiaux", sForce.Position + new Vector2(-150f, 110f), sForce.SectorColor,
                "Maîtrise complète du corps-à-corps à mains nues et pieds.",
                "Parade sans malus non-exclusif. Immunité à la contrainte Canon Entravé (kicks au contact). Refoulement (1 case) sur choc traumatique ou Delta >= 4.");
            AddSpecNode(nMN, "Agripper", sForce.Position + new Vector2(-70f, 140f), sForce.SectorColor,
                "Prise au corps et verrouillage articulaire.",
                "Sur Delta >= 1 face à la cible, applique l'état Immobilisé. La cible doit dépenser 2 PA et réussir un duel de Force pour se dégager.");
            AddSpecNode(nMN, "Étranglement Vital", sForce.Position + new Vector2(10f, 120f), sForce.SectorColor,
                "Compression mécanique des voies respiratoires.",
                "Nécessite une cible immobilisée ou au sol. Coûte 2 PA : inflige Asphyxie et draine 1 point d'essoufflement par tour, ignorant l'armure.");

            var nMA = AddSkillNode(SkillType.ManiementArmes, sForce.Position + new Vector2(70f, -40f), sForce.SectorColor,
                "Maniement tactique des épées de métal, katanas et lames d'assaut.");
            AddSpecNode(nMA, "Maniement de l'Épée", sForce.Position + new Vector2(160f, -40f), sForce.SectorColor,
                "Escrime militaire lourde.",
                "Annule le malus de parade non-exclusive (-1 palier évité). Autorise la contre-attaque immédiate en réaction (malus de 1 sur la riposte).");
            AddSpecNode(nMA, "Fente de Rupture", sForce.Position + new Vector2(120f, 40f), sForce.SectorColor,
                "Frappe d'estoc perforante dans les défauts de blindage.",
                "Dépense +1 PA à l'assaut pour ignorer 2 points d'armure physique balistique ou métallique.");
            AddSpecNode(nMA, "Zweihänder de Ligne", sForce.Position + new Vector2(60f, -120f), sForce.SectorColor,
                "Maniement des espadons à deux mains.",
                "Allonge tactique portée à 2 cases. Permet d'intercepter une charge ennemie en réaction immédiate avant l'impact adverse.");

            var nMC = AddSkillNode(SkillType.ArmesContondantes, sForce.Position + new Vector2(-80f, -60f), sForce.SectorColor,
                "Armes de choc, masses énergétiques et marteaux de brèche.");
            AddSpecNode(nMC, "Marteau de Guerre", sForce.Position + new Vector2(-170f, -70f), sForce.SectorColor,
                "Impact tellurique écrasant.",
                "Sur succès net, la cible tombe À Terre, subit +2 Dégâts bruts et convertit les dégâts en Dégâts Absolus (ignore armure non énergétique).");
            AddSpecNode(nMC, "Fracas de Bouclier", sForce.Position + new Vector2(-120f, -140f), sForce.SectorColor,
                "Écrasement des gardes matérielles.",
                "Sur Delta >= 2, neutralise le bouclier physique ennemi et applique immédiatement l'état Débalancé.");

            // --- PILIER 2 : AGILITÉ & BALISTIQUE ---
            var nBAL = AddSkillNode(SkillType.Ballistique, sAgi.Position + new Vector2(0f, 70f), sAgi.SectorColor,
                "Maniement des fusils laser, carabines de précision et grenades.");
            AddSpecNode(nBAL, "Pistolet & Tir Rapide", sAgi.Position + new Vector2(-120f, 130f), sAgi.SectorColor,
                "Cadence réflexe à une main.",
                "Sur réussite nette, la cible subit l'état Ralenti (PA doublés). Sur un résultat brut de 5 ou 6 sur le dé, la cible s'effondre À Terre.");
            AddSpecNode(nBAL, "Tir de Sniper Calibré", sAgi.Position + new Vector2(0f, 160f), sAgi.SectorColor,
                "Ajustement optique longue distance.",
                "En action de progression (2 PA au tour N) : double la portée efficace au tour N+1 sans malus de distance et ignore les demi-couvertures.");
            AddSpecNode(nBAL, "Armes Lourdes (Canons)", sAgi.Position + new Vector2(120f, 130f), sAgi.SectorColor,
                "Batteries d'artillerie lourde.",
                "Confère +100 dégâts de masse contre les structures, portes blindées et véhicules.");
            AddSpecNode(nBAL, "Tir en Rétention", sAgi.Position + new Vector2(-50f, 50f), sAgi.SectorColor,
                "Tir balistique à bout portant.",
                "Annule le malus net de -2 imposé par la contrainte Canon Entravé au contact direct sans exiger de dépense de PA supplémentaire.");

            var nESQ = AddSkillNode(SkillType.Esquive, sAgi.Position + new Vector2(-90f, -30f), sAgi.SectorColor,
                "Évitement cinétique corporel pur.");
            AddSpecNode(nESQ, "Roulade de Décrochage", sAgi.Position + new Vector2(-170f, -40f), sAgi.SectorColor,
                "Évitement avec translation spatiale.",
                "Sur esquive réussie avec Delta >= 2, accorde 1 case de recul gratuit sans consommer de PA, brisant la mêlée.");

            var nPER = AddSkillNode(SkillType.ArmesPercantes, sAgi.Position + new Vector2(90f, -30f), sAgi.SectorColor,
                "Rapières, dagues et armes d'hast légères.");
            AddSpecNode(nPER, "Escrime", sAgi.Position + new Vector2(170f, -40f), sAgi.SectorColor,
                "Jeu de lame rapide et précis.",
                "Annule la punition de visée chirurgicale vers les membres vitaux légers (bras, cou). Parade sans malus non-exclusif.");
            AddSpecNode(nPER, "Frappe Jugulaire", sAgi.Position + new Vector2(130f, -110f), sAgi.SectorColor,
                "Visée chirurgicale du cou.",
                "Sur Delta >= 3, déclenche l'état Saignement (dégâts résiduels par tour égaux aux entraînements).");

            // --- PILIER 3 : CONSTITUTION ---
            var nDEF = AddSkillNode(SkillType.DefenseCorporelle, sCon.Position + new Vector2(-80f, 40f), sCon.SectorColor,
                "Encaissement musculaire, blindages et boucliers de ligne.");
            AddSpecNode(nDEF, "Bloquer", sCon.Position + new Vector2(-140f, 110f), sCon.SectorColor,
                "Interposition de bouclier.",
                "Permet de bloquer sans aucun malus de dé non-exclusif.");
            AddSpecNode(nDEF, "Rempart Humain", sCon.Position + new Vector2(-60f, 130f), sCon.SectorColor,
                "Protection altruiste d'escouade.",
                "Dépense 1 PA en réaction pour interposer son corps sur une attaque ciblant un allié adjacent. Résout sur sa propre armure.");
            AddSpecNode(nDEF, "Maîtrise Exosquelette", sCon.Position + new Vector2(-150f, 30f), sCon.SectorColor,
                "Compensation d'inertie mécanique.",
                "Réduit de 1 PA la pénalité permanente d'encombrement imposée par les armures lourdes motorisées.");

            var nEND = AddSkillNode(SkillType.EndurancePhysique, sCon.Position + new Vector2(70f, 50f), sCon.SectorColor,
                "Résistance physiologique aux traumatismes ouverts.");
            AddSpecNode(nEND, "Encaissement Résilient", sCon.Position + new Vector2(150f, 100f), sCon.SectorColor,
                "Densité osseuse et musculaire supérieure.",
                "Confère +2 absorption brute permanente contre les chocs contondants, augmentant le seuil de choc traumatique.");

            var nCRD = AddSkillNode(SkillType.Cardio, sCon.Position + new Vector2(50f, -60f), sCon.SectorColor,
                "Gestion cardiovasculaire de la fatigue.");
            AddSpecNode(nCRD, "Seconde Respiration", sCon.Position + new Vector2(130f, -90f), sCon.SectorColor,
                "Ventilation cellulaire d'urgence.",
                "Une fois par combat, l'action de reprise de souffle est gratuite (0 PA) et efface immédiatement 2 points d'essoufflement.");

            // --- PILIER 4 : INTELLIGENCE & ÉRUDITION ---
            var nMED = AddSkillNode(SkillType.MedecineAvancee, sInt.Position + new Vector2(70f, -40f), sInt.SectorColor,
                "Chirurgie de campagne, sutures et pharmacopée.");
            AddSpecNode(nMED, "Chirurgie", sInt.Position + new Vector2(150f, -50f), sInt.SectorColor,
                "Intervention chirurgicale de guerre.",
                "Permet de refermer les blessures critiques et de lever l'état Souffrant (soin supérieur à Constitution x 2 requis).");
            AddSpecNode(nMED, "Apothicaire de Guerre", sInt.Position + new Vector2(110f, -120f), sInt.SectorColor,
                "Synthèse chimique de terrain.",
                "Permet de composer des stimulants (Antidouleurs, Speed) à partir de composants biologiques bruts.");

            var nING = AddSkillNode(SkillType.IngenierieArcanotech, sInt.Position + new Vector2(-60f, -50f), sInt.SectorColor,
                "Science des cristaux de nytharite et générateurs à résonance.");
            AddSpecNode(nING, "Surfréquence Nytharite", sInt.Position + new Vector2(-150f, -60f), sInt.SectorColor,
                "Surcharge des circuits énergétiques.",
                "Dépense 2 PA : double la puissance d'une arme laser ou d'un champ pendant 1 tour, après quoi l'objet devient Jammé.");

            var nTAC = AddSkillNode(SkillType.TactiqueStrategie, sInt.Position + new Vector2(0f, 70f), sInt.SectorColor,
                "Analyse prédictive du champ de bataille.");
            AddSpecNode(nTAC, "Analyse de Faille", sInt.Position + new Vector2(0f, 150f), sInt.SectorColor,
                "Détection des points faibles de blindage.",
                "Dépense 2 PA : le prochain coup porté par vous ou un allié ignore l'encaissement de la cible et réduit son armure de 2.");

            // --- PILIER 5 : CHARISME & SOCIAL ---
            var nCOM = AddSkillNode(SkillType.Communication, sCha.Position + new Vector2(-70f, -60f), sCha.SectorColor,
                "Rhétorique, négociation et manipulation.");
            AddSpecNode(nCOM, "Tromper", sCha.Position + new Vector2(-150f, -70f), sCha.SectorColor,
                "Feintes oratoires et duperie.",
                "Épreuve sociale opposée à l'Intuition adverse pour tromper un interlocuteur ou feinter en duel.");
            AddSpecNode(nCOM, "Négocier", sCha.Position + new Vector2(-100f, -130f), sCha.SectorColor,
                "Marchandage institutionnel impérial.",
                "Réduit de 10% à 50% le prix d'achat des équipements en crédits CE.");

            var nINTIM = AddSkillNode(SkillType.Intimidation, sCha.Position + new Vector2(70f, -60f), sCha.SectorColor,
                "Ascendant psychologique et menaces.");
            AddSpecNode(nINTIM, "Intimider", sCha.Position + new Vector2(150f, -70f), sCha.SectorColor,
                "Terreur de zone sur la piétaille.",
                "Sur un différentiel net Delta >= 3, force un sbire ou milicien paniqué à fuir le champ de bataille.");

            var nLEA = AddSkillNode(SkillType.Leadership, sCha.Position + new Vector2(0f, 60f), sCha.SectorColor,
                "Coordination et galvanisation des troupes.");
            AddSpecNode(nLEA, "Mener (Commandement)", sCha.Position + new Vector2(0f, 140f), sCha.SectorColor,
                "Ordre de ralliement tactique.",
                "Dépense 2 PA : confère +1 PA ou +1 EC à tous les alliés situés à moins de 3 cases ce tour.");

            // --- PILIER 6 : INSTINCT & PERCEPTION ---
            var nOBS = AddSkillNode(SkillType.Observation, sIns.Position + new Vector2(-60f, 50f), sIns.SectorColor,
                "Vigilance sensorielle immédiate.");
            AddSpecNode(nOBS, "Vigilance Réflexe", sIns.Position + new Vector2(-130f, 100f), sIns.SectorColor,
                "Immunité à la surprise.",
                "Permet de toujours conserver sa réaction défensive complète lors du tour d'embuscade d'une escouade adverse.");

            var nINTU = AddSkillNode(SkillType.Intuition, sIns.Position + new Vector2(70f, 40f), sIns.SectorColor,
                "Sixième sens face au danger mortel.");
            AddSpecNode(nINTU, "Prescience Viscérale", sIns.Position + new Vector2(140f, 90f), sIns.SectorColor,
                "Instinct prémonitoire d'esquive.",
                "Une fois par combat, force un assaillant venant d'obtenir une réussite critique à relancer son dé et retenir le second tirage.");

            var nART = AddSkillNode(SkillType.Artisanat, sIns.Position + new Vector2(40f, -60f), sIns.SectorColor,
                "Forge d'acier et préparation mécanique.");
            AddSpecNode(nART, "Affûtage de Précision", sIns.Position + new Vector2(110f, -110f), sIns.SectorColor,
                "Préparation des tranchants.",
                "Confère +1 Dégât brut aux 3 prochaines attaques réussies portées avec l'arme entretenue.");

            // --- PILIER 7 : 5e FORCE (MAGIE AU CENTRE) ---
            var nMAG_E = AddSkillNode(SkillType.MagieElementale, sMag.Position + new Vector2(-90f, -60f), sMag.SectorColor,
                "Canalisation thermodynamique : Feu, Glace, Foudre, Terre.");
            AddSpecNode(nMAG_E, "Pyrocinésie Moléculaire", sMag.Position + new Vector2(-170f, -100f), sMag.SectorColor,
                "Activation des vecteurs ignés purs.",
                "Les attaques de feu infligent l'état EnFeu (2 dégâts résiduels par tour pendant 2 tours). Fait fondre 1 point d'armure sur critique.");
            AddSpecNode(nMAG_E, "Barrière Cryogénique", sMag.Position + new Vector2(-150f, -20f), sMag.SectorColor,
                "Bouclier de zéro absolu.",
                "Dresse un mur de glace de 12 PV d'absorption qui bloque les lignes de vue et arrête les projectiles physiques.");

            var nMAG_P = AddSkillNode(SkillType.MagiePrimale, sMag.Position + new Vector2(90f, -60f), sMag.SectorColor,
                "Destruction arcanique, biokinésie et poisons quantiques.");
            AddSpecNode(nMAG_P, "Biokinésie Métabolique", sMag.Position + new Vector2(170f, -100f), sMag.SectorColor,
                "Régulation cellulaire spontanée.",
                "Dépense 2 PA : efface instantanément 1 point d'essoufflement et stabilise un état agonisant.");
            AddSpecNode(nMAG_P, "Décomposition Corrosive", sMag.Position + new Vector2(150f, -20f), sMag.SectorColor,
                "Acide subquantique dégradant.",
                "Ignore l'armure physique et détruit définitivement 1 point de blindage adverse à chaque touche nette.");

            var nMAG_S = AddSkillNode(SkillType.MagieEsprit, sMag.Position + new Vector2(0f, 90f), sMag.SectorColor,
                "Manipulation gravitationnelle, télépathie et clairsentience causale.");
            AddSpecNode(nMAG_S, "Sonde Télépathique", sMag.Position + new Vector2(-90f, 150f), sMag.SectorColor,
                "Lecture des processus décisionnels (Stade 2).",
                "Lit les pensées actives de la cible : confère +1 palier de dé en défense contre son prochain assaut.");
            AddSpecNode(nMAG_S, "Projection Télékinétique", sMag.Position + new Vector2(0f, 170f), sMag.SectorColor,
                "Calcul spatial et répulsion gravitationnelle.",
                "Projette une cible vivante à 2 cases en arrière et lui impose immédiatement l'état À Terre.");
            AddSpecNode(nMAG_S, "Prescience Causaliste", sMag.Position + new Vector2(90f, 150f), sMag.SectorColor,
                "Échelon 3 de Clairsentience (Livre V).",
                "Dépense 2 PA en réaction : entrevoit le coup 0.5s avant l'impact et augmente d'un niveau le dé d'esquive ou de parade.");
        }

        private CosmosSector AddSector(string name, string sub, Color col, Vector2 pos)
        {
            var s = new CosmosSector { Name = name, Subtitle = sub, SectorColor = col, Position = pos };
            _sectors.Add(s);
            return s;
        }

        private CosmosNode AddSkillNode(SkillType skill, Vector2 pos, Color col, string desc)
        {
            var node = new CosmosNode
            {
                Id = "SKILL_" + skill,
                DisplayName = SkillDefinitions.GetDisplayName(skill),
                IsSpecialization = false,
                Skill = skill,
                Description = desc,
                MechanicalEffect = "+1 Palier de Dé permanent par niveau d'entraînement (max +3).",
                Position = pos,
                NodeColor = col,
                XpCost = CharacterProgressionManager.XP_COST_TRAINING
            };
            _allNodes.Add(node);
            return node;
        }

        private CosmosNode AddSpecNode(CosmosNode parent, string specName, Vector2 pos, Color col, string desc, string mechanic)
        {
            var node = new CosmosNode
            {
                Id = "SPEC_" + specName,
                DisplayName = specName,
                IsSpecialization = true,
                Skill = parent.Skill,
                SpecializationName = specName,
                Description = desc,
                MechanicalEffect = mechanic,
                Position = pos,
                NodeColor = col,
                ParentSkillNode = parent,
                XpCost = CharacterProgressionManager.XP_COST_SPECIALIZATION
            };
            _allNodes.Add(node);
            _conduits.Add(new CosmosConduit { From = parent, To = node, ConduitColor = col });
            return node;
        }

        protected override void DrawContent()
        {
            EnsureStyles();

            float topBarH = 34f;
            float footerH = 105f;
            float pad = 8f;

            // 1. Barre supérieure fixe
            Rect topBarRect = new Rect(pad, pad, _windowRect.width - pad * 2f, topBarH);
            DrawTopCommandBar(topBarRect);

            // 2. Fiche d'inspection inférieure fixe
            Rect footerRect = new Rect(pad, _windowRect.height - footerH - pad, _windowRect.width - pad * 2f, footerH);

            // 3. Viewport central (espace exact restant)
            float viewY = topBarRect.yMax + 4f;
            float viewH = Mathf.Max(180f, footerRect.yMin - viewY - 4f);
            Rect viewportRect = new Rect(pad, viewY, _windowRect.width - pad * 2f, viewH);

            HandleCanvasNavigation(viewportRect);
            DrawCosmosViewport(viewportRect);

            DrawNodeInspectionFooter(footerRect);
        }

        private void DrawTopCommandBar(Rect r)
        {
            GUI.BeginGroup(r, GUI.skin.box);

            string charName = _sheet != null ? _sheet.Name : "Sans Alter-Ego";
            int xp = _sheet != null ? _sheet.AvailableXP : 0;

            GUI.Label(new Rect(8, 6, 260, 22), $"<b>Alter-Ego :</b> <color=#00E5FF>{charName}</color> | <color=#FFE600><b>{xp} XP</b></color>");

            float btnX = 275f;
            float btnW = 42f;

            if (GUI.Button(new Rect(btnX, 5, btnW, 22), "Fer")) FocusSector(-460f, -280f); btnX += btnW + 2f;
            if (GUI.Button(new Rect(btnX, 5, btnW, 22), "Tir")) FocusSector(0f, -440f); btnX += btnW + 2f;
            if (GUI.Button(new Rect(btnX, 5, 48, 22), "Corps")) FocusSector(460f, -280f); btnX += 50f;
            if (GUI.Button(new Rect(btnX, 5, 48, 22), "Intel")) FocusSector(-460f, 260f); btnX += 50f;
            if (GUI.Button(new Rect(btnX, 5, 48, 22), "Char")) FocusSector(0f, 440f); btnX += 50f;
            if (GUI.Button(new Rect(btnX, 5, 46, 22), "Inst")) FocusSector(460f, 260f); btnX += 48f;
            if (GUI.Button(new Rect(btnX, 5, 68, 22), "5e Force")) FocusSector(0f, 0f); btnX += 74f;

            float rightSide = r.width - 150f;
            if (GUI.Button(new Rect(rightSide, 5, 26, 22), "-")) _canvasZoom = Mathf.Clamp(_canvasZoom * 0.85f, 0.35f, 2.5f);
            GUI.Label(new Rect(rightSide + 28, 6, 44, 20), $"{Mathf.RoundToInt(_canvasZoom * 100)}%");
            if (GUI.Button(new Rect(rightSide + 74, 5, 26, 22), "+")) _canvasZoom = Mathf.Clamp(_canvasZoom * 1.15f, 0.35f, 2.5f);
            if (GUI.Button(new Rect(rightSide + 104, 5, 42, 22), "Vue")) ResetView();

            GUI.EndGroup();
        }

        private void FocusSector(float x, float y)
        {
            _canvasPan = new Vector2(-x, -y);
            _canvasZoom = 1.05f;
        }

        private void ResetView()
        {
            _canvasPan = Vector2.zero;
            _canvasZoom = 0.68f;
        }

        private void HandleCanvasNavigation(Rect viewportRect)
        {
            Event e = Event.current;
            if (!viewportRect.Contains(e.mousePosition)) return;

            Vector2 viewCenter = new Vector2(viewportRect.width * 0.5f, viewportRect.height * 0.5f);
            Vector2 localMouse = e.mousePosition - viewportRect.min;

            // 1. Clic gauche sur le vide spatial OU clic droit/milieu n'importe où = Pan
            if (e.type == EventType.MouseDown)
            {
                var hitNode = FindNodeAtScreenPos(localMouse, viewCenter);

                if (hitNode != null && e.button == 0)
                {
                    _selectedNode = hitNode;
                    e.Use();
                    return;
                }

                if (e.button == 0 || e.button == 1 || e.button == 2)
                {
                    _isPanning = true;
                    _lastMousePos = e.mousePosition;
                    e.Use();
                }
            }
            else if (e.type == EventType.MouseDrag && _isPanning)
            {
                Vector2 delta = e.mousePosition - _lastMousePos;
                _canvasPan += delta / _canvasZoom;
                _lastMousePos = e.mousePosition;
                e.Use();
            }
            else if (e.type == EventType.MouseUp && _isPanning)
            {
                _isPanning = false;
                e.Use();
            }

            // 2. Zoom molette
            if (e.type == EventType.ScrollWheel)
            {
                float zoomFactor = e.delta.y > 0 ? 0.88f : 1.12f;
                _canvasZoom = Mathf.Clamp(_canvasZoom * zoomFactor, 0.35f, 2.5f);
                e.Use();
            }
        }

        private CosmosNode FindNodeAtScreenPos(Vector2 localMousePos, Vector2 viewCenter)
        {
            for (int i = _allNodes.Count - 1; i >= 0; i--)
            {
                var node = _allNodes[i];
                Vector2 screenPos = WorldToScreen(node.Position, viewCenter);
                float radius = (node.IsSpecialization ? 16f : 24f) * _canvasZoom;
                if (Vector2.Distance(localMousePos, screenPos) <= radius)
                {
                    return node;
                }
            }
            return null;
        }

        private void DrawCosmosViewport(Rect viewport)
        {
            GUI.BeginClip(viewport);

            DrawSolidRect(new Rect(0, 0, viewport.width, viewport.height), new Color(0.010f, 0.018f, 0.032f, 1.0f));

            Vector2 viewCenter = new Vector2(viewport.width * 0.5f, viewport.height * 0.5f);

            DrawBackdropStarfield(viewCenter, viewport.size);

            // 1. Tracé des Conduits Arcaniques
            for (int i = 0; i < _conduits.Count; i++)
            {
                var c = _conduits[i];
                Vector2 p1 = WorldToScreen(c.From.Position, viewCenter);
                Vector2 p2 = WorldToScreen(c.To.Position, viewCenter);

                bool isUnlocked = IsNodeUnlocked(c.To);
                Color lineCol = isUnlocked ? c.ConduitColor : new Color(c.ConduitColor.r, c.ConduitColor.g, c.ConduitColor.b, 0.18f);
                float thickness = isUnlocked ? 2.5f * _canvasZoom : 1.2f * _canvasZoom;

                DrawCosmicLine(p1, p2, lineCol, thickness);
            }

            // 2. Titres des 7 Constellations
            for (int i = 0; i < _sectors.Count; i++)
            {
                var sec = _sectors[i];
                Vector2 p = WorldToScreen(sec.Position + new Vector2(0f, -160f), viewCenter);
                Color c = sec.SectorColor;
                c.a = 0.65f;
                _constellationTitleStyle.normal.textColor = c;
                GUI.Label(new Rect(p.x - 180f, p.y, 360f, 22f), sec.Name, _constellationTitleStyle);
            }

            // 3. Dessin des Étoiles
            Vector2 localMouse = Event.current.mousePosition;
            _hoveredNode = null;

            for (int i = 0; i < _allNodes.Count; i++)
            {
                var node = _allNodes[i];
                Vector2 screenPos = WorldToScreen(node.Position, viewCenter);

                float baseRadius = node.IsSpecialization ? 12f : 18f;
                float drawRadius = baseRadius * _canvasZoom;

                bool isHover = Vector2.Distance(localMouse, screenPos) <= drawRadius * 1.3f;
                if (isHover) _hoveredNode = node;
                bool isSelected = (_selectedNode == node);

                DrawCosmicStar(node, screenPos, drawRadius, isSelected, isHover);
            }

            // 4. Légende discrète d'orientation
            GUI.color = new Color(0.35f, 0.55f, 0.75f, 0.7f);
            GUI.Label(new Rect(8, viewport.height - 18, 480, 16), "Glisser (Clic-Gauche / Clic-Droit) pour explorer | Molette pour zoomer");
            GUI.color = Color.white;

            GUI.EndClip();
        }

        private Vector2 WorldToScreen(Vector2 worldPos, Vector2 viewCenter)
        {
            return viewCenter + (worldPos + _canvasPan) * _canvasZoom;
        }

        private void DrawBackdropStarfield(Vector2 viewCenter, Vector2 size)
        {
            Color faintStar = new Color(0.4f, 0.65f, 0.95f, 0.35f);
            for (int i = 0; i < 60; i++)
            {
                float sx = Mathf.Sin(i * 123.7f) * 1600f;
                float sy = Mathf.Cos(i * 61.9f) * 1600f;
                Vector2 p = WorldToScreen(new Vector2(sx, sy), viewCenter);
                if (p.x >= 0 && p.x <= size.x && p.y >= 0 && p.y <= size.y)
                {
                    float starSize = (i % 3 == 0) ? 2.5f : 1.5f;
                    GUI.DrawTexture(new Rect(p.x, p.y, starSize, starSize), _pixelTex);
                }
            }
        }

        private void DrawCosmicStar(CosmosNode node, Vector2 center, float radius, bool isSelected, bool isHover)
        {
            bool unlocked = IsNodeUnlocked(node);
            int training = GetSkillTrainingLevel(node);

            float pulse = 0.85f + 0.15f * Mathf.Sin(Time.realtimeSinceStartup * 4f + node.Position.x);
            Color starCol = node.NodeColor;

            if (!unlocked)
            {
                starCol = Color.Lerp(Color.gray, starCol, 0.25f);
                starCol.a = 0.35f;
            }
            else
            {
                starCol.a = pulse;
            }

            if (isSelected) starCol = Color.white;

            // Halo externe diffus
            float haloR = radius * (isSelected ? 3.2f : (isHover ? 2.6f : 1.9f));
            Color haloCol = node.NodeColor;
            haloCol.a = (isSelected ? 0.65f : 0.30f) * (unlocked ? 1f : 0.25f);

            Color prevCol = GUI.color;
            GUI.color = haloCol;
            GUI.DrawTexture(new Rect(center.x - haloR, center.y - haloR, haloR * 2f, haloR * 2f), _starGlowTex);

            // Forme de l'étoile
            GUI.color = starCol;
            Texture2D shapeTex = node.IsSpecialization ? _starDiamondTex : _starCircleTex;
            GUI.DrawTexture(new Rect(center.x - radius, center.y - radius, radius * 2f, radius * 2f), shapeTex);
            GUI.color = prevCol;

            // Reticle de sélection
            if (isSelected)
            {
                GUI.color = new Color(0f, 0.95f, 1f, 0.9f);
                float reticleR = radius * 1.45f;
                DrawWireSquare(new Rect(center.x - reticleR, center.y - reticleR, reticleR * 2f, reticleR * 2f));
                GUI.color = prevCol;
            }

            // Étiquettes textuelles sous l'étoile
            if (_canvasZoom >= 0.55f)
            {
                string tag;
                if (node.IsSpecialization)
                {
                    tag = unlocked ? $"★ {node.DisplayName}" : $"☆ {node.DisplayName}";
                }
                else
                {
                    string dieTag = GetSkillDieString(node.Skill);
                    string pips = TrainingPips(training);
                    tag = $"<b>{node.DisplayName}</b>\n<color=#00E5FF>[{dieTag}]</color> {pips}";
                }

                _nodeLabelStyle.normal.textColor = unlocked ? Color.white : new Color(0.65f, 0.72f, 0.82f, 0.65f);
                GUI.Label(new Rect(center.x - 90f, center.y + radius + 3f, 180f, 32f), tag, _nodeLabelStyle);
            }
        }

        private void DrawWireSquare(Rect r)
        {
            DrawSolidRect(new Rect(r.x, r.y, r.width, 1.5f), GUI.color);
            DrawSolidRect(new Rect(r.x, r.yMax - 1.5f, r.width, 1.5f), GUI.color);
            DrawSolidRect(new Rect(r.x, r.y, 1.5f, r.height), GUI.color);
            DrawSolidRect(new Rect(r.xMax - 1.5f, r.y, 1.5f, r.height), GUI.color);
        }

        private void DrawCosmicLine(Vector2 a, Vector2 b, Color color, float width)
        {
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 1f) return;

            float angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;

            GUIUtility.RotateAroundPivot(angle, a);
            Color prev = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(a.x, a.y - width * 0.5f, len, width), _pixelTex);
            GUI.color = prev;
            GUIUtility.RotateAroundPivot(-angle, a);
        }

        private void DrawNodeInspectionFooter(Rect r)
        {
            GUI.BeginGroup(r, GUI.skin.box);

            var node = _selectedNode ?? _hoveredNode;
            if (node == null)
            {
                GUI.Label(new Rect(12, 10, r.width - 24, 20), $"<i>{_statusFeedback}</i>");
                GUI.Label(new Rect(12, 34, r.width - 24, 40), "<color=#94A3B8>Cliquez sur un nexus stellaire pour examiner ses effets mécaniques du Codex et y allouer vos Points d'Expérience (XP).</color>");
                GUI.EndGroup();
                return;
            }

            bool unlocked = IsNodeUnlocked(node);
            int training = GetSkillTrainingLevel(node);
            string typeHeader = node.IsSpecialization
                ? "<color=#FFE600>SPÉCIALISATION MARTIALE / TECHNIQUE</color>"
                : "<color=#00E5FF>COMPÉTENCE DE BASE</color>";

            GUI.Label(new Rect(12, 6, r.width - 260, 20), $"<b>{node.DisplayName.ToUpperInvariant()}</b> — {typeHeader}", _tooltipHeaderStyle);

            // Bouton d'action à droite (dégagé de la poignée de redimensionnement)
            float actionBtnW = 230f;
            float actionBtnX = r.width - actionBtnW - 25f;

            if (_sheet != null)
            {
                if (!node.IsSpecialization)
                {
                    bool isMax = training >= CharacterProgressionManager.MAX_SKILL_TRAINING;
                    if (isMax)
                    {
                        GUI.color = Color.green;
                        GUI.Label(new Rect(actionBtnX, 6, actionBtnW, 24), "✅ Maîtrise Maximale (+3 Paliers)", _tooltipHeaderStyle);
                        GUI.color = Color.white;
                    }
                    else
                    {
                        bool canAfford = _sheet.AvailableXP >= node.XpCost;
                        GUI.enabled = canAfford;
                        GUI.backgroundColor = canAfford ? new Color(0.2f, 0.8f, 0.4f) : Color.gray;
                        if (GUI.Button(new Rect(actionBtnX, 5, actionBtnW, 26), $"⚡ Entraîner (+1 Palier : {node.XpCost} XP)"))
                        {
                            if (CharacterProgressionManager.TrainSkill(_sheet, node.Skill, out string msg))
                            {
                                SaveSheet();
                                _statusFeedback = msg;
                            }
                        }
                        GUI.enabled = true;
                        GUI.backgroundColor = Color.white;
                    }
                }
                else
                {
                    if (unlocked)
                    {
                        GUI.color = Color.green;
                        GUI.Label(new Rect(actionBtnX, 6, actionBtnW, 24), "★ Spécialisation Gravée", _tooltipHeaderStyle);
                        GUI.color = Color.white;
                    }
                    else
                    {
                        bool canAfford = _sheet.AvailableXP >= node.XpCost;
                        GUI.enabled = canAfford;
                        GUI.backgroundColor = canAfford ? new Color(0.95f, 0.7f, 0.15f) : Color.gray;
                        if (GUI.Button(new Rect(actionBtnX, 5, actionBtnW, 26), $"★ Débloquer Spécialisation ({node.XpCost} XP)"))
                        {
                            if (CharacterProgressionManager.UnlockSpecialization(_sheet, node.SpecializationName, out string msg))
                            {
                                SaveSheet();
                                _statusFeedback = msg;
                            }
                        }
                        GUI.enabled = true;
                        GUI.backgroundColor = Color.white;
                    }
                }
            }

            GUI.Label(new Rect(12, 30, r.width - 24, 18), $"<color=#94A3B8>{node.Description}</color>", _tooltipDescStyle);
            GUI.Label(new Rect(12, 52, r.width - 24, 44), $"<b>Effet Codex :</b> <color=#F8FAFC>{node.MechanicalEffect}</color>", _tooltipRuleStyle);

            GUI.EndGroup();
        }

        private void SaveSheet()
        {
            if (_sheet == null) return;
            CharacterStorageService.SaveCharacter(_sheet);
            if (_boundUnit != null)
            {
                _boundUnit.NotifyInventoryChanged(saveToDisk: false);
            }
        }

        private bool IsNodeUnlocked(CosmosNode node)
        {
            if (_sheet == null) return false;
            if (node.IsSpecialization)
            {
                return _sheet.UnlockedSpecializations != null &&
                       _sheet.UnlockedSpecializations.Contains(node.SpecializationName);
            }
            return true;
        }

        private int GetSkillTrainingLevel(CosmosNode node)
        {
            if (_sheet == null || node.IsSpecialization) return 0;
            return _sheet.GetSkill(node.Skill).TrainingLevel;
        }

        private string GetSkillDieString(SkillType skill)
        {
            if (_sheet == null) return "D4";
            int baseRank = SkillDefinitions.GetBaseRank(skill, _sheet.GetEffectiveAttributes(), false);
            int training = _sheet.GetSkill(skill).TrainingLevel;
            var die = SkillDefinitions.DieFromTotalSteps(SkillDefinitions.CharacteristicSteps(baseRank) + training);
            return DiceTypeHints.GetShortLabel(die);
        }

        private static string TrainingPips(int training)
        {
            int full = Mathf.Clamp(training, 0, CharacterProgressionManager.MAX_SKILL_TRAINING);
            return new string('●', full) + new string('○', CharacterProgressionManager.MAX_SKILL_TRAINING - full);
        }

        private static void InitProceduralTextures()
        {
            if (_pixelTex == null)
            {
                _pixelTex = new Texture2D(1, 1);
                _pixelTex.SetPixel(0, 0, Color.white);
                _pixelTex.Apply();
            }

            if (_starCircleTex == null)
            {
                int sz = 64;
                _starCircleTex = new Texture2D(sz, sz, TextureFormat.RGBA32, false);
                for (int y = 0; y < sz; y++)
                {
                    for (int x = 0; x < sz; x++)
                    {
                        float d = Vector2.Distance(new Vector2(x, y), new Vector2(31.5f, 31.5f)) / 31.5f;
                        float alpha = Mathf.Clamp01(1f - Mathf.SmoothStep(0.25f, 0.95f, d));
                        _starCircleTex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                    }
                }
                _starCircleTex.Apply();
            }

            if (_starGlowTex == null)
            {
                int sz = 64;
                _starGlowTex = new Texture2D(sz, sz, TextureFormat.RGBA32, false);
                for (int y = 0; y < sz; y++)
                {
                    for (int x = 0; x < sz; x++)
                    {
                        float d = Vector2.Distance(new Vector2(x, y), new Vector2(31.5f, 31.5f)) / 31.5f;
                        float alpha = Mathf.Exp(-d * 3.5f) * Mathf.Clamp01(1f - d);
                        _starGlowTex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                    }
                }
                _starGlowTex.Apply();
            }

            if (_starDiamondTex == null)
            {
                int sz = 64;
                _starDiamondTex = new Texture2D(sz, sz, TextureFormat.RGBA32, false);
                for (int y = 0; y < sz; y++)
                {
                    for (int x = 0; x < sz; x++)
                    {
                        float nx = Mathf.Abs(x - 31.5f) / 31.5f;
                        float ny = Mathf.Abs(y - 31.5f) / 31.5f;
                        float d = nx + ny;
                        float alpha = Mathf.Clamp01(1f - d);
                        alpha = Mathf.Pow(alpha, 1.8f);
                        _starDiamondTex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                    }
                }
                _starDiamondTex.Apply();
            }
        }

        private static void EnsureStyles()
        {
            if (_nodeLabelStyle == null)
            {
                _nodeLabelStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 9,
                    alignment = TextAnchor.UpperCenter,
                    richText = true,
                    wordWrap = true
                };
            }

            if (_tooltipHeaderStyle == null)
            {
                _tooltipHeaderStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 11,
                    fontStyle = FontStyle.Bold,
                    richText = true
                };
            }

            if (_tooltipDescStyle == null)
            {
                _tooltipDescStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 9,
                    richText = true,
                    wordWrap = true
                };
            }

            if (_tooltipRuleStyle == null)
            {
                _tooltipRuleStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 9,
                    richText = true,
                    wordWrap = true
                };
            }

            if (_constellationTitleStyle == null)
            {
                _constellationTitleStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 12,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                    richText = true
                };
            }
        }

        private static void DrawSolidRect(Rect r, Color c)
        {
            Color prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, _pixelTex);
            GUI.color = prev;
        }
    }
}