using Killtime.Core.Character;

namespace Killtime.Core.Combat
{
    /// <summary>
    /// Manœuvres véhicules RD-051 (Livre V §22.3) — partiel sans modification
    /// du cœur SpecialManeuverService : embarquement 2 PA, écrasement piéton,
    /// routage dégâts lourdes vs véhicules. Façade : champs + ctor dans
    /// SpecialManeuverService.cs, contenu dans les autres partiels.
    ///
    /// TODO UI : exposer ces manœuvres dans CombatActionRegistry (actions
    /// contextuelles « Embarquer 2PA » / « Écraser ») quand un véhicule est
    /// adjacent — l'API ci-dessous est prête, seul le branchement registre
    /// + arène (placement figurine, trajectoire) reste à faire.
    /// </summary>
    public sealed partial class SpecialManeuverService
    {
        /// <summary>Embarquement 2 PA (délègue à VehicleState, PA via ConsumeActionPoints).</summary>
        public bool TryEmbarkVehicle(CharacterStats unit, string vehicleId, out string log) =>
            VehicleState.TryEmbark(unit, vehicleId, out log);

        /// <summary>Débarquement gratuit (délègue à VehicleState).</summary>
        public bool TryDisembarkVehicle(CharacterStats unit, out string log) =>
            VehicleState.Disembark(unit, out log);

        /// <summary>
        /// Écrasement de piéton par un véhicule mobile (délègue à VehicleState :
        /// dégâts fixes du gabarit + projection 1 case + À Terre).
        /// </summary>
        public bool TryRunOverPedestrian(string vehicleId, CharacterStats pedestrian, bool vehicleMoving, out string log) =>
            VehicleState.TryRunOver(vehicleId, pedestrian, vehicleMoving, out log);

        /// <summary>
        /// Routage dégâts d'arme vs véhicule : bruts + bonus lourdes (+100).
        /// L'appelant (arène, grenades, duel) fournit le flag cible véhicule.
        /// </summary>
        public static int ResolveDamageVsVehicle(int rawDamage, Killtime.Core.Inventory.InventoryItem weapon, bool targetIsVehicleOrStructure) =>
            VehicleMechanics.ApplyHeavyDamage(rawDamage, weapon, targetIsVehicleOrStructure);

        /// <summary>
        /// Inflige des dégâts à un sous-système (bonus lourdes inclus si l'arme
        /// est fournie). Retourne faux si véhicule inconnu ou déjà épave.
        /// </summary>
        public static bool StrikeVehicleSubsystem(
            string vehicleId,
            VehicleSubsystem subsystem,
            int rawDamage,
            Killtime.Core.Inventory.InventoryItem weapon,
            out VehicleHitResult hit)
        {
            int total = VehicleMechanics.ApplyHeavyDamage(rawDamage, weapon, targetIsVehicleOrStructure: true);
            return VehicleState.DamageSubsystem(vehicleId, subsystem, total, out hit);
        }
    }
}
