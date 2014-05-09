using System;
using System.Collections.Generic;
using System.Linq;
using Roslyn.Compilers.Common;
using Roslyn.Compilers.CSharp;
using System.IO;

namespace Top100
{
    interface IWalker
    {
        int Result { get; set; }
        void Visit(SyntaxNode node);
    }
    struct ResultRepresentation
    {
        public string FileName { get; private set; }

        /// <summary>
        /// Function starts line
        /// </summary>
        public int Line { get; private set; }
        public int Value { get; private set; }
        public ResultRepresentation(string fileName, int line, int statementsCount)
            : this()
        {
            FileName = fileName;
            Line = line;
            Value = statementsCount;
        }
        public override string ToString()
        {
            return String.Format("{0}\t{1}:{2}", Value, FileName, Line);
        }
    }
    public static class Extensions
    {
        /// <summary>
        /// Write all items in IEnumerable to file specified by path
        /// </summary>
        /// <typeparam name="T">is T</typeparam>
        /// <param name="source">IEnumerable of items</param>
        /// <param name="path">path to write</param>
        public static void WriteTo<T>(this IEnumerable<T> source, string path)
        {
            File.WriteAllLines(path, source.Select(x => x.ToString()));
        }
    }
    class NestingCounter : SyntaxWalker, IWalker
    {
        private readonly HashSet<SyntaxKind> nestingEnlargerSyntaxKinds;

        /// <summary>
        /// Nesting level
        /// </summary>
        public int Result { get; set; }
        public NestingCounter()
        {
            Result = 0;
            nestingEnlargerSyntaxKinds = new HashSet<SyntaxKind>
            {
                SyntaxKind.AnonymousMethodExpression,
                SyntaxKind.CheckedStatement,
                SyntaxKind.DoStatement,
                SyntaxKind.FixedStatement,
                SyntaxKind.ForStatement,
                SyntaxKind.ForEachStatement,
                SyntaxKind.IfStatement,
                SyntaxKind.LockStatement,
                SyntaxKind.ParenthesizedLambdaExpression,
                SyntaxKind.SwitchStatement,
                SyntaxKind.TryStatement,
                SyntaxKind.UnsafeStatement,
                SyntaxKind.UncheckedStatement,
                SyntaxKind.WhileStatement
            };
        }
        public NestingCounter(int nestingLevel, HashSet<SyntaxKind> nestingEnlarger)
        {
            Result = nestingLevel;
            nestingEnlargerSyntaxKinds = nestingEnlarger;
        }

        /// <summary>
        /// Count nesting in node
        /// </summary>
        /// <param name="node">Something node</param>
        /// <returns>Nesting in node</returns>
        private int CountNesting(SyntaxNode node)
        {
            if (Depth != SyntaxWalkerDepth.Node) return 0;
            var nestingCounter = new NestingCounter(Result, nestingEnlargerSyntaxKinds);
            nestingCounter.Visit(node);
            return nestingCounter.Result + (nestingEnlargerSyntaxKinds.Contains(node.Kind) ? 1 : 0);
        }
        public override void Visit(SyntaxNode node)
        {
            if (!node.ChildNodes().Any()) return;
            Result = 
                node.ChildNodes()
                .Select(CountNesting)
                .Max();
        }
    }
    class StatementCounter : SyntaxWalker, IWalker
    {
        /// <summary>
        /// Statements count
        /// </summary>
        public int Result { get; set; }
        public StatementCounter()
        {
            Result = 0;
        }
        public override void Visit(SyntaxNode node)
        {
            if (Depth != SyntaxWalkerDepth.Node) return;
            if (node is StatementSyntax && node.Kind != SyntaxKind.Block)
                Result++;
            base.Visit(node);
        }
    }
    enum ParserType
    {
        Nesting,
        Long
    }
    class Program
    {
        /// <summary>
        /// Parse all "*.cs" files in path to functions according to definition of function in https://kontur.ru/education/programs/intern/backend
        /// </summary>
        /// <param name="path">Path to scource files</param>
        /// <returns>IEnumerable of functions</returns>
        public static IEnumerable<SyntaxNode> ParseToFunctions(string path)
        {
            return Directory.EnumerateFiles(path)
                .AsParallel()
                .Where(filePath => Path.GetExtension(filePath) == ".cs")
                .SelectMany(filePath => SyntaxTree.ParseFile(filePath)
                    .GetRoot()
                    .DescendantNodes()
                    .OfType<TypeDeclarationSyntax>())
                .Where(x => !(x is InterfaceDeclarationSyntax))
                .SelectMany(x => x.ChildNodes()
                    .Where(y => y.DescendantNodes().Any(z => z is StatementSyntax && !(z is BlockSyntax))));
        }

        /// <summary>
        /// Format result from function and its own IWalker walker
        /// </summary>
        /// <param name="function">parsed function</param>
        /// <param name="walker">IWalker used to parse function</param>
        /// <returns>ResultRepresentation corresponding to function and walker</returns>
        public static ResultRepresentation FormatResult(SyntaxNode function, IWalker walker)
        {
            var methodStartsLine = function.GetLocation().GetLineSpan(true).StartLinePosition.Line + 1;
            var fileName = Path.GetFileName(function.SyntaxTree.FilePath);
            return new ResultRepresentation(fileName, methodStartsLine, walker.Result);
        }

        /// <summary>
        /// Count result corresponding to parser type
        /// </summary>
        /// <param name="function">function to processing</param>
        /// <param name="type">parser type</param>
        /// <returns>result as ResultRepresentation</returns>
        public static ResultRepresentation CountResult(SyntaxNode function, ParserType type)
        {
            IWalker walker;
            if (type == ParserType.Nesting) walker = new NestingCounter();
            else walker = new StatementCounter();
            walker.Visit(function);
            return FormatResult(function, walker);
        }

        /// <summary>
        /// Count Top 100 according to parser type
        /// </summary>
        /// <param name="path">Path of scource files</param>
        /// <param name="type">parser type</param>
        /// <returns>IEnumerable of ResultRepresentation</returns>
        public static IEnumerable<ResultRepresentation> Top100(string path, ParserType type)
        {
            return ParseToFunctions(path)
                .AsParallel()
            .Select(method => CountResult(method, type))
            .OrderByDescending(x => x.Value)
            .ThenBy(x => x.FileName)
            .ThenBy(x => x.Line);
        }
        
        static void Main(string[] args)
        {
            Top100(args[0], ParserType.Long).Take(100).WriteTo(args[1]);
            Top100(args[0], ParserType.Nesting).Take(100).WriteTo(args[2]);
        }
    }
}