using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Core.Inventory;

namespace Killtime.Multi
{

    [Serializable]
    public class VTTMember
    {
        public string clientId;
        public string username;
        public string role; // "gm" | "player"
        public string userId;

        public bool IsGM => role == VTTProtocol.RoleGM;
    }

    [Serializable]
    public class VTTPresencePayload
    {
        public string type;
        public string code;
        public string tableName;
        public string visibility;
        public int maxPlayers;
        public string buildVersion;
        public List<VTTMember> members;
    }

    [Serializable]
    public class VTTWelcomePayload
    {
        public string type;
        public string clientId;
        public string username;
        public string userId;
    }

    [Serializable]
    public class VTTRoomInfoPayload
    {
        public string type;
        public string code;
        public string role;
        public string tableName;
        public string visibility;
        public int maxPlayers;
        public string buildVersion;
        public List<VTTMember> members;
    }

    [Serializable]
    public class VTTRoleUpdatedPayload
    {
        public string type;
        public string code;
        public string targetId;
        public string role;
        public string reason;
        public List<VTTMember> members;
    }

    [Serializable]
    public class VTTVisibilityUpdatedPayload
    {
        public string type;
        public string code;
        public string visibility;
    }

    [Serializable]
    public class VTTErrorPayload
    {
        public string type;
        public string code;
        public string message;
    }

    [Serializable]
    public class VTTOpEnvelope
    {
        public string type; // toujours "op"
        public string from;
        public string fromName;
        public string fromRole;
        public string op;
        public long at;
        // payload reste brut : chaque système (TableSync, chat, dés) le parse à son tour.
    }

    // --- Annuaire public des tables (GET /api/vtt/rooms) ---
    // JsonUtility ne parse pas les tableaux racine : le hub renvoie {"rooms":[...]}.

    [Serializable]
    public class VTTPublicRoom
    {
        public string code;
        public string tableName;
        public string buildVersion;
        public string gmName;
        public int players;
        public int maxPlayers;
        public long createdAt;
        public long lastActive;
    }

    [Serializable]
    public class VTTRoomDirectoryResponse
    {
        public List<VTTPublicRoom> rooms;
        public int count;
        public string build;
    }

    [Serializable]
    public struct VTTCoord
    {
        public int q;
        public int r;

        public VTTCoord(int q, int r)
        {
            this.q = q;
            this.r = r;
        }
    }

    /// <summary>
    /// Paramètres de la table et de l'IA de combat (Livre V & Axiome Fondateur).
    /// Diffusés par le GM via room_settings, appliqués en miroir chez les joueurs.
    /// </summary>
    [Serializable]
    public class VTTRoomSettingsPayload
    {
        public bool isAIEnabled = true;
        public int aiMode = 0; // 0: Normal, 1: FullAuto
        public int aiPersonality = 0; // Balanced, Aggressive, Tactician, Survivor
        public float aiActionDelay = 0.8f;
        public float aiRetreatRatio = 0.25f;
        public int aiDefensiveReserve = 1;
        public string activeMapName = "";
    }

    /// <summary>
    /// Entrée individuelle de jet d'initiative pour le contrôle du tour.
    /// </summary>
    [Serializable]
    public class VTTInitiativeEntry
    {
        public string unitId;
        public string die; // "D6", "2D8", etc.
        public int rawRoll;
        public int total;
        public int rapidite;
        public int agilite;
        public int intelligence;
    }

    [Serializable]
    public class VTTUnitStateSyncEntry
    {
        public string unitId;
        public int q;
        public int r;
        public float rotationY;
        public int currentHealth;
        public int maxHealth;
        public int currentAP;
        public int maxAP;
        public int essoufflement;
        public bool isAlive;
        public bool isDead;
        public int activeStatus;
        public List<string> statusEffects = new();
        // Brouillard asymétrique : portée sensorielle (Vision D6) + cap (cône).
        // Renseignés par le GM dans state_sync ; défaut serveur vision=6, yaw=0.
        public int vision;
        public int ouie;
        public float facingYaw;
        // Spécialités de vision (0/1) : le hub applique le même assouplissement
        // que le client (panoramique 360°, thermique à travers les parois).
        public int pano;
        public int thermal;
        // Faction (0/1) : le hub s'en sert pour le repli coopératif (sans
        // revendication, un client voit le champ de toute sa faction).
        public int isPlayer;
    }

