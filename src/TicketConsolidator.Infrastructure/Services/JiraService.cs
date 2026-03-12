using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TicketConsolidator.Application.Configurations;
using TicketConsolidator.Application.DTOs;
using TicketConsolidator.Application.Interfaces;

namespace TicketConsolidator.Infrastructure.Services
{
    public class JiraService : IJiraService
    {
        private readonly JiraConfiguration _config;
        private readonly ILoggerService _logger;
        private HttpClient _httpClient;
        private CookieContainer _cookieContainer;
        private bool _isAuthenticated;

        public bool IsAuthenticated => _isAuthenticated;
        public event Action AuthenticationStatusChanged;

        public JiraService(JiraConfiguration config, ILoggerService logger)
        {
            _config = config;
            _logger = logger;
            _cookieContainer = new CookieContainer();
            RebuildHttpClient();
        }

        private void RebuildHttpClient()
        {
            var handler = new HttpClientHandler
            {
                CookieContainer = _cookieContainer,
                UseCookies = true,
                UseProxy = false // Fixes the 30-second initial timeout caused by Windows proxy auto-discovery
            };

            _httpClient = new HttpClient(handler)
            {
                BaseAddress = new Uri(_config.JiraBaseUrl.TrimEnd('/') + "/"),
                Timeout = TimeSpan.FromSeconds(30)
            };
        }

        public void SetCookies(IEnumerable<Cookie> cookies)
        {
            _cookieContainer = new CookieContainer();
            var baseUri = new Uri(_config.JiraBaseUrl.TrimEnd('/') + "/");

            foreach (var cookie in cookies)
            {
                try
                {
                    _cookieContainer.Add(baseUri, cookie);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Skipped cookie {cookie.Name}: {ex.Message}");
                }
            }

            RebuildHttpClient();
            bool wasAuthenticated = _isAuthenticated;
            _isAuthenticated = true;
            _logger.LogSuccess($"Jira cookies loaded ({_cookieContainer.Count} cookies).");
            
            if (!wasAuthenticated)
            {
                AuthenticationStatusChanged?.Invoke();
            }
        }

        public void ClearCookies()
        {
            _cookieContainer = new CookieContainer();
            RebuildHttpClient();
            bool wasAuthenticated = _isAuthenticated;
            _isAuthenticated = false;
            
            if (wasAuthenticated)
            {
                AuthenticationStatusChanged?.Invoke();
            }
        }

        public async Task<JiraTicketInfo> GetTicketAsync(string ticketKey)
        {
            if (!_isAuthenticated)
                throw new UnauthorizedAccessException("Not authenticated. Please log in via the embedded browser.");

            _logger.LogInfo($"Fetching ticket {ticketKey} from Jira...");

            var response = await _httpClient.GetAsync(
                $"rest/api/2/issue/{ticketKey}?fields=summary,description,status,assignee,issuelinks,attachment");

            if (response.StatusCode == HttpStatusCode.Unauthorized ||
                response.StatusCode == HttpStatusCode.Forbidden)
            {
                bool wasAuthenticated = _isAuthenticated;
                _isAuthenticated = false;
                if (wasAuthenticated)
                {
                    AuthenticationStatusChanged?.Invoke();
                }
                throw new UnauthorizedAccessException(
                    "Session expired. Please log in again via the browser panel.");
            }

            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var ticket = ParseTicketResponse(ticketKey, json);

            // Also fetch remote links (Swarm reviews are often added as remote links)
            await FetchRemoteLinks(ticketKey, ticket);

            return ticket;
        }

        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("rest/api/2/serverInfo");
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        private JiraTicketInfo ParseTicketResponse(string ticketKey, string json)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var fields = root.GetProperty("fields");

            var ticket = new JiraTicketInfo
            {
                Key = root.GetProperty("key").GetString() ?? ticketKey,
                Summary = fields.TryGetProperty("summary", out var s) ? s.GetString() ?? "" : "",
                Description = fields.TryGetProperty("description", out var d) && d.ValueKind != JsonValueKind.Null
                    ? d.GetString() ?? "" : "",
                Status = fields.TryGetProperty("status", out var st) && st.TryGetProperty("name", out var stName)
                    ? stName.GetString() ?? "" : "",
                Assignee = fields.TryGetProperty("assignee", out var a) && a.ValueKind != JsonValueKind.Null
                    && a.TryGetProperty("displayName", out var aName) ? aName.GetString() ?? "" : "",
                Url = $"{_config.JiraBaseUrl.TrimEnd('/')}/browse/{ticketKey}"
            };

