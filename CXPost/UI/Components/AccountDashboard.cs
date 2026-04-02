using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Helpers;
using SharpConsoleUI.Layout;
using SharpConsoleUI.Parsing;
using CXPost.Models;
using CXPost.Services;

namespace CXPost.UI.Components;

/// <summary>
/// Dashboard interaction callbacks.
/// </summary>
public record DashboardActions(
    Action? OnCompose = null,
    Action? OnSearch = null,
    Action? OnSync = null,
    Action? OnSettings = null,
    Action<string>? OnAccountClick = null,      // accountId
    Action<int>? OnFolderClick = null            // folderId
);

/// <summary>
/// Builds rich dashboard views using proper ConsoleEx controls:
/// TableControl, BarGraphControl, ProgressBarControl, RuleControl.
/// </summary>
public static class AccountDashboard
{
    public static List<IWindowControl> BuildAllAccountsDashboard(
        List<Account> accounts, ICacheService cache, DashboardActions? actions = null)
    {
        var controls = new List<IWindowControl>();

        // Gather stats
        var totalMessages = 0;
        var totalUnread = 0;
        var totalFlagged = 0;
        var totalFolders = 0;
        DateTime? oldestUnread = null;

        var accountStats = new List<(Account account, int messages, int unread, int flagged, int folders, DateTime? lastSync, DateTime? oldestUnreadDate)>();

        foreach (var account in accounts)
        {
            var folders = cache.GetFolders(account.Id);
            var msgs = 0;
            var unread = 0;
            var flagged = 0;
            DateTime? acctOldestUnread = null;

            foreach (var f in folders)
            {
                var fMsgs = cache.GetMessages(f.Id);
                msgs += fMsgs.Count;
                foreach (var m in fMsgs)
                {
                    if (!m.IsRead)
                    {
                        unread++;
                        if (acctOldestUnread == null || m.Date < acctOldestUnread)
                            acctOldestUnread = m.Date;
                    }
                    if (m.IsFlagged) flagged++;
                }
            }
            totalMessages += msgs;
            totalUnread += unread;
            totalFlagged += flagged;
            totalFolders += folders.Count;
            if (acctOldestUnread != null && (oldestUnread == null || acctOldestUnread < oldestUnread))
                oldestUnread = acctOldestUnread;

            accountStats.Add((account, msgs, unread, flagged, folders.Count, account.LastSync, acctOldestUnread));
        }

        // ── Header ──────────────────────────────────────────────────────
        controls.Add(Controls.Markup()
            .AddEmptyLine()
            .AddLine("  [cyan1 bold]\U0001f4ec  All Accounts[/]")
            .AddLine($"  [grey50]{accounts.Count} account{(accounts.Count != 1 ? "s" : "")} configured  \u2022  {totalFolders} folders[/]")
            .AddEmptyLine()
            .WithAlignment(HorizontalAlignment.Stretch)
            .Build());

        // ── Summary stats table ─────────────────────────────────────────
        var oldestStr = oldestUnread?.ToString("MMM d, yyyy") ?? "None";
        var summaryTable = Controls.Table()
            .AddColumn("", width: 18)
            .AddColumn("", width: 18)
            .AddColumn("", width: 18)
            .AddColumn("")
            .HideHeader()
            .AddRow(
                $"[cyan1]{totalMessages}[/]",
                $"[yellow]{totalUnread}[/]",
                $"[green]{totalMessages - totalUnread}[/]",
                $"[grey70]{totalFlagged}[/]")
            .AddRow(
                "[grey50]Total messages[/]",
                "[grey50]Unread[/]",
                "[grey50]Read[/]",
                "[grey50]Flagged[/]")
            .WithMargin(2, 0, 2, 0)
            .Build();
        controls.Add(summaryTable);

        // ── Progress bars ───────────────────────────────────────────────
        controls.Add(Controls.ProgressBar()
            .WithHeader($"[yellow]Unread[/]  [grey50]{totalUnread} / {totalMessages}[/]")
            .ShowHeader()
            .WithValue(totalUnread)
            .WithMaxValue(Math.Max(totalMessages, 1))
            .WithFilledColor(Color.Yellow)
            .WithUnfilledColor(Color.Grey19)
            .WithWidth(60)
            .ShowPercentage()
            .WithMargin(2, 1, 2, 0)
            .Build());

        controls.Add(Controls.ProgressBar()
            .WithHeader($"[green]Read[/]  [grey50]{totalMessages - totalUnread} / {totalMessages}[/]")
            .ShowHeader()
            .WithValue(totalMessages - totalUnread)
            .WithMaxValue(Math.Max(totalMessages, 1))
            .WithFilledColor(Color.Green)
            .WithUnfilledColor(Color.Grey19)
            .WithWidth(60)
            .ShowPercentage()
            .WithMargin(2, 0, 2, 0)
            .Build());

        if (oldestUnread != null)
        {
            controls.Add(Controls.Markup()
                .AddLine($"  [grey50]Oldest unread:[/]  [yellow]{oldestStr}[/]")
                .WithMargin(2, 1, 2, 0)
                .Build());
        }

        // ── Activity sparkline ──────────────────────────────────────────
        var allMessages = accounts.SelectMany(a => cache.GetFolders(a.Id))
            .SelectMany(f => cache.GetMessages(f.Id)).ToList();
        AddActivitySparkline(controls, allMessages, "Activity (14 days)");

        // ── Per-account breakdown ───────────────────────────────────────
        controls.Add(Controls.RuleBuilder()
            .WithTitle("[grey93]Accounts[/]")
            .WithColor(Color.Grey23)
            .WithMargin(2, 1, 2, 0)
            .Build());

        foreach (var (account, messages, unread, flagged, folders, lastSync, acctOldestUnread) in accountStats)
        {
            var syncStr = lastSync?.ToString("MMM d, h:mm tt") ?? "Never";
            var acctId = account.Id;

            var accountBar = Controls.StatusBar()
                .AddLeftText($"  \U0001f4e7 [white bold]{MarkupParser.Escape(account.Name)}[/]  [grey50]{MarkupParser.Escape(account.Email)}[/]",
                    actions?.OnAccountClick != null ? () => actions.OnAccountClick(acctId) : null)
                .WithMargin(0, 1, 0, 0)
                .Build();
            accountBar.BackgroundColor = Color.Transparent;
            controls.Add(accountBar);

            // Bar graph showing unread vs total
            controls.Add(Controls.BarGraph()
                .WithLabel($"{unread} unread")
                .WithLabelWidth(14)
                .WithValue(unread)
                .WithMaxValue(Math.Max(messages, 1))
                .WithBarWidth(30)
                .WithColors(unread > 0 ? Color.Yellow : Color.Green, Color.Grey19)
                .ShowValue()
                .WithValueFormat($"/ {messages}")
                .WithMargin(4, 0, 2, 0)
                .Build());

            // Per-account detail line
            var detailParts = new List<string>
            {
                $"[grey50]{folders} folders[/]"
            };
            if (flagged > 0)
                detailParts.Add($"[yellow]{flagged} flagged[/]");
            if (acctOldestUnread != null)
                detailParts.Add($"[grey50]oldest unread: {acctOldestUnread:MMM d}[/]");
            detailParts.Add($"[grey35]synced: {syncStr}[/]");

            controls.Add(Controls.Markup()
                .AddLine($"    {string.Join("  \u2022  ", detailParts)}")
                .WithAlignment(HorizontalAlignment.Stretch)
                .Build());
        }

        // ── Quick Actions ───────────────────────────────────────────────
        controls.Add(Controls.Markup().AddEmptyLine()
            .WithAlignment(HorizontalAlignment.Stretch).Build());

        controls.Add(Controls.RuleBuilder()
            .WithTitle("[grey93]Quick Actions[/]")
            .WithColor(Color.Grey23)
            .WithMargin(2, 0, 2, 0)
            .Build());

        if (actions != null)
        {
            var actionBar = Controls.StatusBar()
                .AddLeft("Ctrl+N", "Compose", actions.OnCompose)
                .AddLeft("Ctrl+S", "Search", actions.OnSearch)
                .AddLeft("F5", "Sync All", actions.OnSync)
                .AddLeft("Ctrl+,", "Settings", actions.OnSettings)
                .WithMargin(2, 1, 2, 1)
                .Build();
            actionBar.BackgroundColor = Color.Transparent;
            controls.Add(actionBar);
        }
        else
        {
            controls.Add(Controls.Markup()
                .AddEmptyLine()
                .AddLine("  [cyan1]Ctrl+N[/]   [grey70]Compose new message[/]")
                .AddLine("  [cyan1]Ctrl+S[/]   [grey70]Search messages[/]")
                .AddLine("  [cyan1]F5[/]       [grey70]Sync all accounts[/]")
                .AddLine("  [cyan1]Ctrl+,[/]   [grey70]Settings & account management[/]")
                .AddEmptyLine()
                .WithAlignment(HorizontalAlignment.Stretch)
                .Build());
        }

        return controls;
    }

