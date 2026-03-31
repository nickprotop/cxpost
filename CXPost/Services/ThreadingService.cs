using CXPost.Models;

namespace CXPost.Services;

/// <summary>
/// Simplified JWZ threading: groups messages into threads using
/// Message-ID, In-Reply-To, and References headers.
/// Uses union-find to merge related message IDs into thread groups.
/// </summary>
public class ThreadingService
{
    public void AssignThreadIds(List<MailMessage> messages)
    {
        // Union-Find: maps message-id -> root message-id
        var parent = new Dictionary<string, string>();

        string Find(string id)
        {
            if (!parent.ContainsKey(id))
                parent[id] = id;
            while (parent[id] != id)
            {
                parent[id] = parent[parent[id]]; // path compression
                id = parent[id];
            }
            return id;
        }

        void Union(string a, string b)
        {
            var ra = Find(a);
            var rb = Find(b);
            if (ra != rb)
                parent[ra] = rb;
        }

        // Build unions from message relationships
        foreach (var msg in messages)
        {
            var msgId = msg.MessageId;
            if (string.IsNullOrEmpty(msgId))
                continue;

            Find(msgId); // ensure registered

            // Link via In-Reply-To
            if (!string.IsNullOrEmpty(msg.InReplyTo))
                Union(msgId, msg.InReplyTo);

            // Link via References (space-separated message IDs)
            if (!string.IsNullOrEmpty(msg.References))
            {
                var refs = msg.References.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                foreach (var refId in refs)
                    Union(msgId, refId);

                // Also chain consecutive references
                for (var i = 1; i < refs.Length; i++)
                    Union(refs[i - 1], refs[i]);
            }
        }

        // Map roots to stable thread IDs
        var rootToThreadId = new Dictionary<string, string>();

        foreach (var msg in messages)
        {
            if (!string.IsNullOrEmpty(msg.MessageId))
            {
                var root = Find(msg.MessageId);
                if (!rootToThreadId.ContainsKey(root))
                    rootToThreadId[root] = root; // use root message-id as thread id
                msg.ThreadId = rootToThreadId[root];
            }
            else
            {
                // No message ID — standalone thread
                msg.ThreadId = $"<orphan-{msg.Id}>";
            }
        }
    }
}
