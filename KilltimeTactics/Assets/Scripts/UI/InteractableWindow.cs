using System;
using UnityEngine;
using Killtime.Core.Character;
using Killtime.Story.Data;
using Killtime.Tactics.Units;

namespace Killtime.UI
{
    /// <summary>
    /// Fenêtre flottante générique des objets interactifs de scène (window id 887).
    /// Son contenu entier (titre, description, actions, tests de compétence, saut)
    /// vient du JSON de scène ; aucune référence hardcodée à un objet particulier.
    /// </summary>
    public class InteractableWindow : FloatingWindow<InteractableWindow>
    {
        protected override int WindowId => 887;
        protected override string Title => _currentData?.DisplayName ?? "Objet interactif";
        protected override Vector2 MinSize => new Vector2(360f, 240f);
        protected override Rect DefaultRect => new Rect(60f, 110f, 480f, 360f);

        private SceneInteractableSpawnData _currentData;
        private TacticalUnit _currentUnit;
        private Action<TacticalUnit, SceneInteractableSpawnData, SceneInteractableAction> _onAttemptAction;
        private Action<string> _onJump;
        private Func<string, bool> _isActionConsumed;

        private string _currentDescription;
        private string _lastResultMessage;
        private bool _lastResultSuccess;
        private string _pendingTriggerNodeId;
        private Vector2 _scroll;

        /// <summary>Ouvre ou replace la fenêtre au premier plan avec les données d'un interactif.</summary>
        public static void Open(SceneInteractableSpawnData data, TacticalUnit unit,
            Action<TacticalUnit, SceneInteractableSpawnData, SceneInteractableAction> onAttemptAction,
            Action<string> onJump,
            Func<string, bool> isActionConsumed = null)
        {
            Open();
            Instance?.Setup(data, unit, onAttemptAction, onJump, isActionConsumed);
        }

        /// <summary>Ferme la fenêtre proprement.</summary>
        public static void Close()
        {
            Instance?.CloseWindow();
        }

        /// <summary>Le contrôleur appelle cela après résolution d'une action pour afficher le résultat.</summary>
        public void SetResult(bool success, string message, string triggerNodeId)
        {
            _lastResultSuccess = success;
            _lastResultMessage = message ?? "";
            _pendingTriggerNodeId = triggerNodeId ?? "";
        }

        public void Setup(SceneInteractableSpawnData data, TacticalUnit unit,
            Action<TacticalUnit, SceneInteractableSpawnData, SceneInteractableAction> onAttemptAction,
            Action<string> onJump,
            Func<string, bool> isActionConsumed)
        {
            _currentData = data;
            _currentUnit = unit;
            _onAttemptAction = onAttemptAction;
            _onJump = onJump;
            _isActionConsumed = isActionConsumed;
            _lastResultMessage = "";
            _lastResultSuccess = false;
            _pendingTriggerNodeId = "";
            _scroll = Vector2.zero;

            PickDescription();
        }

        protected override void OnOpened()
        {
            base.OnOpened();
            PickDescription();
        }

        protected override void OnClosed()
        {
            base.OnClosed();
            _currentData = null;
            _currentUnit = null;
            _onAttemptAction = null;
            _onJump = null;
            _isActionConsumed = null;
            _lastResultMessage = "";
            _pendingTriggerNodeId = "";
        }

        private void PickDescription()
        {
            if (_currentData == null)
            {
                _currentDescription = "";
                return;
            }
            if (_currentData.DescriptionVariants != null && _currentData.DescriptionVariants.Count > 0)
            {
                int idx = UnityEngine.Random.Range(0, _currentData.DescriptionVariants.Count);
                _currentDescription = _currentData.DescriptionVariants[idx] ?? _currentData.Description ?? "";
            }
            else
            {
                _currentDescription = _currentData.Description ?? "";
            }
        }

        protected override void DrawContent()
        {
            if (_currentData == null) return;

            var wrap = new GUIStyle(GUI.skin.label) { wordWrap = true, richText = true };
            var small = new GUIStyle(GUI.skin.label) { fontSize = 10, wordWrap = true };

            _scroll = GUILayout.BeginScrollView(_scroll, GUIStyle.none, GUI.skin.verticalScrollbar);

            // Description
            if (!string.IsNullOrWhiteSpace(_currentDescription))
            {
                GUILayout.Label(_currentDescription, wrap);
                GUILayout.Space(8f);
            }

            var actions = _currentData.GetEffectiveActions();
            for (int i = 0; i < actions.Count; i++)
            {
                DrawAction(actions[i]);
                GUILayout.Space(6f);
            }

            // Zone résultat
            if (!string.IsNullOrEmpty(_lastResultMessage))
            {
                GUILayout.Space(8f);
                Color prevColor = GUI.color;
                GUI.color = _lastResultSuccess ? new Color(0.5f, 1f, 0.6f) : new Color(1f, 0.5f, 0.5f);
                GUILayout.Label(_lastResultMessage, wrap);
                GUI.color = prevColor;
            }

            // Bouton suivre si l'action résolue a un nœud cible
            if (!string.IsNullOrWhiteSpace(_pendingTriggerNodeId))
            {
                GUILayout.Space(8f);
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                GUI.backgroundColor = new Color(0.25f, 0.85f, 0.45f);
                if (GUILayout.Button($"Suivre \u2794 {_pendingTriggerNodeId}", GUILayout.Height(28f), GUILayout.Width(220f)))
                {
                    string nodeId = _pendingTriggerNodeId;
                    _pendingTriggerNodeId = "";
                    try { _onJump?.Invoke(nodeId); } catch (Exception e) { Debug.LogWarning($"[InteractableWindow] onJump: {e.Message}"); }
                }
                GUI.backgroundColor = Color.white;
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndScrollView();

            // Fermer en bas
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Fermer", GUILayout.Width(90f), GUILayout.Height(24f)))
            {
                CloseWindow();
            }
            GUILayout.EndHorizontal();
        }

        private void DrawAction(SceneInteractableAction action)
        {
            if (action == null) return;
            string label = string.IsNullOrWhiteSpace(action.Label) ? "Interagir" : action.Label;
            string key = GetActionKey(action.ActionId);
            bool consumed = _isActionConsumed != null && _isActionConsumed(key);

            string testLine;
            if (action.RequiredSkill < 0 || action.SkillThreshold <= 0)
            {
                testLine = "Action libre";
            }
            else
            {
                var skill = (SkillType)action.RequiredSkill;
                testLine = $"SD {action.SkillThreshold} · {SkillDefinitions.GetDisplayName(skill)}";
            }

            GUILayout.Label(testLine, new GUIStyle(GUI.skin.label) { fontSize = 10, wordWrap = true });

            using (new GUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                GUI.enabled = !consumed;
                if (GUILayout.Button(consumed ? $"{label} (fait)" : label, GUILayout.Height(28f), GUILayout.Width(220f)))
                {
                    InvokeAction(action);
                }
                GUI.enabled = true;
                GUILayout.FlexibleSpace();
            }
        }

        private void InvokeAction(SceneInteractableAction action)
        {
            try
            {
                _onAttemptAction?.Invoke(_currentUnit, _currentData, action);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[InteractableWindow] onAttemptAction: {e.Message}");
            }
        }

        private string GetActionKey(string actionId)
        {
            string id = _currentData?.InteractableId ?? "";
            return $"{id}/{actionId}";
        }
    }
}
