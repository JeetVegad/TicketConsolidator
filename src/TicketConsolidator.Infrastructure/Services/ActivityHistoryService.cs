using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using TicketConsolidator.Application.DTOs;

namespace TicketConsolidator.Infrastructure.Services
{
    public class ActivityHistoryService
    {
        private readonly string _filePath;
        private const int MaxEntries = 500;
        private List<ActivityEntry> _cache;

        public ActivityHistoryService(SettingsService settingsService)
        {
            _filePath = Path.Combine(settingsService.AppDataFolder, "activity_history.json");
            _cache = LoadFromDisk();
        }

        private List<ActivityEntry> LoadFromDisk()
        {
            try
            {
                if (!File.Exists(_filePath)) return new List<ActivityEntry>();
                var json = File.ReadAllText(_filePath);
                return JsonSerializer.Deserialize<List<ActivityEntry>>(json) ?? new List<ActivityEntry>();
            }
            catch
            {
                return new List<ActivityEntry>();
            }
        }

        private async Task SaveToDiskAsync()
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            await File.WriteAllTextAsync(_filePath, JsonSerializer.Serialize(_cache, options));
        }

        public async Task RecordConsolidationAsync(int ticketCount, int scriptCount, int tables, int sps, int triggers, int data)
        {
            var entry = new ActivityEntry
            {
                Type = "Consolidation",
                Date = DateTime.Now,
                TicketKey = $"{ticketCount} ticket(s)",
                Summary = $"{scriptCount} scripts — {tables}T, {sps}SP, {triggers}Trg, {data}Data",
                ItemCount = scriptCount
            };

            _cache.Insert(0, entry);
            if (_cache.Count > MaxEntries)
                _cache = _cache.Take(MaxEntries).ToList();

            await SaveToDiskAsync();
        }

        public async Task RecordCodeReviewAsync(string ticketKey, int vsCount, int dbCount, int attachCount)
        {
            var entry = new ActivityEntry
            {
                Type = "CodeReview",
                Date = DateTime.Now,
                TicketKey = ticketKey,
                Summary = $"{vsCount} VS, {dbCount} DB, {attachCount} attachment(s)",
                ItemCount = vsCount + dbCount
            };

            _cache.Insert(0, entry);
            if (_cache.Count > MaxEntries)
                _cache = _cache.Take(MaxEntries).ToList();

            await SaveToDiskAsync();
        }

        public int TotalConsolidations => _cache.Count(e => e.Type == "Consolidation");
        public int TotalCodeReviews => _cache.Count(e => e.Type == "CodeReview");

        public int TodayActivity => _cache.Count(e => e.Date.Date == DateTime.Today);
        public int WeeklyActivity
        {
            get
            {
                var weekStart = DateTime.Today.AddDays(-(int)DateTime.Today.DayOfWeek);
                return _cache.Count(e => e.Date >= weekStart);
            }
        }
        public int MonthlyActivity => _cache.Count(e => e.Date.Year == DateTime.Today.Year && e.Date.Month == DateTime.Today.Month);

        public ActivityEntry LastCodeReview => _cache.FirstOrDefault(e => e.Type == "CodeReview");

        public List<ActivityEntry> GetRecentActivity(int count = 10)
        {
            return _cache.Take(count).ToList();
        }

        public void RefreshCache()
        {
            _cache = LoadFromDisk();
        }
    }
}
