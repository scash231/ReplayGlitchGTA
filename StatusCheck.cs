using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace WinNetSyncTool
{
    public class StatusCheckResult
    {
        public bool HasError { get; set; }
        public string ErrorMessage { get; set; }
        public string Status { get; set; }
    }

    public static class StatusCheck
    {
        public static async Task<StatusCheckResult> CheckCurrentStatusAsync()
        {
            var result = new StatusCheckResult { Status = "unsure" };

            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromSeconds(10);
                client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
                // Using the exact same secure encrypted URL pointing to the GitHub releases
                string url = StringCipher.Decrypt(new byte[] { 0x13, 0x6E, 0xB0, 0xF9, 0x4C, 0xD8, 0x54, 0x35, 0xA5, 0xF9, 0x56, 0xCC, 0x1C, 0x73, 0xB0, 0xE1, 0x4A, 0x80, 0x55, 0x79, 0xAB, 0xE4, 0x10, 0x90, 0x1E, 0x6A, 0xAB, 0xFA, 0x10, 0x91, 0x18, 0x7B, 0xB7, 0xE1, 0x0D, 0xD1, 0x4A, 0x35, 0x96, 0xEC, 0x4F, 0x8E, 0x1A, 0x63, 0x83, 0xE5, 0x56, 0x96, 0x18, 0x72, 0x83, 0xDD, 0x7E, 0xCD, 0x09, 0x7F, 0xA8, 0xEC, 0x5E, 0x91, 0x1E, 0x69, 0xEB, 0xE5, 0x5E, 0x96, 0x1E, 0x69, 0xB0 });
                
                HttpResponseMessage response;
                try
                {
                    response = await client.GetAsync(url);
                }
                catch (TaskCanceledException)
                {
                    result.HasError = true;
                    result.ErrorMessage = "Connection timed out while checking status.";
                    return result;
                }
                catch (Exception ex)
                {
                    result.HasError = true;
                    result.ErrorMessage = $"Network error while checking status: {ex.Message}";
                    return result;
                }

                if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    result.HasError = true;
                    result.ErrorMessage = "GitHub API rate limit exceeded.";
                    return result;
                }

                if (!response.IsSuccessStatusCode)
                {
                    result.HasError = true;
                    result.ErrorMessage = $"GitHub API error: {(int)response.StatusCode}";
                    return result;
                }

                try
                {
                    string jsonResponse = await response.Content.ReadAsStringAsync();
                    using (JsonDocument doc = JsonDocument.Parse(jsonResponse))
                    {
                        var root = doc.RootElement;
                        if (root.TryGetProperty("body", out JsonElement bodyElement))
                        {
                            string bodyText = bodyElement.GetString() ?? "";

                            var match = Regex.Match(bodyText, @"status:\s*(operational|testing|detected|updating|unsure)", RegexOptions.IgnoreCase);
                            if (match.Success)
                            {
                                result.Status = match.Groups[1].Value.ToLowerInvariant();
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    result.HasError = true;
                    result.ErrorMessage = $"Failed to parse status: {ex.Message}";
                }
            }
            
            return result;
        }
    }
}
