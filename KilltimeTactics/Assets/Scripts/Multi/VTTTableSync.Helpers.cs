using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Tactics;
using Killtime.Tactics.AI;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Units;
using Killtime.Tactics.TurnSystem;
using Killtime.Core.Combat;
using Killtime.Core.Dice;
using Killtime.Core.Character;
using Killtime.CameraSystem;
using Killtime.Audio;
using Killtime.UI;

namespace Killtime.Multi
{
    public partial class VTTTableSync : MonoBehaviour
    {
        // =========================================================================
        // HELPERS D'EXTRACTION JSON
        // =========================================================================

        private static string ExtractString(string json, string field)
        {
            return VTTProtocol.ExtractString(json, field);
        }

        private static int ExtractInt(string json, string field, int fallback)
        {
            string s = VTTProtocol.ExtractString(json, field, "");
            if (int.TryParse(s, out int val)) return val;
            return fallback;
        }

        private static string ExtractText(string payloadJson)
        {
            return VTTProtocol.ExtractString(payloadJson, "text");
        }

        private static string ExtractObject(string json, string field)
        {
            return VTTProtocol.ExtractObject(json, field);
        }
    }
}
