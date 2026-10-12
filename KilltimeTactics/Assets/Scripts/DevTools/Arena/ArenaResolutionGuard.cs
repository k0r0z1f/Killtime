namespace Killtime.Tactics.Arena
{
    /// <summary>
    /// Seul propriétaire du verrou de résolution (V2 : corrige F1).
    /// Remplace les 15 écritures directes de _isAttackInProgress.
    /// Vrai pendant une passe d'armes OU un lancer de grenade
    /// (anti double-clic / attente IA).
    /// </summary>
    public sealed class ArenaResolutionGuard
    {
        private bool _resolving;

        public bool IsResolving => _resolving;

        /// <summary>Entre en résolution. Retourne faux si déjà en cours (appel à ignorer).</summary>
        public bool TryEnter()
        {
            if (_resolving) return false;
            _resolving = true;
            return true;
        }

        public void Exit()
        {
            _resolving = false;
        }

        /// <summary>Déverrouille de force (reset, changement de carte, nouvel état).</summary>
        public void ForceReset()
        {
            _resolving = false;
        }
    }
}
