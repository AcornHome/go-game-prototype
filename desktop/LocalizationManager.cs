using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;

namespace GoGame
{
    /// <summary>
    /// Multi-language manager (v1.6.0).
    ///
    /// Resource dictionaries live in /Localization/Strings.{lang}.xaml (compiled into the assembly).
    /// Switching language swaps the matching entry in App.Current.Resources.MergedDictionaries.
    /// Text bound in XAML via {DynamicResource Key} refreshes automatically; C# code uses
    /// LocalizationManager.Get(key).
    ///
    /// Languages: en-US (default), zh-CN, ja-JP, ko-KR, es-ES.
    /// The English dictionary is always loaded as the fallback base, so any key missing from a
    /// translated dictionary falls back to English instead of showing the raw key.
    ///
    /// The user's choice persists to %LOCALAPPDATA%\ChinaGo\lang.txt (same folder as LocalProfile).
    /// Default startup language is English.
    /// </summary>
    public static class LocalizationManager
    {
        public static event EventHandler? LanguageChanged;

        public const string DefaultLang = "en-US";
        private static readonly HashSet<string> _supported = new() { "en-US", "zh-CN", "ja-JP", "ko-KR", "es-ES" };

        private static string _lang = DefaultLang;
        private const string FileName = "lang.txt";

        /// <summary>Code of the currently active language.</summary>
        public static string Current => _lang;

        /// <summary>All supported languages with their native display names (language-neutral).</summary>
        public static IReadOnlyList<(string Code, string Display)> SupportedLanguages { get; } =
            new List<(string, string)>
            {
                ("en-US", "English"),
                ("zh-CN", "中文"),
                ("ja-JP", "日本語"),
                ("ko-KR", "한국어"),
                ("es-ES", "Español"),
            };

        public static void Init()
        {
            try
            {
                var p = Path.Combine(LangDir, FileName);
                if (File.Exists(p))
                {
                    var v = File.ReadAllText(p).Trim();
                    if (_supported.Contains(v)) _lang = v;
                }
            }
            catch { }

            // Default startup language is English. Persist it on first run.
            if (!_supported.Contains(_lang)) _lang = DefaultLang;
            try
            {
                Directory.CreateDirectory(LangDir);
                File.WriteAllText(Path.Combine(LangDir, FileName), _lang);
            }
            catch { }

            Apply();
        }

        public static void SetLanguage(string lang)
        {
            if (!_supported.Contains(lang)) return;
            _lang = lang;
            try
            {
                Directory.CreateDirectory(LangDir);
                File.WriteAllText(Path.Combine(LangDir, FileName), _lang);
            }
            catch { }

            Apply();
            LanguageChanged?.Invoke(null, EventArgs.Empty);
        }

        /// <summary>Look up the text for a key in the active language, falling back to English, then to the key itself.</summary>
        public static string Get(string key)
        {
            try
            {
                if (Application.Current.Resources[key] is string s && !string.IsNullOrEmpty(s)) return s;
            }
            catch { }
            return key;
        }

        private static string LangDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ChinaGo");

        private static void Apply()
        {
            var dicts = Application.Current.Resources.MergedDictionaries;
            for (int i = dicts.Count - 1; i >= 0; i--)
            {
                var src = dicts[i].Source?.OriginalString;
                if (src != null && src.Contains("Strings."))
                    dicts.RemoveAt(i);
            }

            // English is always loaded first as the fallback base.
            AddDict(dicts, DefaultLang);
            // Then overlay the active language (skip if it is already English).
            if (_lang != DefaultLang)
                AddDict(dicts, _lang);
        }

        private static void AddDict(System.Collections.ObjectModel.Collection<ResourceDictionary> dicts, string lang)
        {
            try
            {
                var rd = new ResourceDictionary
                {
                    Source = new Uri($"/Localization/Strings.{lang}.xaml", UriKind.Relative)
                };
                dicts.Add(rd);
            }
            catch { }
        }
    }
}
