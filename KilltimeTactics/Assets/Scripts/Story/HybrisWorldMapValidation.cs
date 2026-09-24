using System;
using System.Collections.Generic;
using UnityEngine;

namespace Killtime.Story
{
    public enum WorldMapIssueSeverity
    {
        Info,
        Warning,
        Error
    }

    [Serializable]
    public class WorldMapIssue
    {
        public WorldMapIssueSeverity Severity;
        public string Code;
        public string Message;
        public string NodeId;

        public WorldMapIssue(WorldMapIssueSeverity severity, string code, string message, string nodeId = "")
        {
            Severity = severity;
            Code = code;
            Message = message;
            NodeId = nodeId ?? "";
        }
    }

    /// <summary>
    /// Validation pure (sans UnityEngine) d'une carte monde : ids, références,
    /// doublons, orphelins, atteignabilité depuis le départ, plages de valeurs.
    /// Utilisée par l'éditeur et les tests ; scenarioExists est injecté pour
    /// garder cette classe indépendante du catalogue (null = contrôle ignoré).
    /// </summary>
    public static class HybrisWorldMapValidation
    {
        public static List<WorldMapIssue> Validate(
            List<HybrisSectorNode> nodes,
            List<HybrisSectorLink> links,
            Func<string, bool> scenarioExists = null)
        {
            var issues = new List<WorldMapIssue>();
            nodes ??= new List<HybrisSectorNode>();
            links ??= new List<HybrisSectorLink>();

            var byId = new Dictionary<string, HybrisSectorNode>(StringComparer.OrdinalIgnoreCase);
            var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < nodes.Count; i++)
            {
                var n = nodes[i];
                if (n == null)
                {
                    issues.Add(new WorldMapIssue(WorldMapIssueSeverity.Error, "node_null",
                        $"Secteur #{i} : entrée nulle."));
                    continue;
                }
                string id = (n.Id ?? "").Trim();
                if (string.IsNullOrEmpty(id))
                {
                    issues.Add(new WorldMapIssue(WorldMapIssueSeverity.Error, "node_empty_id",
                        $"Secteur « {n.Name} » : identifiant vide."));
                    continue;
                }
                if (!IsValidId(id))
                {
                    issues.Add(new WorldMapIssue(WorldMapIssueSeverity.Error, "node_bad_id",
                        $"Secteur « {id} » : seuls lettres, chiffres, '_' et '-' sont admis.",
                        id));
                }
                if (!seenIds.Add(id))
                {
                    issues.Add(new WorldMapIssue(WorldMapIssueSeverity.Error, "node_duplicate_id",
                        $"Identifiant dupliqué : « {id} ».", id));
                    continue;
                }
                byId[id] = n;

                if (string.IsNullOrWhiteSpace(n.Name))
                    issues.Add(new WorldMapIssue(WorldMapIssueSeverity.Warning, "node_empty_name",
                        $"Secteur « {id} » : nom d'affichage vide.", id));
                if (n.Danger < 0 || n.Danger > 5)
                    issues.Add(new WorldMapIssue(WorldMapIssueSeverity.Error, "node_bad_danger",
                        $"Secteur « {id} » : danger {n.Danger} hors bornes 0-5.", id));
                if (n.MapPos.x < 0f || n.MapPos.x > 1f || n.MapPos.y < 0f || n.MapPos.y > 1f)
                    issues.Add(new WorldMapIssue(WorldMapIssueSeverity.Error, "node_bad_pos",
                        $"Secteur « {id} » : position {n.MapPos} hors carte 0-1.", id));
                if (!string.IsNullOrWhiteSpace(n.LinkedScenarioId)
                    && scenarioExists != null && !scenarioExists(n.LinkedScenarioId))
                {
                    issues.Add(new WorldMapIssue(WorldMapIssueSeverity.Warning, "node_missing_scenario",
                        $"Secteur « {id} » : scène « {n.LinkedScenarioId} » introuvable dans le catalogue.",
                        id));
                }
            }

