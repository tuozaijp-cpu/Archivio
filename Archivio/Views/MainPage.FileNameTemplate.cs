using Microsoft.UI.Xaml;

namespace Archivio.Views;

public partial class MainPage
{
    // Filename-template commands are isolated from the list/context-menu plumbing.
    // The existing resolver is intentionally reused to preserve aliases and syntax.
    private void RenameMenuItem_Click(object sender, RoutedEventArgs e)
        => RenameMenuItem_ClickCore(sender, e);

    private void RenameFromMetadataMenuItem_Click(object sender, RoutedEventArgs e)
        => RenameFromMetadataMenuItem_ClickCore(sender, e);
}
