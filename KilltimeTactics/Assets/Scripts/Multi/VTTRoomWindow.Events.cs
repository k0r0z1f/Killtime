using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.UI;
using Killtime.Multi.Voice;

namespace Killtime.Multi
{
    public partial class VTTRoomWindow : FloatingWindow<VTTRoomWindow>
    {
        // Callbacks reseau -> UI (HandleConnected...HandleChatReceived).

        // ---------- Événements réseau -> UI ----------

        private void HandleConnected()
        {
            _statusMessage = "Connecté au hub. Créez ou rejoignez une room.";
        }

        private void HandleDisconnected(string reason)
        {
            _statusMessage = $"Déconnecté ({reason}).";
        }

        private void HandleJoined()
        {
            if (_room != null)
                _statusMessage = $"Dans la room {_room.RoomCode} en tant que {(_room.IsGM ? "GM 👑" : "Joueur 🎲")}.";
        }

        private void HandleLeft(string reason)
        {
            _statusMessage = $"Hors room ({reason}).";
            _nextDirRefresh = 0f; // re-liste les tables publiques en sortie de room
        }

        private void HandlePresence()
        {
            // L'état est relu à chaque OnGUI ; rien à faire ici.
        }

        private void HandleRoleChanged(string targetId, string role)
        {
            if (_room != null && targetId == _room.ClientId)
                _statusMessage = role == VTTProtocol.RoleGM
                    ? "Vous êtes maintenant GM 👑."
                    : "Vous êtes maintenant Joueur 🎲.";
            else
                _statusMessage = $"Rôle mis à jour : {targetId} -> {role}.";
        }

        private void HandleKicked(string reason)
        {
            _statusMessage = $"Expulsé de la room ({reason}).";
            _nextDirRefresh = 0f;
        }

        private void HandleVisibilityChanged(string visibility)
        {
            _statusMessage = visibility == VTTProtocol.VisibilityPrivate
                ? "Table privée — absente de l'annuaire."
                : "Table publique — visible dans l'annuaire.";
        }

        private void HandleRoomError(string message)
        {
            _statusMessage = $"⚠️ {message}";
        }

        private void HandleChatReceived(string from, string role, string text)
        {
            string badge = role == VTTProtocol.RoleGM ? "👑" : "🎲";
            AddChatLine($"<b>{from} {badge} :</b> {text}");
        }
    }
}
