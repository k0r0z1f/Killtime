using System;
using System.Collections.Generic;

namespace Killtime.Story
{
    public enum ScenarioNodeKind
    {
        Briefing,
        Choice,
        Objective,
        Resolution
    }

    public enum ScenarioEffectType
    {
        SetRoute,
        AddFlag,
        AddInteger,
        AddJournal
    }

    [Serializable]
    public class ScenarioEffect
    {
        public ScenarioEffectType Type;
        public StoryRoute Route;
        public string Key;
        public int Amount;
        public string Text;
    }

    [Serializable]
    public class ScenarioObjective
    {
        public string Id;
        public string Label;
        public string Detail;
        public bool Optional;
    }

    [Serializable]
    public class ScenarioChoice
    {
        public string Id;
        public string Label;
        public string Consequence;
        public string NextNodeId;
        public List<ScenarioEffect> Effects = new();
    }

    [Serializable]
    public class ScenarioNode
    {
        public string Id;
        public ScenarioNodeKind Kind;
        public string Title;
        public string Location;
        public string Body;
        public string ContinueLabel;
        public string NextNodeId;
        public List<ScenarioObjective> Objectives = new();
        public List<ScenarioChoice> Choices = new();
        public List<ScenarioEffect> EnterEffects = new();
    }

    /// <summary>
    /// Données d'une scène. Le directeur ne connaît ni la carte ni le combat:
    /// il orchestre des nœuds, des objectifs, des choix et leurs conséquences.
    /// Les futures scènes peuvent être alimentées par ScriptableObject sans modifier
    /// le runtime, tant qu'elles produisent cette structure.
    /// </summary>
    [Serializable]
    public class ScenarioDefinition
    {
        public string Id;
        public string Volume;
        public string Title;
        public string CanonReference;
        public string FirstNodeId;
        public List<ScenarioNode> Nodes = new();

        public ScenarioNode FindNode(string nodeId) => Nodes.Find(node => node.Id == nodeId);
    }
}
