using WoF.Simulation.Core;

namespace WoF.Simulation.World.Terrain;

/// <summary>
/// Découpe le bord du cadre en arcs, un par province voisine,
/// et place les points de passage de chaque frontière.
///
/// L'arc d'une voisine est borné par les bissectrices entre son relèvement
/// et ceux des voisines adjacentes. Une case du bord appartient à l'arc
/// qui contient son relèvement vu du centre du cadre.
/// Sa position dans l'arc est une fraction angulaire, dans le sens horaire.
///
/// Les points de passage ne dépendent que de la graine de la frontière :
/// les deux provinces les calculent sans se connaître.
/// </summary>
internal static class ProvinceBorderLayout
{
    internal const double RiverProbabilityPerBorder = 0.3;

    private const double MinParameter = 0.1;
    private const double ParameterSpan = 0.8;

    internal static void Apply(ProvinceHexGrid grid)
    {
        var descriptor = grid.Descriptor;
        int neighborCount = descriptor.Neighbors.Count;

        if (neighborCount == 0)
            return;

        // Voisines triées par relèvement (relèvements distincts, garantis par le descripteur).
        int[] slots = Enumerable.Range(0, neighborCount)
            .OrderBy(s => descriptor.Neighbors[s].BearingDegrees)
            .ToArray();

        var arcStart = new double[neighborCount];
        var arcWidth = new double[neighborCount];

        for (int i = 0; i < slots.Length; i++)
        {
            double bearing =
                descriptor.Neighbors[slots[i]].BearingDegrees;

            double previous =
                descriptor.Neighbors[slots[(i - 1 + slots.Length) % slots.Length]].BearingDegrees;

            double next =
                descriptor.Neighbors[slots[(i + 1) % slots.Length]].BearingDegrees;

            double gapBefore = neighborCount == 1
                ? 360
                : Normalize(bearing - previous);

            double gapAfter = neighborCount == 1
                ? 360
                : Normalize(next - bearing);

            arcStart[slots[i]] = Normalize(bearing - gapBefore / 2);
            arcWidth[slots[i]] = (gapBefore + gapAfter) / 2;
        }

        var cellsBySlot = new List<int>[neighborCount];
        var parametersBySlot = new List<double>[neighborCount];

        for (int s = 0; s < neighborCount; s++)
        {
            cellsBySlot[s] = new List<int>();
            parametersBySlot[s] = new List<double>();
        }

        for (int cell = 0; cell < grid.CellCount; cell++)
        {
            if (!grid.IsFrameBoundary(cell))
                continue;

            double bearing = BearingFromCenter(grid, cell);

            int slot = -1;
            double parameter = 0;

            for (int s = 0; s < neighborCount; s++)
            {
                double offset = Normalize(bearing - arcStart[s]);

                if (offset < arcWidth[s])
                {
                    slot = s;
                    parameter = offset / arcWidth[s];
                    break;
                }
            }

            // Les arcs couvrent les 360° ; ce cas ne sert qu'aux arrondis.
            if (slot < 0)
            {
                slot = slots[0];
                parameter = 0;
            }

            cellsBySlot[slot].Add(cell);
            parametersBySlot[slot].Add(parameter);
        }

        for (int s = 0; s < neighborCount; s++)
        {
            grid.SetBorderCells(s, cellsBySlot[s].ToArray());

            PlaceCrossings(
                grid,
                descriptor.Neighbors[s],
                cellsBySlot[s],
                parametersBySlot[s]);
        }
    }

    private static void PlaceCrossings(
        ProvinceHexGrid grid,
        ProvinceNeighborBorder border,
        List<int> cells,
        List<double> parameters)
    {
        if (cells.Count == 0)
            return;

        int provinceId = grid.Descriptor.ProvinceId;

        ulong state = ProvinceTerrainDescriptor.ComputeBorderSeed(
            grid.Descriptor.WorldSeed,
            provinceId,
            border.NeighborProvinceId);

        bool reversed = provinceId > border.NeighborProvinceId;

        // Ordre fixe des tirages : rivière (présence puis position), puis routes.
        bool hasRiver =
            DeterministicRandom.NextUniform(ref state)
            < RiverProbabilityPerBorder;

        double riverParameter = NextParameter(ref state);

        if (hasRiver)
        {
            AddCrossing(grid, border, BorderCrossingKind.River,
                riverParameter, reversed, cells, parameters);
        }

        for (int road = 0; road < border.RoadLevel; road++)
        {
            AddCrossing(grid, border, BorderCrossingKind.Road,
                NextParameter(ref state), reversed, cells, parameters);
        }
    }

    private static void AddCrossing(
        ProvinceHexGrid grid,
        ProvinceNeighborBorder border,
        BorderCrossingKind kind,
        double canonical,
        bool reversed,
        List<int> cells,
        List<double> parameters)
    {
        double local = reversed ? 1 - canonical : canonical;

        int best = 0;

        for (int i = 1; i < cells.Count; i++)
        {
            if (Math.Abs(parameters[i] - local)
                < Math.Abs(parameters[best] - local))
            {
                best = i;
            }
        }

        grid.AddCrossing(new BorderCrossing(
            border.NeighborProvinceId,
            kind,
            canonical,
            local,
            cells[best]));
    }

    private static double NextParameter(ref ulong state)
    {
        return MinParameter
            + ParameterSpan * DeterministicRandom.NextUniform(ref state);
    }

    /// <summary>Relèvement d'une case vu du centre du cadre : 0° = nord, 90° = est.</summary>
    internal static double BearingFromCenter(ProvinceHexGrid grid, int cell)
    {
        double dx = grid.GetCenterXMeters(cell) - grid.FrameCenterXMeters;
        double dy = grid.GetCenterYMeters(cell) - grid.FrameCenterYMeters;

        return Normalize(Math.Atan2(dx, dy) * 180.0 / Math.PI);
    }

    private static double Normalize(double degrees)
    {
        double value = degrees % 360.0;

        if (value < 0)
            value += 360.0;

        return value;
    }
}