    public static List<IWindowControl> BuildAccountDashboard(
        Account account, ICacheService cache, DashboardActions? actions = null)
    {
        var controls = new List<IWindowControl>();
        var folders = cache.GetFolders(account.Id);

        var totalMessages = 0;
        var totalUnread = 0;
        var folderRows = new List<(string icon, string name, int messages, int unread, int folderId)>();

        foreach (var folder in folders.OrderBy(f => f.Path))
        {
            if (folder.DisplayName.StartsWith("[") && folder.DisplayName.EndsWith("]"))
                continue;
            var msgs = cache.GetMessages(folder.Id);
            var unread = msgs.Count(m => !m.IsRead);
            totalMessages += msgs.Count;
            totalUnread += unread;
            folderRows.Add((GetFolderIcon(folder.DisplayName), folder.DisplayName, msgs.Count, unread, folder.Id));
        }

        // ── Header ──────────────────────────────────────────────────────
        controls.Add(Controls.Markup()
            .AddEmptyLine()
            .AddLine($"  [cyan1 bold]\U0001f4e7  {MarkupParser.Escape(account.Name)}[/]")
            .AddLine($"  [grey50]{MarkupParser.Escape(account.Email)}[/]")
            .AddLine($"  [grey35]{MarkupParser.Escape(account.ImapHost)}:{account.ImapPort}[/]")
            .AddEmptyLine()
            .WithAlignment(HorizontalAlignment.Stretch)
            .Build());

        // ── Unread ratio ────────────────────────────────────────────────
        controls.Add(Controls.ProgressBar()
            .WithHeader($"[yellow]Unread[/]  [grey50]{totalUnread} / {totalMessages}[/]")
            .ShowHeader()
            .WithValue(totalUnread)
            .WithMaxValue(Math.Max(totalMessages, 1))
            .WithFilledColor(Color.Yellow)
            .WithUnfilledColor(Color.Grey19)
            .WithWidth(60)
            .ShowPercentage()
            .WithMargin(2, 0, 2, 1)
            .Build());

        // ── Activity sparkline ──────────────────────────────────────────
        var allMessages = folders.SelectMany(f => cache.GetMessages(f.Id)).ToList();
        AddActivitySparkline(controls, allMessages, "Activity (14 days)");

        // ── Folders with bar graphs ─────────────────────────────────────
        controls.Add(Controls.RuleBuilder()
            .WithTitle("[grey93]Folders[/]")
            .WithColor(Color.Grey23)
            .WithMargin(2, 0, 2, 0)
            .Build());

        var maxFolderSize = folderRows.Count > 0 ? folderRows.Max(f => f.messages) : 1;

        foreach (var (icon, name, messages, unread, folderId) in folderRows)
        {
            if (messages == 0 && unread == 0) continue;

            var fId = folderId;
            if (actions?.OnFolderClick != null)
            {
                // Clickable folder row
                var folderBar = Controls.StatusBar()
                    .AddLeftText(
                        $"  {icon} [{(unread > 0 ? "white bold" : "grey70")}]{MarkupParser.Escape(name)}[/]  [grey50]{messages} msgs{(unread > 0 ? $", [yellow]{unread} unread[/]" : "")}[/]",
                        () => actions.OnFolderClick(fId))
                    .WithMargin(2, 0, 2, 0)
                    .Build();
                folderBar.BackgroundColor = Color.Transparent;
                controls.Add(folderBar);
            }
            else
            {
                var barColor = unread > 0 ? Color.Yellow : Color.Grey30;
                controls.Add(Controls.BarGraph()
                    .WithLabel($"{icon} {name}")
                    .WithLabelWidth(22)
                    .WithValue(messages)
                    .WithMaxValue(Math.Max(maxFolderSize, 1))
                    .WithBarWidth(25)
                    .WithColors(barColor, Color.Grey15)
                    .ShowValue()
                    .WithValueFormat(unread > 0 ? $"[yellow]{unread}[/] / {messages}" : $"{messages}")
                    .WithMargin(2, 0, 2, 0)
                    .Build());
            }
        }

        // ── Account Details ─────────────────────────────────────────────
        controls.Add(Controls.Markup().AddEmptyLine()
            .WithAlignment(HorizontalAlignment.Stretch).Build());

        controls.Add(Controls.RuleBuilder()
            .WithTitle("[grey93]Details[/]")
            .WithColor(Color.Grey23)
            .WithMargin(2, 0, 2, 0)
            .Build());

        var sigPreview = !string.IsNullOrEmpty(account.Signature)
            ? MarkupParser.Escape(account.Signature.Split('\n')[0])
            : "[grey35]Not configured[/]";

        var detailsTable = Controls.Table()
            .AddColumn("", width: 12)
            .AddColumn("")
            .HideHeader()
            .AddRow("[grey50]IMAP[/]", $"[grey70]{MarkupParser.Escape(account.ImapHost)}:{account.ImapPort}[/]")
            .AddRow("[grey50]SMTP[/]", $"[grey70]{MarkupParser.Escape(account.SmtpHost)}:{account.SmtpPort}[/]")
            .AddRow("[grey50]Signature[/]", $"[grey70]{sigPreview}[/]")
            .AddRow("[grey50]Last sync[/]", $"[grey70]{(account.LastSync?.ToString("MMM d, h:mm tt") ?? "Never")}[/]")
            .WithMargin(2, 1, 2, 0)
            .Build();
        controls.Add(detailsTable);

        // ── Quick Actions ───────────────────────────────────────────────
        controls.Add(Controls.Markup().AddEmptyLine()
            .WithAlignment(HorizontalAlignment.Stretch).Build());

        controls.Add(Controls.RuleBuilder()
            .WithTitle("[grey93]Quick Actions[/]")
            .WithColor(Color.Grey23)
            .WithMargin(2, 0, 2, 0)
            .Build());

        if (actions != null)
        {
            var actionBar = Controls.StatusBar()
                .AddLeft("Ctrl+N", "Compose", actions.OnCompose)
                .AddLeft("F5", "Sync", actions.OnSync)
                .AddLeft("Ctrl+,", "Settings", actions.OnSettings)
                .WithMargin(2, 1, 2, 1)
                .Build();
            actionBar.BackgroundColor = Color.Transparent;
            controls.Add(actionBar);
        }
        else
        {
            controls.Add(Controls.Markup()
                .AddEmptyLine()
                .AddLine("  [cyan1]Ctrl+N[/]   [grey70]Compose new message[/]")
                .AddLine("  [cyan1]F5[/]       [grey70]Sync this account[/]")
                .AddLine("  [cyan1]Ctrl+,[/]   [grey70]Edit account settings[/]")
                .AddEmptyLine()
                .WithAlignment(HorizontalAlignment.Stretch)
                .Build());
        }

        return controls;
    }

