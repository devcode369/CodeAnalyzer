using System.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;
class Program
{
    static Dictionary<string, List<string>> callGraph = new();
    static string dotFilePath = "CompleteCallGraph.dot";
    static string outputImagePath = "CompleteCallGraph.png";

    static void Main()
    {
        string solutionPath = null;

        Console.WriteLine("Do you want to analyze specific Controller action method press Yes / Complete sln press NO:");
        string userChoice = Console.ReadLine()?.Trim().ToLower();

        string controllerName = null;
        string actionMethod = null;

        if (userChoice == "yes")
        {
            Console.Write("Enter Controller Name (without 'Controller' suffix): ");
            controllerName = Console.ReadLine()?.Trim() + "Controller";

            Console.Write("Enter Action Method Name: ");
           
            actionMethod = Console.ReadLine()?.Trim();
        }
        else
        {
            Console.WriteLine("Enter full Solution path with extension .sln... ");
            solutionPath = Console.ReadLine();
        }

        var workspace = MSBuildWorkspace.Create();
        var solution = workspace.OpenSolutionAsync(solutionPath).Result;

        foreach (var project in solution.Projects)
        {
            foreach (var document in project.Documents)
            {
                if (document.Name.EndsWith(".cs"))
                {
                    AnalyzeDocument(document, controllerName, actionMethod);
                }
            }
        }

     
        GenerateGraphVizDOT();
      
        GenerateGraphImage();
    }

    static void AnalyzeDocument(Document document, string controllerName, string actionMethod)
    {
        var tree = document.GetSyntaxTreeAsync().Result;
        var root = tree.GetRoot() as CompilationUnitSyntax;
        var semanticModel = document.GetSemanticModelAsync().Result;

        var classNodes = root.DescendantNodes()
                             .OfType<ClassDeclarationSyntax>();

        foreach (var classNode in classNodes)
        {
            string className = classNode.Identifier.Text;

            if (controllerName != null && className != controllerName)
                continue;

            var methodNodes = classNode.DescendantNodes()
                                       .OfType<MethodDeclarationSyntax>();

            foreach (var methodNode in methodNodes)
            {
                string methodName = methodNode.Identifier.Text;

                if (actionMethod != null && methodName != actionMethod)
                    continue;

                string rootMethod = $"{className}.{methodName}";
                if (!callGraph.ContainsKey(rootMethod))
                    callGraph[rootMethod] = new List<string>();

                AnalyzeMethodCalls(methodNode, semanticModel, rootMethod);
            }
        }
    }
    static void AnalyzeMethodCalls(MethodDeclarationSyntax method, SemanticModel semanticModel, string parentMethod)
    {
        var methodCalls = method.DescendantNodes()
                                .OfType<InvocationExpressionSyntax>();

        foreach (var methodCall in methodCalls)
        {
            if (!semanticModel.SyntaxTree.Equals(method.SyntaxTree))
            {
                Console.WriteLine($"Skipping method: {parentMethod} (Syntax tree mismatch)");
                continue;
            }

            var symbol = semanticModel.GetSymbolInfo(methodCall).Symbol as IMethodSymbol;
            if (symbol == null) continue;

            string calledMethod = $"{symbol.ContainingType.Name}.{symbol.Name}";

            if (!callGraph.ContainsKey(parentMethod))
                callGraph[parentMethod] = new List<string>();

            if (!callGraph[parentMethod].Contains(calledMethod))
                callGraph[parentMethod].Add(calledMethod);
        
            var methodDefinition = symbol.DeclaringSyntaxReferences.FirstOrDefault();
            if (methodDefinition != null && methodDefinition.SyntaxTree.Equals(method.SyntaxTree))
            {
                var methodSyntax = methodDefinition.GetSyntax() as MethodDeclarationSyntax;
                if (methodSyntax != null)
                {
                    AnalyzeMethodCalls(methodSyntax, semanticModel, calledMethod);
                }
            }
        }
    }
 

    static void GenerateGraphVizDOT()
    {
        using (StreamWriter writer = new(dotFilePath))
        {
            writer.WriteLine("digraph CallGraph {");
            writer.WriteLine("  node [shape=box, style=filled, fillcolor=lightblue];");

            foreach (var kvp in callGraph)
            {
                foreach (var calledMethod in kvp.Value)
                {
                    writer.WriteLine($"    \"{kvp.Key}\" -> \"{calledMethod}\";");
                }
            }

            writer.WriteLine("}");
        }

        Console.WriteLine($"GraphViz DOT file generated: {dotFilePath}");
    }

    static void GenerateGraphImage()
    {
        string graphvizCommand = $"dot -Tpng {dotFilePath} -o {outputImagePath}";

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
            process.WaitForExit();

            Console.WriteLine($"Call graph image generated: {outputImagePath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error generating graph image: {ex.Message}");
        }
    }
}