    /// <summary>
    /// Contrôle séquentiel du tour (Round, initiative, unité active).
    /// </summary>
    [Serializable]
    public class VTTTurnControlPayload
    {
        public string action; // "round_start" | "turn_start" | "end_turn" | "combat_ended" | "state_sync" | "target_sync"
        public int round = 1;
        public string activeUnitId = "";
        public string targetUnitId = "";
        public int targetQ;
        public int targetR;
        public string outcome = ""; // "Victory" | "Defeat" | "InProgress"
        public List<VTTInitiativeEntry> turnOrder = new();
        public List<VTTUnitStateSyncEntry> unitStates = new();
        // Brouillard §21.4 : portée d'ambiance imposée par le MJ (0 = inchangée).
        public int sightRange;
        // RD-089 : parité plateau complète — zones persistantes (fumée, gaz, feu
        // au sol, durée restante en rounds) + objets au sol, pour réconciliation
        // immédiate des clients connectés ou reconnectés. Info publique de table
        // (effets observables) : le hub les relaie sans filtrage par destinataire.
        // groundSync = 1 : le snapshot sol est présent. Un GM d'ancien build
        // envoie 0 (listes vides) et le client ignore la passe sol au lieu
        // d'effacer ses zones/objets (compatibilité builds mixtes).
        public int groundSync;
        public List<VTTSmokeZoneEntry> smokeZones = new();
        public List<VTTDroppedWeaponPayload> droppedWeapons = new();
    }

    /// <summary>
    /// Intention d'action émise par un joueur client vers le Maître du Jeu pour résolution autoritaire.
    /// </summary>
    [Serializable]
    public class VTTActionRequestPayload
    {
        public string action = ""; // "attack" | "grenade" | "breath" | "end_turn" | "select_target"
        public string actorId = "";
        public string targetId = "";
        public int targetedPart;
        public bool cancelPenaltyWithAP;
        public int attackSkill;
        public int defenseSkill;
        public int attackerBonusAP;
        public int attackerPE;

        public int targetQ;
        public int targetR;
        public string grenadeItemId = "";
        public bool useLauncher;
        public bool aimed;

        public int breathGainAP = 2;

        // RD-089 : objet au sol visé (actions "pickup" | "kick" | "destroy_pickup").
        public string dropId = "";
    }

    /// <summary>
    /// Action de combat résolue par le GM et reproduite séquentiellement chez les joueurs.
    /// </summary>
    [Serializable]
    public class VTTCombatActionPayload
    {
        public string action; // "move" | "attack" | "grenade" | "spell" | "breath"
        public string actorId;
        public string targetId;

        // Déplacement
        public int destQ;
        public int destR;
        public List<VTTCoord> path = new();
        public int apCost;
        // Brouillard asymétrique : cap + portée de l'unité qui bouge (filtre serveur).
        public float facingYaw;
        public int vision;
        // Spécialités de vision (0/1), miroir de VTTUnitStateSyncEntry.
        public int pano;
        public int thermal;

        // Attaque ciblée anatomique
        public int targetedPart; // int cast de BodyPart
        public int actualHitPart;
        public int attackSkill;
        public int defenseSkill;
        public bool isHit;
        public bool isCritical;
        public bool wasDeflected;
        public int finalDamageApplied;
        public int rawDamage;
        public int armorAbsorbed;
        public int differential;
        public string inflictedStatus;
        public bool isMeleeStrike;
        public int attackerCostAP;
        public int defenderCostAP;
        public int attackerPE;
        public int defenderPE;
        public int attackerNewAP;
        public int defenderNewAP;
        public int defenderNewHealth;
        public string combatLog;

        // Grenade
        public string grenadeItemId;
        public bool useLauncher;
        public bool aimed;
        public int blastRadius;

        // Sort
        public string spellName;
        public int spellDamage;

        // Souffle
        public int breathGainAP;

        // Synchronisation d'état, de position et d'orientation
        public int defenderNewActiveStatus;
        public bool defenderIsDead;
        public float actorRotationY;
        public float defenderRotationY;
        public int defenderNewQ;
        public int defenderNewR;
    }

