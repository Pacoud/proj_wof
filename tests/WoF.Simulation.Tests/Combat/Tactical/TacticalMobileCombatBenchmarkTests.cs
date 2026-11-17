using System.Globalization;
using System.Text.Json;
using Microsoft.VisualBasic.FileIO;
using WoF.Benchmarks;
using WoF.Simulation.Military.Combat.Tactical;
using Runner = WoF.Benchmarks.TacticalMobileCombatBenchmark;

namespace WoF.Simulation.Tests.Combat.Tactical;

public sealed class TacticalMobileCombatBenchmarkTests
{
    [Theory]
    [InlineData("detector", "sparse", false)]
    [InlineData("full", "dense", true)]
    public void ParsesBenchmarkCommand(string mode, string density, bool dense)
    {
        Assert.True(Runner.TryParse([mode, "100", "12.5", density, "3", "0", "-42"], out var parameters));
        Assert.NotNull(parameters);
        Assert.Equal(mode, parameters.Mode);
        var expectedDensity = dense ? TacticalMobileBenchmarkDensity.Dense : TacticalMobileBenchmarkDensity.Sparse;
        Assert.Equal(new TacticalMobileBenchmarkOptions(100, 12.5, expectedDensity, -42), parameters.Scenario);
        Assert.Equal(3, parameters.Measurements);
        Assert.Equal(0, parameters.Warmup);
    }

    [Theory]
    [InlineData(0, "tick")]
    [InlineData(1, "1")]
    [InlineData(1, "0")]
    [InlineData(2, "NaN")]
    [InlineData(2, "101")]
    [InlineData(3, "unknown")]
    [InlineData(4, "0")]
    [InlineData(5, "-1")]
    [InlineData(6, "abc")]
    public void RejectsInvalidArguments(int index, string value)
    {
        string[] args = ["detector", "100", "30", "sparse", "3", "1", "42"];
        args[index] = value;
        Assert.False(Runner.TryParse(args, out _));
        Assert.False(Runner.TryParse(args[..^1], out _));
    }

    [Fact]
    public void StatisticsUseValidSamplesAndNearestRankP95()
    {
        var report = Runner.Execute(Parameters("detector"));
        var sample = report.Samples[0];
        var values = Enumerable.Range(1, 20).Reverse().Select(number =>
            sample with
            {
                Number = number,
                Measurement = new Runner.Measurement(number, 2 * number, 1, 2, 3)
            }).Append(sample with { Rejection = "Rejected" }).ToArray();

        var statistics = Runner.Summarize(values);
        Assert.Equal(20, statistics.ValidSamples);
        Assert.Equal(10.5, statistics.MeanMs);
        Assert.Equal(10.5, statistics.MedianMs);
        Assert.Equal(19, statistics.P95Ms);
        Assert.Equal(1, statistics.MinMs);
        Assert.Equal(20, statistics.MaxMs);
        Assert.Equal(21, statistics.MeanAllocatedBytes);
        Assert.Equal(20, statistics.Gen0);
        Assert.Equal(40, statistics.Gen1);
        Assert.Equal(60, statistics.Gen2);
        Assert.Null(Runner.Summarize([values[^1]]).MedianMs);
    }

    [Fact]
    public void PhaseStatisticsUseOnlyValidSamplesThatExecutedEachPhase()
    {
        var template = Runner.Execute(Parameters("detector") with { Measurements = 1, Warmup = 0 });
        var sample = Assert.Single(template.Samples);
        var withoutPhases = sample with { Phases = [] };
        var fireOnly = sample with
        {
            Phases = [new(TacticalBenchmarkPhase.FireExecution, 0, 0, 1)]
        };
        var rejected = sample with
        {
            Rejection = "Rejected",
            Phases =
            [
                new(TacticalBenchmarkPhase.Detection, 1000, 10000, 100),
                new(TacticalBenchmarkPhase.Cohesion, 1000, 10000, 100)
            ]
        };
        var samples = Enumerable.Range(1, 20).Reverse().Select(number => sample with
        {
            Phases = [new(TacticalBenchmarkPhase.Detection, number, 100 * number, number)]
        }).Concat([sample, withoutPhases, fireOnly, rejected]).ToArray();

        var statistics = Runner.SummarizePhases(samples);

        Assert.Collection(statistics,
            detection => Assert.Equal(new Runner.PhaseStatistics(
                TacticalBenchmarkPhase.Detection, 20, 10.5, 10.5, 19, 1050, 10.5), detection),
            fire => Assert.Equal(new Runner.PhaseStatistics(
                TacticalBenchmarkPhase.FireExecution, 1, 0, 0, 0, 0, 1), fire));
        Assert.Empty(Runner.SummarizePhases([sample, withoutPhases, rejected]));
    }

