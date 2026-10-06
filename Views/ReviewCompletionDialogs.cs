using System.Windows;
using Sati.Models;
using Sati.ViewModels;

namespace Sati.Views;

internal static class ReviewCompletionDialogs
{
    public static async Task ShowAsync(Window owner, NewClientViewModel clients, Person person, Form form)
    {
        try
        {
            var prompt = new ReviewCompletionPromptWindow(person, form) { Owner = owner };
            if (prompt.ShowDialog() != true) return;
            var editor = await clients.CreateReviewCompletionEditorAsync(person, form, prompt.CompletedOn);
            new ReviewCompletionNoteWindow(editor) { Owner = owner }.ShowDialog();
        }
        catch (Exception)
        {
            MessageBox.Show(owner, "The review note editor could not be opened. Reopen the client profile and try again.",
                "Review completion", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
