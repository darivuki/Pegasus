﻿// -----------------------------------------------------------------------
// <copyright file="GenerateCodePass.cs" company="(none)">
//   Copyright © 2012 John Gietzen.  All Rights Reserved.
//   This source is subject to the MIT license.
//   Please see license.txt for more information.
// </copyright>
// -----------------------------------------------------------------------

namespace Pegasus.Compiler
{
    using System;
    using System.CodeDom.Compiler;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using System.Text;
    using Pegasus.Expressions;

    internal class GenerateCodePass : CompilePass
    {
        public override IList<string> ErrorsProduced
        {
            get { return new string[0]; }
        }

        public override IList<string> BlockedByErrors
        {
            get { return new[] { "PEG0001", "PEG0002", "PEG0003", "PEG0004", "PEG0005", "PEG0007", "PEG0012" }; }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2202:Do not dispose objects multiple times", Justification = "StringWriter.Dispose is idempotent.")]
        public override void Run(Grammar grammar, CompileResult result)
        {
            using (var stringWriter = new StringWriter())
            using (var codeWriter = new IndentedTextWriter(stringWriter))
            {
                new GenerateCodeExpressionTreeWlaker(result, codeWriter).WalkGrammar(grammar);
                result.Code = stringWriter.ToString();
            }
        }

        private class GenerateCodeExpressionTreeWlaker : ExpressionTreeWalker
        {
            private readonly IndentedTextWriter code;
            private readonly CompileResult result;
            private readonly Dictionary<string, int> variables = new Dictionary<string, int>();
            private Grammar grammar;
            private string currentResultName = null;
            private object currentResultType = null;

            public GenerateCodeExpressionTreeWlaker(CompileResult result, IndentedTextWriter codeWriter)
            {
                this.result = result;
                this.code = codeWriter;
            }

            private string CreateVariable(string prefix)
            {
                int instance;
                this.variables.TryGetValue(prefix, out instance);
                this.variables[prefix] = instance + 1;
                return prefix + instance;
            }

            private static HashSet<string> keywords = new HashSet<string>
            {
                "abstract", "as", "base",
                "bool", "break", "byte",
                "case", "catch", "char",
                "checked", "class", "const",
                "continue", "decimal", "default",
                "delegate", "do", "double",
                "else", "enum", "event",
                "explicit", "extern", "false",
                "finally", "fixed", "float",
                "for", "foreach", "goto",
                "if", "implicit", "in",
                "int", "interface", "internal",
                "is", "lock", "long",
                "namespace", "new", "null",
                "object", "operator", "out",
                "override", "params", "private",
                "protected", "public", "readonly",
                "ref", "return", "sbyte",
                "sealed", "short", "sizeof",
                "stackalloc", "static", "string",
                "struct", "switch", "this",
                "throw", "true", "try",
                "typeof", "uint", "ulong",
                "unchecked", "unsafe", "ushort",
                "using", "virtual", "void",
                "volatile", "while",
            };

            private static string EscapeName(string name)
            {
                return keywords.Contains(name) ? "@" + name : name;
            }

            public override void WalkGrammar(Grammar grammar)
            {
                this.grammar = grammar;
                var settings = grammar.Settings.ToLookup(s => s.Key.Name, s => s.Value);

                var assemblyName = Assembly.GetExecutingAssembly().GetName();
                this.code.WriteLine("// -----------------------------------------------------------------------");
                this.code.WriteLine("// <auto-generated>");
                this.code.WriteLine("// This code was generated by " + assemblyName.Name + " " + assemblyName.Version);
                this.code.WriteLine("//");
                this.code.WriteLine("// Changes to this file may cause incorrect behavior and will be lost if");
                this.code.WriteLine("// the code is regenerated.");
                this.code.WriteLine("// </auto-generated>");
                this.code.WriteLine("// -----------------------------------------------------------------------");
                this.code.WriteLineNoTabs(string.Empty);

                var @namespace = settings["namespace"].SingleOrDefault() ?? "Parsers";
                var classname = settings["classname"].SingleOrDefault() ?? "Parser";
                var accessibility = settings["accessibility"].SingleOrDefault() ?? "public";

                this.code.WriteLine("namespace");
                this.WriteCodeSpanOrString(@namespace);
                this.code.WriteLine("{");
                this.code.Indent++;
                this.code.WriteLine("using System;");
                this.code.WriteLine("using System.Collections.Generic;");

                foreach (var @using in settings["using"])
                {
                    this.code.WriteLine("using");
                    this.WriteCodeSpanOrString(@using);
                    this.code.WriteLine(";");
                }

                this.code.WriteLineNoTabs(string.Empty);

                this.code.WriteLine("[System.CodeDom.Compiler.GeneratedCode(\"" + assemblyName.Name + "\", \"" + assemblyName.Version + "\")]");
                this.code.WriteLine(accessibility + " partial class");
                this.WriteCodeSpanOrString(classname, EscapeName);
                this.code.WriteLine("{");
                this.code.Indent++;

                foreach (var members in settings["members"])
                {
                    if (members is CodeSpan)
                    {
                        this.WriteCodeSpan((CodeSpan)members);
                    }
                    else
                    {
                        this.code.WriteLineNoTabs(members.ToString());
                    }

                    this.code.WriteLineNoTabs(string.Empty);
                }

                var memoize = grammar.Rules.SelectMany(r => r.Flags.Select(f => f.Name)).Any(f => f == "memoize");
                if (memoize)
                {
                    this.code.WriteLine("private Dictionary<string, object> storage;");
                }

                var type = this.GetResultType(grammar.Rules[0].Expression);

                this.code.WriteLine("public " + type + " Parse(string subject, string fileName = null)");
                this.code.WriteLine("{");
                this.code.Indent++;

                if (memoize)
                {
                    this.code.WriteLine("try");
                    this.code.WriteLine("{");
                    this.code.Indent++;
                    this.code.WriteLine("this.storage = new Dictionary<string, object>();");
                }

                this.code.WriteLine("var cursor = new Cursor(subject, 0, fileName);");
                this.code.WriteLine("var result = this." + EscapeName(grammar.Rules[0].Identifier.Name) + "(ref cursor);");
                this.code.WriteLine("if (result == null)");
                this.code.WriteLine("{");
                this.code.Indent++;
                this.code.WriteLine("throw ExceptionHelper(cursor, state => \"Failed to parse '" + grammar.Rules[0].Identifier.Name + "'.\");");
                this.code.Indent--;
                this.code.WriteLine("}");
                this.code.WriteLine("return result.Value;");

                if (memoize)
                {
                    this.code.Indent--;
                    this.code.WriteLine("}");
                    this.code.WriteLine("finally");
                    this.code.WriteLine("{");
                    this.code.Indent++;
                    this.code.WriteLine("this.storage = null;");
                    this.code.Indent--;
                    this.code.WriteLine("}");
                }

                this.code.Indent--;
                this.code.WriteLine("}");

                base.WalkGrammar(grammar);

                this.code.WriteLineNoTabs(string.Empty);
                this.code.WriteLine("private IParseResult<string> ParseLiteral(ref Cursor cursor, string literal, bool ignoreCase = false)");
                this.code.WriteLine("{");
                this.code.Indent++;
                this.code.WriteLine("if (cursor.Location + literal.Length <= cursor.Subject.Length)");
                this.code.WriteLine("{");
                this.code.Indent++;
                this.code.WriteLine("var substr = cursor.Subject.Substring(cursor.Location, literal.Length);");
                this.code.WriteLine("if (ignoreCase ? substr.Equals(literal, StringComparison.OrdinalIgnoreCase) : substr == literal)");
                this.code.WriteLine("{");
                this.code.Indent++;
                this.code.WriteLine("var endCursor = cursor.Advance(substr.Length);");
                this.code.WriteLine("var result = new ParseResult<string>(cursor, endCursor, substr);");
                this.code.WriteLine("cursor = endCursor;");
                this.code.WriteLine("return result;");
                this.code.Indent--;
                this.code.WriteLine("}");
                this.code.Indent--;
                this.code.WriteLine("}");
                this.code.WriteLine("return null;");
                this.code.Indent--;
                this.code.WriteLine("}");

                this.code.WriteLineNoTabs(string.Empty);
                this.code.WriteLine("private IParseResult<string> ParseClass(ref Cursor cursor, string characterRanges, bool negated = false, bool ignoreCase = false)");
                this.code.WriteLine("{");
                this.code.Indent++;
                this.code.WriteLine("if (cursor.Location + 1 <= cursor.Subject.Length)");
                this.code.WriteLine("{");
                this.code.Indent++;
                this.code.WriteLine("var c = cursor.Subject[cursor.Location];");
                this.code.WriteLine("bool match = false;");
                this.code.WriteLine("for (int i = 0; !match && i < characterRanges.Length; i += 2)");
                this.code.WriteLine("{");
                this.code.Indent++;
                this.code.WriteLine("match = c >= characterRanges[i] && c <= characterRanges[i + 1];");
                this.code.Indent--;
                this.code.WriteLine("}");
                this.code.WriteLine("if (!match && ignoreCase && (char.IsUpper(c) || char.IsLower(c)))");
                this.code.WriteLine("{");
                this.code.Indent++;
                this.code.WriteLine("var cs = c.ToString();");
                this.code.WriteLine("for (int i = 0; !match && i < characterRanges.Length; i += 2)");
                this.code.WriteLine("{");
                this.code.Indent++;
                this.code.WriteLine("var min = characterRanges[i];");
                this.code.WriteLine("var max = characterRanges[i + 1];");
                this.code.WriteLine("for (char o = min; !match && o <= max; o++)");
                this.code.WriteLine("{");
                this.code.Indent++;
                this.code.WriteLine("match = (char.IsUpper(o) || char.IsLower(o)) && cs.Equals(o.ToString(), StringComparison.CurrentCultureIgnoreCase);");
                this.code.Indent--;
                this.code.WriteLine("}");
                this.code.Indent--;
                this.code.WriteLine("}");
                this.code.Indent--;
                this.code.WriteLine("}");
                this.code.WriteLine("if (match ^ negated)");
                this.code.WriteLine("{");
                this.code.Indent++;
                this.code.WriteLine("var endCursor = cursor.Advance(1);");
                this.code.WriteLine("var result = new ParseResult<string>(cursor, endCursor, cursor.Subject.Substring(cursor.Location, 1));");
                this.code.WriteLine("cursor = endCursor;");
                this.code.WriteLine("return result;");
                this.code.Indent--;
                this.code.WriteLine("}");
                this.code.Indent--;
                this.code.WriteLine("}");
                this.code.WriteLine("return null;");
                this.code.Indent--;
                this.code.WriteLine("}");

                this.code.WriteLineNoTabs(string.Empty);
                this.code.WriteLine("private IParseResult<string> ParseAny(ref Cursor cursor)");
                this.code.WriteLine("{");
                this.code.Indent++;
                this.code.WriteLine("if (cursor.Location + 1 <= cursor.Subject.Length)");
                this.code.WriteLine("{");
                this.code.Indent++;
                this.code.WriteLine("var substr = cursor.Subject.Substring(cursor.Location, 1);");
                this.code.WriteLine("var endCursor = cursor.Advance(1);");
                this.code.WriteLine("var result = new ParseResult<string>(cursor, endCursor, substr);");
                this.code.WriteLine("cursor = endCursor;");
                this.code.WriteLine("return result;");
                this.code.Indent--;
                this.code.WriteLine("}");
                this.code.WriteLine("return null;");
                this.code.Indent--;
                this.code.WriteLine("}");

                this.code.WriteLineNoTabs(string.Empty);
                this.code.WriteLine("private IParseResult<T> ReturnHelper<T>(Cursor startCursor, Cursor endCursor, Func<Cursor, T> wrappedCode)");
                this.code.WriteLine("{");
                this.code.Indent++;
                this.code.WriteLine("return new ParseResult<T>(startCursor, endCursor, wrappedCode(endCursor));");
                this.code.Indent--;
                this.code.WriteLine("}");

                this.code.WriteLineNoTabs(string.Empty);
                this.code.WriteLine("private Exception ExceptionHelper(Cursor cursor, Func<Cursor, string> wrappedCode)");
                this.code.WriteLine("{");
                this.code.Indent++;
                this.code.WriteLine("var ex = new FormatException(wrappedCode(cursor));");
                this.code.WriteLine("ex.Data[\"cursor\"] = cursor;");
                this.code.WriteLine("return ex;");
                this.code.Indent--;
                this.code.WriteLine("}");

                this.code.WriteLineNoTabs(string.Empty);
                this.code.WriteLine("private T ValueOrDefault<T>(IParseResult<T> result)");
                this.code.WriteLine("{");
                this.code.Indent++;
                this.code.WriteLine("return result == null");
                this.code.Indent++;
                this.code.WriteLine("? default(T)");
                this.code.WriteLine(": result.Value;");
                this.code.Indent--;
                this.code.Indent--;
                this.code.WriteLine("}");

                this.code.Indent--;
                this.code.WriteLine("}");

                this.code.Indent--;
                this.code.WriteLine("}");

                this.grammar = null;
            }

            protected override void WalkRule(Rule rule)
            {
                this.currentResultName = this.CreateVariable("r");
                this.currentResultType = this.GetResultType(rule.Expression);

                this.code.WriteLineNoTabs(string.Empty);
                if (rule.Expression is TypedExpression)
                {
                    this.code.WriteLine("private IParseResult<");
                    this.WriteCodeSpanOrString(this.currentResultType);
                    this.code.WriteLine("> " + EscapeName(rule.Identifier.Name) + "(ref Cursor cursor)");
                }
                else
                {
                    this.code.WriteLine("private IParseResult<" + this.currentResultType + "> " + EscapeName(rule.Identifier.Name) + "(ref Cursor cursor)");
                }

                this.code.WriteLine("{");
                this.code.Indent++;

                this.code.WriteLine("IParseResult<" + this.currentResultType + "> " + this.currentResultName + " = null;");

                var memoize = rule.Flags.Any(f => f.Name == "memoize");
                if (memoize)
                {
                    this.code.WriteLine("var storageKey = " + ToLiteral(rule.Identifier.Name + ":") + " + cursor.StateKey + \":\" + cursor.Location;");
                    this.code.WriteLine("if (this.storage.ContainsKey(storageKey))");
                    this.code.WriteLine("{");
                    this.code.Indent++;
                    this.code.WriteLine(this.currentResultName + " = (IParseResult<" + this.currentResultType + ">)this.storage[storageKey];");
                    this.code.WriteLine("if (" + this.currentResultName + " != null)");
                    this.code.WriteLine("{");
                    this.code.Indent++;
                    this.code.WriteLine("cursor = " + this.currentResultName + ".EndCursor;");
                    this.code.Indent--;
                    this.code.WriteLine("}");
                    this.code.WriteLine("return " + this.currentResultName + ";");
                    this.code.Indent--;
                    this.code.WriteLine("}");
                }

                base.WalkRule(rule);

                if (memoize)
                {
                    this.code.WriteLine("this.storage[storageKey] = " + this.currentResultName + ";");
                }

                this.code.WriteLine("return " + this.currentResultName + ";");
                this.code.Indent--;
                this.code.WriteLine("}");

                this.currentResultName = null;
                this.currentResultType = null;
                this.variables.Clear();
            }

            protected override void WalkLiteralExpression(LiteralExpression literalExpression)
            {
                this.code.WriteLine(this.currentResultName + " = this.ParseLiteral(ref cursor, " + ToLiteral(literalExpression.Value) + (literalExpression.IgnoreCase ? ", ignoreCase: true" : string.Empty) + ");");
            }

            protected override void WalkWildcardExpression(WildcardExpression wildcardExpression)
            {
                this.code.WriteLine(this.currentResultName + " = this.ParseAny(ref cursor);");
            }

            protected override void WalkNameExpression(NameExpression nameExpression)
            {
                this.code.WriteLine(this.currentResultName + " = this." + EscapeName(nameExpression.Identifier.Name) + "(ref cursor);");
            }

            protected override void WalkClassExpression(ClassExpression classExpression)
            {
                var ranges = string.Join(string.Empty, classExpression.Ranges.SelectMany(r => new[] { r.Min, r.Max }));
                this.code.WriteLine(this.currentResultName + " = this.ParseClass(ref cursor, " + ToLiteral(ranges) + (classExpression.Negated ? ", negated: true" : string.Empty) + (classExpression.IgnoreCase ? ", ignoreCase: true" : string.Empty) + ");");
            }

            protected override void WalkCodeExpression(CodeExpression codeExpression)
            {
                if (codeExpression.CodeType != CodeType.State)
                {
                    throw new InvalidOperationException("Code expressions are only valid at the end of a sequence expression.");
                }

                var startCursorName = this.CreateVariable("startCursor");
                this.code.WriteLine("var " + startCursorName + " = cursor;");
                this.code.WriteLine("{");
                this.code.Indent++;
                this.code.WriteLine("var state = cursor.WithMutability(mutable: true);");
                this.WriteCodeSpan(codeExpression.CodeSpan);
                this.code.WriteLine("cursor = state.WithMutability(mutable: false);");
                this.code.Indent--;
                this.code.WriteLine("}");
                this.code.WriteLine(this.currentResultName + " = new ParseResult<string>(" + startCursorName + ", cursor, null);");
            }

            protected override void WalkSequenceExpression(SequenceExpression sequenceExpression)
            {
                var startCursorName = this.CreateVariable("startCursor");
                this.code.WriteLine("var " + startCursorName + " = cursor;");

                var oldResultName = this.currentResultName;
                var oldResultType = this.currentResultType;

                var sequence = sequenceExpression.Sequence;
                CodeExpression codeExpression = null;
                if (sequence.Count > 0)
                {
                    codeExpression = sequence[sequence.Count - 1] as CodeExpression;
                    if (codeExpression != null && codeExpression.CodeType != CodeType.State)
                    {
                        sequence = sequence.Take(sequence.Count - 1).ToList();
                    }
                }

                foreach (var expression in sequence)
                {
                    bool isDefinition;

                    this.currentResultName = this.CreateVariable("r");
                    this.currentResultType = this.GetResultType(expression, out isDefinition);

                    if (this.currentResultType is CodeSpan && isDefinition)
                    {
                        this.code.WriteLine("IParseResult<");
                        this.WriteCodeSpan((CodeSpan)this.currentResultType);
                        this.code.WriteLine("> " + this.currentResultName + " = null;");
                    }
                    else
                    {
                        this.code.WriteLine("IParseResult<" + this.currentResultType + "> " + this.currentResultName + " = null;");
                    }

                    this.WalkExpression(expression);
                    this.code.WriteLine("if (" + this.currentResultName + " != null)");
                    this.code.WriteLine("{");
                    this.code.Indent++;
                }

                this.currentResultName = oldResultName;
                this.currentResultType = oldResultType;

                if (codeExpression == null)
                {
                    this.code.WriteLine("var len = cursor.Location - " + startCursorName + ".Location;");
                    this.code.WriteLine(this.currentResultName + " = new ParseResult<string>(" + startCursorName + ", cursor, cursor.Subject.Substring(" + startCursorName + ".Location, len));");
                }
                else
                {
                    if (codeExpression.CodeType == CodeType.Result)
                    {
                        this.code.WriteLine(this.currentResultName + " = this.ReturnHelper<" + this.currentResultType + ">(" + startCursorName + ", cursor, state =>");
                        this.WriteCodeSpan(codeExpression.CodeSpan);
                        this.code.WriteLine(");");
                    }
                    else if (codeExpression.CodeType == CodeType.Error)
                    {
                        this.code.WriteLine("throw this.ExceptionHelper(" + startCursorName + ", state =>");
                        this.WriteCodeSpan(codeExpression.CodeSpan);
                        this.code.WriteLine(");");
                    }
                }

                for (int i = 0; i < sequence.Count; i++)
                {
                    this.code.Indent--;
                    this.code.WriteLine("}");
                    this.code.WriteLine("else");
                    this.code.WriteLine("{");
                    this.code.Indent++;
                    this.code.WriteLine("cursor = " + startCursorName + ";");
                    this.code.Indent--;
                    this.code.WriteLine("}");
                }
            }

            private void WriteCodeSpanOrString(object value, Func<string, string> stringTransform = null)
            {
                if (value is CodeSpan)
                {
                    this.WriteCodeSpan((CodeSpan)value);
                }
                else
                {
                    var @out = value.ToString();
                    if (stringTransform != null)
                    {
                        @out = stringTransform(@out);
                    }

                    this.code.WriteLine(@out);
                }
            }

            private void WriteCodeSpan(CodeSpan codeSpan)
            {
                this.code.WriteLineNoTabs("#line " + codeSpan.Start.Line + " \"" + Path.GetFileName(codeSpan.Start.FileName) + "\"");
                this.code.WriteLineNoTabs(new string(' ', codeSpan.Start.Column - 1) + codeSpan.Code);
                this.code.WriteLineNoTabs("#line default");
            }

            protected override void WalkChoiceExpression(ChoiceExpression choiceExpression)
            {
                foreach (var expression in choiceExpression.Choices)
                {
                    this.code.WriteLine("if (" + this.currentResultName + " == null)");
                    this.code.WriteLine("{");
                    this.code.Indent++;
                    this.WalkExpression(expression);
                    this.code.Indent--;
                    this.code.WriteLine("}");
                }
            }

            protected override void WalkRepetitionExpression(RepetitionExpression repetitionExpression)
            {
                var startCursorName = this.CreateVariable("startCursor");
                this.code.WriteLine("var " + startCursorName + " = cursor;");

                var listName = this.CreateVariable("l");

                var oldResultName = this.currentResultName;
                var oldResultType = this.currentResultType;
                this.currentResultName = this.CreateVariable("r");
                this.currentResultType = this.GetResultType(repetitionExpression.Expression);

                this.code.WriteLine("var " + listName + " = new List<" + this.currentResultType + ">();");
                this.code.WriteLine("while (" + (repetitionExpression.Max.HasValue ? listName + ".Count < " + repetitionExpression.Max : "true") + ")");
                this.code.WriteLine("{");
                this.code.Indent++;
                this.code.WriteLine("IParseResult<" + this.currentResultType + "> " + this.currentResultName + " = null;");
                this.WalkExpression(repetitionExpression.Expression);
                this.code.WriteLine("if (" + this.currentResultName + " != null)");
                this.code.WriteLine("{");
                this.code.Indent++;
                this.code.WriteLine(listName + ".Add(" + this.currentResultName + ".Value);");
                this.code.Indent--;
                this.code.WriteLine("}");
                this.code.WriteLine("else");
                this.code.WriteLine("{");
                this.code.Indent++;
                this.code.WriteLine("break;");
                this.code.Indent--;
                this.code.WriteLine("}");
                this.code.Indent--;
                this.code.WriteLine("}");

                this.currentResultName = oldResultName;
                this.currentResultType = oldResultType;

                this.code.WriteLine("if (" + listName + ".Count >= " + repetitionExpression.Min + ")");
                this.code.WriteLine("{");
                this.code.Indent++;
                this.code.WriteLine(this.currentResultName + " = new ParseResult<" + this.GetResultType(repetitionExpression) + ">(" + startCursorName + ", cursor, " + listName + ".AsReadOnly());");
                this.code.Indent--;
                this.code.WriteLine("}");
                this.code.WriteLine("else");
                this.code.WriteLine("{");
                this.code.Indent++;
                this.code.WriteLine("cursor = " + startCursorName + ";");
                this.code.Indent--;
                this.code.WriteLine("}");
            }

            protected override void WalkAndCodeExpression(AndCodeExpression andCodeExpression)
            {
                this.WalkAssertionExpression(andCodeExpression.Code, mustMatch: true);
            }

            protected override void WalkAndExpression(AndExpression andExpression)
            {
                this.WalkAssertionExpression(andExpression.Expression, mustMatch: true);
            }

            protected override void WalkNotCodeExpression(NotCodeExpression notCodeExpression)
            {
                this.WalkAssertionExpression(notCodeExpression.Code, mustMatch: false);
            }

            protected override void WalkNotExpression(NotExpression notExpression)
            {
                this.WalkAssertionExpression(notExpression.Expression, mustMatch: false);
            }

            private void WalkAssertionExpression(CodeSpan code, bool mustMatch)
            {
                this.code.WriteLine("if (" + (mustMatch ? string.Empty : "!") + "new Func<Cursor, bool>(state =>");
                this.WriteCodeSpan(code);
                this.code.WriteLine(")(cursor))");
                this.code.WriteLine("{");
                this.code.Indent++;
                this.code.WriteLine(this.currentResultName + " = new ParseResult<string>(cursor, cursor, string.Empty);");
                this.code.Indent--;
                this.code.WriteLine("}");
            }

            private void WalkAssertionExpression(Expression expression, bool mustMatch)
            {
                var startCursorName = this.CreateVariable("startCursor");
                this.code.WriteLine("var " + startCursorName + " = cursor;");

                var oldResultName = this.currentResultName;
                var oldResultType = this.currentResultType;
                this.currentResultName = this.CreateVariable("r");
                this.currentResultType = this.GetResultType(expression);

                this.code.WriteLine("IParseResult<" + this.currentResultType + "> " + this.currentResultName + " = null;");
                this.WalkExpression(expression);

                this.code.WriteLine("cursor = " + startCursorName + ";");

                this.code.WriteLine("if (" + this.currentResultName + " " + (mustMatch ? "!=" : "==") + " null)");
                this.code.WriteLine("{");
                this.code.Indent++;
                this.code.WriteLine(oldResultName + " = new ParseResult<string>(cursor, cursor, string.Empty);");
                this.code.Indent--;
                this.code.WriteLine("}");

                this.currentResultName = oldResultName;
                this.currentResultType = oldResultType;
            }

            protected override void WalkPrefixedExpression(PrefixedExpression prefixedExpression)
            {
                this.code.WriteLine("var " + EscapeName(prefixedExpression.Prefix.Name + "Start") + " = cursor;");
                this.WalkExpression(prefixedExpression.Expression);
                this.code.WriteLine("var " + EscapeName(prefixedExpression.Prefix.Name + "End") + " = cursor;");
                this.code.WriteLine("var " + EscapeName(prefixedExpression.Prefix.Name) + " = ValueOrDefault(" + this.currentResultName + ");");
            }

            private static Dictionary<char, string> simpleEscapeChars = new Dictionary<char, string>()
            {
                { '\'', "\\'" }, { '\"', "\\\"" }, { '\\', "\\\\" }, { '\0', "\\0" },
                { '\a', "\\a" }, { '\b', "\\b" }, { '\f', "\\f" }, { '\n', "\\n" },
                { '\r', "\\r" }, { '\t', "\\t" }, { '\v', "\\v" },
            };

            private static string ToLiteral(string input)
            {
                var sb = new StringBuilder();
                sb.Append("\"");
                for (int i = 0; i < input.Length; i++)
                {
                    var c = input[i];

                    string literal;
                    if (simpleEscapeChars.TryGetValue(c, out literal))
                    {
                        sb.Append(literal);
                    }
                    else if (c >= 32 && c <= 126)
                    {
                        sb.Append(c);
                    }
                    else
                    {
                        sb.Append("\\u").Append(((int)c).ToString("x4"));
                    }
                }

                sb.Append("\"");
                return sb.ToString();
            }

            private object GetResultType(Expression expression)
            {
                bool waste;
                return this.GetResultType(expression, out waste);
            }

            private object GetResultType(Expression expression, out bool isDefinition)
            {
                isDefinition = false;

                ChoiceExpression choiceExpression;
                NameExpression nameExpression;
                PrefixedExpression prefixedExpression;
                RepetitionExpression repetitionExpression;
                TypedExpression typedExpression;

                if ((choiceExpression = expression as ChoiceExpression) != null)
                {
                    return this.GetResultType(choiceExpression.Choices.First(), out isDefinition);
                }
                else if ((nameExpression = expression as NameExpression) != null)
                {
                    var rule = this.grammar.Rules.Where(r => r.Identifier.Name == nameExpression.Identifier.Name).Single();
                    return this.GetResultType(rule.Expression);
                }
                else if ((prefixedExpression = expression as PrefixedExpression) != null)
                {
                    return this.GetResultType(prefixedExpression.Expression, out isDefinition);
                }
                else if ((repetitionExpression = expression as RepetitionExpression) != null)
                {
                    return "IList<" + this.GetResultType(repetitionExpression.Expression, out isDefinition) + ">";
                }
                else if ((typedExpression = expression as TypedExpression) != null)
                {
                    isDefinition = true;
                    return typedExpression.Type;
                }
                else
                {
                    return "string";
                }
            }
        }
    }
}