    [Fact]
    public void MeasurementCountsAllocationsAndCollectionsInsideTheOperation()
    {
        byte[]? buffer = null;
        Runner.Measure(() => { });
        long outside = GC.GetAllocatedBytesForCurrentThread();
        var setup = new byte[1_000_000];
        Assert.True(GC.GetAllocatedBytesForCurrentThread() - outside >= setup.Length);

        int before = 0;
        int after = 0;
        var measurement = Runner.Measure(() =>
        {
            buffer = new byte[4096];
            before = GC.CollectionCount(2);
            GC.Collect(2);
            after = GC.CollectionCount(2);
        });

        Assert.InRange(measurement.AllocatedBytes, 4096, setup.Length - 1);
        Assert.True(after > before);
        Assert.True(measurement.Gen2 >= after - before);
        Assert.True(measurement.Milliseconds >= 0);
        GC.KeepAlive(buffer);
    }

    [Theory]
    [InlineData("detector", 60)]
    [InlineData("full", 26)]
    public void IndependentSamplesAreComparableAfterWarmup(string mode, int mobileCompanies)
    {
        var parameters = Parameters(mode);
        var report = Runner.Execute(parameters);
        var withoutWarmup = Runner.Execute(parameters with { Warmup = 0 });

        Assert.Equal(2, report.Statistics.ValidSamples);
        Assert.All(report.Samples, sample =>
        {
            Assert.Equal(60, report.Info.Parameters.Scenario.MobilityPercent);
            Assert.Equal(mobileCompanies, sample.Load.MobileCompanies);
            Assert.Equal(mobileCompanies, sample.Load.ActualMobilityPercent);
            Assert.Equal(mode == "full" ? mobileCompanies : 0, sample.Load.ActiveMovements);
            Assert.Equal(withoutWarmup.Samples[0].Load, sample.Load);
            Assert.Equal(withoutWarmup.Samples[0].Work, sample.Work);
            Assert.True(sample.Measurement.AllocatedBytes >= 0);
            Assert.True(sample.Measurement.Gen0 >= 0);
            Assert.True(sample.Measurement.Gen1 >= 0);
            Assert.True(sample.Measurement.Gen2 >= 0);
        });

        var work = report.Samples[0].Work;
        if (mode == "full")
        {
            Assert.Contains(report.Warnings, warning => warning.Contains("Requested mobility"));
            Assert.True(work.ConfirmedContacts > 0);
            Assert.True(work.MobileSalvosFired > 0);
            Assert.True(work.RoundsExpended > 0);
            Assert.Null(work.TrajectorySegments);
        }
        else
        {
            Assert.True(work.TrajectorySegments >= 100);
            Assert.True(work.ExactPairChecks > 0);
            Assert.Null(work.SalvosFired);
            Assert.Null(work.ResolutionPasses);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExcludesFullSampleWhenTheFactoryCannotPrepareAnyOrders(bool phases)
    {
        var parameters = new Runner.Parameters("full",
            new(2, 100, TacticalMobileBenchmarkDensity.Sparse, 42), 1, 0, Phases: phases);
        var report = Runner.Execute(parameters);
        var sample = Assert.Single(report.Samples);
        Assert.Equal(0, sample.Load.ActiveMovements);
        Assert.False(sample.Valid);
        Assert.Contains("active orders", sample.Rejection);
        Assert.Equal(0, report.Statistics.ValidSamples);
        Assert.Null(report.Statistics.MedianMs);
        if (phases)
        {
            Assert.NotNull(sample.Phases);
            Assert.NotEmpty(sample.Phases);
            Assert.NotNull(report.PhaseStatistics);
            Assert.Empty(report.PhaseStatistics);
        }
    }

    [Theory]
    [InlineData("detector")]
    [InlineData("full")]
    public void ExportsMachineReadableReportsWithoutOverwriting(string mode)
    {
        var report = Runner.Execute(Parameters(mode));
        var folder = Directory.CreateTempSubdirectory();
        try
        {
            var (json, csv) = Runner.Export(report, folder.FullName);
            var duplicate = Runner.Export(report, folder.FullName);
            Assert.NotEqual(json, duplicate.Json);
            Assert.NotEqual(csv, duplicate.Csv);

            string content = File.ReadAllText(json);
            using var document = JsonDocument.Parse(content);
            Assert.DoesNotContain(folder.FullName, content);
            Assert.Equal(42, document.RootElement.GetProperty("Info").GetProperty("Parameters")
                .GetProperty("Scenario").GetProperty("Seed").GetInt32());
            Assert.Equal(2, document.RootElement.GetProperty("Samples").GetArrayLength());
            Assert.False(document.RootElement.TryGetProperty("PhaseStatistics", out _));
            Assert.False(document.RootElement.GetProperty("Info").GetProperty("Parameters")
                .TryGetProperty("Phases", out _));
            Assert.False(document.RootElement.GetProperty("Samples")[0].TryGetProperty("Phases", out _));

            using var parser = new TextFieldParser(csv);
            parser.SetDelimiters(",");
            var header = parser.ReadFields()!;
            Assert.Contains("Info.Parameters.Scenario.MobilityPercent", header);
            Assert.Contains("Sample.Load.ActualMobilityPercent", header);
            Assert.Contains("Sample.Measurement.Gen2", header);
            Assert.Contains("Statistics.P95Ms", header);
            Assert.DoesNotContain("Info.Parameters.Phases", header);
            Assert.DoesNotContain(header, name => name.StartsWith("Sample.Phases.", StringComparison.Ordinal));
            for (int i = 0; i < report.Samples.Count; i++)
            {
                var row = parser.ReadFields()!;
                Assert.Equal(header.Length, row.Length);
                Assert.Equal((i + 1).ToString(), row[Array.IndexOf(header, "Sample.Number")]);
                Assert.Equal("42", row[Array.IndexOf(header, "Info.Parameters.Scenario.Seed")]);
            }
            Assert.True(parser.EndOfData);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [Fact]
    public void ParsesOptionalPhaseInstrumentation()
    {
        string[] args =
        [
            "full", "100", "60", "sparse",
            "2", "0", "12345", "--phases"
        ];

        Assert.True(Runner.TryParse(args, out var parameters));
        Assert.NotNull(parameters);
        Assert.True(parameters.Phases);

        Assert.True(Runner.TryParse(
            args[..^1], out var normal));

        Assert.NotNull(normal);
        Assert.False(normal.Phases);

        args[0] = "detector";

        Assert.False(Runner.TryParse(args, out _));
    }

    [Fact]
    public void InstrumentedRunnerPreservesMilitaryWork()
    {
        var parameters = Parameters("full") with
        {
            Measurements = 1,
            Warmup = 0
        };

        var normal = Runner.Execute(parameters);

        var instrumented = Runner.Execute(
            parameters with { Phases = true });

        var normalSample = Assert.Single(normal.Samples);
        var measuredSample = Assert.Single(instrumented.Samples);

        Assert.True(normalSample.Valid);
        Assert.True(measuredSample.Valid);

        Assert.Equal(normalSample.Load, measuredSample.Load);
        Assert.Equal(normalSample.Work, measuredSample.Work);

        Assert.Null(normalSample.Phases);
        Assert.NotNull(measuredSample.Phases);
        Assert.NotEmpty(measuredSample.Phases);

        Assert.NotNull(instrumented.PhaseStatistics);

        Assert.Contains(
            measuredSample.Phases,
            phase => phase.Phase ==
                TacticalBenchmarkPhase.MovementPreparation);

        Assert.Contains(
            measuredSample.Phases,
            phase => phase.Phase ==
                TacticalBenchmarkPhase.MovementContactDetection);

        Assert.Contains(
            measuredSample.Phases,
            phase => phase.Phase ==
                TacticalBenchmarkPhase.FireExecution);

        foreach (var phase in measuredSample.Phases)
        {
            Assert.True(double.IsFinite(phase.Milliseconds));
            Assert.True(phase.Milliseconds >= 0);
            Assert.True(phase.AllocatedBytes >= 0);
            Assert.True(phase.Calls >= 1);
        }

        var detection = Assert.Single(
            measuredSample.Phases,
            phase => phase.Phase ==
                TacticalBenchmarkPhase.MovementContactDetection);

        Assert.Equal(2, measuredSample.Work.ResolutionPasses);
        Assert.Equal(1, detection.Calls);

        var validation = Assert.Single(measuredSample.Phases,
            phase => phase.Phase == TacticalBenchmarkPhase.Validation);
        Assert.Equal(1, validation.Calls);

        Assert.Equal(measuredSample.Phases.Count,
            measuredSample.Phases.Select(phase => phase.Phase).Distinct().Count());
        Assert.True(measuredSample.Phases.Sum(phase => phase.AllocatedBytes)
            <= measuredSample.Measurement.AllocatedBytes);

        // Le temps du tick est converti en TimeSpan, dont la résolution est de 100 ns.
        Assert.True(measuredSample.Phases.Sum(phase => phase.Milliseconds)
            <= measuredSample.Measurement.Milliseconds + TimeSpan.FromTicks(1).TotalMilliseconds);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(60)]
    public void InstrumentedRunnerExportsDetailedPhases(int mobility)
    {
        var parameters = Parameters("full");
        var report = Runner.Execute(parameters with
        {
            Scenario = parameters.Scenario with { MobilityPercent = mobility },
            Warmup = 0,
            Phases = true
        });
        Assert.Equal(parameters.Measurements, report.Statistics.ValidSamples);

        var folder = Directory.CreateTempSubdirectory();

        try
        {
            var (json, csv) =
                Runner.Export(report, folder.FullName);

            using var document = JsonDocument.Parse(
                File.ReadAllText(json));

            var root = document.RootElement;

            Assert.True(
                root.GetProperty("Info")
                    .GetProperty("Parameters")
                    .GetProperty("Phases")
                    .GetBoolean());

            var statistics = root.GetProperty("Statistics");
            var phaseStatistics = root.GetProperty("PhaseStatistics").EnumerateArray()
                .ToDictionary(phase => phase.GetProperty("Phase").GetString()!);
            var samples = root.GetProperty("Samples");
            Assert.Equal(parameters.Measurements, samples.GetArrayLength());

            using var parser = new TextFieldParser(csv);
            parser.SetDelimiters(",");
            var header = parser.ReadFields()!;
            var culture = CultureInfo.InvariantCulture;

            foreach (var sample in samples.EnumerateArray())
            {
                var values = parser.ReadFields()!;
                Assert.Equal(header.Length, values.Length);
                var row = header.Zip(values).ToDictionary(column => column.First, column => column.Second);
                var phases = sample.GetProperty("Phases").EnumerateArray()
                    .ToDictionary(phase => phase.GetProperty("Phase").GetString()!);
                Assert.Equal(mobility > 0, phases.ContainsKey(nameof(TacticalBenchmarkPhase.MovementContactDetection)));
                Assert.True(phases.ContainsKey(nameof(TacticalBenchmarkPhase.FireExecution)));

                Assert.Equal(sample.GetProperty("Number").GetInt32(),
                    int.Parse(row["Sample.Number"], culture));
                Assert.Equal(statistics.GetProperty("MedianMs").GetDouble(),
                    double.Parse(row["Statistics.MedianMs"], culture));
                Assert.Equal(statistics.GetProperty("P95Ms").GetDouble(),
                    double.Parse(row["Statistics.P95Ms"], culture));
                Assert.Equal(sample.GetProperty("Measurement").GetProperty("Milliseconds").GetDouble(),
                    double.Parse(row["Sample.Measurement.Milliseconds"], culture));

                foreach (var phase in Enum.GetNames<TacticalBenchmarkPhase>())
                {
                    string prefix = $"Sample.Phases.{phase}.";
                    if (phases.TryGetValue(phase, out var measurement))
                    {
                        Assert.Equal(parameters.Measurements,
                            phaseStatistics[phase].GetProperty("SampleCount").GetInt32());
                        Assert.Equal(measurement.GetProperty("Milliseconds").GetDouble(),
                            double.Parse(row[prefix + "Milliseconds"], culture));
                        Assert.Equal(measurement.GetProperty("AllocatedBytes").GetInt64(),
                            long.Parse(row[prefix + "AllocatedBytes"], culture));
                        Assert.Equal(measurement.GetProperty("Calls").GetInt32(),
                            int.Parse(row[prefix + "Calls"], culture));
                    }
                    else
                    {
                        Assert.False(phaseStatistics.ContainsKey(phase));
                        Assert.Empty(row[prefix + "Milliseconds"]);
                        Assert.Empty(row[prefix + "AllocatedBytes"]);
                        Assert.Empty(row[prefix + "Calls"]);
                    }
                }
            }
            Assert.True(parser.EndOfData);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    private static Runner.Parameters Parameters(string mode) =>
        new(mode, new(100, 60, TacticalMobileBenchmarkDensity.Sparse, 42), 2, 1);
}
