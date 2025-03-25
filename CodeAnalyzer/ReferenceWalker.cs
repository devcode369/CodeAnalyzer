namespace CodeAnalyzer
{
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.CSharp.Syntax;
    using System.Collections.Concurrent;
    class ReferenceWalker : CSharpSyntaxWalker
    {
        private readonly ConcurrentDictionary<string, List<string>> _graph;
        private readonly Dictionary<string, int> _hierarchyLevels = new();
        private readonly Compilation _compilation;
        private readonly string _targetController;
        private readonly string _targetMethod;
        private readonly HashSet<string> _visited = new();
        private readonly Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>> _interfaceImplementations = new();
        private int _currentLevel = 0;

        public ReferenceWalker(ConcurrentDictionary<string, List<string>> graph, Compilation compilation,
            string controllerName, string actionMethod)
        {
            _graph = graph;
            _compilation = compilation;
            _targetController = controllerName;
            _targetMethod = actionMethod;     
            BuildInterfaceMappings();
        }

        private void BuildInterfaceMappings()
        {
            foreach (var symbol in _compilation.GetSymbolsWithName(_ => true, SymbolFilter.Type))
            {
                if (symbol is INamedTypeSymbol typeSymbol && typeSymbol.TypeKind == TypeKind.Class)
                {
                    foreach (var implementedInterface in typeSymbol.AllInterfaces)
                    {
                        if (!_interfaceImplementations.ContainsKey(implementedInterface))
                            _interfaceImplementations[implementedInterface] = new List<INamedTypeSymbol>();

                        _interfaceImplementations[implementedInterface].Add(typeSymbol);
                    }
                }
            }
        }

        public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
        {
            var model = _compilation.GetSemanticModel(node.SyntaxTree);
            var symbol = model.GetDeclaredSymbol(node);
            if (symbol == null) return;

            string classMethod = $"{symbol.ContainingType.Name}.{symbol.Name}";
            if (!string.IsNullOrEmpty(_targetController) && symbol.ContainingType.Name != _targetController) return;
            if (!string.IsNullOrEmpty(_targetMethod) && symbol.Name != _targetMethod) return;

            if (_visited.Add(classMethod))
            {
                _graph.TryAdd(classMethod, new List<string>());
                _hierarchyLevels[classMethod] = _currentLevel++;
                AnalyzeMethod(symbol);
                _currentLevel--;
            }

            base.VisitMethodDeclaration(node);
        }

        private void AnalyzeMethod(IMethodSymbol methodSymbol)
        {
            var syntaxRef = methodSymbol.DeclaringSyntaxReferences.FirstOrDefault();
            if (syntaxRef == null || syntaxRef.GetSyntax() is not MethodDeclarationSyntax methodNode) return;

            var model = _compilation.GetSemanticModel(methodNode.SyntaxTree);
            foreach (var invocation in methodNode.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                var calledSymbol = model.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
                if (calledSymbol == null) continue;

                string calledMethod = $"{calledSymbol.ContainingType.Name}.{calledSymbol.Name}";
                AddToGraph(methodSymbol, calledSymbol);

                if (_visited.Add(calledMethod))
                {
                    _hierarchyLevels[calledMethod] = _currentLevel++;
                    AnalyzeMethod(calledSymbol);
                    _currentLevel--;
                }
            }
        }

        private void AddToGraph(IMethodSymbol caller, IMethodSymbol callee)
        {
            string callerMethod = $"{caller.ContainingType.Name}.{caller.Name}";
            string calleeMethod = $"{callee.ContainingType.Name}.{callee.Name}";
            _graph.GetOrAdd(callerMethod, _ => new List<string>()).Add(calleeMethod);

            foreach (var impl in FindImplementations(callee))
            {
                string implMethod = $"{impl.ContainingType.Name}.{impl.Name}";
                _graph.GetOrAdd(callerMethod, _ => new List<string>()).Add(implMethod);

                if (_visited.Add(implMethod))
                {
                    _hierarchyLevels[implMethod] = _currentLevel++;
                    AnalyzeMethod(impl);
                    _currentLevel--;
                }
            }
        }

        private IEnumerable<IMethodSymbol> FindImplementations(IMethodSymbol methodSymbol)
        {
            if (methodSymbol.ContainingType.TypeKind != TypeKind.Interface) return Enumerable.Empty<IMethodSymbol>();

            if (_interfaceImplementations.TryGetValue(methodSymbol.ContainingType, out var implementations))
            {
                return implementations
                    .Select(type => type.FindImplementationForInterfaceMember(methodSymbol) as IMethodSymbol)
                    .Where(impl => impl != null);
            }

            return Enumerable.Empty<IMethodSymbol>();
        }

        public void PrintHierarchy()
        {
            foreach (var item in _hierarchyLevels.OrderBy(x => x.Value))
                Console.WriteLine(new string(' ', item.Value * 4) + item.Key);
        }
    }
}
