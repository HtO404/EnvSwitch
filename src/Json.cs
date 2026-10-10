using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace EnvSwitch
{
    /// <summary>
    /// 自带的小 JSON 读写（net48 没有 System.Text.Json，也不想为它多背几个 dll）。
    /// 只覆盖本工具需要的场景：解析成 Dictionary/List 树、按路径取值、以及带缩进的写出。
    /// 解析时容忍 // 注释与结尾多余逗号（配置是给人手改的）。
    /// </summary>
    public static class Json
    {
        // ---------------- 解析 ----------------
        public static bool TryParse(string text, out object value, out string error)
        {
            value = null;
            error = null;
            if (text == null) { error = "内容为空"; return false; }
            try
            {
                int i = 0;
                var v = ParseValue(text, ref i);
                SkipWs(text, ref i);
                if (i < text.Length) throw new FormatException("第 " + i + " 个字符后还有多余内容");
                value = v;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static object Parse(string text)
        {
            object v;
            string err;
            if (!TryParse(text, out v, out err)) throw new FormatException(err);
            return v;
        }

        static void SkipWs(string s, ref int i)
        {
            while (i < s.Length)
            {
                char c = s[i];
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n') { i++; continue; }
                if (c == '/' && i + 1 < s.Length && s[i + 1] == '/')
                {
                    while (i < s.Length && s[i] != '\n') i++;
                    continue;
                }
                break;
            }
        }

        static object ParseValue(string s, ref int i)
        {
            SkipWs(s, ref i);
            if (i >= s.Length) throw new FormatException("内容意外结束");
            char c = s[i];
            switch (c)
            {
                case '{': return ParseObject(s, ref i);
                case '[': return ParseArray(s, ref i);
                case '"': return ParseString(s, ref i);
                case 't': Expect(s, ref i, "true"); return true;
                case 'f': Expect(s, ref i, "false"); return false;
                case 'n': Expect(s, ref i, "null"); return null;
                default: return ParseNumber(s, ref i);
            }
        }

        static void Expect(string s, ref int i, string word)
        {
            if (i + word.Length > s.Length || string.CompareOrdinal(s, i, word, 0, word.Length) != 0)
                throw new FormatException("期望 " + word + "（位置 " + i + "）");
            i += word.Length;
        }

        static Dictionary<string, object> ParseObject(string s, ref int i)
        {
            var map = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            i++;                                  // {
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return map; }
            while (true)
            {
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == '}') { i++; break; }   // 容忍结尾逗号
                if (i >= s.Length || s[i] != '"') throw new FormatException("对象的键必须是字符串（位置 " + i + "）");
                var key = ParseString(s, ref i);
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != ':') throw new FormatException("键后面缺冒号（位置 " + i + "）");
                i++;
                map[key] = ParseValue(s, ref i);
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == '}') { i++; break; }
                throw new FormatException("对象里缺 , 或 }（位置 " + i + "）");
            }
            return map;
        }

        static List<object> ParseArray(string s, ref int i)
        {
            var list = new List<object>();
            i++;                                  // [
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return list; }
            while (true)
            {
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ']') { i++; break; }   // 容忍结尾逗号
                list.Add(ParseValue(s, ref i));
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == ']') { i++; break; }
                throw new FormatException("数组里缺 , 或 ]（位置 " + i + "）");
            }
            return list;
        }

        static string ParseString(string s, ref int i)
        {
            var sb = new StringBuilder();
            i++;                                  // 开引号
            while (true)
            {
                if (i >= s.Length) throw new FormatException("字符串没有收尾引号");
                char c = s[i++];
                if (c == '"') break;
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) throw new FormatException("转义符后内容结束");
                char e = s[i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 > s.Length) throw new FormatException("\\u 后面不足 4 位");
                        sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16));
                        i += 4;
                        break;
                    default: throw new FormatException("不认识的转义 \\" + e);
                }
            }
            return sb.ToString();
        }

        static object ParseNumber(string s, ref int i)
        {
            int start = i;
            while (i < s.Length && ("+-0123456789.eE".IndexOf(s[i]) >= 0)) i++;
            var raw = s.Substring(start, i - start);
            double d;
            if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                throw new FormatException("不是合法数字：" + raw);
            return d;
        }

        // ---------------- 取值 ----------------
        public static object Node(object root, params string[] path)
        {
            var cur = root;
            foreach (var key in path)
            {
                var map = cur as Dictionary<string, object>;
                if (map == null) return null;
                object next;
                if (!map.TryGetValue(key, out next)) return null;
                cur = next;
            }
            return cur;
        }

        public static string Str(object root, params string[] path)
        {
            var v = Node(root, path);
            return v as string;
        }

        public static int? Int(object root, params string[] path)
        {
            var v = Node(root, path);
            if (v == null) return null;
            if (v is double) return (int)(double)v;
            int n;
            if (v is string && int.TryParse((string)v, out n)) return n;
            return null;
        }

        public static bool? Bool(object root, params string[] path)
        {
            var v = Node(root, path);
            if (v is bool) return (bool)v;
            return null;
        }

        public static List<object> Arr(object root, params string[] path)
        {
            return Node(root, path) as List<object>;
        }

        public static List<string> StrList(object root, params string[] path)
        {
            var list = new List<string>();
            var arr = Arr(root, path);
            if (arr != null) foreach (var item in arr) if (item is string) list.Add((string)item);
            return list;
        }
    }

    /// <summary>带缩进的 JSON 写出（自动管逗号，避免手写漏逗号/多逗号）。</summary>
    public sealed class JsonWriter
    {
        readonly StringBuilder _sb = new StringBuilder();
        readonly List<bool> _hasItems = new List<bool>();
        readonly List<bool> _isArray = new List<bool>();
        int _depth;
        bool _pendingKey;

        public JsonWriter BeginObject() { Separate(); _sb.Append('{'); Push(false); _depth++; return this; }
        public JsonWriter EndObject() { bool had = HadItems(); _depth--; if (had) NewLine(); _sb.Append('}'); Pop(); return this; }
        public JsonWriter BeginArray() { Separate(); _sb.Append('['); Push(true); _depth++; return this; }
        public JsonWriter EndArray() { bool had = HadItems(); _depth--; if (had) NewLine(); _sb.Append(']'); Pop(); return this; }

        bool HadItems() { return _hasItems.Count > 0 && _hasItems[_hasItems.Count - 1]; }

        public JsonWriter Key(string name) { Separate(); NewLine(); Escape(name); _sb.Append(": "); _pendingKey = true; return this; }
        public JsonWriter Value(string s) { Separate(); if (s == null) _sb.Append("null"); else Escape(s); _pendingKey = false; return this; }
        public JsonWriter Value(int n) { Separate(); _sb.Append(n.ToString(CultureInfo.InvariantCulture)); return this; }
        public JsonWriter Value(bool b) { Separate(); _sb.Append(b ? "true" : "false"); return this; }
        public JsonWriter Raw(string json) { Separate(); _sb.Append(json); return this; }

        public override string ToString() { return _sb.ToString(); }

        void Push(bool array)
        {
            _hasItems.Add(false);
            _isArray.Add(array);
        }

        void Pop()
        {
            if (_hasItems.Count > 0) { _hasItems.RemoveAt(_hasItems.Count - 1); _isArray.RemoveAt(_isArray.Count - 1); }
            if (_hasItems.Count > 0) _hasItems[_hasItems.Count - 1] = true;
            _pendingKey = false;
        }

        void Separate()
        {
            if (_pendingKey) return;                       // 刚写过键，直接写值
            if (_hasItems.Count > 0 && _hasItems[_hasItems.Count - 1])
            {
                _sb.Append(',');
                NewLine();
            }
            else if (_hasItems.Count > 0 && _sb.Length > 0 && (_sb[_sb.Length - 1] == '{' || _sb[_sb.Length - 1] == '['))
            {
                NewLine();                                  // 容器的第一个成员换行
            }
        }

        void NewLine()
        {
            if (_hasItems.Count == 0) return;
            _sb.Append("\r\n");
            _sb.Append(' ', _depth * 2);
        }

        void Escape(string s)
        {
            _sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': _sb.Append("\\\""); break;
                    case '\\': _sb.Append("\\\\"); break;
                    case '\b': _sb.Append("\\b"); break;
                    case '\f': _sb.Append("\\f"); break;
                    case '\n': _sb.Append("\\n"); break;
                    case '\r': _sb.Append("\\r"); break;
                    case '\t': _sb.Append("\\t"); break;
                    default:
                        if (c < ' ') _sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else _sb.Append(c);
                        break;
                }
            }
            _sb.Append('"');
        }
    }
}
