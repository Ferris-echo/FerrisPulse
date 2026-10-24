// ============================================================================
// 模块名称: ExpressionEvaluator.cs
// 所属区域: Hardware (硬件协议计算层)
// 核心职责:
//   FDAS 动态协议微表达式安全求值引擎。负责在纯内存中解析并求值外设协议中的动态算式
//   （如四则运算、移位、位掩码、互补校验、EEPROM 寄存器地址映射等），实现任意厂商
//   私有报文算法在运行时无需重新编译 C# 代码即可动态计算。
// 依赖关系: 无外部依赖，原生 .NET Framework 4.0 / C# 5.0
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 任何 AI Agent 在修改本文件逻辑、语法或签名时，【必须】同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法（❌ 严禁 $"..." 插值，必须使用 string.Format 或 + 拼接）。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RazerBatteryTray
{
    public static class ExpressionEvaluator
    {
        public static long Evaluate(string expression, IDictionary<string, long> variables)
        {
            if (string.IsNullOrEmpty(expression)) return 0;
            try
            {
                var parser = new Parser(expression, variables);
                return parser.Parse();
            }
            catch
            {
                return 0;
            }
        }

        public static string ExpandTemplate(string template, IDictionary<string, long> context, IDictionary<string, string> expressionVariables)
        {
            if (string.IsNullOrEmpty(template)) return string.Empty;

            var vars = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            if (context != null)
            {
                foreach (var kvp in context)
                {
                    vars[kvp.Key] = kvp.Value;
                }
            }

            // 1. 计算由 JSON variables 中定义的派生变量（按依赖或定义顺序逐级求值）
            if (expressionVariables != null)
            {
                foreach (var kvp in expressionVariables)
                {
                    if (string.IsNullOrEmpty(kvp.Key) || string.IsNullOrEmpty(kvp.Value)) continue;
                    long val = Evaluate(kvp.Value, vars);
                    vars[kvp.Key] = val;
                }
            }

            // 2. 扫描并替换模板中的占位符与表达式：{...}
            StringBuilder sb = new StringBuilder();
            int i = 0;
            while (i < template.Length)
            {
                if (template[i] == '{')
                {
                    int close = template.IndexOf('}', i);
                    if (close > i)
                    {
                        string inner = template.Substring(i + 1, close - i - 1).Trim();
                        string formatSpec = "X2"; // 默认两字节十六进制

                        // 支持指定格式，例如 {dpi:X4} 或 {dpiAddr:4}
                        if (inner.Contains(":"))
                        {
                            int colon = inner.IndexOf(':');
                            string spec = inner.Substring(colon + 1).Trim();
                            inner = inner.Substring(0, colon).Trim();
                            if (spec.StartsWith("X", StringComparison.OrdinalIgnoreCase))
                            {
                                formatSpec = spec.ToUpperInvariant();
                            }
                            else
                            {
                                int digits;
                                if (int.TryParse(spec, out digits) && digits > 0)
                                {
                                    formatSpec = "X" + digits;
                                }
                            }
                        }

                        long evaluated;
                        if (vars.ContainsKey(inner))
                        {
                            evaluated = vars[inner];
                        }
                        else
                        {
                            // 作为一个内联表达式直接计算
                            evaluated = Evaluate(inner, vars);
                        }

                        // 格式化输出为十六进制
                        if (formatSpec == "X2")
                        {
                            sb.Append(((byte)(evaluated & 0xFF)).ToString("X2"));
                        }
                        else if (formatSpec == "X4")
                        {
                            sb.Append(((ushort)(evaluated & 0xFFFF)).ToString("X4"));
                        }
                        else
                        {
                            sb.Append(evaluated.ToString(formatSpec));
                        }

                        i = close + 1;
                        continue;
                    }
                }

                sb.Append(template[i]);
                i++;
            }

            return sb.ToString();
        }

        private class Parser
        {
            private readonly string text;
            private readonly IDictionary<string, long> vars;
            private int pos;

            public Parser(string expression, IDictionary<string, long> variables)
            {
                this.text = expression ?? string.Empty;
                this.vars = variables;
                this.pos = 0;
            }

            public long Parse()
            {
                SkipWhitespace();
                long val = ParseOr();
                return val;
            }

            private void SkipWhitespace()
            {
                while (pos < text.Length && char.IsWhiteSpace(text[pos]))
                {
                    pos++;
                }
            }

            private bool Match(string s)
            {
                SkipWhitespace();
                if (pos + s.Length <= text.Length)
                {
                    if (string.Compare(text.Substring(pos, s.Length), s, StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        pos += s.Length;
                        return true;
                    }
                }
                return false;
            }

            // Precedence 1: Bitwise OR (|)
            private long ParseOr()
            {
                long left = ParseXor();
                while (true)
                {
                    if (Match("||"))
                    {
                        long right = ParseXor();
                        left = (left != 0 || right != 0) ? 1 : 0;
                    }
                    else if (Match("|"))
                    {
                        long right = ParseXor();
                        left = left | right;
                    }
                    else
                    {
                        break;
                    }
                }
                return left;
            }

            // Precedence 2: Bitwise XOR (^)
            private long ParseXor()
            {
                long left = ParseAnd();
                while (Match("^"))
                {
                    long right = ParseAnd();
                    left = left ^ right;
                }
                return left;
            }

            // Precedence 3: Bitwise AND (&)
            private long ParseAnd()
            {
                long left = ParseShift();
                while (true)
                {
                    if (Match("&&"))
                    {
                        long right = ParseShift();
                        left = (left != 0 && right != 0) ? 1 : 0;
                    }
                    else if (Match("&"))
                    {
                        long right = ParseShift();
                        left = left & right;
                    }
                    else
                    {
                        break;
                    }
                }
                return left;
            }

            // Precedence 4: Bitwise Shift (<<, >>)
            private long ParseShift()
            {
                long left = ParseAdditive();
                while (true)
                {
                    if (Match("<<"))
                    {
                        long right = ParseAdditive();
                        left = left << (int)(right & 0x3F);
                    }
                    else if (Match(">>"))
                    {
                        long right = ParseAdditive();
                        left = left >> (int)(right & 0x3F);
                    }
                    else
                    {
                        break;
                    }
                }
                return left;
            }

            // Precedence 5: Additive (+, -)
            private long ParseAdditive()
            {
                long left = ParseMultiplicative();
                while (true)
                {
                    if (Match("+"))
                    {
                        long right = ParseMultiplicative();
                        left = left + right;
                    }
                    else if (Match("-"))
                    {
                        long right = ParseMultiplicative();
                        left = left - right;
                    }
                    else
                    {
                        break;
                    }
                }
                return left;
            }

            // Precedence 6: Multiplicative (*, /, %)
            private long ParseMultiplicative()
            {
                long left = ParseUnary();
                while (true)
                {
                    if (Match("*"))
                    {
                        long right = ParseUnary();
                        left = left * right;
                    }
                    else if (Match("/"))
                    {
                        long right = ParseUnary();
                        left = (right == 0) ? 0 : (left / right);
                    }
                    else if (Match("%"))
                    {
                        long right = ParseUnary();
                        left = (right == 0) ? 0 : (left % right);
                    }
                    else
                    {
                        break;
                    }
                }
                return left;
            }

            // Precedence 7: Unary (+, -, ~, !)
            private long ParseUnary()
            {
                SkipWhitespace();
                if (Match("+"))
                {
                    return ParseUnary();
                }
                if (Match("-"))
                {
                    return -ParseUnary();
                }
                if (Match("~"))
                {
                    return ~ParseUnary();
                }
                if (Match("!"))
                {
                    return (ParseUnary() == 0) ? 1 : 0;
                }
                return ParsePrimary();
            }

            // Precedence 8: Primary (Parentheses, Numbers, Identifiers)
            private long ParsePrimary()
            {
                SkipWhitespace();
                if (pos >= text.Length) return 0;

                // Parentheses
                if (Match("("))
                {
                    long val = ParseOr();
                    Match(")");
                    return val;
                }

                // Hex literal: 0x...
                if (pos + 1 < text.Length && text[pos] == '0' && (text[pos + 1] == 'x' || text[pos + 1] == 'X'))
                {
                    pos += 2;
                    int start = pos;
                    while (pos < text.Length && IsHexDigit(text[pos]))
                    {
                        pos++;
                    }
                    string hexStr = text.Substring(start, pos - start);
                    long hexVal;
                    if (long.TryParse(hexStr, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out hexVal))
                    {
                        return hexVal;
                    }
                    return 0;
                }

                // Decimal integer literal
                if (char.IsDigit(text[pos]))
                {
                    int start = pos;
                    while (pos < text.Length && char.IsDigit(text[pos]))
                    {
                        pos++;
                    }
                    string decStr = text.Substring(start, pos - start);
                    long decVal;
                    if (long.TryParse(decStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out decVal))
                    {
                        return decVal;
                    }
                    return 0;
                }

                // Identifier / Variable: $var or varName
                if (text[pos] == '$' || char.IsLetter(text[pos]) || text[pos] == '_')
                {
                    int start = pos;
                    if (text[pos] == '$') pos++;
                    while (pos < text.Length && (char.IsLetterOrDigit(text[pos]) || text[pos] == '_'))
                    {
                        pos++;
                    }
                    string ident = text.Substring(start, pos - start);
                    string rawName = ident.StartsWith("$") ? ident.Substring(1) : ident;

                    if (vars != null)
                    {
                        if (vars.ContainsKey(rawName)) return vars[rawName];
                        if (vars.ContainsKey(ident)) return vars[ident];
                        if (vars.ContainsKey("$" + rawName)) return vars["$" + rawName];
                    }
                    return 0;
                }

                pos++;
                return 0;
            }

            private static bool IsHexDigit(char c)
            {
                return (c >= '0' && c <= '9') ||
                       (c >= 'a' && c <= 'f') ||
                       (c >= 'A' && c <= 'F');
            }
        }
    }
}