            // Parse issue links for Swarm references
            if (fields.TryGetProperty("issuelinks", out var links) && links.ValueKind == JsonValueKind.Array)
            {
                foreach (var link in links.EnumerateArray())
                {
                    ParseIssueLink(link, ticket);
                }
            }

            // Parse attachments for UT documents and other files
            if (fields.TryGetProperty("attachment", out var attachments) && attachments.ValueKind == JsonValueKind.Array)
            {
                foreach (var attachment in attachments.EnumerateArray())
                {
                    if (attachment.TryGetProperty("filename", out var filenameProp))
                    {
                        string filename = filenameProp.GetString() ?? "";
                        ticket.Attachments.Add(filename);

                        if (filename.Contains("UT", StringComparison.OrdinalIgnoreCase))
                        {
                            ticket.HasUTDocument = true;
                        }
                    }
                }
            }

            _logger.LogSuccess($"Fetched: {ticket.Key} - {ticket.Summary}");
            return ticket;
        }

        private void ParseIssueLink(JsonElement link, JiraTicketInfo ticket)
        {
            try
            {
                string relationship = "";
                string title = "";
                string url = "";

                // Get the relationship type
                if (link.TryGetProperty("type", out var type))
                {
                    if (type.TryGetProperty("outward", out var outward))
                        relationship = outward.GetString() ?? "";
                }

                // Check outward issue
                JsonElement? targetIssue = null;
                if (link.TryGetProperty("outwardIssue", out var outIssue))
                    targetIssue = outIssue;
                else if (link.TryGetProperty("inwardIssue", out var inIssue))
                {
                    targetIssue = inIssue;
                    if (link.TryGetProperty("type", out var t) && t.TryGetProperty("inward", out var inward))
                        relationship = inward.GetString() ?? "";
                }

                string summary = "";
                string issueTypeName = "";
                string key = null;
                if (targetIssue.HasValue)
                {
                    var ti = targetIssue.Value;
                     key = ti.TryGetProperty("key", out var k) ? k.GetString() ?? "" : "";
                    if (ti.TryGetProperty("fields", out var f))
                    {
                        if (f.TryGetProperty("summary", out var sum))
                            summary = sum.GetString() ?? "";
                            
                        if (f.TryGetProperty("issuetype", out var it) && it.TryGetProperty("name", out var itName))
                            issueTypeName = itName.GetString() ?? "";
                    }

                    title = string.IsNullOrEmpty(summary) ? key : $"{key} - {summary}";
                    url = $"{_config.JiraBaseUrl.TrimEnd('/')}/browse/{key}";
                }

                // Check if this is a Code Review ticket
                bool isCodeReview = false;
                
                string summaryLower = summary.ToLowerInvariant();
                string typeLower = issueTypeName.ToLowerInvariant();

                if (relationship.IndexOf("satisfies", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    typeLower.Contains("code review") || 
                    summaryLower.Contains("code review") ||
                    typeLower.Contains("self review") || 
                    summaryLower.Contains("self review") ||
                    typeLower.Contains("review defect") ||
                    summaryLower.Contains("review defect"))
                {
                    isCodeReview = true;
                }

                if (!string.IsNullOrEmpty(key) && isCodeReview)
                {
                    bool isSelf = summaryLower.Contains("self") || typeLower.Contains("self");
                    
                    ticket.CodeReviewTickets.Add(new LinkedJiraTicket
                    {
                        Key = key,
                        Summary = summary,
                        Url = url,
                        Type = isSelf ? "Self-Code Review" : "Code Review"
                    });
                }

                // Check if this looks like a Swarm/changelist reference

                if (!string.IsNullOrEmpty(title))
                {
                    // Try to extract changelist number and comment from the title or key
                    var details = ExtractChangeDetails(title + " " + url);

                    string finalComment = string.IsNullOrWhiteSpace(summary) ? details.Comment : summary;
                    if (string.IsNullOrWhiteSpace(finalComment)) 
                    {
                        finalComment = "RAW JSON: " + link.GetRawText();
                    }

                    ticket.SwarmLinks.Add(new SwarmLink
                    {
                        Title = title,
                        Url = url,
                        Relationship = relationship,
                        ChangeNumber = details.ChangeNumber ?? "",
                        Comment = finalComment
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Could not parse issue link: {ex.Message}");
            }
        }

        private async Task FetchRemoteLinks(string ticketKey, JiraTicketInfo ticket)
        {
            try
            {
                var response = await _httpClient.GetAsync($"rest/api/2/issue/{ticketKey}/remotelink");

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning($"Could not fetch remote links: {response.StatusCode}");
                    return;
                }

                var json = await response.Content.ReadAsStringAsync();

                try 
                {
                    string dumpPath = System.IO.Path.Combine(
                        System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments),
                        "TicketConsolidatorData",
                        "Logs",
                        "JiraRemoteLinksDump.txt"
                    );
                    System.IO.File.WriteAllText(dumpPath, json);
                } 
                catch { }

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.ValueKind != JsonValueKind.Array) return;

                foreach (var remoteLink in root.EnumerateArray())
                {
                    try
                    {
                        string relationship = "";
                        if (remoteLink.TryGetProperty("relationship", out var rel))
                            relationship = rel.GetString() ?? "";

                        if (remoteLink.TryGetProperty("object", out var obj))
                        {
                            string title = "";
                            string url = "";
                            string summary = "";

                            if (obj.TryGetProperty("title", out var t))
                                title = t.GetString() ?? "";
                            if (obj.TryGetProperty("url", out var u))
                                url = u.GetString() ?? "";
                            if (obj.TryGetProperty("summary", out var s))
                                summary = s.GetString() ?? "";

                            if (!string.IsNullOrEmpty(title) || !string.IsNullOrEmpty(url))
                            {
                                var details = ExtractChangeDetails(title + " " + url);

                                string finalComment = string.IsNullOrWhiteSpace(summary) ? details.Comment : summary;
                                if (string.IsNullOrWhiteSpace(finalComment)) 
                                {
                                    finalComment = "RAW JSON: " + obj.GetRawText();
                                }

                                ticket.SwarmLinks.Add(new SwarmLink
                                {
                                    Title = string.IsNullOrEmpty(title) ? url : title,
                                    Url = url,
                                    Relationship = relationship,
                                    ChangeNumber = details.ChangeNumber ?? "",
                                    Comment = finalComment
                                });
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning($"Could not parse remote link: {ex.Message}");
                    }
                }

                _logger.LogInfo($"Found {ticket.SwarmLinks.Count} links for {ticketKey}.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Remote links fetch failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Try to extract a changelist/review number and the remaining description from text (title or URL).
        /// Matches patterns like: "Review 12345", "Change 67890", "Commit 12345 - some description"
        /// Returns a tuple of (ChangeNumber, Comment).
        /// </summary>
        private (string ChangeNumber, string Comment) ExtractChangeDetails(string text)
        {
            if (string.IsNullOrEmpty(text)) return (null, null);

            string changeNum = null;
            string comment = text;

            // Match text patterns: "Review 12345", "Change 12345", "CL 12345", "Commit 12345"
            // Also optionally capture a dash or colon after the number
            var textMatch = Regex.Match(text, @"(?:Review|Change|CL|Changelist|Commit)\s*#?\s*(\d+)(?:\s*[-:]\s*)?", RegexOptions.IgnoreCase);
            if (textMatch.Success)
            {
                changeNum = textMatch.Groups[1].Value;
                // The comment is everything after the matched prefix and number
                comment = text.Substring(textMatch.Index + textMatch.Length).Trim();
            }
            else
            {
                // Match Swarm URL patterns: /reviews/NNNNN or /changes/NNNNN
                var urlMatch = Regex.Match(text, @"/(?:reviews|changes)/(\d+)", RegexOptions.IgnoreCase);
                if (urlMatch.Success)
                {
                    changeNum = urlMatch.Groups[1].Value;
                }
                else
                {
                    // Match standalone large numbers that are likely changelist numbers
                    var numMatch = Regex.Match(text, @"\b(\d{4,})\b");
                    if (numMatch.Success)
                    {
                        changeNum = numMatch.Groups[1].Value;
                        comment = text.Substring(numMatch.Index + numMatch.Length).Trim();
                        if (comment.StartsWith("-") || comment.StartsWith(":")) 
                            comment = comment.TrimStart('-', ':', ' ');
                    }
                }
            }

            // If the comment is just the URL or empty, return null so it doesn't clutter the UI
            if (string.IsNullOrWhiteSpace(comment) || comment.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                comment = null;

            return (changeNum, comment);
        }
    }
}
