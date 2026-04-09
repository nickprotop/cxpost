using CXPost.Models;

namespace CXPost.UI.Components;

public class ThreadSummary
{
    public required string ThreadId { get; init; }
    public required List<MailMessage> Messages { get; init; }
    public required MailMessage NewestMessage { get; init; }
    public required string BaseSubject { get; init; }
    public required bool HasUnread { get; init; }
    public required bool HasFlagged { get; init; }
    public required bool HasAttachments { get; init; }
    public required int Count { get; init; }
    public bool IsThread => Count > 1;
}

public static class ConversationGrouper
{
    public static List<ThreadSummary> Group(List<MailMessage> messages)
    {
        var groups = new Dictionary<string, List<MailMessage>>();

        foreach (var msg in messages)
        {
            var threadId = msg.ThreadId ?? $"<orphan-{msg.Id}>";
            if (!groups.TryGetValue(threadId, out var list))
            {
                list = new List<MailMessage>();
                groups[threadId] = list;
            }
            list.Add(msg);
        }

        var summaries = new List<ThreadSummary>();
        foreach (var (threadId, threadMessages) in groups)
        {
            threadMessages.Sort((a, b) => a.Date.CompareTo(b.Date));
            var newest = threadMessages[^1];
            var baseSubject = MessageFormatter.StripReplyPrefix(newest.Subject);

            summaries.Add(new ThreadSummary
            {
                ThreadId = threadId,
                Messages = threadMessages,
                NewestMessage = newest,
                BaseSubject = baseSubject,
                HasUnread = threadMessages.Any(m => !m.IsRead),
                HasFlagged = threadMessages.Any(m => m.IsFlagged),
                HasAttachments = threadMessages.Any(m => m.HasAttachments),
                Count = threadMessages.Count
            });
        }

        summaries.Sort((a, b) => b.NewestMessage.Date.CompareTo(a.NewestMessage.Date));
        return summaries;
    }
}
