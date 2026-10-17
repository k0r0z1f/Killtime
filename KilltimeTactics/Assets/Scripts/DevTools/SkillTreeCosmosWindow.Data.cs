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
            public bool IsImprovement;
            public string ParentSpecializationName;
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

        private CosmosNode AddSpecNode(CosmosNode parent, string specName, Vector2 pos, Color col, string desc = null, string mechanic = null)
        {
            var detail = CharacterProgressionManager.GetSpecializationDetail(specName);
            string d = desc ?? (detail != null ? detail.Description : "");
            string m = mechanic ?? (detail != null ? detail.MechanicalEffect : "");
            SkillType skill = detail != null ? detail.SourceSkill : parent.Skill;

            var node = new CosmosNode
            {
                Id = "SPEC_" + specName,
                DisplayName = specName,
                IsSpecialization = true,
                IsImprovement = false,
                Skill = skill,
                SpecializationName = specName,
                Description = d,
                MechanicalEffect = m,
                Position = pos,
                NodeColor = col,
                ParentSkillNode = parent,
                XpCost = CharacterProgressionManager.XP_COST_SPECIALIZATION
            };
            _allNodes.Add(node);
            _conduits.Add(new CosmosConduit { From = parent, To = node, ConduitColor = col });
            return node;
        }

        private CosmosNode AddImprovementNode(CosmosNode parentNode, string fullSpecName, Vector2 pos, Color col, string desc = null, string mechanic = null)
        {
            var detail = CharacterProgressionManager.GetSpecializationDetail(fullSpecName);
            string d = desc ?? (detail != null ? detail.Description : "");
            string m = mechanic ?? (detail != null ? detail.MechanicalEffect : "");
            SkillType skill = detail != null ? detail.SourceSkill : parentNode.Skill;

            string displayName = fullSpecName;
            int colonIdx = fullSpecName.IndexOf(" : ", StringComparison.Ordinal);
            if (colonIdx > 0) displayName = fullSpecName.Substring(colonIdx + 3).Trim();

            string parentName = detail != null && !string.IsNullOrEmpty(detail.ParentSpecialization)
                ? detail.ParentSpecialization
                : parentNode.SpecializationName;

            var node = new CosmosNode
            {
                Id = "UPG_" + fullSpecName,
                DisplayName = displayName,
                IsSpecialization = false,
                IsImprovement = true,
                ParentSpecializationName = parentName,
                Skill = skill,
                SpecializationName = fullSpecName,
                Description = d,
                MechanicalEffect = m,
                Position = pos,
                NodeColor = col,
                ParentSkillNode = parentNode,
                XpCost = CharacterProgressionManager.XP_COST_SPECIALIZATION
            };
            _allNodes.Add(node);
            _conduits.Add(new CosmosConduit { From = parentNode, To = node, ConduitColor = col });
            return node;
        }

        private void BuildProceduralCosmology()
        {
            _sectors.Clear();
            _allNodes.Clear();
            _conduits.Clear();

            CharacterProgressionManager.EnsureRegistryBuilt();

            bool isMina = IsCurrentMina;
            bool isLucas = IsCurrentLucas;
            bool isJohn = IsCurrentJohn;
            bool isErika = IsCurrentErika;
            bool isVance = IsCurrentVance;

            // =========================================================================
            // LES 7 PILIERS UNIVERSELS DU CODEX (GÉOMÉTRIE ESPACÉE ANTI-COLLISION)
            // =========================================================================
            var sForce = AddSector("PILIER DU FER & DE L'IMPACT", "Attribut : Force (FOR)", new Color(0.95f, 0.25f, 0.35f), new Vector2(-880f, -480f));
            var sAgi = AddSector("PILIER DU VECTEUR & DU TIR", "Attribut : Agilité & Rapidité (AGI/RAP)", new Color(0.2f, 0.75f, 1.0f), new Vector2(0f, -880f));
            var sCon = AddSector("PILIER DU BASTION ORGANIQUE", "Attribut : Constitution (CON)", new Color(0.1f, 0.85f, 0.45f), new Vector2(880f, -480f));
            var sInt = AddSector("PILIER DU PRISME COGNITIF", "Attribut : Intelligence & Érudition (INT/ÉRU)", new Color(0.45f, 0.45f, 1.0f), new Vector2(-880f, 480f));
            var sCha = AddSector("PILIER DE L'ASCENDANT IMPÉRIAL", "Attribut : Charisme (CHA)", new Color(0.98f, 0.7f, 0.15f), new Vector2(0f, 880f));
            var sIns = AddSector("PILIER DU FLEUVE & DE L'INSTINCT", "Attribut : Instinct & Vision (INS)", new Color(0.9f, 0.45f, 0.1f), new Vector2(880f, 480f));
            var sMag = AddSector("LE FOYER DE LA CINQUIÈME FORCE", "Singularité Transdimensionnelle (MAG)", new Color(0.75f, 0.35f, 1.0f), new Vector2(0f, 0f));

            // =========================================================================
            // PILIER 1 : FORCE (FER & IMPACT)
            // =========================================================================
            var nMN = AddSkillNode(SkillType.MainsNues, sForce.Position + new Vector2(-120f, 0f), sForce.SectorColor,
                "Combinaison de la force musculaire et de la résonance cinétique corporelle.");

            var spAM = AddSpecNode(nMN, "Arts Martiaux", sForce.Position + new Vector2(-260f, -140f), sForce.SectorColor);
            var amA1 = AddImprovementNode(spAM, "Arts Martiaux : Enchaînement Fluide", sForce.Position + new Vector2(-360f, -180f), sForce.SectorColor);
            var amA2 = AddImprovementNode(amA1, "Arts Martiaux : Frappe Téléportée", sForce.Position + new Vector2(-460f, -210f), sForce.SectorColor);
            AddImprovementNode(amA2, "Arts Martiaux : Déphasage Cinétique", sForce.Position + new Vector2(-560f, -230f), sForce.SectorColor);

            var amB1 = AddImprovementNode(spAM, "Arts Martiaux : Onde Tellurique", sForce.Position + new Vector2(-360f, -110f), sForce.SectorColor);
            var amB2 = AddImprovementNode(amB1, "Arts Martiaux : Brise-Blindage", sForce.Position + new Vector2(-460f, -120f), sForce.SectorColor);
            AddImprovementNode(amB2, "Arts Martiaux : Paume de Brum'korath", sForce.Position + new Vector2(-560f, -130f), sForce.SectorColor);

            var amC1 = AddImprovementNode(spAM, "Arts Martiaux : Balayage Rotatif", sForce.Position + new Vector2(-330f, -40f), sForce.SectorColor);
            var amC2 = AddImprovementNode(amC1, "Arts Martiaux : Clé d'Articulation", sForce.Position + new Vector2(-420f, -40f), sForce.SectorColor);
            AddImprovementNode(amC2, "Arts Martiaux : Rupture Ligamentaire", sForce.Position + new Vector2(-510f, -40f), sForce.SectorColor);

            var spSil = AddSpecNode(nMN, "Perforation de Silicate", sForce.Position + new Vector2(-70f, 190f), sForce.SectorColor);
            var silA = AddImprovementNode(spSil, "Perforation de Silicate : Fracture Sismique", sForce.Position + new Vector2(-60f, 280f), sForce.SectorColor);
            AddImprovementNode(silA, "Perforation de Silicate : Cœur d'Adamas", sForce.Position + new Vector2(-60f, 370f), sForce.SectorColor);

            var nMA = AddSkillNode(SkillType.ManiementArmes, sForce.Position + new Vector2(100f, -40f), sForce.SectorColor,
                "Maniement tactique des épées de métal, katanas et lames d'assaut.");

            var spEpee = AddSpecNode(nMA, "Maniement de l'Épée", sForce.Position + new Vector2(230f, -110f), sForce.SectorColor);
            var epA1 = AddImprovementNode(spEpee, "Maniement de l'Épée : Riposte Éclair", sForce.Position + new Vector2(330f, -160f), sForce.SectorColor);
            var epA2 = AddImprovementNode(epA1, "Maniement de l'Épée : Tranchant Tempétueux", sForce.Position + new Vector2(430f, -200f), sForce.SectorColor);
            AddImprovementNode(epA2, "Maniement de l'Épée : Lame de Ligne Temporelle Zéro", sForce.Position + new Vector2(530f, -230f), sForce.SectorColor);

            var epB1 = AddImprovementNode(spEpee, "Maniement de l'Épée : Garde Haute Impériale", sForce.Position + new Vector2(330f, -90f), sForce.SectorColor);
            var epB2 = AddImprovementNode(epB1, "Maniement de l'Épée : Lame Miroir", sForce.Position + new Vector2(430f, -90f), sForce.SectorColor);
            AddImprovementNode(epB2, "Maniement de l'Épée : Dôme de Parade", sForce.Position + new Vector2(530f, -90f), sForce.SectorColor);

            var spFente = AddSpecNode(nMA, "Fente de Rupture", sForce.Position + new Vector2(230f, 30f), sForce.SectorColor);
            var fnA1 = AddImprovementNode(spFente, "Fente de Rupture : Pointe Chirurgicale", sForce.Position + new Vector2(340f, 30f), sForce.SectorColor);
            var fnA2 = AddImprovementNode(fnA1, "Fente de Rupture : Transpercement Traversant", sForce.Position + new Vector2(440f, 30f), sForce.SectorColor);
            AddImprovementNode(fnA2, "Fente de Rupture : Fissure Causal", sForce.Position + new Vector2(540f, 30f), sForce.SectorColor);

            var spBloq = AddSpecNode(nMA, "Bloquer", sForce.Position + new Vector2(190f, 140f), sForce.SectorColor);
            var blA1 = AddImprovementNode(spBloq, "Bloquer : Mur de Bouclier", sForce.Position + new Vector2(280f, 180f), sForce.SectorColor);
            var blA2 = AddImprovementNode(blA1, "Bloquer : Heurt de Bouclier", sForce.Position + new Vector2(370f, 210f), sForce.SectorColor);
            AddImprovementNode(blA2, "Bloquer : Forteresse Impénétrable", sForce.Position + new Vector2(460f, 230f), sForce.SectorColor);

            var spCont = AddSpecNode(nMA, "Armes Contondantes", sForce.Position + new Vector2(0f, -170f), sForce.SectorColor);
            var spMart = AddImprovementNode(spCont, "Marteau de Guerre", sForce.Position + new Vector2(0f, -270f), sForce.SectorColor);
            var mtA1 = AddImprovementNode(spMart, "Marteau de Guerre : Écrasement Osseux", sForce.Position + new Vector2(-110f, -340f), sForce.SectorColor);
            var mtA2 = AddImprovementNode(mtA1, "Marteau de Guerre : Brise-Crâne", sForce.Position + new Vector2(-180f, -400f), sForce.SectorColor);
            AddImprovementNode(mtA2, "Marteau de Guerre : Cataclysme de Fer", sForce.Position + new Vector2(-260f, -450f), sForce.SectorColor);

            var mtB1 = AddImprovementNode(spMart, "Marteau de Guerre : Onde de Faille", sForce.Position + new Vector2(100f, -340f), sForce.SectorColor);
            AddImprovementNode(mtB1, "Marteau de Guerre : Onde Sismique Réverbérante", sForce.Position + new Vector2(170f, -410f), sForce.SectorColor);

            var spHache = AddSpecNode(nMA, "Hache de Guerre", sForce.Position + new Vector2(300f, -340f), sForce.SectorColor);
            var hxA1 = AddImprovementNode(spHache, "Hache de Guerre : Fente du Bûcheron", sForce.Position + new Vector2(400f, -370f), sForce.SectorColor);
            var hxA2 = AddImprovementNode(hxA1, "Hache de Guerre : Brise-Garde", sForce.Position + new Vector2(500f, -390f), sForce.SectorColor);
            AddImprovementNode(hxA2, "Hache de Guerre : Exécution du Bourreau", sForce.Position + new Vector2(590f, -410f), sForce.SectorColor);

            var hxB1 = AddImprovementNode(spHache, "Hache de Guerre : Croc de Désarmement", sForce.Position + new Vector2(390f, -290f), sForce.SectorColor);
            AddImprovementNode(hxB1, "Hache de Guerre : Arrache-Bouclier", sForce.Position + new Vector2(480f, -300f), sForce.SectorColor);

            // =========================================================================
            // PILIER 2 : AGILITÉ & RAPIDITÉ (VECTEUR & TIR)
            // =========================================================================
            var nBAL = AddSkillNode(SkillType.Ballistique, sAgi.Position + new Vector2(-120f, 0f), sAgi.SectorColor,
                "Maniement des fusils laser, carabines de précision et grenades.");

            var spPist = AddSpecNode(nBAL, "Pistolet & Tir Rapide", sAgi.Position + new Vector2(-240f, -80f), sAgi.SectorColor);
            var psA1 = AddImprovementNode(spPist, "Pistolet & Tir Rapide : Dégainé Réflexe", sAgi.Position + new Vector2(-340f, -130f), sAgi.SectorColor);
            var psA2 = AddImprovementNode(psA1, "Pistolet & Tir Rapide : Akimbo Assaut", sAgi.Position + new Vector2(-430f, -170f), sAgi.SectorColor);
            AddImprovementNode(psA2, "Pistolet & Tir Rapide : Trait d'Orichalque", sAgi.Position + new Vector2(-520f, -200f), sAgi.SectorColor);

            var psB1 = AddImprovementNode(spPist, "Pistolet & Tir Rapide : Balle dans le Genou", sAgi.Position + new Vector2(-340f, -60f), sAgi.SectorColor);
            AddImprovementNode(psB1, "Pistolet & Tir Rapide : Tir de Neutralisation", sAgi.Position + new Vector2(-440f, -60f), sAgi.SectorColor);

            var spFus = AddSpecNode(nBAL, "Fusil de Précision", sAgi.Position + new Vector2(-240f, 60f), sAgi.SectorColor);
            var fsA1 = AddImprovementNode(spFus, "Fusil de Précision : Visée Millimétrique", sAgi.Position + new Vector2(-340f, 50f), sAgi.SectorColor);
            var fsA2 = AddImprovementNode(fsA1, "Fusil de Précision : Perforation Hyper-Véloce", sAgi.Position + new Vector2(-440f, 50f), sAgi.SectorColor);
            AddImprovementNode(fsA2, "Fusil de Précision : Tir à Travers les Parois", sAgi.Position + new Vector2(-540f, 50f), sAgi.SectorColor);

            var fsB1 = AddImprovementNode(spFus, "Fusil de Précision : Tir de Suppression", sAgi.Position + new Vector2(-330f, 120f), sAgi.SectorColor);
            AddImprovementNode(fsB1, "Fusil de Précision : Calibre Anti-Matériel", sAgi.Position + new Vector2(-420f, 150f), sAgi.SectorColor);

            var spGren = AddSpecNode(nBAL, "Grenadier d'Assaut", sAgi.Position + new Vector2(-160f, 150f), sAgi.SectorColor);
            var grA1 = AddImprovementNode(spGren, "Grenadier d'Assaut : Calcul Balistique", sAgi.Position + new Vector2(-250f, 210f), sAgi.SectorColor);
            var grA2 = AddImprovementNode(grA1, "Grenadier d'Assaut : Dégoupillage Éclair", sAgi.Position + new Vector2(-330f, 260f), sAgi.SectorColor);
            AddImprovementNode(grA2, "Grenadier d'Assaut : Souffle Thermobarique", sAgi.Position + new Vector2(-410f, 310f), sAgi.SectorColor);

            var spArc = AddSpecNode(nBAL, "Tir à l'Arc", sAgi.Position + new Vector2(-80f, 250f), sAgi.SectorColor);
            var arA1 = AddImprovementNode(spArc, "Tir à l'Arc : Flèche Perforante", sAgi.Position + new Vector2(-80f, 330f), sAgi.SectorColor);
            var arA2 = AddImprovementNode(arA1, "Tir à l'Arc : Tir en Cloche", sAgi.Position + new Vector2(-80f, 410f), sAgi.SectorColor);
            AddImprovementNode(arA2, "Tir à l'Arc : Pluie d'Acier", sAgi.Position + new Vector2(-80f, 490f), sAgi.SectorColor);

            var arB1 = AddImprovementNode(spArc, "Tir à l'Arc : Tir Instinctif", sAgi.Position + new Vector2(10f, 300f), sAgi.SectorColor);
            AddImprovementNode(arB1, "Tir à l'Arc : Chasseur Silencieux", sAgi.Position + new Vector2(10f, 380f), sAgi.SectorColor);

            var nATH = AddSkillNode(SkillType.Athletisme, sAgi.Position + new Vector2(100f, -40f), sAgi.SectorColor,
                "Exploitation du vecteur cinétique et mobilité pure.");

            var spCourse = AddSpecNode(nATH, "Course d'Endurance", sAgi.Position + new Vector2(250f, -60f), sAgi.SectorColor);
            var coA1 = AddImprovementNode(spCourse, "Course d'Endurance : Second Souffle", sAgi.Position + new Vector2(350f, -70f), sAgi.SectorColor);
            AddImprovementNode(coA1, "Course d'Endurance : Cœur de Marathon", sAgi.Position + new Vector2(450f, -80f), sAgi.SectorColor);

            var spFranch = AddSpecNode(nATH, "Franchissement", sAgi.Position + new Vector2(250f, 40f), sAgi.SectorColor);
            var frB1 = AddImprovementNode(spFranch, "Franchissement : Escalade Assurée", sAgi.Position + new Vector2(370f, 40f), sAgi.SectorColor);
            AddImprovementNode(frB1, "Franchissement : Nage de Combat", sAgi.Position + new Vector2(470f, 30f), sAgi.SectorColor);

            var nESQ = AddSkillNode(SkillType.Esquive, sAgi.Position + new Vector2(-40f, -150f), sAgi.SectorColor,
                "Évitement cinétique corporel pur.");
            var spAcro = AddSpecNode(nESQ, "Acrobatie d'Évitement", sAgi.Position + new Vector2(40f, -250f), sAgi.SectorColor);
            var acA1 = AddImprovementNode(spAcro, "Acrobatie d'Évitement : Saut Périlleux", sAgi.Position + new Vector2(130f, -310f), sAgi.SectorColor);
            AddImprovementNode(acA1, "Acrobatie d'Évitement : Vrille Balistique", sAgi.Position + new Vector2(210f, -370f), sAgi.SectorColor);

            var nPER = AddSkillNode(SkillType.ArmesPercantes, sAgi.Position + new Vector2(140f, 130f), sAgi.SectorColor,
                "Rapières, dagues et armes d'hast légères.");
            var spEsc = AddSpecNode(nPER, "Escrime", sAgi.Position + new Vector2(250f, 150f), sAgi.SectorColor);
            var esA1 = AddImprovementNode(spEsc, "Escrime : Riposte à l'Estoc", sAgi.Position + new Vector2(350f, 170f), sAgi.SectorColor);
            var esA2 = AddImprovementNode(esA1, "Escrime : Fleuret Éclair", sAgi.Position + new Vector2(440f, 190f), sAgi.SectorColor);
            AddImprovementNode(esA2, "Escrime : Frappe Fantôme", sAgi.Position + new Vector2(530f, 210f), sAgi.SectorColor);

            var esB1 = AddImprovementNode(spEsc, "Escrime : Désarmement Fleuret", sAgi.Position + new Vector2(330f, 110f), sAgi.SectorColor);
            AddImprovementNode(esB1, "Escrime : Entaille Précise", sAgi.Position + new Vector2(420f, 110f), sAgi.SectorColor);

            var spJug = AddSpecNode(nPER, "Frappe Jugulaire", sAgi.Position + new Vector2(180f, 240f), sAgi.SectorColor);
            var jgA1 = AddImprovementNode(spJug, "Frappe Jugulaire : Hémorragie Profonde", sAgi.Position + new Vector2(260f, 300f), sAgi.SectorColor);
            var jgA2 = AddImprovementNode(jgA1, "Frappe Jugulaire : Asphyxie Sanguine", sAgi.Position + new Vector2(340f, 350f), sAgi.SectorColor);
            AddImprovementNode(jgA2, "Frappe Jugulaire : Tranche-Artère Silencieux", sAgi.Position + new Vector2(420f, 400f), sAgi.SectorColor);

            var spHast = AddSpecNode(nPER, "Arme de Hast", sAgi.Position + new Vector2(610f, 230f), sAgi.SectorColor);
            var haA1 = AddImprovementNode(spHast, "Arme de Hast : Arrêt de Charge", sAgi.Position + new Vector2(700f, 250f), sAgi.SectorColor);
            var haA2 = AddImprovementNode(haA1, "Arme de Hast : Mur de Piques", sAgi.Position + new Vector2(780f, 270f), sAgi.SectorColor);
            AddImprovementNode(haA2, "Arme de Hast : Phalange d'Acier", sAgi.Position + new Vector2(860f, 290f), sAgi.SectorColor);

            var haB1 = AddImprovementNode(spHast, "Arme de Hast : Fauchage Latéral", sAgi.Position + new Vector2(690f, 180f), sAgi.SectorColor);
            AddImprovementNode(haB1, "Arme de Hast : Crochet de Hallebarde", sAgi.Position + new Vector2(770f, 150f), sAgi.SectorColor);

            // =========================================================================
            // PILIER 3 : CONSTITUTION (BASTION ORGANIQUE)
            // =========================================================================
            var nDEF = AddSkillNode(SkillType.DefenseCorporelle, sCon.Position + new Vector2(-100f, 0f), sCon.SectorColor,
                "Encaissement musculaire, blindages et boucliers de ligne.");
            var spBv = AddSpecNode(nDEF, "Bouclier Vivant", sCon.Position + new Vector2(-220f, 0f), sCon.SectorColor);
            var bvA1 = AddImprovementNode(spBv, "Bouclier Vivant : Interposition Réflexe", sCon.Position + new Vector2(-320f, -40f), sCon.SectorColor);
            var bvA2 = AddImprovementNode(bvA1, "Bouclier Vivant : Dévier le Calibre", sCon.Position + new Vector2(-410f, -70f), sCon.SectorColor);
            AddImprovementNode(bvA2, "Bouclier Vivant : Mur Inébranlable", sCon.Position + new Vector2(-500f, -90f), sCon.SectorColor);

            var nEND = AddSkillNode(SkillType.EndurancePhysique, sCon.Position + new Vector2(80f, -60f), sCon.SectorColor,
                "Résistance physiologique aux traumatismes ouverts.");

            var spCond = AddSpecNode(nEND, "Condition de Fer", sCon.Position + new Vector2(200f, -80f), sCon.SectorColor);
            var cdA1 = AddImprovementNode(spCond, "Condition de Fer : Dur à Cuire", sCon.Position + new Vector2(300f, -100f), sCon.SectorColor);
            AddImprovementNode(cdA1, "Condition de Fer : Mur de Chair", sCon.Position + new Vector2(400f, -120f), sCon.SectorColor);

            var spTrempe = AddSpecNode(nEND, "Trempe de Fer", sCon.Position + new Vector2(200f, 0f), sCon.SectorColor);
            var tmB1 = AddImprovementNode(spTrempe, "Trempe de Fer : Ignorer la Douleur", sCon.Position + new Vector2(300f, -10f), sCon.SectorColor);
            AddImprovementNode(tmB1, "Trempe de Fer : Esprit de Granit", sCon.Position + new Vector2(400f, -20f), sCon.SectorColor);

            var nCRD = AddSkillNode(SkillType.Cardio, sCon.Position + new Vector2(-40f, 150f), sCon.SectorColor,
                "Gestion cardiovasculaire de la fatigue.");
            var spPouss = AddSpecNode(nCRD, "Poussée Cardiovasculaire", sCon.Position + new Vector2(40f, 250f), sCon.SectorColor);
            var ps1 = AddImprovementNode(spPouss, "Poussée Cardiovasculaire : Redline Stabilisé", sCon.Position + new Vector2(130f, 310f), sCon.SectorColor);
            AddImprovementNode(ps1, "Poussée Cardiovasculaire : Injection d'Adrénaline", sCon.Position + new Vector2(210f, 370f), sCon.SectorColor);

            var nIMM = AddSkillNode(SkillType.SystemeImmunitaire, sCon.Position + new Vector2(120f, 140f), sCon.SectorColor,
                "Résistance aux agents pathogènes et toxines.");
            var spImm = AddSpecNode(nIMM, "Immunité Toxique", sCon.Position + new Vector2(230f, 200f), sCon.SectorColor);
            var imA1 = AddImprovementNode(spImm, "Immunité Toxique : Neutralisation des Poisons", sCon.Position + new Vector2(330f, 230f), sCon.SectorColor);
            var imA2 = AddImprovementNode(imA1, "Immunité Toxique : Filtrage des Gaz de Guerre", sCon.Position + new Vector2(420f, 260f), sCon.SectorColor);
            AddImprovementNode(imA2, "Immunité Toxique : Sang Antidote Universel", sCon.Position + new Vector2(510f, 290f), sCon.SectorColor);

            // =========================================================================
            // PILIER 4 : INTELLIGENCE & ÉRUDITION (PRISME COGNITIF)
            // =========================================================================
            var nMED = AddSkillNode(SkillType.MedecineAvancee, sInt.Position + new Vector2(-80f, -60f), sInt.SectorColor,
                "Chirurgie de campagne, sutures et pharmacopée.");
            var spChir = AddSpecNode(nMED, "Chirurgie", sInt.Position + new Vector2(-200f, -120f), sInt.SectorColor);
            var chA1 = AddImprovementNode(spChir, "Chirurgie : Diagnostic Vital", sInt.Position + new Vector2(-300f, -160f), sInt.SectorColor);
            var chA2 = AddImprovementNode(chA1, "Chirurgie : Suture Réflexe", sInt.Position + new Vector2(-390f, -190f), sInt.SectorColor);
            AddImprovementNode(chA2, "Chirurgie : Greffe d'Urgence", sInt.Position + new Vector2(-480f, -220f), sInt.SectorColor);

            var spApo = AddSpecNode(nMED, "Apothicaire de Guerre", sInt.Position + new Vector2(-200f, 20f), sInt.SectorColor);
            var apA1 = AddImprovementNode(spApo, "Apothicaire de Guerre : Stimulant Neuro-Accélérateur", sInt.Position + new Vector2(-310f, 30f), sInt.SectorColor);
            var apA2 = AddImprovementNode(apA1, "Apothicaire de Guerre : Sérum Antidote", sInt.Position + new Vector2(-410f, 40f), sInt.SectorColor);
            AddImprovementNode(apA2, "Apothicaire de Guerre : Cocktail Panacée", sInt.Position + new Vector2(-510f, 50f), sInt.SectorColor);

            var nING = AddSkillNode(SkillType.IngenierieArcanotech, sInt.Position + new Vector2(80f, -60f), sInt.SectorColor,
                "Science des cristaux de nytharite et générateurs à résonance.");
            var spSur = AddSpecNode(nING, "Surfréquence Nytharite", sInt.Position + new Vector2(200f, -120f), sInt.SectorColor);
            var sfA1 = AddImprovementNode(spSur, "Surfréquence Nytharite : Stabilisateur de Flux", sInt.Position + new Vector2(310f, -150f), sInt.SectorColor);
            var sfA2 = AddImprovementNode(sfA1, "Surfréquence Nytharite : Décharge Harmonique", sInt.Position + new Vector2(410f, -180f), sInt.SectorColor);
            AddImprovementNode(sfA2, "Surfréquence Nytharite : Résonance Infinie", sInt.Position + new Vector2(510f, -210f), sInt.SectorColor);

            var nTAC = AddSkillNode(SkillType.TactiqueStrategie, sInt.Position + new Vector2(0f, 140f), sInt.SectorColor,
                "Analyse prédictive du champ de bataille.");
            var spAnf = AddSpecNode(nTAC, "Analyse de Faille", sInt.Position + new Vector2(0f, 250f), sInt.SectorColor);
            var afA1 = AddImprovementNode(spAnf, "Analyse de Faille : Tir Coordonné", sInt.Position + new Vector2(-110f, 320f), sInt.SectorColor);
            var afA2 = AddImprovementNode(afA1, "Analyse de Faille : Prédiction de Retraite", sInt.Position + new Vector2(-190f, 380f), sInt.SectorColor);
            AddImprovementNode(afA2, "Analyse de Faille : Échec et Mat", sInt.Position + new Vector2(-270f, 440f), sInt.SectorColor);

            // =========================================================================
            // PILIER 5 : CHARISME (ASCENDANT IMPÉRIAL)
            // =========================================================================
            var nCOM = AddSkillNode(SkillType.Communication, sCha.Position + new Vector2(-120f, 0f), sCha.SectorColor,
                "Rhétorique, négociation et manipulation.");
            var spTromp = AddSpecNode(nCOM, "Tromper", sCha.Position + new Vector2(-240f, -70f), sCha.SectorColor);
            var trA1 = AddImprovementNode(spTromp, "Tromper : Feinte Oratoire", sCha.Position + new Vector2(-340f, -120f), sCha.SectorColor);
            var trA2 = AddImprovementNode(trA1, "Tromper : Faux Signalement", sCha.Position + new Vector2(-430f, -160f), sCha.SectorColor);
            AddImprovementNode(trA2, "Tromper : Mascarade Totale", sCha.Position + new Vector2(-520f, -190f), sCha.SectorColor);

            var spNeg = AddSpecNode(nCOM, "Négocier", sCha.Position + new Vector2(-240f, 70f), sCha.SectorColor);
            var ngA1 = AddImprovementNode(spNeg, "Négocier : Marché Noir Impérial", sCha.Position + new Vector2(-340f, 80f), sCha.SectorColor);
            var ngA2 = AddImprovementNode(ngA1, "Négocier : Cessez-le-Feu", sCha.Position + new Vector2(-430f, 90f), sCha.SectorColor);
            AddImprovementNode(ngA2, "Négocier : Rachat d'Allégeance", sCha.Position + new Vector2(-520f, 100f), sCha.SectorColor);

            var nINTIM = AddSkillNode(SkillType.Intimidation, sCha.Position + new Vector2(120f, 0f), sCha.SectorColor,
                "Ascendant psychologique et menaces de mort.");
            var spIntim = AddSpecNode(nINTIM, "Intimider", sCha.Position + new Vector2(240f, 0f), sCha.SectorColor);
            var itA1 = AddImprovementNode(spIntim, "Intimider : Rugissement de Terreur", sCha.Position + new Vector2(350f, -40f), sCha.SectorColor);
            var itA2 = AddImprovementNode(itA1, "Intimider : Regard de Prédateur", sCha.Position + new Vector2(440f, -70f), sCha.SectorColor);
            AddImprovementNode(itA2, "Intimider : Paralysie Psychologique", sCha.Position + new Vector2(530f, -90f), sCha.SectorColor);

            var nLEA = AddSkillNode(SkillType.Leadership, sCha.Position + new Vector2(0f, -140f), sCha.SectorColor,
                "Coordination tactique et galvanisation des troupes.");
            var spMen = AddSpecNode(nLEA, "Mener (Commandement)", sCha.Position + new Vector2(0f, -250f), sCha.SectorColor);
            var mnA1 = AddImprovementNode(spMen, "Mener : En avant !", sCha.Position + new Vector2(-110f, -320f), sCha.SectorColor);
            var mnA2 = AddImprovementNode(mnA1, "Mener : Tenir la Ligne !", sCha.Position + new Vector2(-190f, -380f), sCha.SectorColor);
            AddImprovementNode(mnA2, "Mener : Galvanisation des Héros", sCha.Position + new Vector2(-270f, -440f), sCha.SectorColor);

            // =========================================================================
            // PILIER 6 : INSTINCT & PERCEPTION (FLEUVE & INSTINCT)
            // =========================================================================
            var nOBS = AddSkillNode(SkillType.Observation, sIns.Position + new Vector2(-80f, -60f), sIns.SectorColor,
                "Vigilance sensorielle immédiate et détection.");
            var spVig = AddSpecNode(nOBS, "Vigilance Réflexe", sIns.Position + new Vector2(-200f, -100f), sIns.SectorColor);
            var vgA1 = AddImprovementNode(spVig, "Vigilance Réflexe : Oeil de Lynx", sIns.Position + new Vector2(-300f, -140f), sIns.SectorColor);
            var vgA2 = AddImprovementNode(vgA1, "Vigilance Réflexe : Détection Thermique", sIns.Position + new Vector2(-390f, -170f), sIns.SectorColor);
            AddImprovementNode(vgA2, "Vigilance Réflexe : Perception Panoramique 360°", sIns.Position + new Vector2(-480f, -200f), sIns.SectorColor);

            var nINTU = AddSkillNode(SkillType.Intuition, sIns.Position + new Vector2(80f, -60f), sIns.SectorColor,
                "Sixième sens face au danger mortel.");

            var spDanger = AddSpecNode(nINTU, "Sens du Danger", sIns.Position + new Vector2(200f, 40f), sIns.SectorColor);
            var daA1 = AddImprovementNode(spDanger, "Sens du Danger : Premiers Réflexes", sIns.Position + new Vector2(300f, 60f), sIns.SectorColor);
            AddImprovementNode(daA1, "Sens du Danger : Clairvoyance du Vétéran", sIns.Position + new Vector2(400f, 80f), sIns.SectorColor);

            var spChasse = AddSpecNode(nINTU, "Instinct du Chasseur", sIns.Position + new Vector2(200f, 120f), sIns.SectorColor);
            var chB1 = AddImprovementNode(spChasse, "Instinct du Chasseur : Pistage", sIns.Position + new Vector2(320f, 140f), sIns.SectorColor);
            AddImprovementNode(chB1, "Instinct du Chasseur : Piégeur", sIns.Position + new Vector2(440f, 170f), sIns.SectorColor);
            var spRes = AddSpecNode(nINTU, "Sphère de Résonance", sIns.Position + new Vector2(200f, -100f), sIns.SectorColor);
            var srA1 = AddImprovementNode(spRes, "Sphère de Résonance : Écho Myotatique", sIns.Position + new Vector2(310f, -140f), sIns.SectorColor);
            var srA2 = AddImprovementNode(srA1, "Sphère de Résonance : Champ Prédictif", sIns.Position + new Vector2(410f, -170f), sIns.SectorColor);
            AddImprovementNode(srA2, "Sphère de Résonance : Omniprésence Synaptique", sIns.Position + new Vector2(510f, -200f), sIns.SectorColor);

            var nART = AddSkillNode(SkillType.Artisanat, sIns.Position + new Vector2(0f, 130f), sIns.SectorColor,
                "Forge d'acier et préparation mécanique des armes.");
            var spAff = AddSpecNode(nART, "Affûtage de Précision", sIns.Position + new Vector2(0f, 240f), sIns.SectorColor);
            var af1 = AddImprovementNode(spAff, "Affûtage : Fil Monomoléculaire", sIns.Position + new Vector2(-110f, 310f), sIns.SectorColor);
            var af2 = AddImprovementNode(af1, "Affûtage : Blindage Trempé", sIns.Position + new Vector2(-190f, 370f), sIns.SectorColor);
            AddImprovementNode(af2, "Affûtage : Acier de Maître d'Hybris", sIns.Position + new Vector2(-270f, 430f), sIns.SectorColor);

            // PILIER 7 : 5e FORCE (fiches héroïques : voir MinaCharacter / LucasCharacter)
            // =========================================================================
            if (isMina)
            {
                sMag.Name = MinaCharacter.SectorName;
                sMag.Subtitle = MinaCharacter.SectorSubtitle;
                sMag.SectorColor = new Color(0.15f, 0.95f, 0.55f);

                var nMAG_P = AddSkillNode(SkillType.MagiePrimale, sMag.Position, sMag.SectorColor,
                    "Biokinésie primordiale héroïque : Atavisme Cinétique (Phase 1) & Éclosion Végétale (Phase 2) — voir MinaCharacter.");

                var colP1 = new Color(0.2f, 0.85f, 1.0f);

                // ---------------------------------------------------------------------
                // PHASE 1 : ATAVISME CINÉTIQUE & SOMA-OVERDRIVE (HÉMISPHÈRE OUEST)
                // ---------------------------------------------------------------------

                // Branche 1 : Vecteur / Vitesse (R1 ➔ R2 ➔ R3)
                var spSteps = AddSpecNode(nMAG_P, "Protocole des Pas Invisibles", sMag.Position + new Vector2(-150f, -160f), colP1);
                var stA1 = AddImprovementNode(spSteps, "Protocole des Pas Invisibles : Dissipation d'Échappement", sMag.Position + new Vector2(-220f, -220f), colP1);
                var stA2 = AddImprovementNode(stA1, "Protocole des Pas Invisibles : Pas Fantôme", sMag.Position + new Vector2(-290f, -270f), colP1);
                AddImprovementNode(stA2, "Protocole des Pas Invisibles : Célérité Quantique", sMag.Position + new Vector2(-360f, -320f), colP1);

                var spDrag = AddImprovementNode(spSteps, "Élan Sans Drag", sMag.Position + new Vector2(-240f, -160f), colP1);
                var drA1 = AddImprovementNode(spDrag, "Élan Sans Drag : Friction Zéro", sMag.Position + new Vector2(-310f, -200f), colP1);
                var drA2 = AddImprovementNode(drA1, "Élan Sans Drag : Glissade Balistique", sMag.Position + new Vector2(-380f, -230f), colP1);
                AddImprovementNode(drA2, "Élan Sans Drag : Évasion Inertielle", sMag.Position + new Vector2(-450f, -250f), colP1);

                var spDecel = AddImprovementNode(spDrag, "Décélération Contrôlée", sMag.Position + new Vector2(-330f, -140f), colP1);
                var dc1 = AddImprovementNode(spDecel, "Décélération Contrôlée : Freinage Magnétique", sMag.Position + new Vector2(-400f, -160f), colP1);
                AddImprovementNode(dc1, "Décélération Contrôlée : Ancrage Cinétique Absolu", sMag.Position + new Vector2(-480f, -170f), colP1);

                // Branche 2 : Trajectoire / Agilité (R1 ➔ R2)
                var spMyo = AddSpecNode(nMAG_P, "Réflexes Myotatiques", sMag.Position + new Vector2(-160f, -60f), colP1);
                AddImprovementNode(spMyo, "Réflexes Myotatiques : Synapses Hyper-Accélérées", sMag.Position + new Vector2(-230f, -90f), colP1);

                var spEsq = AddImprovementNode(spMyo, "Roulade de Décrochage", sMag.Position + new Vector2(-250f, -50f), colP1);
                var rqA1 = AddImprovementNode(spEsq, "Roulade de Décrochage : Reprise d'Appui", sMag.Position + new Vector2(-330f, -80f), colP1);
                var rqA2 = AddImprovementNode(rqA1, "Roulade de Décrochage : Ombre Évasive", sMag.Position + new Vector2(-410f, -90f), colP1);
                AddImprovementNode(rqA2, "Roulade de Décrochage : Pas Déphasé", sMag.Position + new Vector2(-490f, -100f), colP1);

                var rqB1 = AddImprovementNode(spEsq, "Roulade de Décrochage : Esquive Réactive", sMag.Position + new Vector2(-330f, -30f), colP1);
                AddImprovementNode(rqB1, "Roulade de Décrochage : Contre-Poussée", sMag.Position + new Vector2(-410f, -30f), colP1);

                // Branche 3 : Impact / Force (R1 Inné ➔ R2 ➔ R3)
                var spCorps = AddSpecNode(nMAG_P, "Corps Augmenté", sMag.Position + new Vector2(-160f, 30f), colP1);
                AddImprovementNode(spCorps, "Corps Augmenté : Résonance Cinétique Pure", sMag.Position + new Vector2(-230f, -10f), colP1);

                var spLev = AddImprovementNode(spCorps, "Leviers Densifiés", sMag.Position + new Vector2(-250f, 30f), colP1);
                var levA = AddImprovementNode(spLev, "Leviers Densifiés : Perforation Osseuse", sMag.Position + new Vector2(-330f, 0f), colP1);
                AddImprovementNode(levA, "Leviers Densifiés : Pointe Cristalline", sMag.Position + new Vector2(-410f, -10f), colP1);
                var levB = AddImprovementNode(spLev, "Leviers Densifiés : Ancrage Myologique", sMag.Position + new Vector2(-330f, 40f), colP1);
                AddImprovementNode(levB, "Leviers Densifiés : Poigne Titanesque", sMag.Position + new Vector2(-410f, 40f), colP1);

                var spTeep = AddImprovementNode(spLev, "Teep de Rupture", sMag.Position + new Vector2(-340f, 70f), colP1);
                var tpA1 = AddImprovementNode(spTeep, "Teep de Rupture : Impact Dévastateur", sMag.Position + new Vector2(-420f, 70f), colP1);
                var tpA2 = AddImprovementNode(tpA1, "Teep de Rupture : Onde de Choc Linéaire", sMag.Position + new Vector2(-500f, 70f), colP1);
                AddImprovementNode(tpA2, "Teep de Rupture : Brise-Châssis Titan", sMag.Position + new Vector2(-580f, 70f), colP1);
                var tpB1 = AddImprovementNode(spTeep, "Teep de Rupture : Interception d'Assaut", sMag.Position + new Vector2(-420f, 110f), colP1);
                AddImprovementNode(tpB1, "Teep de Rupture : Rebond Mural", sMag.Position + new Vector2(-500f, 110f), colP1);

                // Branche 4 : Matrice / Constitution (R1 ➔ R2)
                var spFib = AddSpecNode(nMAG_P, "Fibres Résilientes", sMag.Position + new Vector2(-150f, 120f), colP1);
                var fbA1 = AddImprovementNode(spFib, "Fibres Résilientes : Tissu Densifié", sMag.Position + new Vector2(-220f, 160f), colP1);
                var fbA2 = AddImprovementNode(fbA1, "Fibres Résilientes : Carapace Sous-Cutanée", sMag.Position + new Vector2(-290f, 190f), colP1);
                AddImprovementNode(fbA2, "Fibres Résilientes : Chair d'Inflexible", sMag.Position + new Vector2(-360f, 210f), colP1);

                var spElast = AddImprovementNode(spFib, "Encaissement Élastique", sMag.Position + new Vector2(-240f, 120f), colP1);
                var el1 = AddImprovementNode(spElast, "Encaissement Élastique : Tissu Myo-Amortisseur", sMag.Position + new Vector2(-320f, 150f), colP1);
                AddImprovementNode(el1, "Encaissement Élastique : Dissipation Plastique", sMag.Position + new Vector2(-390f, 160f), colP1);

                // Branche 5 : Homéostasie Interne (R1 ➔ R2)
                var spHom = AddSpecNode(nMAG_P, "Régénération Métabolique", sMag.Position + new Vector2(-80f, 170f), colP1);
                var hmA1 = AddImprovementNode(spHom, "Homéostasie Accélérée : Régénération Cellulaire Avancée", sMag.Position + new Vector2(-150f, 230f), colP1);
                var hmA2 = AddImprovementNode(hmA1, "Homéostasie Accélérée : Coagulation Flash", sMag.Position + new Vector2(-210f, 280f), colP1);
                AddImprovementNode(hmA2, "Homéostasie Accélérée : Pulsion Phénix", sMag.Position + new Vector2(-270f, 320f), colP1);

                var spResp = AddImprovementNode(spHom, "Seconde Respiration", sMag.Position + new Vector2(-160f, 210f), colP1);
                var rsA1 = AddImprovementNode(spResp, "Seconde Respiration : Rechargement Anaérobie", sMag.Position + new Vector2(-230f, 260f), colP1);
                var rsA2 = AddImprovementNode(rsA1, "Seconde Respiration : Poumons d'Acier", sMag.Position + new Vector2(-290f, 300f), colP1);
                AddImprovementNode(rsA2, "Seconde Respiration : Coeur de Berserker", sMag.Position + new Vector2(-350f, 330f), colP1);

                // ---------------------------------------------------------------------
                // PHASE 2 : LE ROYAUME PRIMORDIAL DE MINALIA (HÉMISPHÈRE EST — VOL II)
                // ---------------------------------------------------------------------

                // Branche 1 : Architecture & Bastion Végétal (Nord-Est)
                var spRac = AddSpecNode(nMAG_P, "Barricade de Racines", sMag.Position + new Vector2(130f, -120f), sMag.SectorColor);
                var rc1a = AddImprovementNode(spRac, "Barricade de Racines : Épines de Granit", sMag.Position + new Vector2(210f, -150f), sMag.SectorColor);
                var rc1b = AddImprovementNode(spRac, "Barricade de Racines : Bastion Vivant", sMag.Position + new Vector2(130f, -190f), sMag.SectorColor);
                var rc2a = AddImprovementNode(rc1a, "Barricade de Racines : Rempart de Silice Végétale", sMag.Position + new Vector2(290f, -180f), sMag.SectorColor);
                var rc2b = AddImprovementNode(rc1b, "Barricade de Racines : Palissade Chlorophyllienne", sMag.Position + new Vector2(200f, -230f), sMag.SectorColor);
                AddImprovementNode(rc2a, "Barricade de Racines : Mur d'Orichalque Végétal", sMag.Position + new Vector2(360f, -200f), sMag.SectorColor);
                AddImprovementNode(rc2b, "Barricade de Racines : Arbre-Citadelle de Minalia", sMag.Position + new Vector2(270f, -260f), sMag.SectorColor);

                // Branche 2 : Entraves & Prédation (Nord-Est)
                var spVignes = AddSpecNode(nMAG_P, "Vignes Constrictrices", sMag.Position + new Vector2(60f, -170f), sMag.SectorColor);
                var vg1a = AddImprovementNode(spVignes, "Vignes Constrictrices : Étau Végétal", sMag.Position + new Vector2(110f, -230f), sMag.SectorColor);
                var vg1b = AddImprovementNode(spVignes, "Vignes Constrictrices : Moisson Sanguine", sMag.Position + new Vector2(20f, -230f), sMag.SectorColor);
                var vg2a = AddImprovementNode(vg1a, "Vignes Constrictrices : Vrilles Souterraines", sMag.Position + new Vector2(110f, -290f), sMag.SectorColor);
                var vg2b = AddImprovementNode(vg1b, "Vignes Constrictrices : Épines Neurotoxiques", sMag.Position + new Vector2(20f, -290f), sMag.SectorColor);
                AddImprovementNode(vg2a, "Vignes Constrictrices : Étranglement Toxique", sMag.Position + new Vector2(110f, -350f), sMag.SectorColor);
                AddImprovementNode(vg2b, "Vignes Constrictrices : Lierre Causal de Brum'korath", sMag.Position + new Vector2(20f, -350f), sMag.SectorColor);

                // Branche 3 : Périmètres Létaux & Pièges (Est-Nord-Est)
                var spRon = AddSpecNode(nMAG_P, "Champ de Ronces", sMag.Position + new Vector2(170f, -50f), sMag.SectorColor);
                var rn1a = AddImprovementNode(spRon, "Champ de Ronces : Ronces Neurotoxiques", sMag.Position + new Vector2(250f, -80f), sMag.SectorColor);
                var rn1b = AddImprovementNode(spRon, "Champ de Ronces : Enchevêtrement Explosif", sMag.Position + new Vector2(180f, -120f), sMag.SectorColor);
                var rn2a = AddImprovementNode(rn1a, "Champ de Ronces : Rosier Noir d'Hybris", sMag.Position + new Vector2(330f, -100f), sMag.SectorColor);
                var rn2b = AddImprovementNode(rn1b, "Champ de Ronces : Tapis de Racines Épineuses", sMag.Position + new Vector2(260f, -150f), sMag.SectorColor);
                AddImprovementNode(rn2a, "Champ de Ronces : Jardin des Tourments d'Hybris", sMag.Position + new Vector2(390f, -120f), sMag.SectorColor);

                // Branche 4 : Vecteurs Cinétiques & Mycélium (Est)
                var spGre = AddSpecNode(nMAG_P, "Greffe Cinétique", sMag.Position + new Vector2(180f, 30f), sMag.SectorColor);
                var gr1a = AddImprovementNode(spGre, "Greffe Cinétique : Osmose d'Escouade", sMag.Position + new Vector2(270f, 10f), sMag.SectorColor);
                var gr1b = AddImprovementNode(spGre, "Greffe Cinétique : Frappe Symbiotique", sMag.Position + new Vector2(270f, 60f), sMag.SectorColor);
                var gr2a = AddImprovementNode(gr1a, "Greffe Cinétique : Pont Végétal Aérien", sMag.Position + new Vector2(360f, 10f), sMag.SectorColor);
                var gr2b = AddImprovementNode(gr1b, "Greffe Cinétique : Racines d'Accélération", sMag.Position + new Vector2(360f, 60f), sMag.SectorColor);
                AddImprovementNode(gr2a, "Greffe Cinétique : Réseau Mycélien Primordial", sMag.Position + new Vector2(440f, 35f), sMag.SectorColor);

                // Branche 5 : Éveil héroïque & Avatar Suprême (Sud-Est)
                var spEve = AddSpecNode(nMAG_P, "Éveil Chlorophyllien", sMag.Position + new Vector2(150f, 120f), sMag.SectorColor);
                var ev1a = AddImprovementNode(spEve, "Éveil Chlorophyllien : Photosynthèse Arcanique", sMag.Position + new Vector2(230f, 150f), sMag.SectorColor);
                var ev1b = AddImprovementNode(spEve, "Éveil Chlorophyllien : Peau de Chlorophylle", sMag.Position + new Vector2(150f, 190f), sMag.SectorColor);
                var ev2a = AddImprovementNode(ev1a, "Éveil Chlorophyllien : Floraison Cinétique", sMag.Position + new Vector2(300f, 180f), sMag.SectorColor);
                var ev2b = AddImprovementNode(ev1b, "Éveil Chlorophyllien : Pollen Hypnotique", sMag.Position + new Vector2(220f, 230f), sMag.SectorColor);
                AddImprovementNode(ev2a, "Éveil Chlorophyllien : Avatar de Minalia Primordiale", sMag.Position + new Vector2(370f, 210f), sMag.SectorColor);

                // Branche 6 : Catalyse Tissulaire (Sud)
                var spCat = AddSpecNode(nMAG_P, "Catalyse Tissulaire", sMag.Position + new Vector2(50f, 170f), sMag.SectorColor);
                var ct1a = AddImprovementNode(spCat, "Catalyse Tissulaire : Surcroît Vital", sMag.Position + new Vector2(110f, 230f), sMag.SectorColor);
                var ct1b = AddImprovementNode(spCat, "Catalyse Tissulaire : Bouclier Végétal", sMag.Position + new Vector2(30f, 230f), sMag.SectorColor);
                var ct2a = AddImprovementNode(ct1a, "Catalyse Tissulaire : Régénération Osmotique", sMag.Position + new Vector2(110f, 290f), sMag.SectorColor);
                var ct2b = AddImprovementNode(ct1b, "Catalyse Tissulaire : Cautérisation Sèveuse", sMag.Position + new Vector2(30f, 290f), sMag.SectorColor);
                AddImprovementNode(ct2a, "Catalyse Tissulaire : Transfusion Symbiotique", sMag.Position + new Vector2(110f, 350f), sMag.SectorColor);
                AddImprovementNode(ct2b, "Catalyse Tissulaire : Cœur d'Éclosion de Mina-0", sMag.Position + new Vector2(30f, 350f), sMag.SectorColor);
            }
            else
            {
                if (isLucas)
                {
                    sMag.Name = LucasCharacter.SectorName;
                    sMag.Subtitle = LucasCharacter.SectorSubtitle;
                }
                else
                {
                    sMag.Name = "LE FOYER DE LA CINQUIÈME FORCE";
                    sMag.Subtitle = "Singularité Transdimensionnelle (MAG)";
                }
                sMag.SectorColor = new Color(0.75f, 0.35f, 1.0f);

                // Nœuds de compétence créés en premier : parents des constellations (primale OU vide).
                var nMAG_E = AddSkillNode(SkillType.MagieElementale, sMag.Position + new Vector2(140f, -60f), sMag.SectorColor,
                    "Maîtrise des rayonnements thermiques, plasma et arcs électriques.");
                var nMAG_S = AddSkillNode(SkillType.MagieEsprit, sMag.Position + new Vector2(140f, 70f), sMag.SectorColor,
                    "Projection télékinésique et télépathie tactique.");

                if (isLucas)
                {
                    // Constellation héroïque (voir LucasCharacter.RegisterSpecializations).
                    // La Primale, à laquelle la fiche héroïque n'a aucune affinité, est masquée.
                    var colLucas = new Color(0.35f, 0.6f, 1.0f);

                    // VOIE DU VIDE CALCULANT (Élémentale inversée — soustraction au lieu de création)
                    var spVide = AddSpecNode(nMAG_E, "Vide Calculant", sMag.Position + new Vector2(-60f, -110f), colLucas);
                    var vdA1 = AddImprovementNode(spVide, "Vide Calculant : Chute de Pression", sMag.Position + new Vector2(-160f, -140f), colLucas);
                    var vdA2 = AddImprovementNode(vdA1, "Vide Calculant : Zéro Absolu", sMag.Position + new Vector2(-260f, -170f), colLucas);
                    AddImprovementNode(vdA2, "Vide Calculant : Carnage Somnambulique", sMag.Position + new Vector2(-360f, -200f), colLucas);
                    AddImprovementNode(spVide, "Vide Calculant : Dôme de Vide", sMag.Position + new Vector2(-160f, -80f), colLucas);

                    // ARCHITECTURE NEURALE (Esprit — 6 stades, du DMN à la possession)
                    var spPf = AddSpecNode(nMAG_S, "Pare-feu Psychologique", sMag.Position + new Vector2(-60f, 10f), colLucas);
                    var pfA1 = AddImprovementNode(spPf, "Pare-feu Psychologique : Lecture de Surface", sMag.Position + new Vector2(-160f, 30f), colLucas);
                    var pfA2 = AddImprovementNode(pfA1, "Pare-feu Psychologique : Suggestions Brèves", sMag.Position + new Vector2(-260f, 50f), colLucas);
                    var pfA3 = AddImprovementNode(pfA2, "Pare-feu Psychologique : Réécriture Synaptique", sMag.Position + new Vector2(-360f, 70f), colLucas);
                    AddImprovementNode(pfA3, "Pare-feu Psychologique : Possession Intégrale", sMag.Position + new Vector2(-460f, 90f), colLucas);

                    // CALCUL SPATIAL & THERMOSTAT (lien symbiotique — voir LucasCharacter)
                    AddSpecNode(nMAG_S, "Arrêt Vectoriel", sMag.Position + new Vector2(-60f, 170f), colLucas);
                    AddSpecNode(nMAG_S, "Thermostat Inversé", sMag.Position + new Vector2(-60f, 250f), colLucas);
                }
                else
                {
                var nMAG_P = AddSkillNode(SkillType.MagiePrimale, sMag.Position + new Vector2(-140f, 0f), sMag.SectorColor,
                    "Biokinésie exogène et création végétale.");

                var spRac = AddSpecNode(nMAG_P, "Barricade de Racines", sMag.Position + new Vector2(-280f, -90f), sMag.SectorColor);
                var rcA1 = AddImprovementNode(spRac, "Barricade de Racines : Épines de Granit", sMag.Position + new Vector2(-380f, -140f), sMag.SectorColor);
                var rcA2 = AddImprovementNode(rcA1, "Barricade de Racines : Bastion Vivant", sMag.Position + new Vector2(-470f, -180f), sMag.SectorColor);
                AddImprovementNode(rcA2, "Barricade de Racines : Mur d'Orichalque Végétal", sMag.Position + new Vector2(-560f, -210f), sMag.SectorColor);

                var spVigM = AddSpecNode(nMAG_P, "Vignes Constrictrices", sMag.Position + new Vector2(-280f, 70f), sMag.SectorColor);
                var vgM1 = AddImprovementNode(spVigM, "Vignes Constrictrices : Étau Végétal", sMag.Position + new Vector2(-380f, 90f), sMag.SectorColor);
                var vgM2 = AddImprovementNode(vgM1, "Vignes Constrictrices : Moisson Sanguine", sMag.Position + new Vector2(-470f, 110f), sMag.SectorColor);
                AddImprovementNode(vgM2, "Vignes Constrictrices : Étranglement Toxique", sMag.Position + new Vector2(-560f, 130f), sMag.SectorColor);

                var spCat = AddSpecNode(nMAG_P, "Catalyse Tissulaire", sMag.Position + new Vector2(-160f, 160f), sMag.SectorColor);
                var ctA1 = AddImprovementNode(spCat, "Catalyse Tissulaire : Surcroît Vital", sMag.Position + new Vector2(-250f, 220f), sMag.SectorColor);
                var ctA2 = AddImprovementNode(ctA1, "Catalyse Tissulaire : Bouclier Végétal", sMag.Position + new Vector2(-330f, 270f), sMag.SectorColor);
                AddImprovementNode(ctA2, "Catalyse Tissulaire : Transfusion Symbiotique", sMag.Position + new Vector2(-410f, 320f), sMag.SectorColor);

                var spRonce = AddSpecNode(nMAG_P, "Champ de Ronces", sMag.Position + new Vector2(-80f, -150f), sMag.SectorColor);
                var rnA1 = AddImprovementNode(spRonce, "Champ de Ronces : Ronces Neurotoxiques", sMag.Position + new Vector2(-150f, -240f), sMag.SectorColor);
                AddImprovementNode(rnA1, "Champ de Ronces : Enchevêtrement Explosif", sMag.Position + new Vector2(-220f, -310f), sMag.SectorColor);

                var spGref = AddSpecNode(nMAG_P, "Greffe Cinétique", sMag.Position + new Vector2(40f, -150f), sMag.SectorColor);
                var gfA1 = AddImprovementNode(spGref, "Greffe Cinétique : Osmose d'Escouade", sMag.Position + new Vector2(80f, -240f), sMag.SectorColor);
                AddImprovementNode(gfA1, "Greffe Cinétique : Frappe Symbiotique", sMag.Position + new Vector2(120f, -310f), sMag.SectorColor);
                } // fin hémisphère primal (masqué pour la fiche héroïque sans affinité — voir LucasCharacter)

                // Éventail Élémentale NORD (voies Feu/Air/Eau/Terre) : nappes parallèles
                // vers le nord-est, sans croisement avec l'éventail Esprit (sud-est).
                var spTer = AddSpecNode(nMAG_E, "Géomancie", sMag.Position + new Vector2(250f, -410f), sMag.SectorColor);
                var teA1 = AddImprovementNode(spTer, "Géomancie : Poing Tellurique", sMag.Position + new Vector2(350f, -420f), sMag.SectorColor);
                AddImprovementNode(teA1, "Géomancie : Séisme Localisé", sMag.Position + new Vector2(450f, -430f), sMag.SectorColor);
                AddImprovementNode(spTer, "Géomancie : Peau de Pierre", sMag.Position + new Vector2(370f, -370f), sMag.SectorColor);

                var spAir = AddSpecNode(nMAG_E, "Aéromancie", sMag.Position + new Vector2(260f, -300f), sMag.SectorColor);
                var aiA1 = AddImprovementNode(spAir, "Aéromancie : Lame de Vent", sMag.Position + new Vector2(360f, -310f), sMag.SectorColor);
                AddImprovementNode(aiA1, "Aéromancie : Tempête de Lames", sMag.Position + new Vector2(460f, -320f), sMag.SectorColor);
                AddImprovementNode(spAir, "Aéromancie : Courant Porteur", sMag.Position + new Vector2(380f, -260f), sMag.SectorColor);

                var spInc = AddSpecNode(nMAG_E, "Incinération Pyrocinétique", sMag.Position + new Vector2(270f, -170f), sMag.SectorColor);
                var inA1 = AddImprovementNode(spInc, "Incinération : Flamme Bleue", sMag.Position + new Vector2(370f, -180f), sMag.SectorColor);
                var inA2 = AddImprovementNode(inA1, "Incinération : Fournaise Déferlante", sMag.Position + new Vector2(460f, -190f), sMag.SectorColor);
                AddImprovementNode(inA2, "Incinération : Nova Thermique", sMag.Position + new Vector2(550f, -200f), sMag.SectorColor);

                var spEau = AddSpecNode(nMAG_E, "Hydromancie", sMag.Position + new Vector2(260f, -60f), sMag.SectorColor);
                var hyA1 = AddImprovementNode(spEau, "Hydromancie : Étreinte Abyssale", sMag.Position + new Vector2(360f, -50f), sMag.SectorColor);
                AddImprovementNode(hyA1, "Hydromancie : Raz-de-Marée", sMag.Position + new Vector2(460f, -40f), sMag.SectorColor);
                AddImprovementNode(spEau, "Hydromancie : Brume Aveuglante", sMag.Position + new Vector2(390f, -10f), sMag.SectorColor);

                // Éventail Esprit SUD (voies Télépathie/Télékinésie/Clairvoyance) : nappes
                // parallèles vers le sud-est, séparées de l'éventail Élémentale (jour de 130px).
                var spTele = AddSpecNode(nMAG_S, "Télépathie", sMag.Position + new Vector2(270f, 120f), sMag.SectorColor);
                var tpA1 = AddImprovementNode(spTele, "Télépathie : Lien Empathique", sMag.Position + new Vector2(370f, 130f), sMag.SectorColor);
                var tpA2 = AddImprovementNode(tpA1, "Télépathie : Sondage de Surface", sMag.Position + new Vector2(460f, 140f), sMag.SectorColor);
                AddImprovementNode(tpA2, "Télépathie : Injonction Impérieuse", sMag.Position + new Vector2(550f, 150f), sMag.SectorColor);

                var spTelek = AddSpecNode(nMAG_S, "Télékinésie", sMag.Position + new Vector2(270f, 230f), sMag.SectorColor);
                var tkA1 = AddImprovementNode(spTelek, "Télékinésie : Projection d'Objets", sMag.Position + new Vector2(370f, 240f), sMag.SectorColor);
                var tkA2 = AddImprovementNode(tkA1, "Télékinésie : Barrière Cinétique", sMag.Position + new Vector2(460f, 250f), sMag.SectorColor);
                AddImprovementNode(tkA2, "Télékinésie : Fissure Gravifique", sMag.Position + new Vector2(550f, 260f), sMag.SectorColor);

                var spClair = AddSpecNode(nMAG_S, "Clairvoyance", sMag.Position + new Vector2(270f, 340f), sMag.SectorColor);
                var clA1 = AddImprovementNode(spClair, "Clairvoyance : Œil Distant", sMag.Position + new Vector2(370f, 350f), sMag.SectorColor);
                var clA2 = AddImprovementNode(clA1, "Clairvoyance : Prescience du Danger", sMag.Position + new Vector2(460f, 360f), sMag.SectorColor);
                AddImprovementNode(clA2, "Clairvoyance : Champ de Prescience", sMag.Position + new Vector2(550f, 370f), sMag.SectorColor);

                // Constellation du Passeur (voir JohnCharacter.RegisterSpecializations) :
                // greffée sur les piliers mundans déjà construits (tir, contrat, instinct, plan de vol).
                if (isJohn)
                {
                    var colJohn = new Color(0.98f, 0.7f, 0.15f);
                    var spJohnTir = AddSpecNode(nBAL, "Tir de Couverture du Passeur", sAgi.Position + new Vector2(-240f, -160f), colJohn);
                    var jtA1 = AddImprovementNode(spJohnTir, "Tir de Couverture du Passeur : Rideau de Fer du Hangar", sAgi.Position + new Vector2(-340f, -200f), colJohn);
                    AddImprovementNode(jtA1, "Tir de Couverture du Passeur : Dernier Chargeur", sAgi.Position + new Vector2(-440f, -230f), colJohn);

                    var spJohnContrat = AddSpecNode(nCOM, "Contrat du Passeur", sCha.Position + new Vector2(-240f, 140f), colJohn);
                    var jcA1 = AddImprovementNode(spJohnContrat, "Contrat du Passeur : Renégociation Forcée", sCha.Position + new Vector2(-340f, 160f), colJohn);
                    AddImprovementNode(jcA1, "Contrat du Passeur : Prix du Retour", sCha.Position + new Vector2(-440f, 180f), colJohn);

                    var spJohnVivant = AddSpecNode(nINTU, "Revenir Vivant", sIns.Position + new Vector2(200f, 200f), colJohn);
                    var jvA1 = AddImprovementNode(spJohnVivant, "Revenir Vivant : Lecture du Braconnier", sIns.Position + new Vector2(320f, 220f), colJohn);
                    AddImprovementNode(jvA1, "Revenir Vivant : Passeur Insubmersible", sIns.Position + new Vector2(440f, 240f), colJohn);

                    var spJohnVol = AddSpecNode(nTAC, "Plan de Vol du Starlight", sInt.Position + new Vector2(0f, 330f), colJohn);
                    var jvolA1 = AddImprovementNode(spJohnVol, "Plan de Vol du Starlight : Route Sans Balise", sInt.Position + new Vector2(-110f, 400f), colJohn);
                    AddImprovementNode(jvolA1, "Plan de Vol du Starlight : Traversée du Creuset", sInt.Position + new Vector2(-190f, 460f), colJohn);
                }

                // Constellation Cleyane (voir ErikaCharacter.RegisterSpecializations) :
                // flamme frontale sur le foyer Élémentale, lame sur le pilier Fer, sang-chaud sur le Bastion.
                if (isErika)
                {
                    var colErika = new Color(1.0f, 0.45f, 0.15f);
                    var spFlamme = AddSpecNode(nMAG_E, "Flamme Cleyane", sMag.Position + new Vector2(-60f, -110f), colErika);
                    var flA1 = AddImprovementNode(spFlamme, "Flamme Cleyane : Doigts de Braise", sMag.Position + new Vector2(-160f, -140f), colErika);
                    AddImprovementNode(flA1, "Flamme Cleyane : Fournaise Cleyane", sMag.Position + new Vector2(-260f, -170f), colErika);

                    var spVoile = AddSpecNode(nMAG_E, "Voile de Cendres", sMag.Position + new Vector2(-60f, 10f), colErika);
                    var voA1 = AddImprovementNode(spVoile, "Voile de Cendres : Rideau Aveuglant", sMag.Position + new Vector2(-160f, 30f), colErika);
                    AddImprovementNode(voA1, "Voile de Cendres : Cendre Mémoire", sMag.Position + new Vector2(-260f, 50f), colErika);

                    var spLame = AddSpecNode(nMA, "Lame d'Escorte", sForce.Position + new Vector2(120f, 240f), colErika);
                    var lmA1 = AddImprovementNode(spLame, "Lame d'Escorte : Riposte Enflammée", sForce.Position + new Vector2(220f, 300f), colErika);
                    AddImprovementNode(lmA1, "Lame d'Escorte : Jugement du Creuset", sForce.Position + new Vector2(310f, 360f), colErika);

                    var spSang = AddSpecNode(nEND, "Sang-Chaud Cleyan", sCon.Position + new Vector2(80f, 120f), colErika);
                    var scA1 = AddImprovementNode(spSang, "Sang-Chaud Cleyan : Fièvre Combative", sCon.Position + new Vector2(170f, 180f), colErika);
                    AddImprovementNode(scA1, "Sang-Chaud Cleyan : Cœur de Fournaise", sCon.Position + new Vector2(260f, 240f), colErika);
                }

                // Constellation du Commandant (voir VanceCharacter.RegisterSpecializations) :
                // autorité sur le pilier du commandement, œil sur le prisme cognitif,
                // tir d'ordre sur le vecteur, bras cybernétique sur le bastion.
                if (isVance)
                {
                    var colVance = new Color(0.55f, 0.85f, 0.35f);
                    var spVanceLead = AddSpecNode(nLEA, "Autorité du Commandant", sCha.Position + new Vector2(120f, -250f), colVance);
                    var vaA1 = AddImprovementNode(spVanceLead, "Autorité du Commandant : Ordre de Feu Coordonné", sCha.Position + new Vector2(230f, -310f), colVance);
                    AddImprovementNode(vaA1, "Autorité du Commandant : Tenir la Ligne de Nefris", sCha.Position + new Vector2(320f, -370f), colVance);

                    var spVanceTac = AddSpecNode(nTAC, "Œil du Champ de Bataille", sInt.Position + new Vector2(140f, 250f), colVance);
                    var vtA1 = AddImprovementNode(spVanceTac, "Œil du Champ de Bataille : Redéploiement Éclair", sInt.Position + new Vector2(140f, 340f), colVance);
                    AddImprovementNode(vtA1, "Œil du Champ de Bataille : Enveloppement du Quadra-plen", sInt.Position + new Vector2(140f, 430f), colVance);

                    var spVanceTir = AddSpecNode(nBAL, "Tir d'Ordre", sAgi.Position + new Vector2(140f, -160f), colVance);
                    var vtrA1 = AddImprovementNode(spVanceTir, "Tir d'Ordre : Salve de Suppression", sAgi.Position + new Vector2(240f, -200f), colVance);
                    AddImprovementNode(vtrA1, "Tir d'Ordre : Sentence du Commandement", sAgi.Position + new Vector2(340f, -230f), colVance);

                    var spVanceBras = AddSpecNode(nEND, "Bras Cybernétique Renforcé", sCon.Position + new Vector2(-80f, 120f), colVance);
                    var vbA1 = AddImprovementNode(spVanceBras, "Bras Cybernétique Renforcé : Plaque Sous-cutanée de Nefris", sCon.Position + new Vector2(-170f, 180f), colVance);
                    AddImprovementNode(vbA1, "Bras Cybernétique Renforcé : Protocole Dernier Rempart", sCon.Position + new Vector2(-260f, 240f), colVance);
                }
            }
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
    }
}
