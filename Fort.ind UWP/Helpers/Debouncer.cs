using System;
using System.Threading;

namespace Fort.ind_UWP
{
    /// <summary>
    /// One in-flight debounced operation. The search box and the games filter both need the same
    /// three-step dance - clear the field, cancel, dispose - and both got it right independently;
    /// this exists so there is only one copy of it to keep right.
    ///
    /// Not IDisposable on purpose: an instance is a page field that outlives any single token, so
    /// there is no point at which "dispose the debouncer" is the correct thing to do. Cancel()
    /// disposes the source it is actually finished with.
    /// </summary>
    public sealed class Debouncer
    {

        private CancellationTokenSource _cts;

        /// <summary>
        /// Cancels any pending operation and returns a token for the new one. Always call this
        /// rather than newing a source at the call site, so the previous source is never leaked.
        /// </summary>
        public CancellationToken Restart()
        {
            Cancel();

            var cts = new CancellationTokenSource();
            _cts = cts;
            return cts.Token;
        }

        /// <summary>
        /// Cancels any pending operation. Safe to call when nothing is pending, and safe to call
        /// repeatedly - which matters because both Unloaded and the next keystroke reach it.
        /// </summary>
        public void Cancel()
        {
            // Clear the field before disposing so no later caller can reach the dead source.
            var cts = _cts;
            _cts = null;
            if (cts == null)
            {
                return;
            }

            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Already torn down elsewhere - nothing left to cancel.
            }
            cts.Dispose();
        }

    }
}
