using System.Text.Json;
using Microsoft.VisualBasic.FileIO;
using WoF.Benchmarks;
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

    [Fact]
    public void ExcludesFullSampleWhenTheFactoryCannotPrepareAnyOrders()
    {
        var parameters = new Runner.Parameters("full",
            new(2, 100, TacticalMobileBenchmarkDensity.Sparse, 42), 1, 0);
        var report = Runner.Execute(parameters);
        var sample = Assert.Single(report.Samples);
        Assert.Equal(0, sample.Load.ActiveMovements);
        Assert.False(sample.Valid);
        Assert.Contains("active orders", sample.Rejection);
        Assert.Equal(0, report.Statistics.ValidSamples);
        Assert.Null(report.Statistics.MedianMs);
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

            using var parser = new TextFieldParser(csv);
            parser.SetDelimiters(",");
            var header = parser.ReadFields()!;
            Assert.Contains("Info.Parameters.Scenario.MobilityPercent", header);
            Assert.Contains("Sample.Load.ActualMobilityPercent", header);
            Assert.Contains("Sample.Measurement.Gen2", header);
            Assert.Contains("Statistics.P95Ms", header);
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

    private static Runner.Parameters Parameters(string mode) =>
        new(mode, new(100, 60, TacticalMobileBenchmarkDensity.Sparse, 42), 2, 1);
}
