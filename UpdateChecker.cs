using System;
using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Threading.Tasks;
using System.IO;

namespace GTAFirewallToggle
{
    public class UpdateCheckResult
    {
        public bool HasUpdate { get; set; }
        public bool IsUpToDate { get; set; }
        public bool HasError { get; set; }
        public string ErrorMessage { get; set; }
    }

    public static class UpdateChecker
    {
        public static async Task<UpdateCheckResult> CheckForUpdatesAsync()
        {
            var result = new UpdateCheckResult();
            
            string exePath = Process.GetCurrentProcess().MainModule.FileName;
            string localHash = "";
            try
            {
                using (var sha256 = SHA256.Create())
                {
                    using (var stream = File.OpenRead(exePath))
                    {
                        byte[] hashBytes = sha256.ComputeHash(stream);
                        localHash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
                    }
                }
            }
            catch (Exception ex)
            {
                result.HasError = true;
                result.ErrorMessage = $"Failed to hash local executable: {ex.Message}";
                return result;
            }

            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromSeconds(10);
                client.DefaultRequestHeaders.Add("User-Agent", "GTAFirewallToggle-Updater");
                string url = "https://api.github.com/repos/scash231/ReplayGlitchGTA/releases/latest";
                
                HttpResponseMessage response;
                try
                {
                    response = await client.GetAsync(url);
                }
                catch (TaskCanceledException)
                {
                    result.HasError = true;
                    result.ErrorMessage = "Connection timed out while checking for updates.";
                    return result;
                }
                catch (Exception ex)
                {
                    result.HasError = true;
                    result.ErrorMessage = $"Network error while checking for updates: {ex.Message}";
                    return result;
                }

                if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    result.HasError = true;
                    result.ErrorMessage = "GitHub API rate limit exceeded. Please try again later.";
                    return result;
                }

                if (!response.IsSuccessStatusCode)
                {
                    result.HasError = true;
                    result.ErrorMessage = $"GitHub API returned an error: {(int)response.StatusCode} {response.ReasonPhrase}";
                    return result;
                }

                try
                {
                    string json = await response.Content.ReadAsStringAsync();
                    using (JsonDocument doc = JsonDocument.Parse(json))
                    {
                        if (doc.RootElement.TryGetProperty("body", out JsonElement bodyElement))
                        {
                            string bodyText = bodyElement.GetString() ?? "";
                            var match = Regex.Match(bodyText, @"active[^a-zA-Z0-9]*sha256[^a-zA-Z0-9]*([a-fA-F0-9]{64})", RegexOptions.IgnoreCase);
                            if (match.Success)
                            {
                                string remoteHash = match.Groups[1].Value.ToLowerInvariant();
                                if (localHash != remoteHash)
                                {
                                    result.HasUpdate = true;
                                }
                                else
                                {
                                    result.IsUpToDate = true;
                                }
                            }
                            else
                            {
                                result.HasError = true;
                                result.ErrorMessage = "Could not find the expected SHA256 hash in the latest release notes.";
                            }
                        }
                        else
                        {
                            result.HasError = true;
                            result.ErrorMessage = "Invalid release format returned by GitHub (missing body).";
                        }
                    }
                }
                catch (Exception ex)
                {
                    result.HasError = true;
                    result.ErrorMessage = $"Failed to parse GitHub response: {ex.Message}";
                }
            }
            
            return result;
        }
    }
}
