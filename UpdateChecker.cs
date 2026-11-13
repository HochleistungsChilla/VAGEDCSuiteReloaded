//---------------------------------------------------------------------------
//
// UpdateChecker.cs
//
// Tells whether a newer release of this fork exists.  The releases live on
// GitHub, so the check reads the public API: first the latest release, and
// when the repository has no release yet, the tag list.
//
// No JSON library: the answer is read with two regular expressions.  Every
// failure (no network, TLS too old, GitHub down) is reported instead of
// thrown, so the caller can stay silent during the automatic check.
//
//---------------------------------------------------------------------------

using System;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;

namespace VAGSuite
{
    public static class UpdateChecker
    {
        public const string Repository = "HochleistungsChilla/VAGEDCSuiteReloaded";
        public const string ReleasesPage = "https://github.com/" + Repository + "/releases";

        private const string LatestReleaseApi = "https://api.github.com/repos/" + Repository + "/releases/latest";
        private const string TagsApi = "https://api.github.com/repos/" + Repository + "/tags";
        private const int TimeoutMs = 8000;

        /// <summary>Outcome of one check.  Checked == false means unknown.</summary>
        public sealed class Result
        {
            public bool Checked;
            public string Error = string.Empty;
            public Version Latest;
            public string Url = ReleasesPage;

            public bool IsNewer(Version current)
            {
                return Checked && Latest != null && current != null && Latest > current;
            }
        }

        /// <summary>
        /// Reads the newest version from GitHub.  Never throws.
        /// </summary>
        public static Result Check()
        {
            Result result = new Result();
            try
            {
                // 404 simply means "no release published yet" and must not stop
                // the tag fallback below, so this call has its own guard.
                string json = null;
                try
                {
                    json = Download(LatestReleaseApi);
                }
                catch (WebException)
                {
                    json = null;
                }

                if (json != null)
                {
                    Match tag = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"v?([0-9][0-9.]*)\"");
                    if (tag.Success)
                    {
                        result.Latest = ParseVersion(tag.Groups[1].Value);
                        Match url = Regex.Match(json, "\"html_url\"\\s*:\\s*\"([^\"]+)\"");
                        if (url.Success) result.Url = url.Groups[1].Value;
                        result.Checked = result.Latest != null;
                        return result;
                    }
                }

                // No release published yet: the first tag in the list is the newest.
                json = Download(TagsApi);
                Match name = Regex.Match(json, "\"name\"\\s*:\\s*\"v?([0-9][0-9.]*)\"");
                if (name.Success)
                {
                    result.Latest = ParseVersion(name.Groups[1].Value);
                    result.Checked = result.Latest != null;
                    if (!result.Checked) result.Error = "No usable version tag on GitHub.";
                    return result;
                }
                result.Error = "No release and no version tag found on GitHub.";
            }
            catch (Exception ex)
            {
                result.Checked = false;
                result.Error = ex.Message;
            }
            return result;
        }

        private static Version ParseVersion(string text)
        {
            string[] parts = text.Split('.');
            if (parts.Length == 0) return null;
            int major;
            if (!int.TryParse(parts[0], out major)) return null;
            int minor = 0, patch = 0;
            if (parts.Length > 1) int.TryParse(parts[1], out minor);
            if (parts.Length > 2) int.TryParse(parts[2], out patch);
            return new Version(major, minor, patch);
        }

        private static string Download(string url)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            // GitHub rejects requests without a user agent.
            request.UserAgent = "VAGEDCSuite-Reloaded/" + typeof(UpdateChecker).Assembly.GetName().Version;
            request.Accept = "application/vnd.github+json";
            request.Timeout = TimeoutMs;
            request.ReadWriteTimeout = TimeoutMs;
            request.AllowAutoRedirect = true;

            using (WebResponse response = request.GetResponse())
            using (Stream stream = response.GetResponseStream())
            using (StreamReader reader = new StreamReader(stream))
            {
                return reader.ReadToEnd();
            }
        }
    }
}
