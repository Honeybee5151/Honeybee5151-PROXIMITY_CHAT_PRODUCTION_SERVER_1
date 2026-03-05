using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;

namespace WorldServer.logic
{
    /// <summary>
    /// Roslyn-based semantic analyzer that validates community C# behavior code
    /// against a strict whitelist of allowed types and members.
    /// </summary>
    public static class BehaviorCodeAnalyzer
    {
        private static readonly Logger Log = LogManager.GetCurrentClassLogger();

        // Allowed namespace prefixes — any type in these namespaces is permitted
        private static readonly HashSet<string> AllowedNamespaces = new HashSet<string>
        {
            "WorldServer.logic.behaviors",
            "WorldServer.logic.transitions",
            "WorldServer.logic.loot",
        };

        // Allowed individual types (fully qualified)
        private static readonly HashSet<string> AllowedTypes = new HashSet<string>
        {
            // Core behavior tree
            "WorldServer.logic.State",
            "WorldServer.logic.Cooldown",
            "WorldServer.logic.BehaviorDb",
            "WorldServer.logic.IStateChildren",

            // Safe BCL primitives
            "System.Int32",
            "System.Int64",
            "System.Single",
            "System.Double",
            "System.Boolean",
            "System.String",
            "System.Byte",
            "System.UInt32",
            "System.Math",
            "System.MathF",

            // Enums used by behaviors
            "Shared.resources.ConditionEffectIndex",
        };

        // Blocked syntax constructs
        private static readonly HashSet<string> BlockedKeywords = new HashSet<string>
        {
            "unsafe", "dynamic", "extern", "stackalloc",
        };

        public static (bool IsValid, List<string> Errors) Analyze(string sourceCode)
        {
            var errors = new List<string>();

            // Parse the source
            var tree = CSharpSyntaxTree.ParseText(sourceCode);
            var root = tree.GetRoot();

            // Check for parse errors
            var diagnostics = tree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
            if (diagnostics.Any())
            {
                foreach (var diag in diagnostics)
                    errors.Add($"Parse error: {diag.GetMessage()}");
                return (false, errors);
            }

            // Blocked syntax checks (before semantic analysis)
            CheckBlockedSyntax(root, errors);

            if (errors.Count > 0)
                return (false, errors);

            // Create compilation for semantic analysis
            var references = new List<MetadataReference>
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(State).Assembly.Location), // WorldServer
            };

            // Add System.Runtime reference
            var runtimeDir = System.IO.Path.GetDirectoryName(typeof(object).Assembly.Location);
            var systemRuntime = System.IO.Path.Combine(runtimeDir, "System.Runtime.dll");
            if (System.IO.File.Exists(systemRuntime))
                references.Add(MetadataReference.CreateFromFile(systemRuntime));

