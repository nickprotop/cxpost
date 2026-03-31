using SharpConsoleUI;
using SharpConsoleUI.Builders;
using SharpConsoleUI.Controls;
using SharpConsoleUI.Events;
using SharpConsoleUI.Extensions;
using SharpConsoleUI.Layout;
using CXPost.Models;
using CXPost.UI.Components;

namespace CXPost.UI.Dialogs;

public class FolderPickerDialog : DialogBase<MailFolder?>
{
    private readonly List<MailFolder> _folders;
    private ListControl? _folderList;

    public FolderPickerDialog(List<MailFolder> folders)
    {
        _folders = folders;
    }

    protected override string GetTitle() => "Move to Folder";
    protected override (int width, int height) GetSize() => (40, 16);
    protected override bool GetResizable() => false;
    protected override MailFolder? GetDefaultResult() => null;

    protected override void BuildContent()
    {
        _folderList = Controls.List("Select folder:")
            .WithAutoHighlightOnFocus(true)
            .Build();

        foreach (var folder in _folders)
            _folderList.AddItem(folder.DisplayName);

        _folderList.HorizontalAlignment = HorizontalAlignment.Stretch;
        _folderList.VerticalAlignment = VerticalAlignment.Fill;
        Modal.AddControl(_folderList);

        // Rule before buttons
        Modal.AddControl(Controls.Markup($"[{ColorScheme.MutedMarkup}]{"─".PadRight(36, '─')}[/]")
            .StickyBottom()
            .Build());

        // Button row
        var selectButton = Controls.Button("[grey93]  Select (Enter)  [/]")
            .WithBackgroundColor(Color.Grey30)
            .WithFocusedBackgroundColor(Color.DarkGreen)
            .OnClick((s, e) =>
            {
                var idx = _folderList?.SelectedIndex ?? -1;
                if (idx >= 0 && idx < _folders.Count)
                    CloseWithResult(_folders[idx]);
            })
            .Build();

        var cancelButton = Controls.Button("[grey93]  Cancel (Esc)  [/]")
            .WithBackgroundColor(Color.Grey30)
            .OnClick((s, e) => CloseWithResult(null))
            .Build();

        var buttonGrid = Controls.HorizontalGrid()
            .WithAlignment(HorizontalAlignment.Center)
            .StickyBottom()
            .Column(col => col.Add(selectButton))
            .Column(col => col.Width(2))
            .Column(col => col.Add(cancelButton))
            .Build();
        buttonGrid.Margin = new Margin(0, 1, 0, 0);
        Modal.AddControl(buttonGrid);
    }

    protected override void SetInitialFocus() => _folderList?.RequestFocus();

    protected override void OnKeyPressed(object? sender, KeyPressedEventArgs e)
    {
        if (e.KeyInfo.Key == ConsoleKey.Enter)
        {
            var idx = _folderList?.SelectedIndex ?? -1;
            if (idx >= 0 && idx < _folders.Count)
                CloseWithResult(_folders[idx]);
            e.Handled = true;
        }
        else
        {
            base.OnKeyPressed(sender, e);
        }
    }
}
