using System.Collections.Generic;
using System.Threading.Tasks;
using TicketConsolidator.Application.DTOs;

namespace TicketConsolidator.Application.Interfaces
{
    public interface IEmailService
    {
        /// <summary>
        /// Connects to the mail server.
        /// </summary>
        Task ConnectAsync();

        /// <summary>
        /// Disconnects from the mail server.
        /// </summary>
        Task DisconnectAsync();

        /// <summary>
        /// Searches for emails containing specific ticket numbers in the Subject or Attachment Name.
        /// </summary>
        /// <param name="ticketNumbers">List of ticket IDs to search for.</param>
        /// <param name="folderName">Target folder name (e.g., "Inbox").</param>
        /// <returns>List of matching emails with attachments.</returns>
        Task<List<EmailMessage>> GetEmailsByTicketNumbersAsync(List<string> ticketNumbers, string folderName = "Inbox", System.Threading.CancellationToken cancellationToken = default);

        /// <summary>
        /// Creates a draft email in Outlook with the specified subject, body, and attachments.
        /// </summary>
        /// <param name="subject">Email Subject.</param>
        /// <param name="htmlBody">Email Body (HTML).</param>
        /// <param name="attachmentPaths">List of file paths to attach.</param>
        Task CreateDraftEmailAsync(string subject, string htmlBody, List<string> attachmentPaths);
    }
}
