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
        // Phase 1: group by thread_id (header-based threading from ThreadingService)
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

        // Phase 2: subject-based merge for broken headers (Outlook/Exchange often omits
        // In-Reply-To and References). Guarded to avoid false merges:
        //  - At least one group must contain a message with a RE:/FW: prefix
        //  - Groups must share at least one participant (sender or recipient)
        //  - Newest messages in each group must be within 30 days of each other
        var subjectGroups = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (threadId, threadMessages) in groups)
        {
            var baseSubject = MessageFormatter.StripReplyPrefix(threadMessages[0].Subject);
            if (baseSubject == "(no subject)") continue;
            if (!subjectGroups.TryGetValue(baseSubject, out var threadIds))
            {
                threadIds = new List<string>();
                subjectGroups[baseSubject] = threadIds;
            }
            threadIds.Add(threadId);
        }

        foreach (var (_, threadIds) in subjectGroups)
        {
            if (threadIds.Count < 2) continue;

            // Require at least one group to have a reply/forward prefix
            bool hasReply = false;
            foreach (var tid in threadIds)
            {
                if (groups.TryGetValue(tid, out var msgs) &&
                    msgs.Any(m => MessageFormatter.HasReplyPrefix(m.Subject)))
                {
                    hasReply = true;
                    break;
                }
            }
            if (!hasReply) continue;

            // Merge candidates pairwise: only merge if time + participant constraints pass
            var primary = threadIds[0];
            for (var i = 1; i < threadIds.Count; i++)
            {
                if (!groups.ContainsKey(threadIds[i]) || !groups.ContainsKey(primary))
                    continue;

                var groupA = groups[primary];
                var groupB = groups[threadIds[i]];

                // Time window: newest messages must be within 30 days
                var newestA = groupA.Max(m => m.Date);
                var newestB = groupB.Max(m => m.Date);
                if (Math.Abs((newestA - newestB).TotalDays) > 30) continue;

                // Participant overlap: at least one shared address (sender or recipient)
                if (!HasParticipantOverlap(groupA, groupB)) continue;

                groupA.AddRange(groupB);
                groups.Remove(threadIds[i]);
            }
        }

        // Phase 3: build summaries
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

    private static HashSet<string> CollectAddresses(List<MailMessage> msgs)
    {
        var addrs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in msgs)
        {
            if (!string.IsNullOrEmpty(m.FromAddress)) addrs.Add(m.FromAddress);
            if (!string.IsNullOrEmpty(m.ToAddresses))
                foreach (var a in m.ToAddresses.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                    addrs.Add(a);
            if (!string.IsNullOrEmpty(m.CcAddresses))
                foreach (var a in m.CcAddresses.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                    addrs.Add(a);
        }
        return addrs;
    }

    private static bool HasParticipantOverlap(List<MailMessage> groupA, List<MailMessage> groupB)
    {
        var addrsA = CollectAddresses(groupA);
        var addrsB = CollectAddresses(groupB);
        return addrsA.Overlaps(addrsB);
    }
}