            var seenLinks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < links.Count; i++)
            {
                var l = links[i];
                if (l == null)
                {
                    issues.Add(new WorldMapIssue(WorldMapIssueSeverity.Error, "link_null",
                        $"Liaison #{i} : entrée nulle."));
                    continue;
                }
                string a = (l.FromId ?? "").Trim();
                string b = (l.ToId ?? "").Trim();
                if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
                {
                    issues.Add(new WorldMapIssue(WorldMapIssueSeverity.Error, "link_empty_end",
                        $"Liaison #{i} (« {l.Label} ») : extrémité vide."));
                    continue;
                }
                if (!byId.ContainsKey(a))
                    issues.Add(new WorldMapIssue(WorldMapIssueSeverity.Error, "link_unknown_from",
                        $"Liaison « {l.Label} » : secteur inconnu « {a} ».", a));
                if (!byId.ContainsKey(b))
                    issues.Add(new WorldMapIssue(WorldMapIssueSeverity.Error, "link_unknown_to",
                        $"Liaison « {l.Label} » : secteur inconnu « {b} ».", b));
                if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
                    issues.Add(new WorldMapIssue(WorldMapIssueSeverity.Error, "link_self",
                        $"Liaison « {l.Label} » : boucle sur elle-même ({a}).", a));
                string key = string.Compare(a, b, StringComparison.OrdinalIgnoreCase) < 0
                    ? a + "|" + b : b + "|" + a;
                if (!seenLinks.Add(key))
                    issues.Add(new WorldMapIssue(WorldMapIssueSeverity.Warning, "link_duplicate",
                        $"Liaison dupliquée : {a} ↔ {b}.", a));
                if (l.Miles < 1)
                    issues.Add(new WorldMapIssue(WorldMapIssueSeverity.Warning, "link_bad_miles",
                        $"Liaison {a} ↔ {b} : distance {l.Miles} mile(s), minimum 1.", a));
                if (l.Days < 0 || l.Days > 30)
                    issues.Add(new WorldMapIssue(WorldMapIssueSeverity.Warning, "link_bad_days",
                        $"Liaison {a} ↔ {b} : durée {l.Days} j hors bornes 0-30.", a));
                if (string.IsNullOrWhiteSpace(l.Label))
                    issues.Add(new WorldMapIssue(WorldMapIssueSeverity.Info, "link_empty_label",
                        $"Liaison {a} ↔ {b} : sans libellé.", a));
            }

            // Orphelins (aucune liaison valide).
            var connected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var l in links)
            {
                if (l == null) continue;
                if (byId.ContainsKey(l.FromId ?? "") && byId.ContainsKey(l.ToId ?? "")
                    && !string.Equals(l.FromId, l.ToId, StringComparison.OrdinalIgnoreCase))
                {
                    connected.Add(l.FromId);
                    connected.Add(l.ToId);
                }
            }
            foreach (var id in byId.Keys)
            {
                if (!connected.Contains(id))
                    issues.Add(new WorldMapIssue(WorldMapIssueSeverity.Warning, "node_orphan",
                        $"Secteur « {id} » : orphelin (aucune liaison).", id));
            }

            // Atteignabilité depuis le départ (BFS sur liens valides).
            string start = HybrisWorldMapData.StartNodeId;
            if (byId.ContainsKey(start))
            {
                var reached = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { start };
                var queue = new Queue<string>();
                queue.Enqueue(start);
                while (queue.Count > 0)
                {
                    string cur = queue.Dequeue();
                    foreach (var l in links)
                    {
                        if (l == null) continue;
                        string other = null;
                        if (string.Equals(l.FromId, cur, StringComparison.OrdinalIgnoreCase)) other = l.ToId;
                        else if (string.Equals(l.ToId, cur, StringComparison.OrdinalIgnoreCase)) other = l.FromId;
                        if (!string.IsNullOrEmpty(other) && byId.ContainsKey(other) && reached.Add(other))
                            queue.Enqueue(other);
                    }
                }
                foreach (var id in byId.Keys)
                {
                    if (!reached.Contains(id))
                        issues.Add(new WorldMapIssue(WorldMapIssueSeverity.Warning, "node_unreachable",
                            $"Secteur « {id} » : inatteignable depuis « {start} ».", id));
                }
            }

            if (issues.Count == 0)
                issues.Add(new WorldMapIssue(WorldMapIssueSeverity.Info, "ok",
                    $"Carte valide : {byId.Count} secteur(s), {links.Count} liaison(s)."));
            return issues;
        }

        public static bool IsValidId(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return false;
            foreach (char c in id)
            {
                if (!(char.IsLetterOrDigit(c) || c == '_' || c == '-')) return false;
            }
            return true;
        }

        public static int CountErrors(List<WorldMapIssue> issues)
        {
            int n = 0;
            if (issues == null) return 0;
            foreach (var i in issues)
                if (i != null && i.Severity == WorldMapIssueSeverity.Error) n++;
            return n;
        }
    }
}
