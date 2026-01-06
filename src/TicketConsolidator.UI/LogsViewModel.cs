using System.Collections.ObjectModel;
using TicketConsolidator.Application.DTOs;
using TicketConsolidator.Application.Interfaces;

namespace TicketConsolidator.UI
{
    public class LogsViewModel
    {
        private readonly ILoggerService _loggerService;

        public ObservableCollection<LogSession> Sessions => _loggerService.Sessions;

        public LogsViewModel(ILoggerService loggerService)
        {
            _loggerService = loggerService;
        }
    }
}
