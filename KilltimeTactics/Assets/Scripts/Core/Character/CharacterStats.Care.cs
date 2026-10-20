using System;
using Killtime.Core.Dice;
using Killtime.Core.Combat;

namespace Killtime.Core.Character
{
    // Split move-only de CharacterStats — Care : Soins, Soins Majeurs, resurrection RD-083.
    public partial class CharacterStats
    {

        /// <summary>
        /// Soin par consommable marketplace (Livre VII §28.3 : seringues, bandage).
        /// Ne réanime pas (IsDead ou Inconscient profond exclu) : la réanimation
        /// reste à la Défibrillation. Plafonné à MaxHealth. Retourne le soin réel.
        /// RD-045 : tout soin stoppe le Saignement (compression + cautérisation).
        /// Empoisonne/EnFeu/Asphyxie exigent un traitement dédié (kit, eau, airway).
        /// RD-083 (Livre VI §24.3) : SOIN ORDINAIRE — ne lève JAMAIS Souffrant,
        /// même si le montant dépasse CON×2. Seul ApplyMajorCare (Premiers Soins /
        /// Chirurgie en un seul coup >= CON×2) referme la blessure critique.
        /// </summary>
        public int Heal(int amount)
        {
            if (IsDead || amount <= 0) return 0;
            if (ActiveStatus.HasFlag(StatusEffect.Inconscient)) return 0;
            if (!IsAlive && CurrentHealth <= 0) return 0;
            int before = CurrentHealth;
            CurrentHealth = Math.Min(MaxHealth, CurrentHealth + amount);
            if (ActiveStatus.HasFlag(StatusEffect.Saignement))
                RemoveStatus(StatusEffect.Saignement);
            if (CurrentHealth > (int)Math.Ceiling(MaxHealth * 0.28f))
            {
                HasTestedMorale = false;
            }
            return Math.Max(0, CurrentHealth - before);
        }

        /// <summary>
        /// Bonus temporaire d'encaissement (Antidouleurs +2, Livre VII).
        /// Cumulable en combat, réinitialisé par RecalculateDerivedStats.
        /// </summary>
        public void AddEncaissementBonus(int amount)
        {
            if (amount == 0) return;
            EncaissementThreshold = Math.Max(0, EncaissementThreshold + amount);
        }

        // =====================================================================
        // RD-083 : SOUFFRANT & SOINS MAJEURS (Livre VI §24.3, §25.1)
        // =====================================================================

        /// <summary>
        /// Seuil des Soins Majeurs (Livre VI §25.1) : soin unique >= CON × 2
        /// via Premiers Soins / Chirurgie pour refermer la blessure critique.
        /// Paramétrable via CoreRulesConfig.SouffrantMajorCareMultiplier.
        /// </summary>
        public int GetMajorCareThreshold()
        {
            int mult = 2;
            try { mult = Rules.CoreRulesConfig.Instance.SouffrantMajorCareMultiplier; }
            catch { mult = 2; }
            // Migration : 0 = ancienne sauvegarde sans le champ → défaut Codex (×2).
            if (mult < 1) mult = 2;
            return Math.Max(1, Attributes.Constitution * mult);
        }

        public bool IsSouffrant => ActiveStatus.HasFlag(StatusEffect.Souffrant);

        /// <summary>
        /// Soin ordinaire bloqué par Souffrant ? Vrai si Souffrant actif : les
        /// seringues / bandages / repos restaurent des PV (Heal) mais ne lèvent
        /// jamais le statut. Sert aux logs UI.
        /// </summary>
        public bool IsOrdinaryCareBlockedBySouffrant() => IsSouffrant;

        /// <summary>
        /// Soins Majeurs (Livre VI §25.1) : épreuve de Premiers Soins / Chirurgie
        /// au chevet du blessé soignant D'UN SEUL COUP >= CON×2 dégâts.
        /// Soigne les PV (plafond MaxHealth, ne réanime pas) + stoppe le
        /// Saignement comme un soin ordinaire. Si le seuil est atteint et que
        /// Souffrant est actif, le statut est levé. Sous le seuil : PV rendus
        /// mais Souffrant persiste (blessure toujours ouverte).
        /// Retourne le détail pour logs d'arène et tests.
        /// </summary>
        public MajorCareResult ApplyMajorCare(int healAmount)
        {
            var result = new MajorCareResult
            {
                RequestedAmount = Math.Max(0, healAmount),
                Threshold = GetMajorCareThreshold(),
                HadSouffrant = IsSouffrant,
                Log = string.Empty
            };
            if (IsDead || healAmount <= 0) return result;
            if (ActiveStatus.HasFlag(StatusEffect.Inconscient)) return result;
            if (!IsAlive && CurrentHealth <= 0) return result;

            int before = CurrentHealth;
            CurrentHealth = Math.Min(MaxHealth, CurrentHealth + healAmount);
            result.Healed = Math.Max(0, CurrentHealth - before);

            if (ActiveStatus.HasFlag(StatusEffect.Saignement))
                RemoveStatus(StatusEffect.Saignement);
            if (CurrentHealth > (int)Math.Ceiling(MaxHealth * 0.28f))
                HasTestedMorale = false;

            result.MeetsThreshold = result.RequestedAmount >= result.Threshold;
            if (result.HadSouffrant && result.MeetsThreshold)
            {
                RemoveStatus(StatusEffect.Souffrant);
                result.SouffrantLifted = true;
                result.Log = $"🩹 <b>SOINS MAJEURS</b> : {Name} referme sa blessure critique (+{result.Healed} PV, seuil CON×2 = {result.Threshold}) ➔ <b>Souffrant LEVÉ</b>.";
            }
            else if (result.HadSouffrant)
            {
                result.SouffrantLifted = false;
                result.Log = $"🩹 <b>SOINS MAJEURS INSUFFISANTS</b> : {Name} (+{result.Healed} PV, {result.RequestedAmount}/{result.Threshold} requis) ➔ <b>Souffrant PERSISTE</b> (blessure toujours ouverte).";
            }
            else
            {
                result.Log = $"🩹 <b>Soins Majeurs</b> : {Name} (+{result.Healed} PV).";
            }
            return result;
        }