    /// <summary>
    /// Message de chat textuel de table.
    /// </summary>
    [Serializable]
    public class VTTChatPayload
    {
        public string text = "";
    }

    /// <summary>
    /// Jet de dés synchronisé à la table (D100, dés d'initiative, tests).
    /// </summary>
    [Serializable]
    public class VTTDicePayload
    {
        public int roll;
        public string formula = ""; // ex: "1D100", "2D6+3"
        public string detail = "";  // ex: "(14) + 3 = 17"
        public string reason = "";  // ex: "Jet d'initiative", "Test d'Agilité"
        public bool isCritical;
        public bool isFumble;
    }

    /// <summary>
    /// Opération de contrôle de scène (GM uniquement) : ping d'hexagone, caméra, brouillard, éclairage.
    /// </summary>
    [Serializable]
    public class VTTSceneControlPayload
    {
        public string action = ""; // "ping_hex" | "focus_camera" | "fog_reveal" | "fog_hide" | "set_lighting"
        public int targetQ;
        public int targetR;
        public float cameraX;
        public float cameraY;
        public float cameraZoom;
        public string colorHex = "#FFDD00";
        public string message = "";
    }

    /// <summary>
    /// Revendication d'avatars possédés par un client (brouillard asymétrique).
    /// </summary>
    [Serializable]
    public class VTTUnitClaimPayload
    {
        public List<string> unitIds = new();
    }

    /// <summary>
    /// Opération carte monde overworld (GM uniquement) : état compact de campagne,
    /// ping ou focus d'un secteur. Le contenu complet de la carte (WorldMaps/*.json)
    /// voyage hors-bande (export/import) ; seuls l'identité (mapName+revision) et
    /// l'état de campagne transitent ici. Prévu pour la feature joueur VTT.
    /// </summary>
    [Serializable]
    public class VTTWorldMapPayload
    {
        public string action = ""; // "state" | "ping_sector" | "focus_sector"
        public string mapName = "";
        public int revision;
        public string partyNodeId = "";
        public List<string> unlocked = new();
        public List<string> visited = new();
        public int jours;
        public string sectorId = "";
        public string colorHex = "#FFDD00";
        public string message = "";
    }

    /// <summary>
    /// Objet au sol synchronisé (RD-089) : arme ou objet tenu tombé (K.O.,
    /// désarmement, lancer d'arme). L'exemplaire est unique : l'item voyage
    /// en clair (JsonUtility imbriqué) et le GM fait autorité. dropId stable
    /// inter-clients (GUID assigné à la création) pour le suivi pickup/kick
    /// et la réconciliation via state_sync.
    /// </summary>
    [Serializable]
    public class VTTDroppedWeaponPayload
    {
        public string dropId = "";
        public string formerOwner = "";
        public InventoryItem item;
        // Pose monde exacte (le client pose sans re-simuler la chute).
        public float x;
        public float y;
        public float z;
        // Ramassage / shoot : acteur (nom de GameObject d'unité) + origine.
        public string actorId = "";
        public float kx;
        public float ky;
        public float kz;
        public float force;
    }

    /// <summary>
    /// Zone persistante active (RD-089) : fumigène, gaz, feu au sol...
    /// Version plate de SmokeZone (entiers + chaîne) pour le state_sync.
    /// </summary>
    [Serializable]
    public class VTTSmokeZoneEntry
    {
        public int q;
        public int r;
        public int radius;
        public string kind = "";
        public int statuses;
        public int turnsLeft;
        public bool arcanotech;
    }

    /// <summary>
    /// Trame audio de voix compressée transmise via WebSocket.
    /// </summary>
    [Serializable]
    public class VTTVoicePayload
    {
        public int codec;        // VoiceCodecType
        public int sampleRate;   // 16000 ou 8000
        public int seq;          // Numéro de séquence
        public string data = ""; // Audio compressé encodé Base64
    }

    /// <summary>
    /// Trame vidéo d'une image de webcam compressée transmise via WebSocket.
    /// </summary>
    [Serializable]
    public class VTTVideoPayload
    {
        public int width;
        public int height;
        public int seq;
        public string data = ""; // Image JPEG encodée Base64
    }
}
