using AVAEligibility.Core.Eligibility;

namespace AVAEligibility.Tests;

/// <summary>The samples folder, found from the test's output folder, and its files read.</summary>
public static class Samples
{
    public static string Folder { get; } = Find();

    public static string Path(string name) => System.IO.Path.Combine(Folder, name);

    public static EligibilityFile Load(string name) => EligibilityReader.Load(Path(name));

    /// <summary>A person in a sample, by first name (each is unique in the samples).</summary>
    public static Person Person(string file, string first) => Load(file).People.Single(p => p.First == first);

    /// <summary>A copy of the samples in a new temporary folder (agents and tests write into it).</summary>
    public static string Copy()
    {
        var root = Directory.CreateTempSubdirectory("eligibility-samples-").FullName;
        foreach (var f in Directory.EnumerateFiles(Folder, "*", SearchOption.AllDirectories))
        {
            var to = System.IO.Path.Combine(root, System.IO.Path.GetRelativePath(Folder, f));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(to)!);
            File.Copy(f, to);
        }
        return root;
    }

    private static string Find()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (Directory.Exists(System.IO.Path.Combine(d.FullName, "samples")) && File.Exists(System.IO.Path.Combine(d.FullName, "AVAEligibility.slnx")))
                return System.IO.Path.Combine(d.FullName, "samples");
        throw new DirectoryNotFoundException("The samples folder wasn't found above the test's output folder.");
    }
}
