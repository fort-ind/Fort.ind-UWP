using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation.Metadata;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Fort.ind_UWP
{
    /// <summary>
    /// Every ContentDialog in the app goes through here.
    ///
    /// The gate is one static semaphore for the whole process, which is the scope the question
    /// "is a dialog open right now?" actually has. It used to be a static per page, so a dialog
    /// opened from the shell and one opened from ProfilePage could both be up at once - and
    /// UWP's second ShowAsync throws in that state. Never made instance: ProfilePage sets no
    /// NavigationCacheMode, so a fresh page is built on every visit, and disposing in Unloaded
    /// is unsafe because UWP can raise Unloaded then Loaded on the same instance.
    ///
    /// Every helper here sets CloseButtonText. It is the button the docs call required and the
    /// one Esc is wired to, so an acknowledge-only dialog whose sole button is the *primary* one
    /// cannot be dismissed from the keyboard at all.
    /// </summary>
    public static class DialogService
    {

        private static readonly SemaphoreSlim s_gate = new SemaphoreSlim(1, 1);

        /// <summary>
        /// UIElement.XamlRoot was added in Windows 10 1903 (10.0.18362.0). This app's
        /// TargetPlatformMinVersion is 1809 (10.0.17763.0), where the property doesn't exist -
        /// reading or setting it throws, which every ContentDialog call site swallows in a
        /// try/catch, so on 1809 dialogs silently never appear. A single-window UWP app shows
        /// ContentDialogs fine with XamlRoot left unset, so just skip it when unsupported.
        /// </summary>
        private static readonly bool s_xamlRootSupported =
            ApiInformation.IsPropertyPresent("Windows.UI.Xaml.UIElement", "XamlRoot");

        public static void ApplyXamlRoot(ContentDialog dialog, UIElement owner)
        {
            if (s_xamlRootSupported && owner != null)
            {
                dialog.XamlRoot = owner.XamlRoot;
            }
        }

        /// <summary>
        /// Shows an acknowledge-only dialog. Returns false without showing anything if another
        /// dialog already holds the gate.
        /// </summary>
        public static async Task<bool> ShowMessageAsync(UIElement owner, string title, string content, string closeText)
        {
            return await RunExclusiveAsync(async () =>
            {
                var dialog = new ContentDialog()
                {
                    Title = title,
                    Content = content,
                    CloseButtonText = closeText
                };
                ApplyXamlRoot(dialog, owner);
                await dialog.ShowAsync();
            });
        }

        /// <summary>
        /// Shows a two-button confirmation and reports whether the primary button was chosen.
        /// A busy gate is reported the same way as a declined dialog - false - which is what
        /// every caller wants, since all of them only act on yes.
        /// </summary>
        public static async Task<bool> ShowConfirmAsync(UIElement owner,
                                                        string title,
                                                        string content,
                                                        string primaryText,
                                                        string closeText,
                                                        ContentDialogButton defaultButton)
        {
            bool confirmed = false;

            await RunExclusiveAsync(async () =>
            {
                confirmed = await ShowConfirmCoreAsync(owner, title, content, primaryText, closeText, defaultButton);
            });

            return confirmed;
        }

        /// <summary>
        /// The confirmation itself, without taking the gate. For use inside a
        /// <see cref="RunExclusiveAsync"/> body that shows several dialogs in sequence - taking
        /// the gate again there would deadlock-by-refusal, since SemaphoreSlim is not reentrant.
        /// </summary>
        public static async Task<bool> ShowConfirmCoreAsync(UIElement owner,
                                                            string title,
                                                            string content,
                                                            string primaryText,
                                                            string closeText,
                                                            ContentDialogButton defaultButton)
        {
            var dialog = new ContentDialog()
            {
                Title = title,
                Content = content,
                PrimaryButtonText = primaryText,
                CloseButtonText = closeText,
                DefaultButton = defaultButton
            };
            ApplyXamlRoot(dialog, owner);

            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }

        /// <summary>
        /// Shows a caller-built dialog under the gate - for the two that need custom content
        /// (the colour picker and the welcome dialog) rather than a title and a string.
        /// </summary>
        public static async Task<ContentDialogResult> ShowAsync(UIElement owner, ContentDialog dialog)
        {
            var result = ContentDialogResult.None;

            await RunExclusiveAsync(async () =>
            {
                ApplyXamlRoot(dialog, owner);
                result = await dialog.ShowAsync();
            });

            return result;
        }

        /// <summary>
        /// Runs <paramref name="body"/> holding the dialog gate, or returns false immediately if
        /// something else already holds it. Use directly when one user action shows a sequence of
        /// dialogs that must not be interleaved with anything else.
        ///
        /// Exceptions are logged rather than propagated: every caller is an async void event
        /// handler, where an escaping exception crashes the app.
        /// </summary>
        public static async Task<bool> RunExclusiveAsync(Func<Task> body)
        {
            if (!await s_gate.WaitAsync(0))
            {
                return false; // Another dialog is already open.
            }

            try
            {
                await body();
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"DialogService: dialog failed - {ex.Message}");
                return false;
            }
            finally
            {
                s_gate.Release();
            }
        }

    }
}
