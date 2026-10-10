using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace EnvSwitch
{
    /// <summary>一个环境（线上 / 本地）。</summary>
    public sealed class EnvSpec
    {
        public string Label { get; set; }
        public string Host { get; set; }
        public int Port { get; set; } = 443;
        public string Url { get; set; }
        public string UrlDataServer { get; set; }
    }

    /// <summary>一个软件（DemoClient / DemoConsole）。Dir 为空表示"还没找到，等自动识别"。</summary>
    public sealed class AppSpec
    {
        public string Label { get; set; }
        public string Dir { get; set; }
        public string Exe { get; set; }
        /// <summary>该软件的 appsettings.json 里是否有 urlDataServer 字段。</summary>
        public bool UrlDataServer { get; set; }
        public List<string> AltDirs { get; set; } = new List<string>();

        public string ExePath { get { return string.IsNullOrEmpty(Dir) ? null : Path.Combine(Dir, Exe ?? ""); } }
        public string ConfigPath { get { return string.IsNullOrEmpty(Dir) ? null : Path.Combine(Dir, "appsettings.json"); } }
        public string ExeName { get { return Path.GetFileNameWithoutExtension(Exe ?? ""); } }
    }

    public sealed class LastChoice
    {
        public string App { get; set; }
        public string Env { get; set; }
        public string Version { get; set; }
        public string StudioDir { get; set; }
    }

    /// <summary>配置.json 的模型：唯一需要手工编辑的文件。</summary>
    public sealed class AppConfig
    {
        public Dictionary<string, EnvSpec> Environments { get; set; } = new Dictionary<string, EnvSpec>();
        public List<string> Versions { get; set; } = new List<string>();
        public Dictionary<string, AppSpec> Apps { get; set; } = new Dictionary<string, AppSpec>();
        public bool WriteBoth { get; set; } = true;
        public string Theme { get; set; } = "auto";
        public string Density { get; set; } = "comfortable";
        public LastChoice Last { get; set; } = new LastChoice();

        public string LoadWarning { get; set; }

        public static readonly string[] AppKeys = { "terminal", "studio" };

        public EnvSpec Env(string key)
        {
            EnvSpec e;
            return Environments != null && Environments.TryGetValue(key, out e) ? e : null;
        }

        public AppSpec App(string key)
        {
            AppSpec a;
            return Apps != null && Apps.TryGetValue(key, out a) ? a : null;
        }

        public static AppConfig Default()
        {
            var c = new AppConfig();
            c.Environments["online"] = new EnvSpec { Label = "线上", Host = "10.0.0.100", Port = 443, Url = "https://10.0.0.100/api/", UrlDataServer = "https://10.0.0.100/api/" };
            c.Environments["local"] = new EnvSpec { Label = "本地", Host = "127.0.0.1", Port = 443, Url = "https://127.0.0.1/api/", UrlDataServer = "https://127.0.0.1/api/" };
            c.Versions.AddRange(new[] { "proj-1", "proj-2", "proj-3", "proj-4" });
            c.Apps["terminal"] = new AppSpec { Label = "DemoClient", Dir = "", Exe = "DemoClient.exe", UrlDataServer = true };
            c.Apps["studio"] = new AppSpec { Label = "DemoConsole", Dir = "", Exe = "DemoConsole.exe", UrlDataServer = false };
            c.Last = new LastChoice { App = "studio", Env = "local", Version = "", StudioDir = "" };
            return c;
        }

        public static AppConfig Load(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    var fresh = Default();
                    fresh.LoadWarning = "没有找到 配置.json，已按内置默认生成";
                    fresh.Save(path);
                    return fresh;
                }
                var text = File.ReadAllText(path, Encoding.UTF8);
                object root;
                string err;
                if (!Json.TryParse(text, out root, out err))
                    throw new InvalidDataException("JSON 解析失败：" + err);
                var cfg = FromTree(root);
                if (cfg.Environments.Count == 0) cfg.Environments = Default().Environments;
                if (cfg.Apps.Count == 0) cfg.Apps = Default().Apps;
                return cfg;
            }
            catch (Exception ex)
            {
                var fb = Default();
                fb.LoadWarning = "配置.json 读取失败（" + ex.Message + "），本次用内置默认值";
                return fb;
            }
        }

        static AppConfig FromTree(object root)
        {
            var c = new AppConfig();
            foreach (var key in new[] { "online", "local" })
            {
                var node = Json.Node(root, "environments", key);
                if (node == null) continue;
                c.Environments[key] = new EnvSpec
                {
                    Label = Json.Str(root, "environments", key, "label") ?? (key == "online" ? "线上" : "本地"),
                    Host = Json.Str(root, "environments", key, "host"),
                    Port = Json.Int(root, "environments", key, "port") ?? 443,
                    Url = Json.Str(root, "environments", key, "url"),
                    UrlDataServer = Json.Str(root, "environments", key, "urlDataServer")
                };
            }
            c.Versions = Json.StrList(root, "versions");
            foreach (var key in AppKeys)
            {
                if (Json.Node(root, "apps", key) == null) continue;
                c.Apps[key] = new AppSpec
                {
                    Label = Json.Str(root, "apps", key, "label") ?? key,
                    Dir = Json.Str(root, "apps", key, "dir") ?? "",
                    Exe = Json.Str(root, "apps", key, "exe") ?? (key == "terminal" ? "DemoClient.exe" : "DemoConsole.exe"),
                    UrlDataServer = Json.Bool(root, "apps", key, "urlDataServer") ?? (key == "terminal"),
                    AltDirs = Json.StrList(root, "apps", key, "altDirs")
                };
            }
            var wb = Json.Bool(root, "writeBoth");
            if (wb.HasValue) c.WriteBoth = wb.Value;
            var theme = Json.Str(root, "theme");
            if (!string.IsNullOrEmpty(theme)) c.Theme = theme;
            var density = Json.Str(root, "density");
            if (!string.IsNullOrEmpty(density)) c.Density = density;
            if (Json.Node(root, "last") != null)
            {
                c.Last = new LastChoice
                {
                    App = Json.Str(root, "last", "app"),
                    Env = Json.Str(root, "last", "env"),
                    Version = Json.Str(root, "last", "version"),
                    StudioDir = Json.Str(root, "last", "studioDir")
                };
            }
            if (c.Last == null) c.Last = new LastChoice();
            return c;
        }

        public void Save(string path)
        {
            try
            {
                File.WriteAllText(path, ToJson(), new UTF8Encoding(false));
                LoadWarning = null;
            }
            catch (Exception ex)
            {
                LoadWarning = "配置.json 保存失败：" + ex.Message;
            }
        }

        public string ToJson()
        {
            var w = new JsonWriter();
            w.BeginObject();

            w.Key("environments");
            w.BeginObject();
            foreach (var kv in Environments)
            {
                var e = kv.Value ?? new EnvSpec();
                w.Key(kv.Key);
                w.BeginObject();
                w.Key("label"); w.Value(e.Label);
                w.Key("host"); w.Value(e.Host);
                w.Key("port"); w.Value(e.Port);
                w.Key("url"); w.Value(e.Url);
                w.Key("urlDataServer"); w.Value(e.UrlDataServer);
                w.EndObject();
            }
            w.EndObject();

            w.Key("versions");
            w.BeginArray();
            foreach (var v in Versions) w.Value(v);
            w.EndArray();

            w.Key("apps");
            w.BeginObject();
            foreach (var kv in Apps)
            {
                var a = kv.Value ?? new AppSpec();
                w.Key(kv.Key);
                w.BeginObject();
                w.Key("label"); w.Value(a.Label);
                w.Key("dir"); w.Value(a.Dir ?? "");
                w.Key("exe"); w.Value(a.Exe);
                w.Key("urlDataServer"); w.Value(a.UrlDataServer);
                w.Key("altDirs");
                w.BeginArray();
                if (a.AltDirs != null) foreach (var d in a.AltDirs) w.Value(d);
                w.EndArray();
                w.EndObject();
            }
            w.EndObject();

            w.Key("writeBoth"); w.Value(WriteBoth);
            w.Key("theme"); w.Value(Theme);
            w.Key("density"); w.Value(Density);

            w.Key("last");
            w.BeginObject();
            w.Key("app"); w.Value(Last != null ? Last.App : null);
            w.Key("env"); w.Value(Last != null ? Last.Env : null);
            w.Key("version"); w.Value(Last != null ? Last.Version : null);
            w.Key("studioDir"); w.Value(Last != null ? Last.StudioDir : null);
            w.EndObject();

            w.EndObject();
            return w.ToString();
        }
    }
}
