
using WoF.Simulation.Military.Organization;

namespace WoF.Simulation.Military.Reinforcements;

// Reçu immuable d'un transfert réussi.
public sealed record ReinforcementTransferReceipt(
    string SourceName,
    int? BattalionId,
    int CompanyId,
    ReinforcementCargo Delivered
);

public static class ReinforcementTransferService
{
    public static ReinforcementTransferReceipt Transfer(
        ReinforcementReserve source,
        Company destination,
        ReinforcementCargo cargo)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);

        cargo.ValidateNonNegative();

        var composition = destination.Composition
            ?? throw new InvalidOperationException(
                "Destination company has no composition.");

        // PHASE 1 : vérifier la source.
        if (!source.Stock.Contains(cargo))
        {
            throw new InvalidOperationException(
                "Insufficient resources in reserve.");
        }

        // PHASE 2 : vérifier la destination.
        //
        // Les blessés indisponibles conservent
        // leur place dans l'effectif théorique.
        if (cargo.Personnel >
                composition.Manpower.VacantPositions ||
            cargo.InfantryEquipment >
                composition.InfantryEquipment.Missing ||
            cargo.Artillery >
                composition.Artillery.Missing ||
            cargo.Tanks >
                composition.Tanks.Missing ||
            cargo.Trucks >
                composition.Trucks.Missing)
        {
            throw new InvalidOperationException(
                "Destination cannot receive all resources.");
        }

        // PHASE 3 : transfert validé.
        //
        // Le moteur est séquentiel à ce stade.
        // Aucun stock ne doit changer entre
        // la validation et les mutations.
        source.Withdraw(cargo);

        composition.Manpower.Reinforce(
            cargo.Personnel);

        composition.InfantryEquipment.Reinforce(
            cargo.InfantryEquipment);

        composition.Artillery.Reinforce(
            cargo.Artillery);

        composition.Tanks.Reinforce(
            cargo.Tanks);

        composition.Trucks.Reinforce(
            cargo.Trucks);

        // PHASE 4 : retourner le bilan.
        return new ReinforcementTransferReceipt(
            SourceName: source.Name,
            BattalionId: destination.ParentBattalion?.Id,
            CompanyId: destination.Id,
            Delivered: cargo
        );
    }
}
