using Tracker.Api.Services;

namespace Tracker.Tests;

public class ParserTests
{
    [Fact]
    public void Parses_topics_in_file_order_and_numbers_problems_globally()
    {
        var files = Fx.ReadFolder(Path.Combine(Fx.MiniDsaDir, "tools", "problems"));
        var defs = ProblemSourceParser.Parse(files);

        Assert.Equal(3, defs.Count);
        Assert.Equal([1, 2, 3], defs.Select(d => d.Num));
        var twoSum = defs[1];
        Assert.Equal("Two Sum", twoSum.Title);
        Assert.Equal(1, twoSum.Lc);
        Assert.Equal("src/01-ArraysHashing/002-TwoSum", twoSum.RelDir);
        Assert.Equal("NC002_TwoSum", twoSum.ProjName);
        Assert.Equal("https://leetcode.com/problems/two-sum/", twoSum.SolveUrl);
        Assert.Equal("02-TwoPointers", defs[2].TopicDir);
    }

    [Fact]
    public void Pascal_case_matches_gen_ps1()
    {
        Assert.Equal("ArraysHashing", ProblemSourceParser.ToPascal("Arrays & Hashing"));
        Assert.Equal("BestTimeToBuyAndSellStockII", ProblemSourceParser.ToPascal("Best Time to Buy and Sell Stock II"));
        Assert.Equal("RangeSumQuery2DImmutable", ProblemSourceParser.ToPascal("Range Sum Query 2D - Immutable"));
    }

    [Fact]
    public void Generated_stub_matches_the_real_project_file()
    {
        // Only runs on a machine that has the real DSA project next to the tracker.
        const string real = @"C:\interview\dsa";
        if (!Directory.Exists(real)) return;
        var defs = ProblemSourceParser.Parse(Fx.ReadFolder(Path.Combine(real, "tools", "problems")));
        Assert.Equal(250, defs.Count);
        foreach (var d in defs.Take(30))
        {
            var path = Path.Combine(real, d.RelDir, "Solution.cs");
            var file = File.ReadAllText(path).Replace("\r\n", "\n");
            if (!file.Contains("NotImplementedException")) continue;   // already solved locally
            Assert.Equal(file, ProblemSourceParser.GenerateStub(d));
            Assert.Equal(File.ReadAllText(Path.Combine(real, d.RelDir, "SolutionTests.cs")).Replace("\r\n", "\n"), ProblemSourceParser.GenerateTests(d));
        }
    }

    [Fact]
    public void Strips_single_root_folder_from_upload()
    {
        var files = Fx.ReadFolder(Fx.MiniDsaDir, "dsa/");
        var stripped = ImportService.StripCommonRoot(files);
        Assert.Contains("Directory.Build.props", stripped.Keys);
        Assert.Contains("tools/problems/01-arrays-hashing.txt", stripped.Keys);
    }

    [Theory]
    [InlineData("../evil.cs", null)]
    [InlineData("a/../../b", null)]
    [InlineData("C:/x/y.cs", null)]
    [InlineData("\\src\\A.cs", "src/A.cs")]
    [InlineData("root/src/A.cs", "root/src/A.cs")]
    public void Zip_paths_are_sanitised(string input, string? expected) => Assert.Equal(expected, ZipReader.Normalize(input));

    [Fact]
    public void Trx_results_are_parsed()
    {
        const string trx = """
            <?xml version="1.0" encoding="utf-8"?>
            <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
              <Results>
                <UnitTestResult testName="NC002_TwoSum.SolutionTests.Example1" outcome="Passed" duration="00:00:00.0010000" />
                <UnitTestResult testName="NC002_TwoSum.SolutionTests.Example2" outcome="Failed" duration="00:00:00.0020000">
                  <Output><ErrorInfo><Message>Assert.Equal() Failure</Message></ErrorInfo></Output>
                </UnitTestResult>
              </Results>
            </TestRun>
            """;
        var r = TrxParser.Parse(trx);
        Assert.Equal(2, r.Count);
        Assert.Equal("Example2", r[0].Name);          // failures first
        Assert.Equal("Failed", r[0].Outcome);
        Assert.Equal("Assert.Equal() Failure", r[0].Message);
        Assert.Equal("Example1", r[1].Name);
    }
}