    private static void AddActivitySparkline(List<IWindowControl> controls, List<MailMessage> messages, string title)
    {
        const int days = 14;
        var today = DateTime.Today;
        var dailyCounts = new double[days];

        foreach (var msg in messages)
        {
            var age = (today - msg.Date.Date).Days;
            if (age >= 0 && age < days)
                dailyCounts[days - 1 - age]++;
        }

        // Only show if there's any activity
        if (dailyCounts.Any(c => c > 0))
        {
            controls.Add(Controls.RuleBuilder()
                .WithTitle($"[grey93]{title}[/]")
                .WithColor(Color.Grey23)
                .WithMargin(2, 1, 2, 0)
                .Build());

            var sparkline = Controls.Sparkline()
                .WithHeight(3)
                .WithData(dailyCounts)
                .WithGradient(ColorGradient.FromColors(new Color(50, 80, 140), new Color(80, 200, 255)))
                .WithBackgroundColor(Color.Transparent)
                .WithBaseline(true, '┈', Color.Grey23)
                .WithTitle($"[grey35]{today.AddDays(-(days - 1)):MMM d} \u2192 {today:MMM d}[/]")
                .WithTitlePosition(TitlePosition.Bottom)
                .WithInlineTitleBaseline()
                .WithAutoFitDataPoints()
                .WithAlignment(HorizontalAlignment.Stretch)
                .WithMargin(2, 0, 2, 0)
                .Build();
            controls.Add(sparkline);
        }
    }

    private static string GetFolderIcon(string folderName) => MessageFormatter.GetFolderIcon(folderName);
}
