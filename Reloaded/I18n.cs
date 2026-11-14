// I18n.cs - UI-free internationalization for VAGEDCSuite-Reloaded.
//
// Deliberately dependency-free: .NET Framework 4.0 / C# 4 only, and only
// mscorlib + System.dll (Microsoft.Win32.Registry).  It does not reference
// Krypton, OxyPlot, DevExpress or any third-party assembly, so it can be
// compiled into the existing net40 build as-is.
//
// Locale files are flat JSON objects:  { "key": "value", ... }
// They live in <application base directory>\locales\<code>.json (see
// LocaleDirectory).  No JSON library is available in the net40 base build,
// so a small strict parser is embedded below; see I18N.md for its limits.
//
// The selected language is persisted in the registry, matching the theme
// store already used by the app:
//     HKCU\Software\VAGEDCSuiteReloaded   value "Language" (REG_SZ, e.g. "de")
//
// Usage:
//     I18n.Initialize();                        // optional; T() loads lazily
//     label.Text = I18n.T("window_title");
//     label.Text = I18n.T("checksum_report", ok, total);
//     I18n.SetLanguage("de");                   // applies + persists
//
// Threading: lookups and loads are safe from any thread.  The dictionary is
// replaced wholesale, so a reader never sees a half-loaded table.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace Reloaded
{
    /// <summary>
    /// Static translation helper: loads flat <c>locales\&lt;code&gt;.json</c>
    /// files and resolves keys to strings.  Missing keys fall back to the
    /// English table and then to the key itself, so a missing translation can
    /// never blank out the UI.
    /// </summary>
    public static class I18n
    {
        /// <summary>Language used when nothing is persisted or a file is missing.</summary>
        public const string DefaultLanguage = "en";

        // Same registry key as ThemeStore (Theming.cs) so the app keeps one
        // per-user settings location.
        private const string RegistryKeyPath = @"Software\VAGEDCSuiteReloaded";
        private const string RegistryValueName = "Language";

        private const string LocaleFolderName = "locales";
        private const string LocaleExtension = ".json";

        private static readonly object Sync = new object();
        private static readonly object LoadLock = new object();

        private static string _current = DefaultLanguage;
        private static Dictionary<string, string> _strings =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private static Dictionary<string, string> _defaultStrings;
        private static bool _loaded;
        private static string _lastError;
        private static string _localeDirectory;

        /// <summary>Raised after the active language was changed successfully.</summary>
        public static event EventHandler LanguageChanged;

        /// <summary>Language code currently in use (for example "en" or "de").</summary>
        public static string CurrentLanguage
        {
            get { lock (Sync) { return _current; } }
        }

        /// <summary>
        /// Message describing the last load/persist failure, or null.  A
        /// missing translation key is not an error (it falls back).
        /// </summary>
        public static string LastError
        {
            get { lock (Sync) { return _lastError; } }
        }

        /// <summary>
        /// Directory the locale files are read from:
        /// <c>&lt;base directory&gt;\locales</c>, with the executing
        /// assembly's directory as a fallback probe.
        /// </summary>
        public static string LocaleDirectory
        {
            get { lock (Sync) { return ResolveLocaleDirectory(); } }
        }

        // ---------------------------------------------------------------- API

        /// <summary>
        /// Loads the persisted language (or English) once.  Optional: every
        /// public entry point loads lazily on first use.  Call it explicitly
        /// only if you want a deterministic failure code before building UI.
        /// </summary>
        public static bool Initialize()
        {
            string saved = GetSavedLanguage();
            if (Apply(saved, false)) return true;

            if (!string.Equals(saved, DefaultLanguage, StringComparison.OrdinalIgnoreCase) &&
                Apply(DefaultLanguage, false))
            {
                return true;
            }

            lock (Sync) { _loaded = true; }
            return false;
        }

        /// <summary>
        /// Loads <paramref name="code"/> from <c>locales\&lt;code&gt;.json</c>
        /// and makes it current.  Does not persist the choice.  Returns false
        /// (leaving the previous table active) when the code is invalid or the
        /// file is missing/malformed; see <see cref="LastError"/>.
        /// </summary>
        public static bool LoadLanguage(string code)
        {
            return Apply(code, true);
        }

        /// <summary>
        /// Loads <paramref name="code"/> and persists it to
        /// HKCU\Software\VAGEDCSuiteReloaded "Language".  Returns false only if
        /// the language could not be applied; a registry write failure is
        /// reported through <see cref="LastError"/> but does not undo the
        /// switch.
        /// </summary>
        public static bool SetLanguage(string code)
        {
            if (!Apply(code, true)) return false;

            string applied = CurrentLanguage;
            string ignored;
            if (SaveLanguage(applied, out ignored)) return true;

            lock (Sync)
            {
                // Load succeeded, only persistence failed: keep the switch.
                if (_lastError == null) _lastError = ignored;
            }
            return true;
        }

        /// <summary>
        /// Reads HKCU\Software\VAGEDCSuiteReloaded "Language" without changing
        /// state.  Returns <see cref="DefaultLanguage"/> when the value is
        /// absent, unreadable or not a plausible language code.
        /// </summary>
        public static string GetSavedLanguage()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath, false))
                {
                    if (key != null)
                    {
                        string value = key.GetValue(RegistryValueName) as string;
                        string normalized;
                        if (TryNormalizeCode(value, out normalized)) return normalized;
                    }
                }
            }
            catch
            {
                // Registry unavailable (locked-down profile): use the default.
            }
            return DefaultLanguage;
        }

        /// <summary>
        /// Language codes that have a locale file on disk, sorted ordinally.
        /// Used by a future language picker; the English default may be
        /// absent from the list if its file is missing.
        /// </summary>
        public static string[] GetAvailableLanguages()
        {
            List<string> codes = new List<string>();
            try
            {
                string directory = LocaleDirectory;
                if (Directory.Exists(directory))
                {
                    string[] files = Directory.GetFiles(directory, "*" + LocaleExtension);
                    for (int i = 0; i < files.Length; i++)
                    {
                        string name = Path.GetFileNameWithoutExtension(files[i]);
                        string normalized;
                        if (TryNormalizeCode(name, out normalized) && !codes.Contains(normalized))
                            codes.Add(normalized);
                    }
                }
            }
            catch
            {
                // Listing is cosmetic; an unreadable directory yields an empty list.
            }

            codes.Sort(StringComparer.Ordinal);
            return codes.ToArray();
        }

        /// <summary>
        /// Translates <paramref name="key"/> in the current language.
        /// Fallback order: current language, English, the key itself.
        /// A null key yields an empty string.
        /// </summary>
        public static string T(string key)
        {
            if (key == null) return string.Empty;
            EnsureLoaded();

            string value;
            lock (Sync)
            {
                if (_strings.TryGetValue(key, out value) && value != null) return value;
                if (_defaultStrings != null &&
                    _defaultStrings.TryGetValue(key, out value) && value != null)
                {
                    return value;
                }
            }
            return key;
        }

        /// <summary>
        /// Translates <paramref name="key"/> and applies <c>string.Format</c>
        /// with the current culture (placeholders such as <c>{0}</c> /
        /// <c>{1:X}</c>).  A malformed template returns its raw text instead of
        /// throwing.
        /// </summary>
        public static string T(string key, params object[] args)
        {
            string template = T(key);
            if (args == null || args.Length == 0) return template;

            try
            {
                return string.Format(CultureInfo.CurrentCulture, template, args);
            }
            catch (FormatException)
            {
                // Translators occasionally break a placeholder; keep the UI
                // readable and let the caller notice the raw braces.
                return template;
            }
        }

        // ------------------------------------------------------------ internals

        private static bool Apply(string code, bool raiseEvent)
        {
            string normalized;
            if (!TryNormalizeCode(code, out normalized))
            {
                SetError("invalid language code: " + (code == null ? "(null)" : "\"" + code + "\""));
                return false;
            }

            string error;
            Dictionary<string, string> strings = ReadLocale(normalized, out error);
            if (strings == null)
            {
                SetError(error);
                return false;
            }

            // Keep the English table as a per-key fallback for incomplete
            // translations.  Loaded once and reused.
            Dictionary<string, string> defaults = null;
            if (!string.Equals(normalized, DefaultLanguage, StringComparison.Ordinal))
            {
                lock (Sync) { defaults = _defaultStrings; }
                if (defaults == null)
                {
                    string ignored;
                    defaults = ReadLocale(DefaultLanguage, out ignored);
                }
            }

            lock (Sync)
            {
                _current = normalized;
                _strings = strings;
                if (string.Equals(normalized, DefaultLanguage, StringComparison.Ordinal))
                    _defaultStrings = strings;
                else if (defaults != null)
                    _defaultStrings = defaults;

                _loaded = true;
                _lastError = null;
            }

            if (raiseEvent) RaiseLanguageChanged();
            return true;
        }

        private static void EnsureLoaded()
        {
            if (_loaded) return;

            // LoadLock (not Sync) so concurrent first lookups wait for the
            // table instead of briefly seeing an empty one.
            lock (LoadLock)
            {
                if (_loaded) return;

                string saved = GetSavedLanguage();
                if (!Apply(saved, false) &&
                    !string.Equals(saved, DefaultLanguage, StringComparison.OrdinalIgnoreCase))
                {
                    Apply(DefaultLanguage, false);
                }

                // Mark loaded even on failure: a missing locales folder must
                // not cause disk I/O on every T() call.
                lock (Sync) { _loaded = true; }
            }
        }

        private static Dictionary<string, string> ReadLocale(string code, out string error)
        {
            error = null;
            string path = Path.Combine(ResolveLocaleDirectory(), code + LocaleExtension);

            string text;
            try
            {
                text = ReadAllTextUtf8(path);
            }
            catch (Exception ex)
            {
                error = "cannot read locale file " + path + ": " + ex.Message;
                return null;
            }

            try
            {
                return FlatJsonReader.Parse(text, path);
            }
            catch (Exception ex)
            {
                error = "invalid locale file " + path + ": " + ex.Message;
                return null;
            }
        }

        private static string ReadAllTextUtf8(string path)
        {
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (StreamReader reader = new StreamReader(stream, new UTF8Encoding(false), true))
            {
                return reader.ReadToEnd();
            }
        }

        private static string ResolveLocaleDirectory()
        {
            if (_localeDirectory != null) return _localeDirectory;

            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string candidate = Path.Combine(baseDirectory, LocaleFolderName);
            if (Directory.Exists(candidate))
            {
                _localeDirectory = candidate;
                return _localeDirectory;
            }

            // Some hosts (and Mono under Wine) report a BaseDirectory that is
            // not the assembly location; probe next to the assembly as well.
            try
            {
                string assemblyDirectory = Path.GetDirectoryName(typeof(I18n).Assembly.Location);
                if (!string.IsNullOrEmpty(assemblyDirectory))
                {
                    candidate = Path.Combine(assemblyDirectory, LocaleFolderName);
                    if (Directory.Exists(candidate))
                    {
                        _localeDirectory = candidate;
                        return _localeDirectory;
                    }
                }
            }
            catch
            {
                // Fall through to the base-directory path for the error message.
            }

            _localeDirectory = Path.Combine(baseDirectory, LocaleFolderName);
            return _localeDirectory;
        }

        private static bool SaveLanguage(string code, out string error)
        {
            error = null;
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryKeyPath))
                {
                    if (key == null)
                    {
                        error = "cannot open HKCU\\" + RegistryKeyPath + " for writing";
                        return false;
                    }
                    key.SetValue(RegistryValueName, code, RegistryValueKind.String);
                }
                return true;
            }
            catch (Exception ex)
            {
                error = "cannot persist language \"" + code + "\": " + ex.Message;
                return false;
            }
        }

        private static bool TryNormalizeCode(string code, out string normalized)
        {
            normalized = null;
            if (code == null) return false;

            string trimmed = code.Trim();
            if (trimmed.Length == 0 || trimmed.Length > 35) return false;

            for (int i = 0; i < trimmed.Length; i++)
            {
                char c = trimmed[i];
                bool allowed =
                    (c >= 'a' && c <= 'z') ||
                    (c >= 'A' && c <= 'Z') ||
                    (c >= '0' && c <= '9') ||
                    c == '-' || c == '_';
                if (!allowed) return false; // rejects path separators and dots
            }

            normalized = trimmed.ToLowerInvariant();
            return true;
        }

        private static void SetError(string message)
        {
            lock (Sync) { _lastError = message; }
        }

        private static void RaiseLanguageChanged()
        {
            EventHandler handler = LanguageChanged;
            if (handler == null) return;

            try
            {
                handler(null, EventArgs.Empty);
            }
            catch
            {
                // A subscriber must never break translation lookups.
            }
        }

        // ------------------------------------------------------- flat JSON reader

        /// <summary>
        /// Minimal strict reader for a flat JSON object of string values.
        /// Supports \" \\ \/ \b \f \n \r \t \uXXXX escapes.  Nested objects,
        /// arrays, numbers, booleans, null and comments are rejected with a
        /// line/column message; duplicate keys keep the last value.
        /// </summary>
        private sealed class FlatJsonReader
        {
            private readonly string _text;
            private readonly string _source;
            private int _pos;

            private FlatJsonReader(string text, string source)
            {
                _text = text;
                _source = source;
            }

            public static Dictionary<string, string> Parse(string text, string source)
            {
                FlatJsonReader reader = new FlatJsonReader(text ?? string.Empty, source);
                return reader.ParseRoot();
            }

            private Dictionary<string, string> ParseRoot()
            {
                Dictionary<string, string> map =
                    new Dictionary<string, string>(StringComparer.Ordinal);

                SkipWhitespace();
                Expect('{');
                SkipWhitespace();
                if (TryConsume('}'))
                {
                    ExpectEnd();
                    return map;
                }

                while (true)
                {
                    SkipWhitespace();
                    string key = ParseString();

                    SkipWhitespace();
                    Expect(':');
                    SkipWhitespace();
                    string value = ParseValueString();

                    map[key] = value; // duplicate keys: last one wins

                    SkipWhitespace();
                    if (TryConsume(',')) continue;
                    if (TryConsume('}')) break;
                    Fail("expected ',' or '}'");
                }

                ExpectEnd();
                return map;
            }

            private string ParseValueString()
            {
                if (AtEnd() || _text[_pos] != '"')
                {
                    Fail("expected a string value (a flat {\"key\": \"value\"} object; " +
                         "numbers, booleans, null, arrays and nested objects are not supported)");
                }
                return ParseString();
            }

            private string ParseString()
            {
                Expect('"');
                StringBuilder builder = new StringBuilder();

                while (true)
                {
                    if (AtEnd()) Fail("unterminated string");

                    char c = _text[_pos++];
                    if (c == '"') return builder.ToString();
                    if (c == '\\')
                    {
                        builder.Append(ParseEscape());
                        continue;
                    }
                    if (c < ' ') Fail("unescaped control character in string");
                    builder.Append(c);
                }
            }

            private char ParseEscape()
            {
                if (AtEnd()) Fail("unterminated escape sequence");

                char escape = _text[_pos++];
                switch (escape)
                {
                    case '"': return '"';
                    case '\\': return '\\';
                    case '/': return '/';
                    case 'b': return '\b';
                    case 'f': return '\f';
                    case 'n': return '\n';
                    case 'r': return '\r';
                    case 't': return '\t';
                    case 'u':
                        if (_pos + 4 > _text.Length) Fail("incomplete \\u escape");
                        int code = 0;
                        for (int i = 0; i < 4; i++)
                        {
                            int digit = HexDigit(_text[_pos + i]);
                            if (digit < 0) Fail("invalid \\u escape");
                            code = (code << 4) | digit;
                        }
                        _pos += 4;
                        return (char)code; // surrogates arrive as two escapes
                    default:
                        Fail("invalid escape '\\" + escape + "'");
                        return '?'; // unreachable; Fail throws
                }
            }

            private static int HexDigit(char c)
            {
                if (c >= '0' && c <= '9') return c - '0';
                if (c >= 'a' && c <= 'f') return c - 'a' + 10;
                if (c >= 'A' && c <= 'F') return c - 'A' + 10;
                return -1;
            }

            private void SkipWhitespace()
            {
                while (!AtEnd())
                {
                    char c = _text[_pos];
                    if (c == ' ' || c == '\t' || c == '\r' || c == '\n') _pos++;
                    else return;
                }
            }

            private bool TryConsume(char expected)
            {
                if (!AtEnd() && _text[_pos] == expected)
                {
                    _pos++;
                    return true;
                }
                return false;
            }

            private void Expect(char expected)
            {
                if (AtEnd() || _text[_pos] != expected) Fail("expected '" + expected + "'");
                _pos++;
            }

            private void ExpectEnd()
            {
                SkipWhitespace();
                if (AtEnd()) return;

                // A decoded UTF-8 BOM can survive StreamReader in exotic cases.
                if (_text[_pos] == '\uFEFF')
                {
                    _pos++;
                    SkipWhitespace();
                    if (AtEnd()) return;
                }
                Fail("unexpected trailing content after the JSON object");
            }

            private bool AtEnd()
            {
                return _pos >= _text.Length;
            }

            private void Fail(string message)
            {
                int line = 1;
                int column = 1;
                int limit = _pos < _text.Length ? _pos : _text.Length;
                for (int i = 0; i < limit; i++)
                {
                    if (_text[i] == '\n') { line++; column = 1; }
                    else column++;
                }

                throw new FormatException(
                    message + " at line " + line.ToString(CultureInfo.InvariantCulture) +
                    ", column " + column.ToString(CultureInfo.InvariantCulture) +
                    " of " + _source);
            }
        }
    }
}
