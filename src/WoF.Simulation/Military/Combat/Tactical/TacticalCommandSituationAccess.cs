
namespace WoF.Simulation.Military.Combat.Tactical;

/// <summary>
/// Cle opaque propre a un enregistrement.
///
/// L'identite de cette instance est utilisee
/// pour identifier le bataillon en interne.
/// Aucun objet militaire n'est expose.
/// </summary>
internal sealed class TacticalCommandSituationRegistration
{
    internal TacticalCommandSituationRegistration()
    {
    }
}

/// <summary>
/// Resultat accessible au commandement du
/// bataillon titulaire de la cle.
///
/// Un seul des deux champs est renseigne.
/// </summary>
internal sealed record TacticalCommandSituationReadResult(
    TacticalBattalionCommandSituationSnapshot? Snapshot,
    TacticalCommandSituationCaptureFailure? Failure);