            // Add Shared assembly (for ConditionEffectIndex)
            var sharedAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "Shared");
            if (sharedAssembly != null)
                references.Add(MetadataReference.CreateFromFile(sharedAssembly.Location));

            var compilation = CSharpCompilation.Create("CommunityBehaviors",
                syntaxTrees: new[] { tree },
                references: references,
                options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            var model = compilation.GetSemanticModel(tree);

            // Walk the syntax tree and validate all type/member references
            var walker = new WhitelistWalker(model, errors);
            walker.Visit(root);

            return (errors.Count == 0, errors);
        }

        private static void CheckBlockedSyntax(SyntaxNode root, List<string> errors)
        {
            // No unsafe blocks
            foreach (var node in root.DescendantNodes().OfType<UnsafeStatementSyntax>())
                errors.Add($"Line {node.GetLocation().GetLineSpan().StartLinePosition.Line + 1}: 'unsafe' blocks are not allowed");

            // No extern declarations
            foreach (var node in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                if (node.Modifiers.Any(SyntaxKind.ExternKeyword))
                    errors.Add($"Line {node.GetLocation().GetLineSpan().StartLinePosition.Line + 1}: 'extern' methods are not allowed");
            }

            // No delegate declarations
            foreach (var node in root.DescendantNodes().OfType<DelegateDeclarationSyntax>())
                errors.Add($"Line {node.GetLocation().GetLineSpan().StartLinePosition.Line + 1}: Delegate declarations are not allowed");

            // No lambda expressions
            foreach (var node in root.DescendantNodes().OfType<LambdaExpressionSyntax>())
                errors.Add($"Line {node.GetLocation().GetLineSpan().StartLinePosition.Line + 1}: Lambda expressions are not allowed");

            // No dynamic type
            foreach (var node in root.DescendantNodes().OfType<IdentifierNameSyntax>())
            {
                if (node.Identifier.Text == "dynamic")
                    errors.Add($"Line {node.GetLocation().GetLineSpan().StartLinePosition.Line + 1}: 'dynamic' type is not allowed");
            }
        }

        private static bool IsTypeAllowed(ITypeSymbol typeSymbol)
        {
            if (typeSymbol == null) return true;

            var fullName = typeSymbol.ToDisplayString();

            // Check exact type match
            if (AllowedTypes.Contains(fullName))
                return true;

            // Check namespace prefix match
            var ns = typeSymbol.ContainingNamespace?.ToDisplayString() ?? "";
            if (AllowedNamespaces.Any(allowed => ns == allowed || ns.StartsWith(allowed + ".")))
                return true;

            // Allow primitive types and void
            if (typeSymbol.SpecialType != SpecialType.None)
                return true;

            // Allow arrays of allowed types
            if (typeSymbol is IArrayTypeSymbol arrayType)
                return IsTypeAllowed(arrayType.ElementType);

            return false;
        }

        /// <summary>
        /// Walks the syntax tree and validates every symbol reference against the whitelist.
        /// </summary>
        private class WhitelistWalker : CSharpSyntaxWalker
        {
            private readonly SemanticModel _model;
            private readonly List<string> _errors;

            public WhitelistWalker(SemanticModel model, List<string> errors)
            {
                _model = model;
                _errors = errors;
            }

            public override void VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
            {
                var symbolInfo = _model.GetSymbolInfo(node);
                var symbol = symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();

                if (symbol != null)
                {
                    var type = symbol.ContainingType;
                    if (!IsTypeAllowed(type))
                    {
                        var line = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                        _errors.Add($"Line {line}: Type '{type.ToDisplayString()}' is not allowed");
                    }
                }

                base.VisitObjectCreationExpression(node);
            }

            public override void VisitInvocationExpression(InvocationExpressionSyntax node)
            {
                var symbolInfo = _model.GetSymbolInfo(node);
                var symbol = symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();

                if (symbol != null)
                {
                    var containingType = symbol.ContainingType;
                    if (!IsTypeAllowed(containingType))
                    {
                        var line = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                        _errors.Add($"Line {line}: Method '{symbol.ToDisplayString()}' on type '{containingType.ToDisplayString()}' is not allowed");
                    }
                }

                base.VisitInvocationExpression(node);
            }

            public override void VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
            {
                var symbolInfo = _model.GetSymbolInfo(node);
                var symbol = symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();

                if (symbol != null)
                {
                    var containingType = symbol.ContainingType;
                    if (containingType != null && !IsTypeAllowed(containingType))
                    {
                        var line = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                        _errors.Add($"Line {line}: Access to '{containingType.ToDisplayString()}.{symbol.Name}' is not allowed");
                    }
                }

                base.VisitMemberAccessExpression(node);
            }

            public override void VisitIdentifierName(IdentifierNameSyntax node)
            {
                // Check typeof() expressions for non-whitelisted types
                if (node.Parent is TypeOfExpressionSyntax)
                {
                    var typeInfo = _model.GetTypeInfo(node);
                    if (typeInfo.Type != null && !IsTypeAllowed(typeInfo.Type))
                    {
                        var line = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                        _errors.Add($"Line {line}: typeof({typeInfo.Type.ToDisplayString()}) is not allowed");
                    }
                }

                base.VisitIdentifierName(node);
            }
        }
    }
}