        // =====================================================================
        // RD-083 : SÉQUELLE POST-MORTEM (Livre VI §25.3 — Poids du Trépas)
        // =====================================================================

        /// <summary>
        /// Instant réel (UTC) de la dernière résurrection réussie. Null = jamais
        /// ressuscité (aucune séquelle). Base du malus -1 ec pendant (10-CON)j.
        /// </summary>
        public DateTime? LastResurrectionUtc { get; set; } = null;

        /// <summary>
        /// Durée de convalescence post-résurrection en jours réels : 10 - CON
        /// (plancher 0 : CON 10+ = âme réancrée immédiatement). Paramétrable.
        /// </summary>
        public int GetResurrectionSequelaeDurationDays()
        {
            int baseDays = 10;
            try { baseDays = Rules.CoreRulesConfig.Instance.ResurrectionSequelaeBaseDays; }
            catch { baseDays = 10; }
            // Migration : 0 = ancienne sauvegarde sans le champ → défaut Codex (10).
            if (baseDays < 1) baseDays = 10;
            return Math.Max(0, baseDays - Attributes.Constitution);
        }

        /// <summary>
        /// Vrai si le Poids du Trépas s'applique encore à l'instant donné
        /// (défaut : maintenant UTC). Faux si jamais ressuscité ou convalescence
        /// purgée, ou durée nulle (CON élevée).
        /// </summary>
        public bool HasResurrectionSequelae(DateTime? nowUtc = null)
        {
            if (!LastResurrectionUtc.HasValue) return false;
            int duration = GetResurrectionSequelaeDurationDays();
            if (duration <= 0) return false;
            DateTime now = nowUtc ?? DateTime.UtcNow;
            double elapsedDays = (now - LastResurrectionUtc.Value).TotalDays;
            if (elapsedDays < 0) return true;
            return elapsedDays < duration;
        }

        /// <summary>Jours réels restants de convalescence (arrondi supérieur, 0 si purgée).</summary>
        public int GetResurrectionSequelaeRemainingDays(DateTime? nowUtc = null)
        {
            if (!HasResurrectionSequelae(nowUtc)) return 0;
            int duration = GetResurrectionSequelaeDurationDays();
            DateTime now = nowUtc ?? DateTime.UtcNow;
            double elapsed = (now - LastResurrectionUtc.Value).TotalDays;
            return Math.Max(0, (int)Math.Ceiling(duration - Math.Max(0, elapsed)));
        }

        /// <summary>
        /// Malus du Poids du Trépas sur tous les jets (Livre VI §25.3 : -1 ec).
        /// 0 si aucune séquelle active. Paramétrable via CoreRulesConfig.
        /// </summary>
        public int GetResurrectionSequelaeModifier(DateTime? nowUtc = null)
        {
            if (!HasResurrectionSequelae(nowUtc)) return 0;
            try
            {
                int penalty = Rules.CoreRulesConfig.Instance.ResurrectionSequelaePenalty;
                // Migration : 0 = ancienne sauvegarde sans le champ → défaut Codex (-1).
                // (La fenêtre de règles ne propose que -3..-1, 0 n'est pas exprimable.)
                return penalty == 0 ? -1 : penalty;
            }
            catch { return -1; }
        }

        /// <summary>
        /// Résurrection (Livre VI §25.3) : rappelle l'âme dans la chair.
        /// Lève la mort (IsDead=false, Dernier Souffle purgé), restaure les PV
        /// (défaut 1, défibrillation ; 50% pour Transfusion Symbiotique via
        /// paramètre), dissipe Inconscient/Agonisant, et HORODATE le retour
        /// pour la séquelle -1 ec pendant (10-CON) jours réels.
        /// Ne lève PAS Souffrant (blessure toujours ouverte : Soins Majeurs requis).
        /// Retourne la durée de convalescence pour logs.
        /// </summary>
        public int ApplyResurrection(int healthAfter = 1, DateTime? nowUtc = null)
        {
            DateTime now = nowUtc ?? DateTime.UtcNow;
            IsDead = false;
            IsInLastBreath = false;
            LastFatalBlowResolution = FatalBlowResolution.None;
            CurrentHealth = Math.Clamp(healthAfter <= 0 ? 1 : healthAfter, 1, Math.Max(1, MaxHealth));
            RemoveStatus(StatusEffect.Inconscient);
            RemoveStatus(StatusEffect.Agonisant);
            HasTestedMorale = false;
            LastResurrectionUtc = now;
            return GetResurrectionSequelaeDurationDays();
        }
    }
}
