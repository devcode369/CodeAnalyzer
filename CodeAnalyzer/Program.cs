using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using System.Collections.Concurrent;
using CodeAnalyzer;

class Program
{
    static ConcurrentDictionary<string, List<string>> callGraph = new();
    static string dotFilePath = "CompleteCallGraph.dot";
    static string outputImagePath = "CompleteCallGraph.png";

    static async Task Main()
    {
        Console.WriteLine("Do you want to analyze specific Controller action method press Yes / Complete sln press N0:");
        string userChoice = Console.ReadLine()?.Trim().ToLower();


        string solutionPath = null;
        string controllerName = null;
        string actionMethod = null;

        Console.WriteLine("Enter full Solution path with extension .sln... ");
        solutionPath = Console.ReadLine();
        if (userChoice == "yes")
        {
            Console.Write("Enter Controller Name (without 'Controller' suffix): ");
            controllerName = Console.ReadLine()?.Trim() + "Controller";

            Console.Write("Enter Action Method Name: ");

            actionMethod = Console.ReadLine()?.Trim();
            dotFilePath = $"{controllerName}_{actionMethod}.dot";
            outputImagePath = $"{controllerName}_{actionMethod}.png";
        }

        try
        {
            var workspace = MSBuildWorkspace.Create();
            var solution = await workspace.OpenSolutionAsync(solutionPath);
            var compilation = (await Task.WhenAll(solution.Projects.Select(async p => await p.GetCompilationAsync())))
                .First(c => c != null);

            AnalyzeSolution(compilation, controllerName, actionMethod);
            GenerateGraphVizDOT();
            GenerateGraphImage();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }

    static void AnalyzeSolution(Compilation compilation, string controllerName, string actionMethod)
    {
        var walker = new ReferenceWalker(callGraph, compilation, controllerName, actionMethod);
        Parallel.ForEach(compilation.SyntaxTrees, tree =>
        {
            walker.Visit(tree.GetRoot());
        });
    }

    static void GenerateGraphVizDOT()
    {
        using (StreamWriter writer = new(dotFilePath))
        {
            writer.WriteLine("digraph CallGraph {");
            writer.WriteLine("  node [shape=box, style=filled, fillcolor=lightblue];");
            writer.WriteLine("  rankdir=TB;");

            var interfaceMethods = callGraph.Keys.Where(m => m.StartsWith("I") && m.Contains("Service")).ToList();
            var serviceMethods = callGraph.Keys.Where(m => m.Contains("Service")).ToList();
            var repositoryMethods = callGraph.Keys.Where(m => m.Contains("Repository")).ToList();

            foreach (var method in interfaceMethods)
                writer.WriteLine($"  \"{method}\" [group=InterfaceLayer];");
            foreach (var method in serviceMethods)
                writer.WriteLine($"  \"{method}\" [group=ServiceLayer];");
            foreach (var method in repositoryMethods)
                writer.WriteLine($"  \"{method}\" [group=RepositoryLayer];");

            foreach (var kvp in callGraph)
            {
                foreach (var calledMethod in kvp.Value)
                {
                    writer.WriteLine($"  \"{kvp.Key}\" -> \"{calledMethod}\";");
                }
            }

            writer.WriteLine("}");
        }
        Console.WriteLine($"GraphViz DOT file generated: {dotFilePath}");
    }

    static void GenerateGraphImage()
    {
        string graphvizCommand = $"dot -Tpng \"{dotFilePath}\" -o \"{outputImagePath}\"";
        try
        {
            Process process = new()
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/C {graphvizCommand}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode == 0)
                Console.WriteLine($"Call graph image generated: {outputImagePath}");
            else
                Console.WriteLine($"Error generating graph: {error}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error generating graph image: {ex.Message}");
        }
    }
}



