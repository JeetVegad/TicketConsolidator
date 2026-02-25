using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TicketConsolidator.Application.Configurations;
using TicketConsolidator.Application.DTOs;
using TicketConsolidator.Application.Interfaces;

namespace TicketConsolidator.Infrastructure.Services
{
    public class PerforceService : IPerforceService
    {
        private readonly JiraConfiguration _config;
        private readonly ILoggerService _logger;
        private bool? _cliAvailable;

        public PerforceService(JiraConfiguration config, ILoggerService logger)
        {
            _config = config;
            _logger = logger;
        }

        public async Task<List<PerforceChangelist>> GetChangelistsByTicketAsync(string ticketNumber)
        {
            _logger.LogInfo($"Searching Perforce changelists for {ticketNumber}...");

            try
            {
                // Try CLI first, fallback to P4.NET
                if (await IsCliAvailableAsync())
                {
                    return await GetChangelistsViaCli(ticketNumber);
                }
                else
                {
                    _logger.LogInfo("P4 CLI not found. Using P4.NET SDK fallback.");
                    return await GetChangelistsViaSdk(ticketNumber);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Perforce search failed: {ex.Message}");
                throw new Exception($"Failed to search Perforce for {ticketNumber}: {ex.Message}", ex);
            }
        }

        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                if (await IsCliAvailableAsync())
                {
                    var result = await RunP4CommandAsync("info");
                    return !string.IsNullOrEmpty(result) && result.Contains("Server address");
                }
                return false; // SDK connection test would go here
            }
            catch
            {
                return false;
            }
        }

        #region P4 CLI Approach

        private async Task<bool> IsCliAvailableAsync()
        {
            if (_cliAvailable.HasValue) return _cliAvailable.Value;

            try
            {
                var result = await RunP4CommandAsync("info");
                _cliAvailable = !string.IsNullOrEmpty(result) && !result.Contains("not recognized");
                if (_cliAvailable.Value)
                    _logger.LogInfo("P4 CLI detected.");
                return _cliAvailable.Value;
            }
            catch
            {
                _cliAvailable = false;
                return false;
            }
        }

        private async Task<List<PerforceChangelist>> GetChangelistsViaCli(string ticketNumber)
        {
            // Get changelists with long descriptions
            var output = await RunP4CommandAsync("changes -l -s submitted //...");

            if (string.IsNullOrEmpty(output))
                return new List<PerforceChangelist>();

            var allChangelists = ParseChangelistOutput(output);

            // Filter by ticket number in description
            var matching = allChangelists
                .Where(c => c.Description != null &&
                            c.Description.IndexOf(ticketNumber, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();

            // Get affected files for each matching changelist
            foreach (var cl in matching)
            {
                var filesOutput = await RunP4CommandAsync($"describe -s {cl.ChangeNumber}");
                cl.AffectedFiles = ParseAffectedFiles(filesOutput);
                cl.IsDatabase = cl.AffectedFiles.Any(f =>
                    f.IndexOf(_config.DatabaseDepotPattern ?? "Database", StringComparison.OrdinalIgnoreCase) >= 0);
            }

            _logger.LogSuccess($"Found {matching.Count} Perforce changelists for {ticketNumber} via CLI.");
            return matching;
        }

        private async Task<string> RunP4CommandAsync(string arguments)
        {
            return await Task.Run(() =>
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "p4",
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                // Set server/user/workspace if configured
                if (!string.IsNullOrEmpty(_config.PerforceServer))
                    psi.Arguments = $"-p {_config.PerforceServer} " + psi.Arguments;
                if (!string.IsNullOrEmpty(_config.PerforceUser))
                    psi.Arguments = $"-u {_config.PerforceUser} " + psi.Arguments;
                if (!string.IsNullOrEmpty(_config.PerforceWorkspace))
                    psi.Arguments = $"-c {_config.PerforceWorkspace} " + psi.Arguments;

                using var process = Process.Start(psi);
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(30000); // 30s timeout
                return output;
            });
        }

        private List<PerforceChangelist> ParseChangelistOutput(string output)
        {
            var changelists = new List<PerforceChangelist>();
            // Format: Change 12345 on 2026/02/24 by user@workspace 'description...'
            // With -l flag, description follows on subsequent indented lines

            var lines = output.Split('\n');
            PerforceChangelist current = null;
            var descBuilder = new StringBuilder();

            foreach (var rawLine in lines)
            {
                var line = rawLine.TrimEnd('\r');

                var match = Regex.Match(line, @"^Change (\d+) on (\d{4}/\d{2}/\d{2}) .* by (\S+)@");
                if (match.Success)
                {
                    // Save previous changelist
                    if (current != null)
                    {
                        current.Description = descBuilder.ToString().Trim();
                        changelists.Add(current);
                    }

                    current = new PerforceChangelist
                    {
                        ChangeNumber = int.Parse(match.Groups[1].Value),
                        User = match.Groups[3].Value,
                        Date = DateTime.ParseExact(match.Groups[2].Value, "yyyy/MM/dd", CultureInfo.InvariantCulture)
                    };
                    descBuilder.Clear();
                }
                else if (current != null && (line.StartsWith("\t") || line.StartsWith("    ")))
                {
                    descBuilder.AppendLine(line.Trim());
                }
            }

            // Don't forget the last one
            if (current != null)
            {
                current.Description = descBuilder.ToString().Trim();
                changelists.Add(current);
            }

            return changelists;
        }

        private List<string> ParseAffectedFiles(string describeOutput)
        {
            var files = new List<string>();
            if (string.IsNullOrEmpty(describeOutput)) return files;

            bool inFilesSection = false;
            foreach (var rawLine in describeOutput.Split('\n'))
            {
                var line = rawLine.Trim();

                if (line.StartsWith("Affected files"))
                {
                    inFilesSection = true;
                    continue;
                }

                if (inFilesSection)
                {
                    if (string.IsNullOrEmpty(line)) break;

                    // Format: ... //depot/path/file.cs#rev action
                    var match = Regex.Match(line, @"\.\.\.\ (//[^#]+)#");
                    if (match.Success)
                    {
                        files.Add(match.Groups[1].Value);
                    }
                }
            }

            return files;
        }

        #endregion

        #region P4.NET SDK Fallback

        private Task<List<PerforceChangelist>> GetChangelistsViaSdk(string ticketNumber)
        {
            // P4.NET (p4api.net NuGet) fallback implementation
            // This provides the same functionality without requiring the P4 CLI to be installed.
            //
            // To enable: Install NuGet package 'p4api.net' and uncomment below.
            //
            // using Perforce.P4;
            // var server = new Server(new ServerAddress(_config.PerforceServer));
            // var repo = new Repository(server);
            // var con = repo.Connection;
            // con.UserName = _config.PerforceUser;
            // con.Client = new Client { Name = _config.PerforceWorkspace };
            // con.Connect(null);
            //
            // var opts = new ChangesCmdOptions(ChangesCmdFlags.FullDescription, null, 0, ChangeListStatus.Submitted, null);
            // var changes = repo.GetChangelists(opts);
            // var matching = changes.Where(c => c.Description.Contains(ticketNumber)).ToList();
            // ... convert to PerforceChangelist DTOs ...

            _logger.LogError("P4.NET SDK is not yet configured. Please install the P4 CLI or add the p4api.net NuGet package.");
            return Task.FromResult(new List<PerforceChangelist>());
        }

        #endregion
    }
}
