using CXPost.Models;
using CXPost.Services;

namespace CXPost.Tests.Services;

public class ThreadingServiceTests
{
    private readonly ThreadingService _service = new();

    [Fact]
    public void AssignThreadIds_groups_reply_chain_by_message_id()
    {
        var messages = new List<MailMessage>
        {
            new() { Id = 1, MessageId = "<msg1@ex>", Subject = "Hello" },
            new() { Id = 2, MessageId = "<msg2@ex>", InReplyTo = "<msg1@ex>", Subject = "Re: Hello" },
            new() { Id = 3, MessageId = "<msg3@ex>", InReplyTo = "<msg2@ex>", Subject = "Re: Re: Hello" }
        };

        _service.AssignThreadIds(messages);

        Assert.NotNull(messages[0].ThreadId);
        Assert.Equal(messages[0].ThreadId, messages[1].ThreadId);
        Assert.Equal(messages[0].ThreadId, messages[2].ThreadId);
    }

    [Fact]
    public void AssignThreadIds_uses_references_header()
    {
        var messages = new List<MailMessage>
        {
            new() { Id = 1, MessageId = "<root@ex>", Subject = "Thread" },
            new() { Id = 2, MessageId = "<reply@ex>", References = "<root@ex>", Subject = "Re: Thread" }
        };

        _service.AssignThreadIds(messages);

        Assert.Equal(messages[0].ThreadId, messages[1].ThreadId);
    }

    [Fact]
    public void AssignThreadIds_separate_threads_get_different_ids()
    {
        var messages = new List<MailMessage>
        {
            new() { Id = 1, MessageId = "<a@ex>", Subject = "Topic A" },
            new() { Id = 2, MessageId = "<b@ex>", Subject = "Topic B" }
        };

        _service.AssignThreadIds(messages);

        Assert.NotEqual(messages[0].ThreadId, messages[1].ThreadId);
    }

    [Fact]
    public void AssignThreadIds_handles_missing_message_id()
    {
        var messages = new List<MailMessage>
        {
            new() { Id = 1, Subject = "No message ID" }
        };

        _service.AssignThreadIds(messages);

        Assert.NotNull(messages[0].ThreadId);
    }

    [Fact]
    public void AssignThreadIds_chains_through_references_list()
    {
        var messages = new List<MailMessage>
        {
            new() { Id = 1, MessageId = "<a@ex>", Subject = "Start" },
            new() { Id = 2, MessageId = "<b@ex>", InReplyTo = "<a@ex>", References = "<a@ex>", Subject = "Re: Start" },
            new() { Id = 3, MessageId = "<c@ex>", InReplyTo = "<b@ex>", References = "<a@ex> <b@ex>", Subject = "Re: Re: Start" }
        };

        _service.AssignThreadIds(messages);

        var threadId = messages[0].ThreadId;
        Assert.All(messages, m => Assert.Equal(threadId, m.ThreadId));
    }
}
