using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace EnvSwitch
{
    public sealed class FieldChange
    {
        public string Key;
        public string Label;
        public string OldValue;
        public string NewValue;
        public bool Present;
        public bool Changed { get { return Present && OldValue != NewValue; } }
    }

    /// <summary>
    /// 一次「appsettings.json 改写」的完整计划。界面预览与实际写入用的是同一个 Build 结果，
    /// 所以「看到的就是写进去的」。只替换被引号包住的值，其余字节（缩进/换行/字段顺序）逐字不动。
    /// </summary>
    public sealed class WritePlan
    {
        public string AppKey;
        public string AppLabel;
        public string Path;
        public string Original;
        public string Updated;
        public string Error;
        public bool IsTarget;
        public string CurrentVersion;
        public string CurrentUrl;
        public List<FieldChange> Changes = new List<FieldChange>();

        public bool Ok { get { return Error == null; } }

        public static readonly Regex VersionRule = new Regex(@"^[A-Za-z0-9_.\-]{1,32}$");

        static string Pattern(string key)
        {
            return "(\"" + key + "\"\\s*:\\s*)\"[^\"]*\"";
        }

        public static bool HasField(string text, string key)
        {
            return Regex.IsMatch(text, "\"" + key + "\"\\s*:");
        }

        public static string ReadField(string text, string key)
        {
            var m = Regex.Match(text, "\"" + key + "\"\\s*:\\s*\"([^\"]*)\"");
            return m.Success ? m.Groups[1].Value : null;
        }

        public static string MaskFields(string text)
        {
            var t = Regex.Replace(text, Pattern("url"), "${1}\"@\"");
            t = Regex.Replace(t, Pattern("urlDataServer"), "${1}\"@\"");
            t = Regex.Replace(t, Pattern("ProjectName"), "${1}\"@\"");
            return t;
        }

        public static WritePlan Build(string appKey, AppSpec app, EnvSpec env, string version, bool isTarget)
        {
            var plan = new WritePlan { AppKey = appKey, IsTarget = isTarget };
            try
            {
                plan.AppLabel = app != null ? app.Label : appKey;
                if (app == null) { plan.Error = "配置.json 里没有这个软件"; return plan; }
                plan.Path = app.ConfigPath;
                if (string.IsNullOrEmpty(app.Dir))
                {
                    plan.Error = "还没找到「" + app.Label + "」的程序目录：点「重扫」自动找，或点「浏览」手动选";
                    return plan;
                }
                if (string.IsNullOrEmpty(plan.Path) || !File.Exists(plan.Path))
                {
                    plan.Error = "找不到 appsettings.json：" + (plan.Path ?? "(目录未配置)");
                    return plan;
                }
                plan.Original = File.ReadAllText(plan.Path, Encoding.UTF8);
                plan.Updated = plan.Original;

                if (!VersionRule.IsMatch(version ?? ""))
                {
                    plan.Error = "版本号不合法（只允许字母数字 . _ -，长度 1–32）：" + version;
                    return plan;
                }

                object parsed; string jsonError;
                if (!Json.TryParse(plan.Original, out parsed, out jsonError))
                {
                    plan.Error = "appsettings.json 解析失败，拒绝写入：" + jsonError;
                    return plan;
                }

                var fields = new List<string> { "url" };
                if (app.UrlDataServer) fields.Add("urlDataServer");
                fields.Add("ProjectName");

                var targets = new Dictionary<string, string>();
                targets["url"] = env != null ? env.Url : null;
                targets["urlDataServer"] = env != null ? env.UrlDataServer : null;
                targets["ProjectName"] = version;

                plan.CurrentUrl = ReadField(plan.Original, "url");
                plan.CurrentVersion = ReadField(plan.Original, "ProjectName");

                foreach (var key in fields)
                {
                    var hit = Regex.Matches(plan.Original, Pattern(key)).Count;
                    if (!HasField(plan.Original, key))
                    {
                        plan.Error = "该文件缺少 " + key + " 字段，未写入（不擅自加键）";
                        return plan;
                    }
                    if (hit != 1)
                    {
                        plan.Error = key + " 字段出现 " + hit + " 次，无法确定改哪一处，未写入";
                        return plan;
                    }
                    var old = ReadField(plan.Original, key);
                    var now = targets[key];
                    if (now == null) { plan.Error = "环境未配置 " + key + " 的值"; return plan; }
                    plan.Updated = Regex.Replace(plan.Updated, Pattern(key), "${1}\"" + now + "\"");
                    plan.Changes.Add(new FieldChange
                    {
                        Key = key,
                        Label = key == "ProjectName" ? "版本" : (key == "url" ? "接口地址" : "数据服务"),
                        OldValue = old,
                        NewValue = now,
                        Present = true
                    });
                }

                // 写后校验：改完必须仍是合法 JSON，且三个值确实是目标值
                try
                {
                    object root; string jerr;
                    if (!Json.TryParse(plan.Updated, out root, out jerr))
                        throw new InvalidDataException("改写后不是合法 JSON：" + jerr);
                    var gotUrl = Json.Str(root, "API_Access", "url");
                    if (gotUrl != targets["url"]) throw new InvalidDataException("url 校验不符");
                    var gotVer = Json.Str(root, "TERMINAL_Access", "ProjectName");
                    if (gotVer != targets["ProjectName"]) throw new InvalidDataException("ProjectName 校验不符");
                    if (app.UrlDataServer)
                    {
                        var gotDs = Json.Str(root, "API_Access", "urlDataServer");
                        if (gotDs != targets["urlDataServer"]) throw new InvalidDataException("urlDataServer 校验不符");
                    }
                }
                catch (Exception ex)
                {
                    plan.Updated = plan.Original;
                    plan.Error = "改写结果自校验失败，已放弃：" + ex.Message;
                    return plan;
                }

                // 结构不变性：把被改的值全部遮成 @ 之后，两份文本必须逐字相同
                if (MaskFields(plan.Original) != MaskFields(plan.Updated))
                {
                    plan.Updated = plan.Original;
                    plan.Error = "改写会动到值以外的内容，已放弃";
                    return plan;
                }
                return plan;
            }
            catch (Exception ex)
            {
                plan.Error = "读取失败：" + ex.Message;
                return plan;
            }
        }

        /// <summary>写盘 + 回读校验；任何不符都还原原文并抛错。</summary>
        public void Apply()
        {
            if (!Ok) throw new InvalidOperationException(Error);
            var bytes = new UTF8Encoding(false).GetBytes(Updated);
            try
            {
                File.WriteAllBytes(Path, bytes);
            }
            catch (Exception ex)
            {
                throw new IOException("写入失败（文件可能被占用，请先关闭软件）：" + ex.Message);
            }
            try
            {
                var back = File.ReadAllText(Path, Encoding.UTF8);
                object root; string jerr;
                if (!Json.TryParse(back, out root, out jerr))
                    throw new InvalidDataException("回读不是合法 JSON：" + jerr);
                foreach (var c in Changes)
                {
                    var group = c.Key == "ProjectName" ? "TERMINAL_Access" : "API_Access";
                    if (Json.Str(root, group, c.Key) != c.NewValue)
                        throw new InvalidDataException(c.Key + " 回读不一致");
                }
                if (MaskFields(back) != MaskFields(Updated)) throw new InvalidDataException("回读内容与预期不一致");
                var head = new byte[3];
                using (var fs = File.OpenRead(Path)) { fs.Read(head, 0, 3); }
                if (head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF) throw new InvalidDataException("文件被写成了带 BOM");
            }
            catch (Exception ex)
            {
                try { File.WriteAllText(Path, Original, new UTF8Encoding(false)); } catch { }
                throw new IOException("写入后校验失败，已还原原文：" + ex.Message);
            }
        }
    }
}
