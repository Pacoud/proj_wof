using WoF.Simulation.Diplomacy;
using WoF.Simulation.World;

namespace WoF.Simulation.Military;

public static class MilitaryAccessRules
{
    public static bool CanTraverse(
        Country? movingCountry,
        Province province,
        DiplomacySystem diplomacy)
    {
        // Compatibilité avec nos anciens tests.
        if (movingCountry == null)
            return true;

        Country? controller =
            province.Controller;

        // Territoire non contrôlé.
        if (controller == null)
            return true;

        // Notre propre territoire contrôlé.
        return ReferenceEquals(
            controller,
            movingCountry
        );
    }

    public static bool CanEnterAsDestination(
        Country? movingCountry,
        Province province,
        DiplomacySystem diplomacy)
    {
        if (movingCountry == null)
            return true;

        Country? controller =
            province.Controller;

        if (controller == null)
            return true;

        if (ReferenceEquals(
                controller,
                movingCountry))
        {
            return true;
        }

        // Une province étrangère n'est accessible
        // comme cible que si nous sommes en guerre.
        return diplomacy.AreAtWar(
            movingCountry,
            controller
        );
    }
}