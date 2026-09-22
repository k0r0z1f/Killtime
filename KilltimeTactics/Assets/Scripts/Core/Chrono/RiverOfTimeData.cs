using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace Killtime.Core.Chrono
{
    public static class HybrisCalendar
    {
        public const int DaysPerMonth = 30;
        public const int MonthsPerYear = 10;
        public const int DaysPerYear = 300;

        public static float DateToFloat(int year, int month, int day)
        {
            float fraction = ((month - 1) * DaysPerMonth + (day - 1)) / (float)DaysPerYear;
            return year + fraction;
        }

        public static void FloatToDate(float val, out int year, out int month, out int day)
        {
            float clean = Mathf.Round(val * DaysPerYear) / (float)DaysPerYear;
            year = Mathf.FloorToInt(clean);
            float rem = clean - year;
            int totalDays = Mathf.RoundToInt(rem * DaysPerYear);
            month = (totalDays / DaysPerMonth) + 1;
            day = (totalDays % DaysPerMonth) + 1;
        }

        public static string FormatDate(int year, int month, int day)
        {
            return $"{year:D4}-{month:D2}-{day:D2}";
        }

        public static bool TryParseDate(string str, out int year, out int month, out int day)
        {
            year = 1772;
            month = 1;
            day = 1;
            if (string.IsNullOrEmpty(str)) return false;

            string[] parts = str.Split('-');
            if (parts.Length != 3) return false;

            bool yOk = int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out year);
            bool mOk = int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out month);
            bool dOk = int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out day);

            return yOk && mOk && dOk;
        }
    }

    [Serializable]
    public class RiverTimelineData
    {
        public string Name;
        public float Y;
        public Color Color = Color.white;
        public float StartVal;
        public string Parent;
    }

    [Serializable]
    public class RiverEventData
    {
        public float FloatVal;
        public string DateStr;
        public string Name;
        public string LineName;
        public string Type;
        public string TargetBranch;
        public Color Color = Color.white;
        public string ChapterPart;
    }

    [Serializable]
    public class RiverDayTitleData
    {
        public string DateKey;
        public string Title;
        public string LineName;
        public float FloatVal;
    }

    public class RiverProjectData
    {
        public Dictionary<string, RiverTimelineData> Timelines = new();
        public List<RiverEventData> Events = new();
        public Dictionary<string, RiverDayTitleData> DayTitles = new();
        public int ColorIndex;

        public static RiverProjectData ParseJson(string json)
        {
            var project = new RiverProjectData();
            if (string.IsNullOrEmpty(json)) return project;

            var root = MiniJsonParser.Parse(json) as Dictionary<string, object>;
            if (root == null) return project;

            if (root.TryGetValue("timelines", out var timelinesObj) && timelinesObj is Dictionary<string, object> timelinesDict)
            {
                foreach (var kvp in timelinesDict)
                {
                    if (kvp.Value is Dictionary<string, object> tDict)
                    {
                        var t = new RiverTimelineData
                        {
                            Name = kvp.Key,
                            Y = ParseFloat(tDict, "y", 0f),
                            StartVal = ParseFloat(tDict, "start_val", 0f),
                            Parent = tDict.TryGetValue("parent", out var pVal) && pVal != null ? pVal.ToString() : null,
                            Color = ParseColor(tDict, "color", Color.yellow)
                        };
                        project.Timelines[kvp.Key] = t;
                    }
                }
            }

            if (root.TryGetValue("day_titles", out var daysObj) && daysObj is Dictionary<string, object> daysDict)
            {
                foreach (var kvp in daysDict)
                {
                    if (kvp.Value is Dictionary<string, object> dDict)
                    {
                        var dt = new RiverDayTitleData
                        {
                            DateKey = kvp.Key,
                            Title = dDict.TryGetValue("title", out var titleVal) ? titleVal?.ToString() : "",
                            LineName = dDict.TryGetValue("line_name", out var lineVal) ? lineVal?.ToString() : "",
                            FloatVal = ParseFloat(dDict, "float_val", 0f)
                        };
                        project.DayTitles[kvp.Key] = dt;
                    }
                }
            }

            if (root.TryGetValue("events", out var eventsObj) && eventsObj is List<object> eventsList)
            {
                foreach (var evItem in eventsList)
                {
                    if (evItem is Dictionary<string, object> eDict)
                    {
                        var ev = new RiverEventData
                        {
                            FloatVal = ParseFloat(eDict, "float_val", 0f),
                            DateStr = eDict.TryGetValue("date_str", out var dVal) ? dVal?.ToString() : "",
                            Name = eDict.TryGetValue("name", out var nVal) ? nVal?.ToString() : "",
                            LineName = eDict.TryGetValue("line_name", out var lVal) ? lVal?.ToString() : "",
                            Type = eDict.TryGetValue("type", out var tVal) ? tVal?.ToString() : "fixed",
                            TargetBranch = eDict.TryGetValue("target_branch", out var bVal) ? bVal?.ToString() : null,
                            ChapterPart = eDict.TryGetValue("chapter_part", out var cVal) ? cVal?.ToString() : null,
                            Color = ParseColor(eDict, "color", Color.white)
                        };
                        project.Events.Add(ev);
                    }
                }
            }

            project.ColorIndex = (int)ParseFloat(root, "color_index", 0f);
            return project;
        }

        public string ToJson()
        {
            var sb = new StringBuilder();
            sb.AppendLine("{");

            sb.AppendLine("    \"timelines\": {");
            int tIndex = 0;
            foreach (var kvp in Timelines)
            {
                var t = kvp.Value;
                sb.AppendLine($"        \"{EscapeString(kvp.Key)}\": {{");
                sb.AppendLine($"            \"y\": {t.Y.ToString(CultureInfo.InvariantCulture)},");
                sb.AppendLine($"            \"color\": \"#{ColorUtility.ToHtmlStringRGB(t.Color).ToLowerInvariant()}\",");
                sb.AppendLine($"            \"start_val\": {t.StartVal.ToString(CultureInfo.InvariantCulture)},");
                string parentStr = string.IsNullOrEmpty(t.Parent) ? "null" : $"\"{EscapeString(t.Parent)}\"";
                sb.AppendLine($"            \"parent\": {parentStr}");
                sb.Append("        }");
                sb.AppendLine(++tIndex < Timelines.Count ? "," : "");
            }
            sb.AppendLine("    },");

            sb.AppendLine("    \"day_titles\": {");
            int dIndex = 0;
            foreach (var kvp in DayTitles)
            {
                var dt = kvp.Value;
                sb.AppendLine($"        \"{EscapeString(kvp.Key)}\": {{");
                sb.AppendLine($"            \"title\": \"{EscapeString(dt.Title)}\",");
                sb.AppendLine($"            \"line_name\": \"{EscapeString(dt.LineName)}\",");
                sb.AppendLine($"            \"float_val\": {dt.FloatVal.ToString(CultureInfo.InvariantCulture)}");
                sb.Append("        }");
                sb.AppendLine(++dIndex < DayTitles.Count ? "," : "");
            }
            sb.AppendLine("    },");

            sb.AppendLine("    \"events\": [");
            for (int i = 0; i < Events.Count; i++)
            {
                var ev = Events[i];
                sb.AppendLine("        {");
                sb.AppendLine($"            \"float_val\": {ev.FloatVal.ToString(CultureInfo.InvariantCulture)},");
                sb.AppendLine($"            \"date_str\": \"{EscapeString(ev.DateStr)}\",");
                sb.AppendLine($"            \"name\": \"{EscapeString(ev.Name)}\",");
                sb.AppendLine($"            \"line_name\": \"{EscapeString(ev.LineName)}\",");
                sb.AppendLine($"            \"type\": \"{EscapeString(ev.Type)}\",");
                if (!string.IsNullOrEmpty(ev.TargetBranch))
                {
                    sb.AppendLine($"            \"target_branch\": \"{EscapeString(ev.TargetBranch)}\",");
                }
                if (!string.IsNullOrEmpty(ev.ChapterPart))
                {
                    sb.AppendLine($"            \"chapter_part\": \"{EscapeString(ev.ChapterPart)}\",");
                }
                sb.AppendLine($"            \"color\": \"#{ColorUtility.ToHtmlStringRGB(ev.Color).ToLowerInvariant()}\"");
                sb.Append("        }");
                sb.AppendLine(i < Events.Count - 1 ? "," : "");
            }
            sb.AppendLine("    ],");

            sb.AppendLine($"    \"color_index\": {ColorIndex}");
            sb.Append("}");
            return sb.ToString();
        }

        private static string EscapeString(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");
        }

        private static float ParseFloat(Dictionary<string, object> dict, string key, float defaultVal)
        {
            if (dict.TryGetValue(key, out var val) && val != null)
            {
                if (float.TryParse(val.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out float result))
                {
                    return result;
                }
            }
            return defaultVal;
        }

        private static Color ParseColor(Dictionary<string, object> dict, string key, Color defaultColor)
        {
            if (dict.TryGetValue(key, out var val) && val != null)
            {
                string hex = val.ToString().Trim();
                if (!hex.StartsWith("#")) hex = "#" + hex;
                if (ColorUtility.TryParseHtmlString(hex, out Color parsed))
                {
                    return parsed;
                }
            }
            return defaultColor;
        }
    }

    internal static class MiniJsonParser
    {
        public static object Parse(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            int index = 0;
            return ParseValue(json, ref index);
        }

        private static object ParseValue(string json, ref int index)
        {
            SkipWhitespace(json, ref index);
            if (index >= json.Length) return null;

            char c = json[index];
            if (c == '{') return ParseObject(json, ref index);
            if (c == '[') return ParseArray(json, ref index);
            if (c == '"') return ParseString(json, ref index);
            if (c == 't' || c == 'f') return ParseBool(json, ref index);
            if (c == 'n') return ParseNull(json, ref index);
            return ParseNumber(json, ref index);
        }

        private static Dictionary<string, object> ParseObject(string json, ref int index)
        {
            var dict = new Dictionary<string, object>();
            index++;

            while (index < json.Length)
            {
                SkipWhitespace(json, ref index);
                if (index >= json.Length) break;
                if (json[index] == '}') { index++; return dict; }

                string key = ParseString(json, ref index);
                SkipWhitespace(json, ref index);
                if (index < json.Length && json[index] == ':') index++;

                object val = ParseValue(json, ref index);
                dict[key] = val;

                SkipWhitespace(json, ref index);
                if (index < json.Length && json[index] == ',') index++;
            }
            return dict;
        }

        private static List<object> ParseArray(string json, ref int index)
        {
            var list = new List<object>();
            index++;

            while (index < json.Length)
            {
                SkipWhitespace(json, ref index);
                if (index >= json.Length) break;
                if (json[index] == ']') { index++; return list; }

                object val = ParseValue(json, ref index);
                list.Add(val);

                SkipWhitespace(json, ref index);
                if (index < json.Length && json[index] == ',') index++;
            }
            return list;
        }

        private static string ParseString(string json, ref int index)
        {
            var sb = new StringBuilder();
            index++;
            while (index < json.Length)
            {
                char c = json[index++];
                if (c == '"') return sb.ToString();
                if (c == '\\' && index < json.Length)
                {
                    char esc = json[index++];
                    if (esc == '"') sb.Append('"');
                    else if (esc == '\\') sb.Append('\\');
                    else if (esc == 'n') sb.Append('\n');
                    else if (esc == 'r') sb.Append('\r');
                    else if (esc == 't') sb.Append('\t');
                    else sb.Append(esc);
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        private static object ParseNumber(string json, ref int index)
        {
            int start = index;
            while (index < json.Length && "0123456789+-.eE".IndexOf(json[index]) >= 0)
            {
                index++;
            }
            string numStr = json.Substring(start, index - start);
            if (double.TryParse(numStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double dVal))
            {
                return dVal;
            }
            return 0.0;
        }

        private static bool ParseBool(string json, ref int index)
        {
            if (json.Substring(index).StartsWith("true")) { index += 4; return true; }
            if (json.Substring(index).StartsWith("false")) { index += 5; return false; }
            return false;
        }

        private static object ParseNull(string json, ref int index)
        {
            if (json.Substring(index).StartsWith("null")) { index += 4; }
            return null;
        }

        private static void SkipWhitespace(string json, ref int index)
        {
            while (index < json.Length && char.IsWhiteSpace(json[index])) index++;
        }
    }
}