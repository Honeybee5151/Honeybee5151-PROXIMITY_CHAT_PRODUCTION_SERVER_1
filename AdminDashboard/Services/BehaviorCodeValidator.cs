using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace AdminDashboard.Services
{
    /// <summary>
    /// Lightweight syntax-level validator for community C# behavior code.
    /// Runs at approval time in AdminDashboard (no WorldServer assembly available).
    /// The full semantic whitelist analysis runs at server startup via BehaviorCodeAnalyzer.
    /// </summary>
    public static class BehaviorCodeValidator
    {
        // Dangerous namespace prefixes that should never appear in behavior code
        private static readonly string[] BlockedNamespaces = new[]
        {
            "System.IO", "System.Net", "System.Reflection", "System.Diagnostics",
            "System.Runtime", "System.Threading", "System.Security",
            "Microsoft.Win32", "System.Environment",
        };

        private const string TEMPLATE = @"
using WorldServer.logic;
using WorldServer.logic.behaviors;
using WorldServer.logic.transitions;
using WorldServer.logic.loot;
using Shared.resources;

namespace WorldServer.logic.db.community
{{
    public static class Behavior_{0}
    {{
        public static void Register(BehaviorDb db)
        {{
            {1}
        }}
    }}
}}
";

        public static (bool IsValid, List<string> Errors) Validate(string userCode)
        {
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(userCode))
            {
                errors.Add("Behavior code is empty");
                return (false, errors);
            }

            // Wrap in template for parsing
            var safeName = Regex.Replace("UserCode", @"[^\w]", "_");
            var fullSource = string.Format(TEMPLATE, safeName, userCode);

            // Parse with Roslyn
            var tree = CSharpSyntaxTree.ParseText(fullSource);
            var root = tree.GetRoot();

            // Check for parse errors
            var diagnostics = tree.GetDiagnostics()
                .Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
            if (diagnostics.Any())
            {
                foreach (var diag in diagnostics)
                    errors.Add($"Syntax error: {diag.GetMessage()}");
                return (false, errors);
            }

            // Block unsafe constructs
            foreach (var node in root.DescendantNodes().OfType<UnsafeStatementSyntax>())
                errors.Add("'unsafe' blocks are not allowed");

            foreach (var node in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                if (node.Modifiers.Any(SyntaxKind.ExternKeyword))
                    errors.Add("'extern' methods are not allowed");
            }

            foreach (var node in root.DescendantNodes().OfType<DelegateDeclarationSyntax>())
                errors.Add("Delegate declarations are not allowed");

            foreach (var node in root.DescendantNodes().OfType<LambdaExpressionSyntax>())
                errors.Add("Lambda expressions are not allowed");

            foreach (var node in root.DescendantNodes().OfType<IdentifierNameSyntax>())
            {
                if (node.Identifier.Text == "dynamic")
                    errors.Add("'dynamic' type is not allowed");
            }

            // Block class/struct/interface declarations (user code should only be statements)
            foreach (var node in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
            {
                if (node.Identifier.Text != $"Behavior_{safeName}")
                    errors.Add($"Class declarations are not allowed: '{node.Identifier.Text}'");
            }

            foreach (var node in root.DescendantNodes().OfType<StructDeclarationSyntax>())
                errors.Add($"Struct declarations are not allowed: '{node.Identifier.Text}'");

            foreach (var node in root.DescendantNodes().OfType<InterfaceDeclarationSyntax>())
                errors.Add($"Interface declarations are not allowed: '{node.Identifier.Text}'");

            // Text-level check for blocked namespaces in identifier names
            var sourceText = userCode;
            foreach (var ns in BlockedNamespaces)
            {
                if (sourceText.Contains(ns))
                    errors.Add($"Reference to '{ns}' is not allowed");
            }

            return (errors.Count == 0, errors);
        }
    }
}
